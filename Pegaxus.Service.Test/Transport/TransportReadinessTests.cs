using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;

using DossierRequest = Pegaxus.Service.ComplianceDossierService.Request;

namespace Pegaxus.Service.Test;

/// <summary>
/// A transport is READY only with an approved MAIN dossier, a confirmed primary and at least one confirmed backup (none in draft),
/// and a driver and an escort on every internal primary segment; whichever of these comes last moves it to READY.
/// </summary>
public class TransportReadinessTests
{
    private static readonly string Dgal = CompetentAuthorities.All[5].Name;
    private static readonly string Bmel = CompetentAuthorities.All[7].Name;

    private sealed record Trip(Transport Transport, RoutePlan Primary, RoutePlanRoute Segment, RoutePlan Backup, ComplianceDossier Dossier);

    /// <summary>A trip that meets every condition; each test takes one away.</summary>
    private static async Task<Trip> TripAsync(AppDbContext db)
    {
        var (primary, segment) = await PersonnelAssignmentTests.RouteAsync(db);
        var transport = primary.TransportPlan.Transport;
        transport.Status = TransportStatus.PLANNING;
        segment.RouteType = RouteType.PRIMARY;
        segment.DriverEmployeeId = (await PersonnelAssignmentTests.EmployeeAsync(db, EmployeeRole.DRIVER)).EmployeeId;
        segment.EscortEmployeeId = (await PersonnelAssignmentTests.EmployeeAsync(db, EmployeeRole.ESCORT)).EmployeeId;
        var backup = new RoutePlan { RoutePlanId = Guid.NewGuid(), RoutePlanCode = "RP-B-" + Guid.NewGuid().ToString("N"), TransportPlan = primary.TransportPlan,
            Status = RoutePlanStatus.CONFIRMED, CreatedAt = PersonnelAssignmentTests.Now, UpdatedAt = PersonnelAssignmentTests.Now };
        var detour = PersonnelAssignmentTests.Segment(backup, 1);
        detour.RouteType = RouteType.BACKUP;
        var dossier = new ComplianceDossier { ComplianceDossierId = Guid.NewGuid(), DossierCode = "CD-" + Guid.NewGuid().ToString("N")[..8],
            TransportId = transport.TransportId, DossierType = DossierType.MAIN, Status = ComplianceDossierStatus.APPROVED,
            OpenedAt = PersonnelAssignmentTests.Now, CreatedAt = PersonnelAssignmentTests.Now, UpdatedAt = PersonnelAssignmentTests.Now };
        db.AddRange(detour, dossier);
        await db.SaveChangesAsync();
        return new(transport, primary, segment, backup, dossier);
    }

    private static Task SyncAsync(AppDbContext db, Trip trip) => TransportReadiness.SyncAsync(db, trip.Transport.TransportId, PersonnelAssignmentTests.Now, CancellationToken.None);

    [Fact]
    public async Task Sync_EveryConditionMet_MakesTheTransportReady()
    {
        await using var db = TestSupport.CreateDb();
        var trip = await TripAsync(db);
        await SyncAsync(db, trip);
        Assert.Equal(TransportStatus.READY, trip.Transport.Status);
    }

    [Theory]
    [InlineData("dossier")]
    [InlineData("no backup")]
    [InlineData("draft backup")]
    [InlineData("crew")]
    [InlineData("draft primary")]
    public async Task Sync_AnyConditionMissing_KeepsPlanning_AndTakesReadyAwayBeforePickup(string missing)
    {
        await using var db = TestSupport.CreateDb();
        var trip = await TripAsync(db);
        switch (missing)
        {
            case "dossier": trip.Dossier.Status = ComplianceDossierStatus.IN_PREPARATION; break;
            case "no backup": trip.Backup.Status = RoutePlanStatus.CANCELLED; break;
            case "draft backup": trip.Backup.Status = RoutePlanStatus.DRAFT; break;
            case "crew": trip.Segment.EscortEmployeeId = null; break;
            case "draft primary": trip.Primary.Status = RoutePlanStatus.DRAFT; break;
        }
        await db.SaveChangesAsync();

        await SyncAsync(db, trip);
        Assert.Equal(TransportStatus.PLANNING, trip.Transport.Status);

        trip.Transport.Status = TransportStatus.READY;
        await SyncAsync(db, trip);
        Assert.Equal(TransportStatus.PLANNING, trip.Transport.Status);

        // Once the horses are picked up the status is the journey's, not the planning's.
        trip.Transport.Status = TransportStatus.IN_TRANSIT;
        await SyncAsync(db, trip);
        Assert.Equal(TransportStatus.IN_TRANSIT, trip.Transport.Status);
    }

    [Fact]
    public async Task Sync_AnOutsourcedPrimarySegment_NeedsNoCrew_AndBackupCrewIsNotRequired()
    {
        await using var db = TestSupport.CreateDb();
        var trip = await TripAsync(db);
        var provider = PersonnelAssignmentTests.Provider();
        db.Add(provider);
        var outsourced = PersonnelAssignmentTests.Segment(trip.Primary, 2);
        outsourced.TransportProviderId = provider.TransportProviderId;
        db.Add(outsourced);
        await db.SaveChangesAsync();

        await SyncAsync(db, trip);
        Assert.Equal(TransportStatus.READY, trip.Transport.Status);
    }

    [Fact]
    public async Task Assign_CrewAsTheLastCondition_MakesReady_ButCrewAloneDoesNot()
    {
        await using var db = TestSupport.CreateDb();
        var trip = await TripAsync(db);
        trip.Segment.DriverEmployeeId = trip.Segment.EscortEmployeeId = null;
        trip.Dossier.Status = ComplianceDossierStatus.IN_PREPARATION;
        await db.SaveChangesAsync();
        var manager = await PersonnelAssignmentTests.EmployeeAsync(db, EmployeeRole.LOGISTICS_MANAGER);
        var driver = await PersonnelAssignmentTests.EmployeeAsync(db, EmployeeRole.DRIVER);
        var escort = await PersonnelAssignmentTests.EmployeeAsync(db, EmployeeRole.ESCORT);
        var service = PersonnelAssignmentTests.CreateService(db);

        await service.AssignAsync(trip.Primary.RoutePlanId, PersonnelAssignmentTests.Assign(trip.Primary, trip.Segment, driver, escort), manager.AccountId);
        Assert.Equal(TransportStatus.PLANNING, (await db.Transports.AsNoTracking().SingleAsync()).Status);

        // Same assignment on a trip whose dossier is approved: the crew is the last condition.
        await using var other = TestSupport.CreateDb();
        var ready = await TripAsync(other);
        ready.Segment.DriverEmployeeId = ready.Segment.EscortEmployeeId = null;
        await other.SaveChangesAsync();
        var otherManager = await PersonnelAssignmentTests.EmployeeAsync(other, EmployeeRole.LOGISTICS_MANAGER);
        await PersonnelAssignmentTests.CreateService(other).AssignAsync(ready.Primary.RoutePlanId, PersonnelAssignmentTests.Assign(ready.Primary, ready.Segment,
            await PersonnelAssignmentTests.EmployeeAsync(other, EmployeeRole.DRIVER), await PersonnelAssignmentTests.EmployeeAsync(other, EmployeeRole.ESCORT)), otherManager.AccountId);
        Assert.Equal(TransportStatus.READY, (await other.Transports.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task AuthorityApproval_AsTheLastCondition_MakesTheTransportReady()
    {
        await using var db = TestSupport.CreateDb();
        var (specialist, dossierId) = await AuthorityPresignedFileTests.SeedAsync(db);
        var dossier = await db.ComplianceDossiers.SingleAsync(item => item.ComplianceDossierId == dossierId);
        var (primary, segment) = await PersonnelAssignmentTests.RouteAsync(db);
        // Move the confirmed, crewed plans onto the dossier's transport.
        primary.TransportPlan.TransportId = dossier.TransportId;
        segment.DriverEmployeeId = (await PersonnelAssignmentTests.EmployeeAsync(db, EmployeeRole.DRIVER)).EmployeeId;
        segment.EscortEmployeeId = (await PersonnelAssignmentTests.EmployeeAsync(db, EmployeeRole.ESCORT)).EmployeeId;
        var backup = new RoutePlan { RoutePlanId = Guid.NewGuid(), RoutePlanCode = "RP-B-AUTH", TransportPlan = primary.TransportPlan,
            Status = RoutePlanStatus.CONFIRMED, CreatedAt = PersonnelAssignmentTests.Now, UpdatedAt = PersonnelAssignmentTests.Now };
        var detour = PersonnelAssignmentTests.Segment(backup, 1);
        detour.RouteType = RouteType.BACKUP;
        db.Add(detour);
        var transport = await db.Transports.SingleAsync(item => item.TransportId == dossier.TransportId);
        transport.Status = TransportStatus.PLANNING;
        await db.SaveChangesAsync();
        var service = DocumentUploadTests.CreateService(db, new DocumentUploadTests.RecordingStorage());

        var submission = await service.RecordAuthoritySubmissionAsync(specialist, dossierId, Submission(Dgal, true));
        Assert.Equal(TransportStatus.PLANNING, (await db.Transports.AsNoTracking().SingleAsync(item => item.TransportId == transport.TransportId)).Status);
        var decided = await service.RecordAuthorityDecisionAsync(specialist, submission.AuthoritySubmissionId, Approve());

        Assert.Equal("APPROVED", decided.DossierStatus);
        Assert.Equal(TransportStatus.READY, (await db.Transports.AsNoTracking().SingleAsync(item => item.TransportId == transport.TransportId)).Status);
    }

    [Fact]
    public async Task Returned_ANonRequiredSubmission_DoesNotPullAnApprovedDossierBack()
    {
        await using var db = TestSupport.CreateDb();
        var (specialist, dossierId) = await AuthorityPresignedFileTests.SeedAsync(db);
        var service = DocumentUploadTests.CreateService(db, new DocumentUploadTests.RecordingStorage());
        var required = await service.RecordAuthoritySubmissionAsync(specialist, dossierId, Submission(Dgal, true));
        var optional = await service.RecordAuthoritySubmissionAsync(specialist, dossierId, Submission(Bmel, false));
        Assert.Equal("APPROVED", (await service.RecordAuthorityDecisionAsync(specialist, required.AuthoritySubmissionId, Approve())).DossierStatus);

        var returned = await service.RecordAuthorityDecisionAsync(specialist, optional.AuthoritySubmissionId, Return());

        Assert.Equal("APPROVED", returned.DossierStatus);
    }

    [Fact]
    public async Task Returned_ARequiredSubmission_StillSendsAPendingDossierBackToPreparation()
    {
        await using var db = TestSupport.CreateDb();
        var (specialist, dossierId) = await AuthorityPresignedFileTests.SeedAsync(db);
        var service = DocumentUploadTests.CreateService(db, new DocumentUploadTests.RecordingStorage());
        var optional = await service.RecordAuthoritySubmissionAsync(specialist, dossierId, Submission(Bmel, false));
        var required = await service.RecordAuthoritySubmissionAsync(specialist, dossierId, Submission(Dgal, true));

        Assert.NotEqual("IN_PREPARATION", (await service.RecordAuthorityDecisionAsync(specialist, optional.AuthoritySubmissionId, Return())).DossierStatus);
        Assert.Equal("IN_PREPARATION", (await service.RecordAuthorityDecisionAsync(specialist, required.AuthoritySubmissionId, Return())).DossierStatus);
    }

    [Fact]
    public async Task Approval_AnAuthorityOnceRequired_StaysRequired_WhenItsResubmissionIsNotTicked()
    {
        await using var db = TestSupport.CreateDb();
        var (specialist, dossierId) = await AuthorityPresignedFileTests.SeedAsync(db);
        var service = DocumentUploadTests.CreateService(db, new DocumentUploadTests.RecordingStorage());
        var france = await service.RecordAuthoritySubmissionAsync(specialist, dossierId, Submission(Dgal, true));
        var germany = await service.RecordAuthoritySubmissionAsync(specialist, dossierId, Submission(Bmel, true));
        await service.RecordAuthorityDecisionAsync(specialist, germany.AuthoritySubmissionId, Reject());
        await AuthorityDecisionRulesTests.ReviseRequirementAsync(db, dossierId);
        await service.MarkReadyForSubmissionAsync(specialist, dossierId);
        // The resubmission to the German authority forgets the "required" tick.
        var again = await service.RecordAuthoritySubmissionAsync(specialist, dossierId, Submission(Bmel, false));

        Assert.Equal("PENDING", (await service.RecordAuthorityDecisionAsync(specialist, france.AuthoritySubmissionId, Approve())).DossierStatus);
        Assert.Equal("APPROVED", (await service.RecordAuthorityDecisionAsync(specialist, again.AuthoritySubmissionId, Approve())).DossierStatus);
    }

    [Fact]
    public async Task CreateBackup_IsRefusedOnceTheTransportIsReady()
    {
        await using var db = TestSupport.CreateDb();
        var f = await RouteChangeTests.Fixture.CreateAsync(db);
        var completed = await db.Routes.SingleAsync(item => item.RouteId == f.Completed.RouteId);
        f.Transport.Status = TransportStatus.READY;
        await db.SaveChangesAsync();

        var refused = await Assert.ThrowsAsync<ConflictException>(() => TestSupport.CreateRoutePlans(db).CreateAsync(f.Coordinator.AccountId, f.Plan.TransportPlanId, new()
        {
            RouteType = "BACKUP", Action = "SAVE_DRAFT", RouteNote = "Snow on the primary road near the safe stop",
            Segments = [
                new() { SequenceNo = 1, RouteType = "PRIMARY", RouteId = completed.RouteId,
                    PlannedDepartureAt = RouteChangeTests.Fixture.StartAt.AddHours(-3), PlannedArrivalAt = RouteChangeTests.Fixture.StartAt.AddHours(-1) },
                new() { SequenceNo = 2, RouteType = "BACKUP", RouteId = f.Alternative.RouteId,
                    PlannedDepartureAt = RouteChangeTests.Fixture.StartAt, PlannedArrivalAt = RouteChangeTests.Fixture.StartAt.AddHours(2) }]
        }));

        Assert.Equal("TRANSPORT_ALREADY_READY", refused.ErrorCode);
    }

    private static DossierRequest.RecordAuthoritySubmissionRequest Submission(string authority, bool required) => new()
    {
        AuthorityName = authority, SubmissionReference = "REF-" + Guid.NewGuid().ToString("N")[..8], SubmittedAt = DateTimeOffset.UtcNow,
        IsRequired = required, ProofFile = DocumentUploadTests.File("proof.pdf", "application/pdf")
    };

    private static DossierRequest.RecordAuthorityDecisionRequest Approve() =>
        new() { Decision = "APPROVED", PermitNumber = "PERMIT-" + Guid.NewGuid().ToString("N")[..6], DecisionFile = DocumentUploadTests.File("permit.pdf", "application/pdf") };

    private static DossierRequest.RecordAuthorityDecisionRequest Reject() =>
        new() { Decision = "REJECTED", RejectionReason = "Missing vaccination record", DecisionFile = DocumentUploadTests.File("rejection.pdf", "application/pdf") };

    private static DossierRequest.RecordAuthorityDecisionRequest Return() =>
        new() { Decision = "RETURNED", RevisionDueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
            DecisionNote = "Please add the stamped copy", DecisionFile = DocumentUploadTests.File("returned.pdf", "application/pdf") };
}
