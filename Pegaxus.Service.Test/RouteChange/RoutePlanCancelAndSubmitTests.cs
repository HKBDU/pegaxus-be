using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.RoutePlanService;

using RoutePlanRequest = Pegaxus.Service.RoutePlanService.Request;

namespace Pegaxus.Service.Test;

public class RoutePlanCancelAndSubmitTests
{
    private sealed class RecordingRealtime : IRoutePlanRealtimeService
    {
        public List<(IReadOnlyList<Guid> Recipients, Response.RoutePlanCancelledEvent Event)> Cancelled { get; } = [];

        public Task PublishCancelledAsync(IReadOnlyList<Guid> recipientAccountIds, Response.RoutePlanCancelledEvent cancelled, CancellationToken cancellationToken = default)
        {
            Cancelled.Add((recipientAccountIds, cancelled));
            return Task.CompletedTask;
        }
    }

    private static async Task<Employee> AddUnassignedSpecialistAsync(AppDbContext db)
    {
        var (_, other) = await TestSupport.AddLogisticsManagerAsync(db);
        other.Role = EmployeeRole.TRANSPORT_SPECIALIST;
        await db.SaveChangesAsync();
        return other;
    }

    [Fact]
    public async Task Cancel_NotifiesTheAssignedSpecialistOnly_AndPushesRoutePlanCancelledAfterTheCommit()
    {
        await using var db = TestSupport.CreateDb();
        var f = await RouteChangeTests.Fixture.CreateAsync(db);
        var backup = await f.AlternateAsync(RouteType.BACKUP, RoutePlanStatus.DRAFT);
        var specialist = await db.Employees.SingleAsync(item => item.Role == EmployeeRole.TRANSPORT_SPECIALIST);
        var unassigned = await AddUnassignedSpecialistAsync(db);
        var realtime = new RecordingRealtime();

        var result = await TestSupport.CreateRoutePlans(db, realtime).CancelAsync(f.Coordinator.AccountId, backup.RoutePlanId,
            new() { LastUpdatedAt = RouteChangeTests.Fixture.Version(backup), Reason = "The road reopened" });

        var pushed = Assert.Single(realtime.Cancelled);
        Assert.Equal([specialist.AccountId], pushed.Recipients);
        Assert.Equal((backup.RoutePlanId, backup.TransportPlanId, "BACKUP"), (pushed.Event.RoutePlanId, pushed.Event.TransportPlanId, pushed.Event.RouteType));
        Assert.Equal(result.CancelledAt, pushed.Event.CancelledAt);
        var notification = Assert.Single(await db.Notifications.Where(item => item.ReferenceId == backup.RoutePlanId).ToListAsync());
        Assert.Contains("The road reopened", notification.Message);
        var recipients = await db.AccountNotifications.Where(item => item.NotificationId == notification.NotificationId).Select(item => item.AccountId).ToListAsync();
        Assert.Equal([specialist.AccountId], recipients);
        Assert.DoesNotContain(unassigned.AccountId, recipients);
    }

    [Fact]
    public async Task Cancel_ThatFails_SendsNothing()
    {
        await using var db = TestSupport.CreateDb();
        var f = await RouteChangeTests.Fixture.CreateAsync(db);
        var confirmed = await f.AlternateAsync(RouteType.BACKUP, RoutePlanStatus.CONFIRMED);
        var realtime = new RecordingRealtime();

        var error = await Assert.ThrowsAsync<ConflictException>(() => TestSupport.CreateRoutePlans(db, realtime).CancelAsync(f.Coordinator.AccountId,
            confirmed.RoutePlanId, new() { LastUpdatedAt = RouteChangeTests.Fixture.Version(confirmed), Reason = "Too late to cancel" }));

        Assert.Equal("INVALID_ROUTE_PLAN_STATUS", error.ErrorCode);
        Assert.Empty(realtime.Cancelled);
        Assert.Empty(await db.AccountNotifications.ToListAsync());
    }

    [Fact]
    public async Task LegalReview_NeedsAPlanThatWasSubmitted()
    {
        await using var db = TestSupport.CreateDb();
        var f = await RouteChangeTests.Fixture.CreateAsync(db);
        var backup = await f.AlternateAsync(RouteType.BACKUP, RoutePlanStatus.DRAFT);
        backup.SubmittedForReviewAt = null;
        await db.SaveChangesAsync();
        var specialist = await db.Employees.SingleAsync(item => item.Role == EmployeeRole.TRANSPORT_SPECIALIST);

        var error = await Assert.ThrowsAsync<ConflictException>(() => TestSupport.CreateRoutePlans(db).ReviewLegalAsync(specialist.AccountId, backup.RoutePlanId,
            new() { LastUpdatedAt = RouteChangeTests.Fixture.Version(backup), IsFeasible = true }));

        Assert.Equal("ROUTE_PLAN_NOT_SUBMITTED", error.ErrorCode);
        Assert.Equal(RoutePlanStatus.DRAFT, backup.Status);
    }

    [Fact]
    public async Task LegalReview_ThatAsksForChanges_MustBeSubmittedAgainBeforeTheNextReview()
    {
        await using var db = TestSupport.CreateDb();
        var f = await RouteChangeTests.Fixture.CreateAsync(db);
        var backup = await f.AlternateAsync(RouteType.BACKUP, RoutePlanStatus.DRAFT);
        var specialist = await db.Employees.SingleAsync(item => item.Role == EmployeeRole.TRANSPORT_SPECIALIST);
        var service = TestSupport.CreateRoutePlans(db);

        await service.ReviewLegalAsync(specialist.AccountId, backup.RoutePlanId,
            new() { LastUpdatedAt = RouteChangeTests.Fixture.Version(backup), IsFeasible = false, Note = "A permit is missing" });

        Assert.Null(backup.SubmittedForReviewAt);
        var error = await Assert.ThrowsAsync<ConflictException>(() => service.ReviewLegalAsync(specialist.AccountId, backup.RoutePlanId,
            new() { LastUpdatedAt = RouteChangeTests.Fixture.Version(backup), IsFeasible = true }));
        Assert.Equal("ROUTE_PLAN_NOT_SUBMITTED", error.ErrorCode);
    }

    [Fact]
    public async Task Submit_SetsTheLegalReviewRequest_AndSavingAsDraftKeepsIt()
    {
        await using var db = TestSupport.CreateDb();
        var f = await RouteChangeTests.Fixture.CreateAsync(db);
        f.Transport.Booking.OriginCountry = f.Transport.Booking.DestinationCountry = "FR";
        await db.SaveChangesAsync();
        var completed = await db.Routes.SingleAsync(item => item.RouteId == f.Completed.RouteId);
        RoutePlanRequest.SegmentRequest Segment(int sequenceNo, string type, Route route, int fromHour, int toHour) => new()
        {
            SequenceNo = sequenceNo, RouteType = type, RouteId = route.RouteId,
            PlannedDepartureAt = RouteChangeTests.Fixture.StartAt.AddHours(fromHour), PlannedArrivalAt = RouteChangeTests.Fixture.StartAt.AddHours(toHour)
        };
        var segments = new[] { Segment(1, "PRIMARY", completed, -3, -1), Segment(2, "BACKUP", f.Alternative, 0, 2) };
        var service = TestSupport.CreateRoutePlans(db);

        var submitted = await service.CreateAsync(f.Coordinator.AccountId, f.Plan.TransportPlanId, new()
        {
            RouteType = "BACKUP", Action = "SUBMIT_FOR_LEGAL_REVIEW", RouteNote = "Snow on the primary road near the safe stop", Segments = [.. segments]
        });
        Assert.NotNull(submitted.SubmittedForReviewAt);
        Assert.True(submitted.LegalReviewRequested);
        Assert.Equal(submitted.SubmittedForReviewAt, submitted.LegalReviewRequestedAt);
        // A plan created and submitted in one step also e-mails the Transport Specialist, like a later submit does.
        Assert.Contains(await db.OutboxMessages.ToListAsync(), item => item.MessageType == "EMAIL");

        f.Transport.Status = TransportStatus.READY;
        await db.SaveChangesAsync();
        var saved = await service.UpdateAsync(f.Coordinator.AccountId, submitted.RoutePlanId, new()
        {
            LastUpdatedAt = submitted.UpdatedAt, UpdateReason = "Back to editing", Action = "SAVE_DRAFT",
            RouteNote = "Snow on the primary road near the safe stop", Segments = [.. segments]
        });

        Assert.Equal("DRAFT", saved.Status);
        Assert.True(saved.LegalReviewRequested);
        Assert.Equal(submitted.LegalReviewRequestedAt, saved.LegalReviewRequestedAt);
        var detail = await service.GetByIdAsync(f.Manager.AccountId, submitted.RoutePlanId);
        Assert.True(detail.LegalReviewRequested);
        Assert.Equal(submitted.LegalReviewRequestedAt, detail.LegalReviewRequestedAt);
        var listed = Assert.Single((await service.GetListAsync(f.Manager.AccountId, f.Plan.TransportPlanId, new() { RouteType = "BACKUP" })).Items, item => item.RoutePlanId == submitted.RoutePlanId);
        Assert.True(listed.LegalReviewRequested);
    }

    [Fact]
    public async Task SaveDraft_OnCreate_DoesNotRequestALegalReview()
    {
        await using var db = TestSupport.CreateDb();
        var f = await RouteChangeTests.Fixture.CreateAsync(db);
        f.Transport.Booking.OriginCountry = f.Transport.Booking.DestinationCountry = "FR";
        await db.SaveChangesAsync();
        var completed = await db.Routes.SingleAsync(item => item.RouteId == f.Completed.RouteId);
        RoutePlanRequest.SegmentRequest Segment(int sequenceNo, string type, Route route, int fromHour, int toHour) => new()
        {
            SequenceNo = sequenceNo, RouteType = type, RouteId = route.RouteId,
            PlannedDepartureAt = RouteChangeTests.Fixture.StartAt.AddHours(fromHour), PlannedArrivalAt = RouteChangeTests.Fixture.StartAt.AddHours(toHour)
        };

        var draft = await TestSupport.CreateRoutePlans(db).CreateAsync(f.Coordinator.AccountId, f.Plan.TransportPlanId, new()
        {
            RouteType = "BACKUP", Action = "SAVE_DRAFT", RouteNote = "Snow on the primary road near the safe stop",
            Segments = [Segment(1, "PRIMARY", completed, -3, -1), Segment(2, "BACKUP", f.Alternative, 0, 2)]
        });

        Assert.Equal("DRAFT", draft.Status);
        Assert.False(draft.LegalReviewRequested);
        Assert.Null(draft.LegalReviewRequestedAt);
        Assert.Null(draft.SubmittedForReviewAt);
        Assert.DoesNotContain(await db.OutboxMessages.ToListAsync(), item => item.MessageType == "EMAIL");
    }

    [Fact]
    public async Task LegalReview_AnswersTheRequest_WhateverTheResult()
    {
        await using var db = TestSupport.CreateDb();
        var f = await RouteChangeTests.Fixture.CreateAsync(db);
        var backup = await f.AlternateAsync(RouteType.BACKUP, RoutePlanStatus.DRAFT);
        var specialist = await db.Employees.SingleAsync(item => item.Role == EmployeeRole.TRANSPORT_SPECIALIST);
        var service = TestSupport.CreateRoutePlans(db);
        Assert.NotNull(backup.SubmittedForReviewAt);

        var rejected = await service.ReviewLegalAsync(specialist.AccountId, backup.RoutePlanId,
            new() { LastUpdatedAt = RouteChangeTests.Fixture.Version(backup), IsFeasible = false, Note = "A permit is missing" });
        Assert.Equal("DRAFT", rejected.Status);
        Assert.False(rejected.LegalReviewRequested);
        Assert.Null(rejected.LegalReviewRequestedAt);
        Assert.Equal("A permit is missing", rejected.LegalReview.Note);
        Assert.Empty(rejected.LegalReview.BlockedSegments);

        backup.SubmittedForReviewAt = backup.UpdatedAt;
        await db.SaveChangesAsync();
        var confirmed = await service.ReviewLegalAsync(specialist.AccountId, backup.RoutePlanId,
            new() { LastUpdatedAt = RouteChangeTests.Fixture.Version(backup), IsFeasible = true });
        Assert.Equal("CONFIRMED", confirmed.Status);
        Assert.False(confirmed.LegalReviewRequested);
        Assert.Null(confirmed.LegalReviewRequestedAt);
        Assert.Null(backup.SubmittedForReviewAt);
    }
}
