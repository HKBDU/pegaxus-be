using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.NotificationService;
using Pegaxus.Service.PersonnelAssignmentService;
using Pegaxus.Service.PersonnelAssignmentService.Validator;
using Request = Pegaxus.Service.PersonnelAssignmentService.Request;
using Response = Pegaxus.Service.PersonnelAssignmentService.Response;
using AssignmentService = Pegaxus.Service.PersonnelAssignmentService.Service;

namespace Pegaxus.Service.Test;

public class PersonnelAssignmentTests
{
    internal sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
    }
    internal static readonly TimeProvider TestClock = new Clock();
    internal static DateTime Now => DateTime.SpecifyKind(TestClock.GetUtcNow().UtcDateTime, DateTimeKind.Unspecified);
    internal static AssignmentService CreateService(AppDbContext db) => new(db, TestClock, new AssignRequestValidator(),
        new ReplaceRequestValidator(), new Pegaxus.Service.NotificationService.Service(db, new NoopNotificationRealtimeService()), NullLogger<AssignmentService>.Instance);
    internal static DateTimeOffset Version(RoutePlan route) => new(DateTime.SpecifyKind(route.UpdatedAt, DateTimeKind.Utc));
    internal static Request.AssignRequest Assign(RoutePlan route, RoutePlanRoute segment, Employee driver, Employee escort) => new()
    { LastUpdatedAt = Version(route), Segments = [new() { RoutePlanRouteId = segment.RoutePlanRouteId, DriverEmployeeId = driver.EmployeeId, EscortEmployeeId = escort.EmployeeId }] };
    internal static async Task<Employee> EmployeeAsync(AppDbContext db, EmployeeRole role)
    {
        var (_, employee) = await TestSupport.AddLogisticsManagerAsync(db);
        employee.Role = role; await db.SaveChangesAsync(); return employee;
    }
    internal static async Task<(RoutePlan Route, RoutePlanRoute Segment)> RouteAsync(AppDbContext db, RoutePlanStatus status = RoutePlanStatus.CONFIRMED)
    {
        var (_, customer) = await TestSupport.AddCustomerAsync(db);
        var (booking, _) = await TestSupport.AddPendingBookingAsync(db, customer);
        var plan = new TransportPlan { TransportPlanId = Guid.NewGuid(), PlanCode = "PLAN-" + Guid.NewGuid().ToString("N"), Status = TransportPlanStatus.APPROVED,
            Transport = new Transport { TransportId = Guid.NewGuid(), BookingId = booking.BookingId, TransportCode = "TR-" + Guid.NewGuid().ToString("N") } };
        var route = new RoutePlan { RoutePlanId = Guid.NewGuid(), RoutePlanCode = "RP-" + Guid.NewGuid().ToString("N"), TransportPlan = plan,
            Status = status, CreatedAt = Now, UpdatedAt = Now };
        var segment = Segment(route, 1);
        db.Add(segment); await db.SaveChangesAsync(); return (route, segment);
    }
    internal static RoutePlanRoute Segment(RoutePlan plan, int sequence) => new()
    {
        RoutePlanRouteId = Guid.NewGuid(), RoutePlan = plan, SequenceNo = sequence,
        PlannedDepartureAt = Now.AddDays(2).AddHours(sequence * 2), PlannedArrivalAt = Now.AddDays(2).AddHours(sequence * 2 + 1),
        CreatedAt = Now, UpdatedAt = Now,
        Route = new Route { RouteId = Guid.NewGuid(), RouteCode = "ROUTE-" + Guid.NewGuid().ToString("N"), TransportMode = TransportMode.ROAD,
            FromLocation = new Location { LocationId = Guid.NewGuid(), LocationName = "Stable", Country = "FR" },
            ToLocation = new Location { LocationId = Guid.NewGuid(), LocationName = "Airport", Country = "FR" } }
    };
    internal static TransportProvider Provider() => new() { TransportProviderId = Guid.NewGuid(), ProviderCode = "PRV-" + Guid.NewGuid().ToString("N"),
        ProviderName = "Outsource Air", Email = "provider@example.com", Status = ProviderStatus.ACTIVE, SupportedModes = "AIR" };

    [Fact]
    public async Task Read_Phase1Visible_Phase2LockedUntilConfirmedPrimary()
    {
        await using var db = TestSupport.CreateDb();
        var manager = await EmployeeAsync(db, EmployeeRole.LOGISTICS_MANAGER);
        var specialist = await EmployeeAsync(db, EmployeeRole.TRANSPORT_SPECIALIST);
        var coordinator = await EmployeeAsync(db, EmployeeRole.FLEET_ROUTE_COORDINATOR);
        var (route, segment) = await RouteAsync(db, RoutePlanStatus.DRAFT);
        db.EmployeeTransportPlans.AddRange(new EmployeeTransportPlan { EmployeeId = specialist.EmployeeId, TransportPlanId = route.TransportPlanId, Responsibility = "TRANSPORT_SPECIALIST", IsPrimary = true },
            new EmployeeTransportPlan { EmployeeId = coordinator.EmployeeId, TransportPlanId = route.TransportPlanId, Responsibility = "FLEET_ROUTE_COORDINATOR", IsPrimary = true });
        await db.SaveChangesAsync();
        var service = CreateService(db);
        foreach (var actor in new[] { manager, specialist, coordinator })
        {
            var view = await service.GetAsync(route.TransportPlanId, actor.AccountId);
            Assert.False(view.Phase2.Enabled); Assert.Null(view.Phase2.RoutePlanId); Assert.Empty(view.Phase2.Segments);
            Assert.Equal(specialist.EmployeeId, view.Phase1.TransportSpecialist!.EmployeeId);
            Assert.Equal(coordinator.EmployeeId, view.Phase1.RouteCoordinator!.EmployeeId);
        }
        var driver = await EmployeeAsync(db, EmployeeRole.DRIVER); var escort = await EmployeeAsync(db, EmployeeRole.ESCORT);
        Assert.Equal("ROUTE_NOT_CONFIRMED", (await Assert.ThrowsAsync<UnprocessableEntityException>(() => service.AssignAsync(route.RoutePlanId, Assign(route, segment, driver, escort), manager.AccountId))).ErrorCode);
        route.Status = RoutePlanStatus.CONFIRMED; await db.SaveChangesAsync();
        Assert.True((await service.GetAsync(route.TransportPlanId, manager.AccountId)).Phase2.Enabled);
        segment.RouteType = RouteType.BACKUP; await db.SaveChangesAsync();
        Assert.False((await service.GetAsync(route.TransportPlanId, manager.AccountId)).Phase2.Enabled);
        Assert.Equal("ROUTE_NOT_CONFIRMED", (await Assert.ThrowsAsync<UnprocessableEntityException>(() => service.AssignAsync(route.RoutePlanId, Assign(route, segment, driver, escort), manager.AccountId))).ErrorCode);
    }

    [Theory]
    [InlineData(TransportMode.ROAD)]
    [InlineData(TransportMode.SEA)]
    [InlineData(TransportMode.AIR)]
    public async Task Assign_InternalAndOutsource_FullAtomicLinksAuditNotifications_RetryNoDuplicates(TransportMode mode)
    {
        await using var db = TestSupport.CreateDb();
        var manager = await EmployeeAsync(db, EmployeeRole.LOGISTICS_MANAGER);
        var driver = await EmployeeAsync(db, EmployeeRole.DRIVER); var escort = await EmployeeAsync(db, EmployeeRole.ESCORT);
        var (route, segment) = await RouteAsync(db);
        segment.Route.TransportMode = mode;
        var outsource = Segment(route, 2); outsource.TransportProvider = Provider(); outsource.Route.TransportMode = TransportMode.AIR;
        db.Add(outsource); await db.SaveChangesAsync();
        var service = CreateService(db);
        var result = await service.AssignAsync(route.RoutePlanId, Assign(route, segment, driver, escort), manager.AccountId);
        Assert.True(result.IsPersonnelComplete);
        Assert.Equal(driver.EmployeeId, result.Segments[0].Driver!.EmployeeId);
        Assert.Equal(escort.EmployeeId, result.Segments[0].Escort!.EmployeeId);
        Assert.Null(result.Segments[1].Driver); Assert.Null(result.Segments[1].Escort);
        Assert.Equal(outsource.TransportProviderId, result.Segments[1].Provider!.TransportProviderId);
        Assert.All(result.Segments, x => Assert.True(x.IsComplete));
        Assert.Equal(2, await db.EmployeeTransportPlans.CountAsync());
        Assert.Equal(new[] { "DRIVER", "ESCORT" }, await db.EmployeeTransportPlans.OrderBy(x => x.Responsibility).Select(x => x.Responsibility).ToArrayAsync());
        var audit = await db.CatalogAuditLogs.SingleAsync();
        Assert.Equal(manager.AccountId, audit.ActorAccountId); Assert.Equal("PERSONNEL_ASSIGNED", audit.ActionType);
        Assert.Equal(2, await db.Notifications.CountAsync());
        Assert.All(await db.Notifications.ToArrayAsync(), x => { Assert.Equal(route.TransportPlan.TransportId, x.TransportId); Assert.Contains("Segment 1", x.Message); });
        Assert.Equal(2, await db.OutboxMessages.CountAsync()); // Only assigned employees receive emails.
        Assert.DoesNotContain(await db.OutboxMessages.ToListAsync(), x => x.Payload.Contains("provider@example.com"));
        Assert.Equal(TransportStatus.READY, route.TransportPlan.Transport.Status);
        Assert.True(result.UpdatedAt > TestClock.GetUtcNow());
        Assert.Equal("SEGMENT_ALREADY_ASSIGNED", (await Assert.ThrowsAsync<ConflictException>(() =>
            service.AssignAsync(route.RoutePlanId, Assign(route, segment, driver, escort), manager.AccountId))).ErrorCode);
        Assert.Single(db.CatalogAuditLogs); Assert.Equal(2, await db.OutboxMessages.CountAsync());
    }

    [Fact]
    public async Task PartialAssign_ReplacePreservesUnchangedRole_DeletesOnlyUnusedLinks_RequiresVersion()
    {
        await using var db = TestSupport.CreateDb();
        var manager = await EmployeeAsync(db, EmployeeRole.LOGISTICS_MANAGER);
        var driver = await EmployeeAsync(db, EmployeeRole.DRIVER); var replacement = await EmployeeAsync(db, EmployeeRole.DRIVER);
        var escort = await EmployeeAsync(db, EmployeeRole.ESCORT);
        var (route, first) = await RouteAsync(db); var second = Segment(route, 2); db.Add(second); await db.SaveChangesAsync();
        var service = CreateService(db);
        await Assert.ThrowsAsync<BadRequestException>(() => service.AssignAsync(route.RoutePlanId, Assign(route, first, driver, escort), manager.AccountId));
        Assert.Null(first.DriverEmployeeId);
        var request = Assign(route, first, driver, escort);
        request.Segments.Add(new() { RoutePlanRouteId = second.RoutePlanRouteId, DriverEmployeeId = driver.EmployeeId, EscortEmployeeId = escort.EmployeeId });
        var oldVersion = Version(route);
        var assigned = await service.AssignAsync(route.RoutePlanId, request, manager.AccountId);
        Assert.True(assigned.IsPersonnelComplete);
        Assert.Equal("ROUTE_PLAN_MODIFIED", (await Assert.ThrowsAsync<ConflictException>(() => service.ReplaceAsync(route.RoutePlanId, first.RoutePlanRouteId,
            new() { LastUpdatedAt = oldVersion, DriverEmployeeId = replacement.EmployeeId, Reason = "Sick" }, manager.AccountId))).ErrorCode);
        var replaced = await service.ReplaceAsync(route.RoutePlanId, first.RoutePlanRouteId, new() { LastUpdatedAt = Version(route), DriverEmployeeId = replacement.EmployeeId, Reason = "<b>Sick</b>" }, manager.AccountId);
        Assert.Equal(replacement.EmployeeId, replaced.Segment.Driver!.EmployeeId); Assert.Equal(escort.EmployeeId, replaced.Segment.Escort!.EmployeeId);
        Assert.True(replaced.IsPersonnelComplete);
        Assert.Equal(3, await db.EmployeeTransportPlans.CountAsync()); // Old driver still serves the second segment.
        await service.ReplaceAsync(route.RoutePlanId, second.RoutePlanRouteId, new() { LastUpdatedAt = Version(route), DriverEmployeeId = replacement.EmployeeId, Reason = "Sick" }, manager.AccountId);
        Assert.False(await db.EmployeeTransportPlans.AnyAsync(x => x.EmployeeId == driver.EmployeeId));
        var audit = await db.CatalogAuditLogs.FirstAsync(x => x.Summary == "<b>Sick</b>");
        Assert.Contains(driver.EmployeeId.ToString(), audit.BeforeData.RootElement.GetRawText());
        Assert.Contains(replacement.EmployeeId.ToString(), audit.AfterData.RootElement.GetRawText());
        Assert.Contains(await db.Notifications.ToListAsync(), x => x.Message.Contains("<b>Sick</b>"));
        Assert.Contains(await db.OutboxMessages.ToListAsync(), x => x.Payload.Contains("Sick"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Schedule_BlocksDriverAndEscortAcrossPlans(bool conflictEscort)
    {
        await using var db = TestSupport.CreateDb();
        var manager = await EmployeeAsync(db, EmployeeRole.LOGISTICS_MANAGER);
        var driver = await EmployeeAsync(db, EmployeeRole.DRIVER); var escort = await EmployeeAsync(db, EmployeeRole.ESCORT);
        var (route, segment) = await RouteAsync(db); var (other, otherSegment) = await RouteAsync(db);
        var otherDriver = conflictEscort ? await EmployeeAsync(db, EmployeeRole.DRIVER) : driver;
        var otherEscort = conflictEscort ? escort : await EmployeeAsync(db, EmployeeRole.ESCORT);
        var service = CreateService(db);
        await service.AssignAsync(other.RoutePlanId, Assign(other, otherSegment, otherDriver, otherEscort), manager.AccountId);
        var error = await Assert.ThrowsAsync<ConflictException>(() => service.AssignAsync(route.RoutePlanId, Assign(route, segment, driver, escort), manager.AccountId));
        Assert.Equal("SCHEDULE_CONFLICT", error.ErrorCode);
        Assert.Contains(conflictEscort ? escort.EmployeeCode : driver.EmployeeCode, error.Message);
        Assert.Null(segment.DriverEmployeeId); Assert.Null(segment.EscortEmployeeId);
        Assert.Single(db.CatalogAuditLogs);
        other.Status = RoutePlanStatus.CANCELLED; await db.SaveChangesAsync();
        await service.AssignAsync(route.RoutePlanId, Assign(route, segment, driver, escort), manager.AccountId);
    }

    [Fact]
    public async Task Schedule_SamePlanOverlapsRejected_AdjacentNonoverlapAllowed_UnknownTimesRejected()
    {
        await using var db = TestSupport.CreateDb();
        var manager = await EmployeeAsync(db, EmployeeRole.LOGISTICS_MANAGER); var driver = await EmployeeAsync(db, EmployeeRole.DRIVER); var escort = await EmployeeAsync(db, EmployeeRole.ESCORT);
        var (route, first) = await RouteAsync(db); var second = Segment(route, 2);
        second.PlannedDepartureAt = first.PlannedArrivalAt; db.Add(second); await db.SaveChangesAsync();
        var request = Assign(route, first, driver, escort);
        request.Segments.Add(new() { RoutePlanRouteId = second.RoutePlanRouteId, DriverEmployeeId = driver.EmployeeId, EscortEmployeeId = escort.EmployeeId });
        var service = CreateService(db);
        var error = await Assert.ThrowsAsync<ConflictException>(() => service.AssignAsync(route.RoutePlanId, request, manager.AccountId));
        Assert.Equal("SCHEDULE_CONFLICT", error.ErrorCode);
        var conflict = Assert.IsType<Response.ScheduleConflictResponse>(error.Value);
        Assert.Equal(driver.EmployeeId, conflict.EmployeeId); Assert.Equal(driver.EmployeeCode, conflict.EmployeeCode);
        Assert.Equal("DRIVER", conflict.Role); Assert.Equal(first.RoutePlanRouteId, conflict.RoutePlanRouteId);
        Assert.Equal(route.TransportPlan.PlanCode, conflict.ConflictingTransportPlanCode);
        Assert.Equal(second.Route.FromLocation.LocationName, conflict.ConflictingFromLocationName);
        Assert.Equal(second.Route.ToLocation.LocationName, conflict.ConflictingToLocationName);
        Assert.Equal(new DateTimeOffset(DateTime.SpecifyKind(second.PlannedDepartureAt!.Value, DateTimeKind.Utc)), conflict.ConflictingPlannedDepartureAt);
        Assert.Equal(new DateTimeOffset(DateTime.SpecifyKind(second.PlannedArrivalAt!.Value, DateTimeKind.Utc)), conflict.ConflictingPlannedArrivalAt);
        Assert.Empty(db.EmployeeTransportPlans); Assert.Empty(db.CatalogAuditLogs);
        second.PlannedDepartureAt = null; await db.SaveChangesAsync();
        Assert.Equal("INVALID_SEGMENT_SCHEDULE", (await Assert.ThrowsAsync<BadRequestException>(() => service.AssignAsync(route.RoutePlanId, request, manager.AccountId))).ErrorCode);
        second.PlannedDepartureAt = first.PlannedArrivalAt!.Value.AddMinutes(1); await db.SaveChangesAsync();
        Assert.True((await service.AssignAsync(route.RoutePlanId, request, manager.AccountId)).IsPersonnelComplete);
    }

    [Theory]
    [InlineData("inactive")]
    [InlineData("deleted")]
    [InlineData("account")]
    [InlineData("role")]
    [InlineData("missing")]
    public async Task EmployeeValidation_FailsWithoutWrites(string invalid)
    {
        await using var db = TestSupport.CreateDb();
        var manager = await EmployeeAsync(db, EmployeeRole.LOGISTICS_MANAGER); var driver = await EmployeeAsync(db, EmployeeRole.DRIVER); var escort = await EmployeeAsync(db, EmployeeRole.ESCORT);
        var (route, segment) = await RouteAsync(db); var request = Assign(route, segment, driver, escort);
        switch (invalid) { case "inactive": driver.IsActive = false; break; case "deleted": driver.IsDeleted = true; break;
            case "account": driver.Account.Status = AccountStatus.INACTIVE; break; case "role": driver.Role = EmployeeRole.ESCORT; break; case "missing": request.Segments[0].DriverEmployeeId = Guid.NewGuid(); break; }
        await db.SaveChangesAsync();
        var error = await Record.ExceptionAsync(() => CreateService(db).AssignAsync(route.RoutePlanId, request, manager.AccountId));
        Assert.Equal(invalid == "missing" ? "EMPLOYEE_NOT_FOUND" : "INVALID_ROLE_ASSIGNMENT", Assert.IsAssignableFrom<Pegaxus.Service.Exceptions.Exceptions>(error).ErrorCode);
        Assert.Null(segment.DriverEmployeeId); Assert.Empty(db.CatalogAuditLogs); Assert.Empty(db.Notifications);
    }

    [Theory]
    [InlineData("started")]
    [InlineData("completed")]
    [InlineData("outsourced")]
    [InlineData("foreign")]
    public async Task SegmentLocks_AndMembershipApplyToPutAndPatch(string invalid)
    {
        await using var db = TestSupport.CreateDb();
        var manager = await EmployeeAsync(db, EmployeeRole.LOGISTICS_MANAGER); var driver = await EmployeeAsync(db, EmployeeRole.DRIVER); var escort = await EmployeeAsync(db, EmployeeRole.ESCORT);
        var (route, segment) = await RouteAsync(db);
        if (invalid == "started") segment.ActualDepartureAt = Now;
        if (invalid == "completed") segment.ActualArrivalAt = Now;
        if (invalid == "outsourced") { segment.TransportProvider = Provider(); db.Add(segment.TransportProvider); }
        if (invalid == "foreign") segment = (await RouteAsync(db)).Segment;
        await db.SaveChangesAsync();
        var service = CreateService(db);
        var errors = new[] {
            await Record.ExceptionAsync(() => service.AssignAsync(route.RoutePlanId, Assign(route, segment, driver, escort), manager.AccountId)),
            await Record.ExceptionAsync(() => service.ReplaceAsync(route.RoutePlanId, segment.RoutePlanRouteId, new() { LastUpdatedAt = Version(route), DriverEmployeeId = driver.EmployeeId, Reason = "Sick" }, manager.AccountId)) };
        Assert.All(errors, x => Assert.Equal(invalid == "foreign" ? "SEGMENT_NOT_FOUND" : invalid == "outsourced" ? "OUTSOURCED_SEGMENT" : "SEGMENT_LOCKED", Assert.IsAssignableFrom<Pegaxus.Service.Exceptions.Exceptions>(x).ErrorCode));
        Assert.Empty(db.CatalogAuditLogs);
    }

    [Fact]
    public async Task AllOutsource_AssignsProviderWithoutPersonnel()
    {
        await using var db = TestSupport.CreateDb();
        var manager = await EmployeeAsync(db, EmployeeRole.LOGISTICS_MANAGER); var (route, segment) = await RouteAsync(db);
        segment.TransportProvider = Provider(); db.Add(segment.TransportProvider); segment.Route.TransportMode = TransportMode.AIR; await db.SaveChangesAsync();
        var result = await CreateService(db).AssignAsync(route.RoutePlanId, new() { LastUpdatedAt = Version(route) }, manager.AccountId);
        Assert.True(result.IsPersonnelComplete); Assert.Empty(db.EmployeeTransportPlans);
        Assert.Empty(db.OutboxMessages); Assert.Single(db.CatalogAuditLogs);
    }

    [Theory]
    [InlineData(EmployeeRole.TRANSPORT_SPECIALIST)]
    [InlineData(EmployeeRole.FLEET_ROUTE_COORDINATOR)]
    [InlineData(EmployeeRole.DRIVER)]
    [InlineData(EmployeeRole.ESCORT)]
    public async Task OnlyManagerWrites(EmployeeRole role)
    {
        await using var db = TestSupport.CreateDb();
        var actor = await EmployeeAsync(db, role); var (route, segment) = await RouteAsync(db); var service = CreateService(db);
        await Assert.ThrowsAsync<ForbiddenException>(() => service.AssignAsync(route.RoutePlanId, new(), actor.AccountId));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.ReplaceAsync(route.RoutePlanId, segment.RoutePlanRouteId, new(), actor.AccountId));
        if (role is EmployeeRole.DRIVER or EmployeeRole.ESCORT) await Assert.ThrowsAsync<ForbiddenException>(() => service.GetAsync(route.TransportPlanId, actor.AccountId));
    }

    [Fact]
    public async Task ExpiredSchedulesAndInactiveProvidersCannotBypassValidation()
    {
        await using var db = TestSupport.CreateDb();
        var manager = await EmployeeAsync(db, EmployeeRole.LOGISTICS_MANAGER); var driver = await EmployeeAsync(db, EmployeeRole.DRIVER); var escort = await EmployeeAsync(db, EmployeeRole.ESCORT);
        var (route, segment) = await RouteAsync(db); var (other, busy) = await RouteAsync(db);
        busy.DriverEmployeeId = driver.EmployeeId; busy.EscortEmployeeId = escort.EmployeeId;
        busy.PlannedDepartureAt = null; await db.SaveChangesAsync();
        var service = CreateService(db);
        var error = await Assert.ThrowsAsync<ConflictException>(() => service.AssignAsync(route.RoutePlanId, Assign(route, segment, driver, escort), manager.AccountId));
        Assert.Equal("SCHEDULE_CONFLICT", error.ErrorCode);
        var conflict = Assert.IsType<Response.ScheduleConflictResponse>(error.Value);
        Assert.Null(conflict.ConflictingPlannedDepartureAt);
        Assert.Equal(other.TransportPlan.PlanCode, conflict.ConflictingTransportPlanCode);
        Assert.Equal(new DateTimeOffset(DateTime.SpecifyKind(busy.PlannedArrivalAt!.Value, DateTimeKind.Utc)), conflict.ConflictingPlannedArrivalAt);
        busy.PlannedDepartureAt = Now.AddHours(-2); busy.PlannedArrivalAt = Now.AddHours(-1); busy.ActualDepartureAt = busy.PlannedDepartureAt;
        await db.SaveChangesAsync();
        Assert.Equal("SCHEDULE_CONFLICT", (await Assert.ThrowsAsync<ConflictException>(() => service.AssignAsync(route.RoutePlanId, Assign(route, segment, driver, escort), manager.AccountId))).ErrorCode);
        busy.ActualArrivalAt = Now; await db.SaveChangesAsync();
        await service.AssignAsync(route.RoutePlanId, Assign(route, segment, driver, escort), manager.AccountId);
        var (providerRoute, outsourced) = await RouteAsync(db);
        outsourced.TransportProvider = Provider(); outsourced.TransportProvider.Status = ProviderStatus.INACTIVE;
        db.Add(outsourced.TransportProvider); await db.SaveChangesAsync();
        Assert.Equal("PROVIDER_INACTIVE", (await Assert.ThrowsAsync<UnprocessableEntityException>(() => service.AssignAsync(providerRoute.RoutePlanId,
            new() { LastUpdatedAt = Version(providerRoute) }, manager.AccountId))).ErrorCode);
    }

    [Theory]
    [InlineData("plan-started")]
    [InlineData("plan-completed")]
    [InlineData("plan-cancelled")]
    [InlineData("transport-completed")]
    [InlineData("transport-cancelled")]
    [InlineData("transport-started")]
    public async Task TerminalPlansDisablePhase2AndBlockChanges(string terminal)
    {
        await using var db = TestSupport.CreateDb();
        var manager = await EmployeeAsync(db, EmployeeRole.LOGISTICS_MANAGER); var driver = await EmployeeAsync(db, EmployeeRole.DRIVER); var escort = await EmployeeAsync(db, EmployeeRole.ESCORT);
        var (route, segment) = await RouteAsync(db);
        switch (terminal)
        {
            case "plan-started": route.TransportPlan.Status = TransportPlanStatus.IN_PROGRESS; break;
            case "plan-completed": route.TransportPlan.Status = TransportPlanStatus.COMPLETED; break;
            case "plan-cancelled": route.TransportPlan.Status = TransportPlanStatus.CANCELLED; break;
            case "transport-completed": route.TransportPlan.Transport.Status = TransportStatus.COMPLETED; break;
            case "transport-cancelled": route.TransportPlan.Transport.Status = TransportStatus.CANCELLED; break;
            case "transport-started": route.TransportPlan.Transport.Status = TransportStatus.IN_TRANSIT; break;
        }
        await db.SaveChangesAsync(); var service = CreateService(db);
        Assert.Equal(terminal is "transport-started" or "plan-started", (await service.GetAsync(route.TransportPlanId, manager.AccountId)).Phase2.Enabled);
        Assert.Equal("PLAN_LOCKED", (await Assert.ThrowsAsync<ConflictException>(() => service.AssignAsync(route.RoutePlanId, Assign(route, segment, driver, escort), manager.AccountId))).ErrorCode);
        Assert.Empty(db.CatalogAuditLogs);
    }

    [Fact]
    public async Task Phase1_ValidatesRolesInactivityAndPlanLocks()
    {
        await using var db = TestSupport.CreateDb();
        var manager = await EmployeeAsync(db, EmployeeRole.LOGISTICS_MANAGER);
        var specialist = await EmployeeAsync(db, EmployeeRole.TRANSPORT_SPECIALIST);
        var coordinator = await EmployeeAsync(db, EmployeeRole.FLEET_ROUTE_COORDINATOR);
        var (route, _) = await RouteAsync(db, RoutePlanStatus.DRAFT);
        var service = new Pegaxus.Service.TransportPlanService.Service(db);
        var request = new Pegaxus.Service.TransportPlanService.Request.Phase1AssignmentRequest {
            TransportSpecialistId = specialist.EmployeeId, RouteCoordinatorId = coordinator.EmployeeId };
        specialist.IsActive = false; await db.SaveChangesAsync();
        Assert.Equal("INVALID_ROLE_ASSIGNMENT", (await Assert.ThrowsAsync<UnprocessableEntityException>(() =>
            service.AssignPhase1Async(manager.AccountId, route.TransportPlanId, request))).ErrorCode);
        specialist.IsActive = true; specialist.Role = EmployeeRole.DRIVER; await db.SaveChangesAsync();
        Assert.Equal("INVALID_ROLE_ASSIGNMENT", (await Assert.ThrowsAsync<UnprocessableEntityException>(() =>
            service.AssignPhase1Async(manager.AccountId, route.TransportPlanId, request))).ErrorCode);
        specialist.Role = EmployeeRole.TRANSPORT_SPECIALIST;
        route.TransportPlan.Status = TransportPlanStatus.IN_PROGRESS; await db.SaveChangesAsync();
        Assert.Equal("PLAN_LOCKED", (await Assert.ThrowsAsync<ConflictException>(() =>
            service.AssignPhase1Async(manager.AccountId, route.TransportPlanId, request))).ErrorCode);
        route.TransportPlan.Status = TransportPlanStatus.APPROVED; await db.SaveChangesAsync();
        var result = await service.AssignPhase1Async(manager.AccountId, route.TransportPlanId, request);
        Assert.Equal(2, result.AssignedEmployees.Count);
        var view = await CreateService(db).GetAsync(route.TransportPlanId, coordinator.AccountId);
        Assert.True(view.Phase1.IsAssigned); Assert.False(view.Phase1.IsLocked);
        Assert.Equal(coordinator.EmployeeId, view.Phase1.RouteCoordinator!.EmployeeId);
        var replacement = await EmployeeAsync(db, EmployeeRole.FLEET_ROUTE_COORDINATOR);
        await service.UpdatePhase1Async(manager.AccountId, route.TransportPlanId, new() {
            LastUpdatedAt = result.UpdatedAt, RouteCoordinatorId = replacement.EmployeeId, Reason = "On leave" });
        await Assert.ThrowsAsync<ForbiddenException>(() => CreateService(db).GetAsync(route.TransportPlanId, coordinator.AccountId));
        Assert.True((await CreateService(db).GetAsync(route.TransportPlanId, replacement.AccountId)).Phase1.IsAssigned);
    }

    [Theory]
    [InlineData(EmployeeRole.TRANSPORT_SPECIALIST)]
    [InlineData(EmployeeRole.FLEET_ROUTE_COORDINATOR)]
    [InlineData(EmployeeRole.DRIVER)]
    [InlineData(EmployeeRole.ESCORT)]
    public async Task Read_RequiresPlanAssignment_ActivePrimaryRemainsVisible(EmployeeRole role)
    {
        await using var db = TestSupport.CreateDb();
        var actor = await EmployeeAsync(db, role);
        var (route, segment) = await RouteAsync(db, RoutePlanStatus.ACTIVE);
        var service = CreateService(db);
        await Assert.ThrowsAsync<ForbiddenException>(() => service.GetAsync(route.TransportPlanId, actor.AccountId));
        db.EmployeeTransportPlans.Add(new() { EmployeeId = actor.EmployeeId, TransportPlanId = route.TransportPlanId,
            Responsibility = role.ToString(), IsPrimary = true });
        route.TransportPlan.Transport.Status = TransportStatus.IN_TRANSIT;
        await db.SaveChangesAsync();
        var result = await service.GetAsync(route.TransportPlanId, actor.AccountId);
        Assert.True(result.Phase2.Enabled); Assert.True(result.Phase1.IsLocked);
        Assert.False(result.Phase1.IsAssigned);
        Assert.False(result.Phase2.Segments[0].IsOutsourced);
        var (other, _) = await RouteAsync(db);
        await Assert.ThrowsAsync<ForbiddenException>(() => service.GetAsync(other.TransportPlanId, actor.AccountId));
    }

    [Fact]
    public async Task Assignable_FiltersRoleActiveSearchPaginationAndSchedules()
    {
        await using var db = TestSupport.CreateDb();
        var manager = await EmployeeAsync(db, EmployeeRole.LOGISTICS_MANAGER);
        var driver = await EmployeeAsync(db, EmployeeRole.DRIVER);
        var free = await EmployeeAsync(db, EmployeeRole.DRIVER);
        var inactive = await EmployeeAsync(db, EmployeeRole.DRIVER); inactive.IsActive = false;
        var specialist = await EmployeeAsync(db, EmployeeRole.TRANSPORT_SPECIALIST);
        var (route, segment) = await RouteAsync(db);
        segment.DriverEmployeeId = driver.EmployeeId; await db.SaveChangesAsync();
        var service = CreateService(db);
        var query = new Request.AssignableRequest { Role = "DRIVER",
            AvailableFrom = new DateTimeOffset(DateTime.SpecifyKind(segment.PlannedDepartureAt!.Value, DateTimeKind.Utc)),
            AvailableTo = new DateTimeOffset(DateTime.SpecifyKind(segment.PlannedArrivalAt!.Value, DateTimeKind.Utc)),
            PageSize = 1 };
        var result = await service.GetAssignableAsync(query, manager.AccountId);
        Assert.Equal(free.EmployeeId, Assert.Single(result.Items).EmployeeId); Assert.Equal(1, result.TotalItems);
        route.Status = RoutePlanStatus.DRAFT; await db.SaveChangesAsync();
        Assert.Equal(2, (await service.GetAssignableAsync(query, manager.AccountId)).TotalItems);
        query.Search = driver.EmployeeCode;
        Assert.Equal(driver.EmployeeId, Assert.Single((await service.GetAssignableAsync(query, manager.AccountId)).Items).EmployeeId);
        Assert.Equal(specialist.EmployeeId, Assert.Single((await service.GetAssignableAsync(new() { Role = "TRANSPORT_SPECIALIST" }, manager.AccountId)).Items).EmployeeId);
        await Assert.ThrowsAsync<BadRequestException>(() => service.GetAssignableAsync(new() { Role = "DRIVER" }, manager.AccountId));
        await Assert.ThrowsAsync<BadRequestException>(() => service.GetAssignableAsync(new() { Role = "LOGISTICS_MANAGER" }, manager.AccountId));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.GetAssignableAsync(query, free.AccountId));
    }

    [Fact]
    public async Task Replace_RequiresInitialAssignment()
    {
        await using var db = TestSupport.CreateDb();
        var manager = await EmployeeAsync(db, EmployeeRole.LOGISTICS_MANAGER);
        var driver = await EmployeeAsync(db, EmployeeRole.DRIVER);
        var (route, segment) = await RouteAsync(db);
        var error = await Assert.ThrowsAsync<ConflictException>(() => CreateService(db).ReplaceAsync(route.RoutePlanId,
            segment.RoutePlanRouteId, new() { LastUpdatedAt = Version(route), DriverEmployeeId = driver.EmployeeId, Reason = "Replacement" }, manager.AccountId));
        Assert.Equal("SEGMENT_NOT_ASSIGNED", error.ErrorCode);
    }

    [Fact]
    public void Validators_RequireVersionPairsUniqueSegmentsReasonAndReplacement()
    {
        var assign = new AssignRequestValidator();
        Assert.False(assign.Validate(new Request.AssignRequest()).IsValid);
        var row = new Request.SegmentRequest { RoutePlanRouteId = Guid.NewGuid(), DriverEmployeeId = Guid.NewGuid() };
        Assert.False(assign.Validate(new Request.AssignRequest { LastUpdatedAt = TestClock.GetUtcNow(), Segments = [row] }).IsValid);
        row.EscortEmployeeId = row.DriverEmployeeId;
        Assert.False(assign.Validate(new Request.AssignRequest { LastUpdatedAt = TestClock.GetUtcNow(), Segments = [row] }).IsValid);
        row.EscortEmployeeId = Guid.NewGuid();
        Assert.False(assign.Validate(new Request.AssignRequest { LastUpdatedAt = TestClock.GetUtcNow(), Segments = [row, row] }).IsValid);
        Assert.False(assign.Validate(new Request.AssignRequest { LastUpdatedAt = TestClock.GetUtcNow(), Segments = null! }).IsValid);
        var replace = new ReplaceRequestValidator();
        Assert.False(replace.Validate(new Request.ReplaceRequest { DriverEmployeeId = Guid.NewGuid(), LastUpdatedAt = TestClock.GetUtcNow(), Reason = " " }).IsValid);
        Assert.False(replace.Validate(new Request.ReplaceRequest { LastUpdatedAt = TestClock.GetUtcNow(), Reason = "Sick" }).IsValid);
    }
}
