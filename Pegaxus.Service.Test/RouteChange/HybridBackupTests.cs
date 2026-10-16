using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;

using RoutePlanRequest = Pegaxus.Service.RoutePlanService.Request;

namespace Pegaxus.Service.Test;

public class HybridBackupTests
{
    private static readonly DateTimeOffset Start = RouteChangeTests.Fixture.StartAt;

    private static async Task<RouteChangeTests.Fixture> CreateFixtureAsync(AppDbContext db)
    {
        var f = await RouteChangeTests.Fixture.CreateAsync(db);
        f.Transport.Booking.OriginCountry = f.Transport.Booking.DestinationCountry = "FR";
        await db.SaveChangesAsync();
        return f;
    }

    private static RoutePlanRequest.SegmentRequest Segment(int sequenceNo, string? type, Route route, int fromHour, int toHour, params Guid[] vehicleIds) => new()
    {
        SequenceNo = sequenceNo, RouteType = type, RouteId = route.RouteId, VehicleIds = [.. vehicleIds],
        PlannedDepartureAt = Start.AddHours(fromHour), PlannedArrivalAt = Start.AddHours(toHour)
    };

    private static RoutePlanRequest.SaveRequest Backup(params RoutePlanRequest.SegmentRequest[] segments) => new()
    {
        RouteType = "BACKUP", Action = "SAVE_DRAFT", RouteNote = "Snow on the primary road near the safe stop", Segments = [.. segments]
    };

    private static async Task<(Route Completed, Route Current)> PrimaryRoutesAsync(AppDbContext db, RouteChangeTests.Fixture f) =>
        (await db.Routes.SingleAsync(x => x.RouteId == f.Completed.RouteId), await db.Routes.SingleAsync(x => x.RouteId == f.Current.RouteId));

    [Fact]
    public async Task Rp1_CreatesAMixedBackup_ReportsTheBranchPoint_AndLeavesTheDossierAndTheSharedVehicleAlone()
    {
        await using var db = TestSupport.CreateDb();
        var f = await CreateFixtureAsync(db);
        var (completed, _) = await PrimaryRoutesAsync(db, f);
        var vehicle = await db.Vehicles.SingleAsync();

        var result = await TestSupport.CreateRoutePlans(db).CreateAsync(f.Coordinator.AccountId, f.Plan.TransportPlanId, Backup(
            Segment(1, "PRIMARY", completed, -3, -1),
            Segment(2, "BACKUP", f.Alternative, 0, 2, vehicle.VehicleId)));

        Assert.Equal("BACKUP", result.RouteType);
        Assert.Equal(["PRIMARY", "BACKUP"], result.Segments.Select(x => x.RouteType).ToArray());
        Assert.Equal((2, f.Start.LocationId), (result.BranchSequenceNo, result.BranchLocation!.LocationId));
        Assert.Equal(vehicle.VehicleId, Assert.Single(result.Segments[1].Vehicles).VehicleId);
        Assert.Equal(ComplianceDossierStatus.APPROVED, f.Transport.MainDossier!.Status);
    }

    [Fact]
    public async Task Rp1_OmittedSegmentTypes_FallBackToThePlanType()
    {
        await using var db = TestSupport.CreateDb();
        var f = await CreateFixtureAsync(db);
        var (completed, _) = await PrimaryRoutesAsync(db, f);

        var result = await TestSupport.CreateRoutePlans(db).CreateAsync(f.Coordinator.AccountId, f.Plan.TransportPlanId, Backup(
            Segment(1, null, completed, -3, -1), Segment(2, null, f.Alternative, 0, 2)));

        Assert.Equal("BACKUP", result.RouteType);
        Assert.Equal(["BACKUP", "BACKUP"], result.Segments.Select(x => x.RouteType).ToArray());
        Assert.Equal((1, f.Origin.LocationId), (result.BranchSequenceNo, result.BranchLocation!.LocationId));
    }

    [Theory]
    [InlineData("PRIMARY", "BACKUP", "RETURN")]
    [InlineData("BACKUP", "PRIMARY", "PRIMARY")]
    [InlineData("BACKUP", "BACKUP", "RETURN")]
    public async Task Rp1_RejectsSegmentTypesThatDoNotFitThePlanType(string planType, string first, string second)
    {
        await using var db = TestSupport.CreateDb();
        var f = await CreateFixtureAsync(db);
        var (completed, _) = await PrimaryRoutesAsync(db, f);
        var request = Backup(Segment(1, first, completed, -3, -1), Segment(2, second, f.Alternative, 0, 2));
        request.RouteType = planType;

        var error = await Assert.ThrowsAsync<BadRequestException>(() =>
            TestSupport.CreateRoutePlans(db).CreateAsync(f.Coordinator.AccountId, f.Plan.TransportPlanId, request));

        Assert.Equal("VALIDATION_ERROR", error.ErrorCode);
    }

    [Fact]
    public async Task Rp1_ChecksAnchoringSegmentMatchAndIdenticalRoute()
    {
        await using var db = TestSupport.CreateDb();
        var f = await CreateFixtureAsync(db);
        var (completed, current) = await PrimaryRoutesAsync(db, f);
        var stray = f.NewRoute(f.Start, f.Origin, 40);
        var twin = f.NewRoute(f.Origin, f.Start, 70);
        await db.SaveChangesAsync();
        var service = TestSupport.CreateRoutePlans(db);

        var notAnchored = await Assert.ThrowsAsync<UnprocessableEntityException>(() => service.CreateAsync(f.Coordinator.AccountId, f.Plan.TransportPlanId,
            Backup(Segment(1, "PRIMARY", completed, -3, -1), Segment(2, "BACKUP", stray, 0, 2))));
        var mismatch = await Assert.ThrowsAsync<UnprocessableEntityException>(() => service.CreateAsync(f.Coordinator.AccountId, f.Plan.TransportPlanId,
            Backup(Segment(1, "PRIMARY", twin, -3, -1), Segment(2, "BACKUP", f.Alternative, 0, 2))));
        var identical = await Assert.ThrowsAsync<ConflictException>(() => service.CreateAsync(f.Coordinator.AccountId, f.Plan.TransportPlanId,
            Backup(Segment(1, "PRIMARY", completed, -3, -1), Segment(2, "BACKUP", current, 0, 2))));

        Assert.Equal("BACKUP_NOT_ANCHORED", notAnchored.ErrorCode);
        Assert.Equal("BACKUP_SEGMENT_MISMATCH", mismatch.ErrorCode);
        Assert.Equal("IDENTICAL_CONTINGENCY_ROUTE", identical.ErrorCode);
    }

    [Fact]
    public async Task Rp4_KeepsEachSegmentsTypeWhenTheRequestOmitsIt()
    {
        await using var db = TestSupport.CreateDb();
        var f = await CreateFixtureAsync(db);
        var (completed, _) = await PrimaryRoutesAsync(db, f);
        var service = TestSupport.CreateRoutePlans(db);
        f.Transport.Status = TransportStatus.READY;
        await db.SaveChangesAsync();
        var created = await service.CreateAsync(f.Coordinator.AccountId, f.Plan.TransportPlanId, Backup(
            Segment(1, "PRIMARY", completed, -3, -1), Segment(2, "BACKUP", f.Alternative, 0, 2)));

        var updated = await service.UpdateAsync(f.Coordinator.AccountId, created.RoutePlanId, new()
        {
            LastUpdatedAt = created.UpdatedAt, UpdateReason = "Adjust the detour schedule", RouteNote = "Snow on the primary road near the safe stop",
            Segments = [Segment(1, null, completed, -3, -1), Segment(2, null, f.Alternative, 0, 3)]
        });

        Assert.Equal((2, f.Start.LocationId), (updated.BranchSequenceNo, updated.BranchLocation!.LocationId));
        var detail = await service.GetByIdAsync(f.Coordinator.AccountId, created.RoutePlanId);
        Assert.Equal(["PRIMARY", "BACKUP"], detail.Segments.Select(x => x.RouteType).ToArray());
    }

    private static async Task<RoutePlan> DraftPrimaryWithDraftBackupAsync(AppDbContext db, RouteChangeTests.Fixture f)
    {
        f.Plan.Status = RoutePlanStatus.DRAFT;
        f.Transport.MainDossier!.Status = ComplianceDossierStatus.DRAFT;
        await db.SaveChangesAsync();
        return await f.AlternateAsync(RouteType.BACKUP, RoutePlanStatus.DRAFT);
    }

    [Fact]
    public async Task Rp7_OnThePrimary_AlsoConfirmsEveryDraftBackupOfTheSameTransportPlan()
    {
        await using var db = TestSupport.CreateDb();
        var f = await CreateFixtureAsync(db);
        var backup = await DraftPrimaryWithDraftBackupAsync(db, f);
        var cancelled = await f.AlternateAsync(RouteType.BACKUP, RoutePlanStatus.CANCELLED);
        var vehicle = await db.Vehicles.SingleAsync();
        var specialist = await db.Employees.SingleAsync(x => x.Role == EmployeeRole.TRANSPORT_SPECIALIST);

        var result = await TestSupport.CreateRoutePlans(db).ReviewLegalAsync(specialist.AccountId, f.Plan.RoutePlanId,
            new() { LastUpdatedAt = RouteChangeTests.Fixture.Version(f.Plan), IsFeasible = true });

        Assert.Equal("CONFIRMED", result.Status);
        Assert.Equal([backup.RoutePlanId], result.ConfirmedBackupRoutePlanIds);
        Assert.Equal(RoutePlanStatus.CONFIRMED, backup.Status);
        Assert.Equal(RoutePlanStatus.CANCELLED, cancelled.Status);
        Assert.Equal(("MAIN", "IN_PREPARATION"), (result.ComplianceDossier!.DossierType, result.ComplianceDossier.Status));
        Assert.True(result.CustomerUploadEnabled);
        Assert.True(result.PersonnelAssignmentPhase2Enabled);
        Assert.Equal(VehicleStatus.ASSIGNED, vehicle.Status);
    }

    [Fact]
    public async Task Rp7_NotFeasible_ConfirmsNoBackup()
    {
        await using var db = TestSupport.CreateDb();
        var f = await CreateFixtureAsync(db);
        var backup = await DraftPrimaryWithDraftBackupAsync(db, f);
        var specialist = await db.Employees.SingleAsync(x => x.Role == EmployeeRole.TRANSPORT_SPECIALIST);

        var result = await TestSupport.CreateRoutePlans(db).ReviewLegalAsync(specialist.AccountId, f.Plan.RoutePlanId,
            new() { LastUpdatedAt = RouteChangeTests.Fixture.Version(f.Plan), IsFeasible = false, Note = "Permit missing" });

        Assert.Equal("DRAFT", result.Status);
        Assert.Empty(result.ConfirmedBackupRoutePlanIds);
        Assert.Equal(RoutePlanStatus.DRAFT, backup.Status);
        Assert.Equal(ComplianceDossierStatus.DRAFT, f.Transport.MainDossier!.Status);
    }

    [Fact]
    public async Task Rp7_OnABackupCreatedAfterThePrimary_NeedsNoDossierAndOpensNoUpload()
    {
        await using var db = TestSupport.CreateDb();
        var f = await CreateFixtureAsync(db);
        var backup = await f.AlternateAsync(RouteType.BACKUP, RoutePlanStatus.DRAFT);
        var dossier = f.Transport.MainDossier!;
        db.ComplianceDossiers.Remove(dossier);
        await db.SaveChangesAsync();
        var specialist = await db.Employees.SingleAsync(x => x.Role == EmployeeRole.TRANSPORT_SPECIALIST);

        var result = await TestSupport.CreateRoutePlans(db).ReviewLegalAsync(specialist.AccountId, backup.RoutePlanId,
            new() { LastUpdatedAt = RouteChangeTests.Fixture.Version(backup), IsFeasible = true });

        Assert.Equal("CONFIRMED", result.Status);
        Assert.Null(result.ComplianceDossier);
        Assert.False(result.CustomerUploadEnabled);
        Assert.False(result.PersonnelAssignmentPhase2Enabled);
        Assert.Empty(result.ConfirmedBackupRoutePlanIds);
        Assert.Empty(db.ComplianceDossiers);
    }

    [Fact]
    public async Task Rp7_OnAReturnWithoutExtraDossier_IsReportedAsNotApproved()
    {
        await using var db = TestSupport.CreateDb();
        var f = await CreateFixtureAsync(db);
        var returning = await f.AlternateAsync(RouteType.RETURN);
        var specialist = await db.Employees.SingleAsync(x => x.Role == EmployeeRole.TRANSPORT_SPECIALIST);

        var error = await Assert.ThrowsAsync<ConflictException>(() => TestSupport.CreateRoutePlans(db).ReviewLegalAsync(specialist.AccountId,
            returning.RoutePlanId, new() { LastUpdatedAt = RouteChangeTests.Fixture.Version(returning), IsFeasible = true }));

        Assert.Equal("RETURN_NOT_APPROVED", error.ErrorCode);
    }

    [Fact]
    public async Task Rp7_OnAReturn_TellsTheCustomerToUploadTheExtraDocuments()
    {
        await using var db = TestSupport.CreateDb();
        var f = await CreateFixtureAsync(db);
        var returning = await f.AlternateAsync(RouteType.RETURN);
        await f.AddExtraAsync(ComplianceDossierStatus.DRAFT);
        var specialist = await db.Employees.SingleAsync(x => x.Role == EmployeeRole.TRANSPORT_SPECIALIST);

        await TestSupport.CreateRoutePlans(db).ReviewLegalAsync(specialist.AccountId, returning.RoutePlanId,
            new() { LastUpdatedAt = RouteChangeTests.Fixture.Version(returning), IsFeasible = true });

        var customerAccount = f.Transport.Booking.Customer.AccountId;
        var notification = Assert.Single(await db.Notifications.ToListAsync(), item => item.Message.Contains("return route"));
        var recipients = await db.AccountNotifications.Where(item => item.NotificationId == notification.NotificationId).Select(item => item.AccountId).ToListAsync();
        Assert.Equal([customerAccount], recipients);
    }

    [Fact]
    public async Task Ec2_ApprovingAReturn_CreatesAnExtraDossierThatInheritsOnlyTheLiveRequirements()
    {
        await using var db = TestSupport.CreateDb();
        var f = await CreateFixtureAsync(db);
        var returning = await f.AlternateAsync(RouteType.RETURN);
        var main = f.Transport.MainDossier!;
        main.DossierTemplateId = Guid.NewGuid();
        var (requirementId, horseId) = (Guid.NewGuid(), Guid.NewGuid());
        DossierRequirement Requirement(bool deleted, Guid? horse) => new()
        {
            DossierRequirementId = Guid.NewGuid(), ComplianceDossierId = main.ComplianceDossierId, ComplianceRequirementId = requirementId,
            HorseId = horse, IsMandatory = true, Instruction = "Upload a fresh copy", IsDeleted = deleted,
            Status = DossierRequirementStatus.SATISFIED, CreatedAt = main.CreatedAt, UpdatedAt = main.UpdatedAt
        };
        db.DossierRequirements.AddRange(Requirement(false, horseId), Requirement(true, null));
        await db.SaveChangesAsync();

        var decision = await f.Service.DecideAsync(returning.RoutePlanId, f.Manager.AccountId,
            new() { Decision = "APPROVE", LastUpdatedAt = RouteChangeTests.Fixture.Version(returning) });

        Assert.Equal("DRAFT", decision.Status);
        var extra = await db.ComplianceDossiers.SingleAsync(x => x.DossierType == DossierType.EXTRA);
        Assert.Equal((extra.ComplianceDossierId, "EXTRA", "DRAFT", 1),
            (decision.ComplianceDossier!.ComplianceDossierId, decision.ComplianceDossier.DossierType, decision.ComplianceDossier.Status, decision.ComplianceDossier.InheritedRequirementCount));
        Assert.Equal((main.TransportId, main.DossierTemplateId, "Emergency route planning"), (extra.TransportId, extra.DossierTemplateId, extra.Note));
        var inherited = await db.DossierRequirements.SingleAsync(x => x.ComplianceDossierId == extra.ComplianceDossierId);
        Assert.Equal((requirementId, horseId, DossierRequirementStatus.PENDING, true, "Upload a fresh copy"),
            (inherited.ComplianceRequirementId, inherited.HorseId, inherited.Status, inherited.IsMandatory, inherited.Instruction));
        Assert.Equal(ComplianceDossierStatus.APPROVED, main.Status);
        var audit = Assert.Single(db.AuditDossiers);
        Assert.Equal((extra.ComplianceDossierId, "EXTRA_DOSSIER_CREATED"), (audit.ComplianceDossierId, audit.ActionType));

        var twice = await Assert.ThrowsAsync<ConflictException>(() => f.Service.DecideAsync(returning.RoutePlanId, f.Manager.AccountId,
            new() { Decision = "APPROVE", LastUpdatedAt = decision.UpdatedAt }));
        Assert.Equal("EXTRA_DOSSIER_ALREADY_EXISTS", twice.ErrorCode);
    }

    [Fact]
    public async Task Ec3_ActivatingAReturn_NeedsAnApprovedExtraDossier()
    {
        await using var db = TestSupport.CreateDb();
        var f = await CreateFixtureAsync(db);
        var returning = await f.AlternateAsync(RouteType.RETURN, RoutePlanStatus.CONFIRMED);

        var missing = await Assert.ThrowsAsync<UnprocessableEntityException>(() => f.Service.ActivateReturnAsync(returning.RoutePlanId, f.Manager.AccountId,
            new() { LastUpdatedAt = RouteChangeTests.Fixture.Version(returning) }));
        Assert.Equal("RETURN_DOSSIER_NOT_APPROVED", missing.ErrorCode);

        await f.AddExtraAsync(ComplianceDossierStatus.IN_PREPARATION);
        var pending = await Assert.ThrowsAsync<UnprocessableEntityException>(() => f.Service.ActivateReturnAsync(returning.RoutePlanId, f.Manager.AccountId,
            new() { LastUpdatedAt = RouteChangeTests.Fixture.Version(returning) }));
        Assert.Equal("RETURN_DOSSIER_NOT_APPROVED", pending.ErrorCode);
    }

    [Fact]
    public async Task Ec1_AHybridBackupIsComparedOnlyOverTheSegmentsItReplaces()
    {
        await using var db = TestSupport.CreateDb();
        var f = await CreateFixtureAsync(db);
        f.Alternative.DistanceKm = 160;
        var backup = await f.AlternateAsync();

        var created = await f.Service.CreateAsync(backup.RoutePlanId, f.Coordinator.AccountId, f.Proposal(0));

        Assert.Equal(("BACKUP", "CONFIRMED"), (created.RouteType, created.Status));
        Assert.Contains("Emergency backup activation requested", (await db.Notifications.ToListAsync()).Select(x => x.Title));
    }

    [Fact]
    public async Task Ec1_FailsWhenEveryPrimarySegmentHasAlreadyDeparted()
    {
        await using var db = TestSupport.CreateDb();
        var f = await CreateFixtureAsync(db);
        f.Current.ActualDepartureAt = f.Current.PlannedDepartureAt;
        await db.SaveChangesAsync();
        var backup = await f.AlternateAsync();

        var error = await Assert.ThrowsAsync<ConflictException>(() => f.Service.CreateAsync(backup.RoutePlanId, f.Coordinator.AccountId, f.Proposal()));

        Assert.Equal("SEGMENT_LOCKED", error.ErrorCode);
    }
}
