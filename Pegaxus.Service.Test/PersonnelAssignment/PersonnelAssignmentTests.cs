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
            new EmployeeTransportPlan { EmployeeId = coordinator.EmployeeId, TransportPlanId = route.TransportPlanId, Responsibility = "ROUTE_COORDINATOR", IsPrimary = true });
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
        Assert.Equal("ROUTE_NOT_APPROVED", (await Assert.ThrowsAsync<UnprocessableEntityException>(() => service.AssignAsync(route.RoutePlanId, Assign(route, segment, driver, escort), manager.AccountId))).ErrorCode);
        route.Status = RoutePlanStatus.CONFIRMED; await db.SaveChangesAsync();
        Assert.True((await service.GetAsync(route.TransportPlanId, manager.AccountId)).Phase2.Enabled);
        segment.RouteType = RouteType.BACKUP; await db.SaveChangesAsync();
        Assert.False((await service.GetAsync(route.TransportPlanId, manager.AccountId)).Phase2.Enabled);
        Assert.Equal("ROUTE_NOT_APPROVED", (await Assert.ThrowsAsync<UnprocessableEntityException>(() => service.AssignAsync(route.RoutePlanId, Assign(route, segment, driver, escort), manager.AccountId))).ErrorCode);
    }

    [Fact]
    public async Task Assign_InternalAndOutsource_FullAtomicLinksAuditNotifications_RetryNoDuplicates()
    {
        await using var db = TestSupport.CreateDb();
        var manager = await EmployeeAsync(db, EmployeeRole.LOGISTICS_MANAGER);
        var driver = await EmployeeAsync(db, EmployeeRole.DRIVER); var escort = await EmployeeAsync(db, EmployeeRole.ESCORT);
        var (route, segment) = await RouteAsync(db);
        var outsource = Segment(route, 2); outsource.TransportProvider = Provider(); outsource.Route.TransportMode = TransportMode.AIR;
        db.Add(outsource); await db.SaveChangesAsync();
        var service = CreateService(db);
        var result = await service.AssignAsync(route.RoutePlanId, Assign(route, segment, driver, escort), manager.AccountId);
        Assert.Equal("FULLY_ASSIGNED", result.PersonnelStatus);
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
        Assert.Equal(3, await db.OutboxMessages.CountAsync()); // InMemory queues emails; relational additionally queues SignalR.
        var providerMail = await db.OutboxMessages.SingleAsync(x => x.Payload.Contains("provider@example.com"));
        Assert.DoesNotContain(driver.EmployeeCode, providerMail.Payload);
        Assert.DoesNotContain("Segment 1", providerMail.Payload);
        Assert.Equal(TransportStatus.CREATED, route.TransportPlan.Transport.Status);
        Assert.True(result.UpdatedAt > TestClock.GetUtcNow());
        await service.AssignAsync(route.RoutePlanId, Assign(route, segment, driver, escort), manager.AccountId);
        Assert.Single(db.CatalogAuditLogs); Assert.Equal(3, await db.OutboxMessages.CountAsync());
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
        var partial = await service.AssignAsync(route.RoutePlanId, Assign(route, first, driver, escort), manager.AccountId);
        Assert.Equal("PARTIALLY_ASSIGNED", partial.PersonnelStatus); Assert.Null(partial.Segments[1].Driver);
        await service.AssignAsync(route.RoutePlanId, Assign(route, second, driver, escort), manager.AccountId);
        var oldVersion = partial.UpdatedAt;
        Assert.Equal("ROUTE_PLAN_MODIFIED", (await Assert.ThrowsAsync<ConflictException>(() => service.ReplaceAsync(route.RoutePlanId, first.RoutePlanRouteId,
            new() { LastUpdatedAt = oldVersion, DriverEmployeeId = replacement.EmployeeId, Reason = "Sick" }, manager.AccountId))).ErrorCode);
        var replaced = await service.ReplaceAsync(route.RoutePlanId, first.RoutePlanRouteId, new() { LastUpdatedAt = Version(route), DriverEmployeeId = replacement.EmployeeId, Reason = "<b>Sick</b>" }, manager.AccountId);
        Assert.Equal(replacement.EmployeeId, replaced.Segment.Driver!.EmployeeId); Assert.Equal(escort.EmployeeId, replaced.Segment.Escort!.EmployeeId);
        Assert.Equal("FULLY_ASSIGNED", replaced.PersonnelStatus);
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
        Assert.Equal("SCHEDULE_CONFLICT", (await Assert.ThrowsAsync<ConflictException>(() => service.AssignAsync(route.RoutePlanId, request, manager.AccountId))).ErrorCode);
        Assert.Empty(db.EmployeeTransportPlans); Assert.Empty(db.CatalogAuditLogs);
        second.PlannedDepartureAt = null; await db.SaveChangesAsync();
        Assert.Equal("INVALID_SEGMENT_SCHEDULE", (await Assert.ThrowsAsync<BadRequestException>(() => service.AssignAsync(route.RoutePlanId, request, manager.AccountId))).ErrorCode);
        second.PlannedDepartureAt = first.PlannedArrivalAt!.Value.AddMinutes(1); await db.SaveChangesAsync();
        Assert.Equal("FULLY_ASSIGNED", (await service.AssignAsync(route.RoutePlanId, request, manager.AccountId)).PersonnelStatus);
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
        Assert.Equal(invalid == "role" ? "INVALID_ROLE_ASSIGNMENT" : "EMPLOYEE_NOT_FOUND", Assert.IsAssignableFrom<Pegaxus.Service.Exceptions.Exceptions>(error).ErrorCode);
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
        Assert.Equal("FULLY_ASSIGNED", result.PersonnelStatus); Assert.Empty(db.EmployeeTransportPlans);
        Assert.Single(db.OutboxMessages); Assert.Single(db.CatalogAuditLogs);
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
        Assert.Equal("SCHEDULE_CONFLICT", (await Assert.ThrowsAsync<ConflictException>(() => service.AssignAsync(route.RoutePlanId, Assign(route, segment, driver, escort), manager.AccountId))).ErrorCode);
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
            case "plan-completed": route.TransportPlan.Status = TransportPlanStatus.COMPLETED; break;
            case "plan-cancelled": route.TransportPlan.Status = TransportPlanStatus.CANCELLED; break;
            case "transport-completed": route.TransportPlan.Transport.Status = TransportStatus.COMPLETED; break;
            case "transport-cancelled": route.TransportPlan.Transport.Status = TransportStatus.CANCELLED; break;
            case "transport-started": route.TransportPlan.Transport.Status = TransportStatus.IN_TRANSIT; break;
        }
        await db.SaveChangesAsync(); var service = CreateService(db);
        Assert.False((await service.GetAsync(route.TransportPlanId, manager.AccountId)).Phase2.Enabled);
        Assert.Equal("PLAN_LOCKED", (await Assert.ThrowsAsync<ConflictException>(() => service.AssignAsync(route.RoutePlanId, Assign(route, segment, driver, escort), manager.AccountId))).ErrorCode);
        Assert.Empty(db.CatalogAuditLogs);
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
