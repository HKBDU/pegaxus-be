using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.NotificationService;
using Pegaxus.Service.RouteChangeService;
using Pegaxus.Service.RouteChangeService.Validator;
using RouteChange = Pegaxus.Service.RouteChangeService;
using Request = Pegaxus.Service.RouteChangeService.Request;

namespace Pegaxus.Service.Test;

public class RouteChangeTests
{
    [Theory]
    [InlineData(15, 20, false)]
    [InlineData(49.99, 59, false)]
    [InlineData(50, 0, true)]
    [InlineData(0, 60, true)]
    public async Task BR036_UsesStrictExemptionBoundaries(decimal extraDistance, int delay, bool needsApproval)
    {
        await using var db = TestSupport.CreateDb();
        var fixture = await Fixture.CreateAsync(db);
        fixture.Alternative.DistanceKm = 100 + extraDistance;
        await db.SaveChangesAsync();
        var request = fixture.Proposal(delay);
        if (needsApproval)
        {
            var created = await fixture.Service.CreateAsync(fixture.Plan.RoutePlanId, fixture.Coordinator.AccountId, request);
            Assert.True(created.RequiresApproval);
            Assert.Contains(extraDistance >= 50 ? "DEVIATION_OVER_50KM" : "DELAY_OVER_1H", created.ApprovalReasons);
        }
        else
        {
            var error = await Assert.ThrowsAsync<UnprocessableEntityException>(() => fixture.Service.CreateAsync(fixture.Plan.RoutePlanId, fixture.Coordinator.AccountId, request));
            Assert.Equal("APPROVAL_NOT_REQUIRED", error.ErrorCode);
            Assert.Empty(db.RouteChangeRequests);
            Assert.Empty(db.Notifications);
        }
    }

    [Fact]
    public async Task AC001_ApproveChangedBcpAnd24hRest_PreservesHistoryCostsNotificationsAndRetry()
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        var oldBcp = f.Alternative.ToLocation;
        oldBcp.LocationType = LocationType.BORDER_GATE;
        var newBcp = new Location { LocationId = Guid.NewGuid(), LocationName = "Dunkirk", Country = "FR", LocationType = LocationType.BORDER_GATE };
        db.Locations.Add(newBcp);
        var toBcp = f.NewRoute(f.Start, newBcp, 80);
        var fromBcp = f.NewRoute(newBcp, f.End, 20);
        await db.SaveChangesAsync();
        var proposal = f.Proposal(1500);
        proposal.ReplacementSegments = [f.Segment(toBcp, 1, Fixture.StartAt, Fixture.StartAt.AddHours(1)),
            f.Segment(fromBcp, 2, Fixture.StartAt.AddHours(25), Fixture.StartAt.AddHours(27))];
        proposal.NewEstimatedArrivalAt = Fixture.StartAt.AddHours(27);
        var created = await f.Service.CreateAsync(f.Plan.RoutePlanId, f.Coordinator.AccountId, proposal);
        Assert.Contains("BCP_CHANGED", created.ApprovalReasons);
        Assert.Contains("ADDITIONAL_24H_REST", created.ApprovalReasons);
        var detail = await f.Service.GetAsync(created.ChangeRequestId, f.Manager.AccountId);
        Assert.True(detail.Comparison.BcpChanged);
        Assert.True(detail.Comparison.Additional24hRest);
        var decision = new Request.DecisionRequest { Decision = "APPROVE", LastUpdatedAt = detail.UpdatedAt };
        var approved = await f.Service.DecideAsync(created.ChangeRequestId, f.Manager.AccountId, decision);
        Assert.Equal("APPROVED", approved.Status);
        Assert.Equal(new[] { "CUSTOMER", "DRIVER", "TRANSPORT_SPECIALIST" }, approved.Notified);
        Assert.Equal(2, approved.NewSegmentIds.Length);
        Assert.Equal(f.Current.RoutePlanRouteId, Assert.Single(approved.SupersededSegmentIds));
        Assert.NotNull(f.Current.SupersededAt);
        Assert.Equal(4, await db.RoutePlanRoutes.CountAsync());
        Assert.Equal(1800, Assert.Single(db.EmergencyCostRequests).RequestedAmount);
        Assert.Equal(EmergencyCostStatus.PENDING, Assert.Single(db.EmergencyCostRequests).Status);
        Assert.Equal(f.Transport.TransportId, Assert.Single(db.IncidentReports).TransportId);
        Assert.Equal(proposal.NewEstimatedArrivalAt.UtcDateTime, f.Plan.TransportPlan.PlannedEndAt);
        var notifications = await db.Notifications.CountAsync();
        var outbox = await db.OutboxMessages.CountAsync();
        var retry = await f.Service.DecideAsync(created.ChangeRequestId, f.Manager.AccountId, decision);
        Assert.Equal(approved.NewSegmentIds, retry.NewSegmentIds);
        Assert.Equal(notifications, await db.Notifications.CountAsync());
        Assert.Equal(outbox, await db.OutboxMessages.CountAsync());
        Assert.Single(db.IncidentReports);
        var changed = new Request.DecisionRequest { Decision = "REJECT", LastUpdatedAt = detail.UpdatedAt, Note = "Different instructions" };
        Assert.Equal("CHANGE_REQUEST_ALREADY_DECIDED", (await Assert.ThrowsAsync<ConflictException>(() =>
            f.Service.DecideAsync(created.ChangeRequestId, f.Manager.AccountId, changed))).ErrorCode);
    }

    [Fact]
    public async Task Reject_KeepsRouteAndNotifiesCoordinator()
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        var created = await f.Service.CreateAsync(f.Plan.RoutePlanId, f.Coordinator.AccountId, f.Proposal());
        var result = await f.Service.DecideAsync(created.ChangeRequestId, f.Manager.AccountId,
            new() { Decision = "REJECT", LastUpdatedAt = created.CreatedAt, Note = "Choose a safer itinerary" });
        Assert.Equal("REJECTED", result.Status);
        Assert.Null(f.Current.SupersededAt);
        Assert.Empty(db.IncidentReports);
        Assert.Equal(2, await db.RoutePlanRoutes.CountAsync());
        Assert.Contains(db.AccountNotifications, x => x.AccountId == f.Coordinator.AccountId);
        Assert.Single((await f.Service.ListAsync(f.Coordinator.AccountId, new() { Status = "REJECTED", TransportPlanId = f.Plan.TransportPlanId })).Items);
    }

    [Fact]
    public async Task EmergencyReturn_CancelsOnlyUntravelledSegmentsAndTriggersSettlement()
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        var created = await f.Service.CreateAsync(f.Plan.RoutePlanId, f.Coordinator.AccountId, f.Proposal());
        var back = f.NewRoute(f.Start, f.Origin, 70);
        await db.SaveChangesAsync();
        var result = await f.Service.DecideAsync(created.ChangeRequestId, f.Manager.AccountId,
            new() { Decision = "EMERGENCY_RETURN", LastUpdatedAt = created.CreatedAt, Note = "No safe outbound route remains",
                ReturnSegments = [f.Segment(back, 1, Fixture.StartAt, Fixture.StartAt.AddHours(2))] });
        Assert.True(result.IncidentSettlementTriggered);
        Assert.NotEmpty(result.ReturnSegmentIds);
        Assert.Equal(f.Current.RoutePlanRouteId, Assert.Single(result.CancelledSegmentIds));
        Assert.Contains(db.RoutePlanRoutes, x => x.RouteType == RouteType.RETURN && x.Route.ToLocationId == f.Origin.LocationId);
        Assert.True(Assert.Single(db.IncidentReports).EmergencySettlementRequired);
        Assert.Null(f.Completed.SupersededAt);
    }

    [Fact]
    public async Task Backup_MustBeConfirmedAndLinkedToSamePrimary()
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        var backup = new RoutePlan { RoutePlanId = Guid.NewGuid(), TransportPlanId = f.Plan.TransportPlanId,
            RoutePlanCode = "BACKUP", Status = RoutePlanStatus.DRAFT, PrimaryRoutePlanId = f.Plan.RoutePlanId };
        db.Add(backup);
        db.Add(new RoutePlanRoute { RoutePlanRouteId = Guid.NewGuid(), RoutePlanId = backup.RoutePlanId, RouteId = f.Alternative.RouteId,
            SequenceNo = 1, RouteType = RouteType.BACKUP, DriverEmployeeId = f.Driver.EmployeeId, EscortEmployeeId = f.Escort.EmployeeId,
            PlannedDepartureAt = Fixture.StartAt.UtcDateTime, PlannedArrivalAt = Fixture.StartAt.AddHours(3).UtcDateTime });
        await db.SaveChangesAsync();
        var request = f.Proposal();
        request.ReplacementSegments = null;
        request.BackupRoutePlanId = backup.RoutePlanId;
        Assert.Equal("BACKUP_NOT_APPROVED", (await Assert.ThrowsAsync<ConflictException>(() =>
            f.Service.CreateAsync(f.Plan.RoutePlanId, f.Coordinator.AccountId, request))).ErrorCode);
        backup.Status = RoutePlanStatus.CONFIRMED;
        await db.SaveChangesAsync();
        var created = await f.Service.CreateAsync(f.Plan.RoutePlanId, f.Coordinator.AccountId, request);
        Assert.Equal(backup.RoutePlanId, (await f.Service.GetAsync(created.ChangeRequestId, f.Manager.AccountId)).BackupRoutePlanId);
    }

    [Fact]
    public async Task Pending_StaleDecision_AndProgressAreGuarded()
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        var created = await f.Service.CreateAsync(f.Plan.RoutePlanId, f.Coordinator.AccountId, f.Proposal());
        Assert.Equal("CHANGE_REQUEST_PENDING_EXISTS", (await Assert.ThrowsAsync<ConflictException>(() =>
            f.Service.CreateAsync(f.Plan.RoutePlanId, f.Coordinator.AccountId, f.Proposal()))).ErrorCode);
        Assert.Equal("CHANGE_REQUEST_MODIFIED", (await Assert.ThrowsAsync<ConflictException>(() =>
            f.Service.DecideAsync(created.ChangeRequestId, f.Manager.AccountId, new() { Decision = "APPROVE", LastUpdatedAt = created.CreatedAt.AddSeconds(1) }))).ErrorCode);
        f.Current.ActualDepartureAt = Fixture.StartAt.UtcDateTime;
        await db.SaveChangesAsync();
        Assert.Equal("ROUTE_PROGRESS_CHANGED", (await Assert.ThrowsAsync<ConflictException>(() =>
            f.Service.DecideAsync(created.ChangeRequestId, f.Manager.AccountId, new() { Decision = "APPROVE", LastUpdatedAt = created.CreatedAt }))).ErrorCode);
        Assert.Null(f.Current.SupersededAt);
    }

    [Theory]
    [InlineData(false, "PROVIDER_NOT_ACTIVE")]
    [InlineData(true, "PROVIDER_MODE_MISMATCH")]
    public async Task Provider_ValidatesActivityAndMode(bool active, string expected)
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        var provider = new TransportProvider { TransportProviderId = Guid.NewGuid(), ProviderCode = "P1", ProviderName = "Provider",
            Status = active ? ProviderStatus.ACTIVE : ProviderStatus.INACTIVE, SupportedModes = "AIR" };
        db.Add(provider);
        await db.SaveChangesAsync();
        var request = f.Proposal();
        request.ReplacementSegments![0].Outsourcing = new() { TransportProviderId = provider.TransportProviderId, ServiceType = "HAULAGE" };
        Assert.Equal(expected, (await Assert.ThrowsAsync<ConflictException>(() =>
            f.Service.CreateAsync(f.Plan.RoutePlanId, f.Coordinator.AccountId, request))).ErrorCode);
    }

    [Fact]
    public void Validators_AreSeparateAndRejectAmbiguousUnsafeRequests()
    {
        Assert.False(new CreateRequestValidator().Validate(new Request.CreateRequest()).IsValid);
        Assert.False(new DecisionRequestValidator().Validate(new Request.DecisionRequest { Decision = "REJECT", LastUpdatedAt = Fixture.StartAt, Note = "short" }).IsValid);
        Assert.False(new ListRequestValidator().Validate(new Request.ListRequest { PageIndex = int.MaxValue, PageSize = 100 }).IsValid);
        Assert.False(new SegmentRequestValidator().Validate(new Request.SegmentRequest { SequenceNo = 1, RouteId = Guid.NewGuid(),
            FromLocationId = Guid.NewGuid(), PlannedDepartureAt = Fixture.StartAt, PlannedArrivalAt = Fixture.StartAt.AddHours(1) }).IsValid);
        Assert.False(new CreateRequestValidator().Validate(new Request.CreateRequest { Reason = "Road closed", ReplacementSegments = [null!],
            NewEstimatedArrivalAt = Fixture.StartAt }).IsValid);
    }

    [Fact]
    public async Task Approval_RechecksProviderAndVehicleAvailability()
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        var created = await f.Service.CreateAsync(f.Plan.RoutePlanId, f.Coordinator.AccountId, f.Proposal());
        var vehicle = await db.Vehicles.SingleAsync();
        vehicle.Status = VehicleStatus.MAINTENANCE;
        await db.SaveChangesAsync();
        Assert.Equal("VEHICLE_UNAVAILABLE", (await Assert.ThrowsAsync<ConflictException>(() =>
            f.Service.DecideAsync(created.ChangeRequestId, f.Manager.AccountId, new() { Decision = "APPROVE", LastUpdatedAt = created.CreatedAt }))).ErrorCode);
        Assert.Null(f.Current.SupersededAt);
        Assert.Empty(db.IncidentReports);
    }

    [Fact]
    public async Task CreateRetry_ReplaysEvenAfterDecision_WithoutDuplicatingNotifications()
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        var request = f.Proposal();
        request.ClientRequestId = Guid.NewGuid();
        var created = await f.Service.CreateAsync(f.Plan.RoutePlanId, f.Coordinator.AccountId, request);
        await f.Service.DecideAsync(created.ChangeRequestId, f.Manager.AccountId,
            new() { Decision = "REJECT", LastUpdatedAt = created.CreatedAt, Note = "Choose another safe itinerary" });
        var retry = await f.Service.CreateAsync(f.Plan.RoutePlanId, f.Coordinator.AccountId, request);
        Assert.Equal(created.ChangeRequestId, retry.ChangeRequestId);
        Assert.Equal(created.Status, retry.Status);
        Assert.Equal(created.CreatedAt, retry.CreatedAt);
        Assert.Equal(created.ApprovalReasons, retry.ApprovalReasons);
        Assert.Single(db.RouteChangeRequests);
        Assert.Equal(2, await db.Notifications.CountAsync());
        request.Reason = "Different incident";
        Assert.Equal("IDEMPOTENCY_KEY_REUSED", (await Assert.ThrowsAsync<ConflictException>(() =>
            f.Service.CreateAsync(f.Plan.RoutePlanId, f.Coordinator.AccountId, request))).ErrorCode);
    }

    [Fact]
    public async Task DecisionRequiresTransit_AndReturnRequiresSafeApprovedItinerary()
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        var created = await f.Service.CreateAsync(f.Plan.RoutePlanId, f.Coordinator.AccountId, f.Proposal());
        var request = new Request.DecisionRequest { Decision = "EMERGENCY_RETURN", LastUpdatedAt = created.CreatedAt, Note = "All outbound routes are unsafe" };
        Assert.Equal("SAFE_RETURN_ROUTE_REQUIRED", (await Assert.ThrowsAsync<ConflictException>(() =>
            f.Service.DecideAsync(created.ChangeRequestId, f.Manager.AccountId, request))).ErrorCode);
        Assert.Null(f.Current.SupersededAt);
        f.Transport.Status = TransportStatus.COMPLETED;
        await db.SaveChangesAsync();
        Assert.Equal("TRANSPORT_NOT_IN_TRANSIT", (await Assert.ThrowsAsync<ConflictException>(() =>
            f.Service.DecideAsync(created.ChangeRequestId, f.Manager.AccountId, request))).ErrorCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResourceConflict_IsRejectedBeforeRouteChanges(bool runningLate)
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        var created = await f.Service.CreateAsync(f.Plan.RoutePlanId, f.Coordinator.AccountId, f.Proposal());
        var otherPlan = new RoutePlan { RoutePlanId = Guid.NewGuid(), TransportPlanId = f.Plan.TransportPlanId, RoutePlanCode = "OTHER",
            Status = runningLate ? RoutePlanStatus.ACTIVE : RoutePlanStatus.CONFIRMED };
        db.AddRange(otherPlan, new RoutePlanRoute { RoutePlanRouteId = Guid.NewGuid(), RoutePlanId = otherPlan.RoutePlanId,
            RouteId = f.Alternative.RouteId, DriverEmployeeId = f.Driver.EmployeeId, SequenceNo = 1,
            ActualDepartureAt = runningLate ? DateTime.SpecifyKind(Fixture.StartAt.AddHours(-4).UtcDateTime, DateTimeKind.Unspecified) : null,
            PlannedDepartureAt = DateTime.SpecifyKind(Fixture.StartAt.AddHours(runningLate ? -4 : 0).UtcDateTime, DateTimeKind.Unspecified),
            PlannedArrivalAt = DateTime.SpecifyKind(Fixture.StartAt.AddHours(runningLate ? -2 : 2).UtcDateTime, DateTimeKind.Unspecified) });
        await db.SaveChangesAsync();
        Assert.Equal("RESOURCE_SCHEDULE_CONFLICT", (await Assert.ThrowsAsync<ConflictException>(() =>
            f.Service.DecideAsync(created.ChangeRequestId, f.Manager.AccountId, new() { Decision = "APPROVE", LastUpdatedAt = created.CreatedAt }))).ErrorCode);
        Assert.Null(f.Current.SupersededAt);
    }

    [Fact]
    public async Task FullBackup_UsesOnlyRemainderAndRebasesConfirmedSchedule()
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        var backup = new RoutePlan { RoutePlanId = Guid.NewGuid(), TransportPlanId = f.Plan.TransportPlanId,
            RoutePlanCode = "FULL_BACKUP", Status = RoutePlanStatus.CONFIRMED, PrimaryRoutePlanId = f.Plan.RoutePlanId };
        db.Add(backup);
        db.AddRange(new RoutePlanRoute { RoutePlanRouteId = Guid.NewGuid(), RoutePlanId = backup.RoutePlanId, RouteId = f.Completed.RouteId,
            SequenceNo = 1, RouteType = RouteType.BACKUP, PlannedDepartureAt = f.Completed.PlannedDepartureAt, PlannedArrivalAt = f.Completed.PlannedArrivalAt },
            new RoutePlanRoute { RoutePlanRouteId = Guid.NewGuid(), RoutePlanId = backup.RoutePlanId, RouteId = f.Alternative.RouteId,
                SequenceNo = 2, RouteType = RouteType.BACKUP, PlannedDepartureAt = f.Current.PlannedDepartureAt, PlannedArrivalAt = f.Current.PlannedArrivalAt });
        await db.SaveChangesAsync();
        var request = f.Proposal();
        request.ReplacementSegments = null;
        request.BackupRoutePlanId = backup.RoutePlanId;
        var created = await f.Service.CreateAsync(f.Plan.RoutePlanId, f.Coordinator.AccountId, request);
        var detail = await f.Service.GetAsync(created.ChangeRequestId, f.Manager.AccountId);
        var segment = Assert.Single(detail.ProposedSegments);
        Assert.Equal(f.Start.LocationId, segment.FromLocation.LocationId);
        Assert.Equal(request.NewEstimatedArrivalAt, segment.PlannedArrivalAt);
        Assert.Null(f.Completed.SupersededAt);
        var result = await f.Service.DecideAsync(created.ChangeRequestId, f.Manager.AccountId,
            new() { Decision = "APPROVE", LastUpdatedAt = created.CreatedAt });
        Assert.Single(result.NewSegmentIds);
        Assert.Equal(f.Driver.EmployeeId, db.RoutePlanRoutes.Single(x => x.RoutePlanRouteId == result.NewSegmentIds[0]).DriverEmployeeId);
    }

    [Fact]
    public async Task OutsourcedApproval_ClearsInternalResourcesAndLinksProvider()
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        var provider = new TransportProvider { TransportProviderId = Guid.NewGuid(), ProviderCode = "P1", ProviderName = "Provider",
            Status = ProviderStatus.ACTIVE, SupportedModes = "ROAD,SEA" };
        db.Add(provider);
        await db.SaveChangesAsync();
        var request = f.Proposal();
        request.ReplacementSegments![0].Outsourcing = new() { TransportProviderId = provider.TransportProviderId, ServiceType = "HAULAGE", ProviderReference = "REF-42" };
        var created = await f.Service.CreateAsync(f.Plan.RoutePlanId, f.Coordinator.AccountId, request);
        var result = await f.Service.DecideAsync(created.ChangeRequestId, f.Manager.AccountId,
            new() { Decision = "APPROVE", LastUpdatedAt = created.CreatedAt });
        var segment = await db.RoutePlanRoutes.Include(x => x.RoutePlanRouteVehicles).SingleAsync(x => x.RoutePlanRouteId == result.NewSegmentIds[0]);
        Assert.Null(segment.DriverEmployeeId);
        Assert.Null(segment.EscortEmployeeId);
        Assert.Empty(segment.RoutePlanRouteVehicles);
        Assert.Equal("REF-42", segment.ProviderReference);
        Assert.Equal(provider.TransportProviderId, Assert.Single(db.TransportPlanProviders).TransportProviderId);
        Assert.Contains("DRIVER", result.Notified);
    }

    [Fact]
    public async Task SameDistanceAndDelay_StillRequiresApprovalForNewBcp()
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        f.Alternative.DistanceKm = 100;
        var bcp = new Location { LocationId = Guid.NewGuid(), LocationName = "New BCP", Country = "FR", LocationType = LocationType.BORDER_GATE };
        db.Add(bcp);
        var first = f.NewRoute(f.Start, bcp, 50);
        var second = f.NewRoute(bcp, f.End, 50);
        await db.SaveChangesAsync();
        var request = f.Proposal(0);
        request.ReplacementSegments = [f.Segment(first, 1, Fixture.StartAt, Fixture.StartAt.AddHours(1)),
            f.Segment(second, 2, Fixture.StartAt.AddHours(1), Fixture.StartAt.AddHours(2))];
        var created = await f.Service.CreateAsync(f.Plan.RoutePlanId, f.Coordinator.AccountId, request);
        Assert.Equal(new[] { "BCP_CHANGED" }, created.ApprovalReasons);
    }

    [Fact]
    public async Task EmergencyReturn_ActivatesConfirmedBackupAndKeepsDriverInboxUntilAcknowledged()
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        var created = await f.Service.CreateAsync(f.Plan.RoutePlanId, f.Coordinator.AccountId, f.Proposal());
        var back = f.NewRoute(f.Start, f.Origin, 70);
        var backup = new RoutePlan { RoutePlanId = Guid.NewGuid(), TransportPlanId = f.Plan.TransportPlanId,
            PrimaryRoutePlanId = f.Plan.RoutePlanId, RoutePlanCode = "RETURN_BACKUP", Status = RoutePlanStatus.CONFIRMED };
        db.AddRange(backup, new RoutePlanRoute { RoutePlanRouteId = Guid.NewGuid(), RoutePlanId = backup.RoutePlanId,
            RouteId = back.RouteId, RouteType = RouteType.RETURN, SequenceNo = 1, PlannedDepartureAt = f.Current.PlannedDepartureAt,
            PlannedArrivalAt = f.Current.PlannedArrivalAt });
        await db.SaveChangesAsync();
        var result = await f.Service.DecideAsync(created.ChangeRequestId, f.Manager.AccountId,
            new() { Decision = "EMERGENCY_RETURN", LastUpdatedAt = created.CreatedAt, Note = "Return to the safe original stable" });
        var segment = db.RoutePlanRoutes.Single(x => x.RoutePlanRouteId == result.ReturnSegmentIds[0]);
        Assert.Equal(f.Driver.EmployeeId, segment.DriverEmployeeId);
        var inbox = new Pegaxus.Service.NotificationService.Service(db, new NoopNotificationRealtimeService());
        var items = (await inbox.GetAsync(f.Driver.AccountId, new Pegaxus.Service.NotificationService.Request.NotificationListRequest())).Items;
        var notification = Assert.Single(items);
        Assert.Contains("Origin", notification.Message);
        var row = await db.AccountNotifications.SingleAsync(x => x.NotificationId == notification.Id && x.AccountId == f.Driver.AccountId);
        Assert.Equal(RecipientStatus.PENDING, row.Status);
        await inbox.AcknowledgeDeliveredAsync(f.Driver.AccountId, notification.Id);
        Assert.Equal(RecipientStatus.DELIVERED, row.Status);
        Assert.Contains(db.OutboxMessages, x => x.MessageType == "EMAIL");
    }

    internal sealed class Fixture(AppDbContext db)
    {
        internal static readonly DateTimeOffset StartAt = new(2030, 1, 2, 10, 0, 0, TimeSpan.Zero);
        internal Account Coordinator = null!, Manager = null!;
        internal Employee Driver = null!, Escort = null!;
        internal Location Origin = null!, Start = null!, End = null!;
        internal Route Alternative = null!;
        internal RoutePlan Plan = null!;
        internal RoutePlanRoute Current = null!, Completed = null!;
        internal Transport Transport = null!;
        internal RouteChange.Service Service = new(db, new Pegaxus.Service.NotificationService.Service(db, new NoopNotificationRealtimeService()), new FixedClock());
        internal static async Task<Fixture> CreateAsync(AppDbContext db)
        {
            var f = new Fixture(db);
            var (managerAccount, _) = await TestSupport.AddLogisticsManagerAsync(db);
            f.Manager = managerAccount;
            var (rcAccount, rc) = await TestSupport.AddLogisticsManagerAsync(db);
            rc.Role = EmployeeRole.FLEET_ROUTE_COORDINATOR;
            f.Coordinator = rcAccount;
            var (_, driver) = await TestSupport.AddLogisticsManagerAsync(db);
            driver.Role = EmployeeRole.DRIVER;
            f.Driver = driver;
            var (_, escort) = await TestSupport.AddLogisticsManagerAsync(db);
            escort.Role = EmployeeRole.ESCORT;
            f.Escort = escort;
            var (_, specialist) = await TestSupport.AddLogisticsManagerAsync(db);
            specialist.Role = EmployeeRole.TRANSPORT_SPECIALIST;
            var (_, customer) = await TestSupport.AddCustomerAsync(db);
            var (booking, _) = await TestSupport.AddPendingBookingAsync(db, customer, true);
            f.Origin = new() { LocationId = Guid.NewGuid(), LocationName = "Origin", Country = "FR", LocationType = LocationType.STABLE };
            f.Start = new() { LocationId = Guid.NewGuid(), LocationName = "Safe stop", Country = "FR" };
            f.End = new() { LocationId = Guid.NewGuid(), LocationName = "Destination", Country = "DE" };
            db.AddRange(f.Origin, f.Start, f.End);
            var completedRoute = f.NewRoute(f.Origin, f.Start, 70);
            var currentRoute = f.NewRoute(f.Start, f.End, 100);
            f.Alternative = f.NewRoute(f.Start, f.End, 115);
            f.Transport = new() { TransportId = Guid.NewGuid(), TransportCode = "TR1", BookingId = booking.BookingId, Status = TransportStatus.IN_TRANSIT };
            var transportPlan = new TransportPlan { TransportPlanId = Guid.NewGuid(), TransportId = f.Transport.TransportId, PlanCode = "TP1", PlannedEndAt = DbTime(StartAt.AddHours(2)) };
            f.Plan = new() { RoutePlanId = Guid.NewGuid(), TransportPlanId = transportPlan.TransportPlanId, RoutePlanCode = "RP1", Status = RoutePlanStatus.ACTIVE };
            f.Completed = new() { RoutePlanRouteId = Guid.NewGuid(), RoutePlanId = f.Plan.RoutePlanId, RouteId = completedRoute.RouteId, SequenceNo = 1,
                PlannedDepartureAt = DbTime(StartAt.AddHours(-3)), PlannedArrivalAt = DbTime(StartAt.AddHours(-1)),
                ActualDepartureAt = DbTime(StartAt.AddHours(-3)), ActualArrivalAt = DbTime(StartAt.AddHours(-1)) };
            f.Current = new() { RoutePlanRouteId = Guid.NewGuid(), RoutePlanId = f.Plan.RoutePlanId, RouteId = currentRoute.RouteId, SequenceNo = 2,
                DriverEmployeeId = driver.EmployeeId, EscortEmployeeId = escort.EmployeeId, PlannedDepartureAt = DbTime(StartAt), PlannedArrivalAt = DbTime(StartAt.AddHours(2)) };
            var vehicle = new Vehicle { VehicleId = Guid.NewGuid(), VehicleCode = "V1", CapacityHorses = 5, TransportMode = TransportMode.ROAD,
                RegistrationExpiryDate = new(2031, 1, 1), InsuranceExpiryDate = new(2031, 1, 1) };
            db.AddRange(f.Transport, transportPlan, f.Plan, f.Completed, f.Current, vehicle,
                new RoutePlanRouteVehicle { RoutePlanRouteId = f.Current.RoutePlanRouteId, VehicleId = vehicle.VehicleId, AssignedAt = DbTime(StartAt) },
                new EmployeeTransportPlan { TransportPlanId = transportPlan.TransportPlanId, EmployeeId = specialist.EmployeeId, Responsibility = "TRANSPORT_SPECIALIST", AssignedAt = DbTime(StartAt) });
            await db.SaveChangesAsync();
            return f;
        }
        internal Route NewRoute(Location from, Location to, decimal distance)
        {
            var route = new Route { RouteId = Guid.NewGuid(), RouteCode = "R-" + Guid.NewGuid().ToString("N"),
                FromLocationId = from.LocationId, ToLocationId = to.LocationId, FromLocation = from, ToLocation = to, DistanceKm = distance,
                EstimatedDurationMinutes = 120, TransportMode = TransportMode.ROAD };
            db.Add(route);
            return route;
        }
        internal Request.SegmentRequest Segment(Route route, int sequence, DateTimeOffset departure, DateTimeOffset arrival) =>
            new() { SequenceNo = sequence, RouteId = route.RouteId, PlannedDepartureAt = departure, PlannedArrivalAt = arrival };
        internal Request.CreateRequest Proposal(int delay = 60) => new()
        {
            Reason = "Road blocked by severe weather", ReplacementSegments = [Segment(Alternative, 1, StartAt, StartAt.AddHours(2).AddMinutes(delay))],
            NewEstimatedArrivalAt = StartAt.AddHours(2).AddMinutes(delay), EstimatedExtraCost = 1800, Currency = "EUR"
        };
        private static DateTime DbTime(DateTimeOffset value) => DateTime.SpecifyKind(value.UtcDateTime, DateTimeKind.Unspecified);
        internal sealed class FixedClock : TimeProvider { public override DateTimeOffset GetUtcNow() => StartAt.AddHours(-1); }
    }
}
