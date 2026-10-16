using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;

using Transports = Pegaxus.Service.TransportService;

namespace Pegaxus.Service.Test;

public class JourneyProgressTests
{
    [Fact]
    public async Task Overview_UsesLatestHealthPerBookingHorseAndOnlyActiveIncidentsWithoutWrites()
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        db.ChangeTracker.Clear();
        var result = await f.Service.GetOverviewAsync(f.Manager.AccountId, f.Transport.TransportId);
        Assert.Equal(f.Clock.GetUtcNow(), result.ServerTime);
        Assert.Equal(f.Segments[1].RoutePlanRouteId, result.CurrentSegment?.RoutePlanRouteId);
        Assert.Equal(2, result.CurrentSegment?.DisplayOrder);
        Assert.Equal("IN_TRANSIT", result.CurrentSegment?.Status);
        Assert.Equal(3, result.TotalSegments);
        Assert.Equal(1, result.CompletedSegments);
        Assert.Equal(new Transports.Response.HorseHealthSummary(3, 0, 1, 0, 1, 1), result.HorseHealthSummary);
        Assert.Equal(2, result.ActiveIncidents.Count);
        Assert.All(result.ActiveIncidents, item => Assert.NotNull(item.Description));
        Assert.Null(Assert.Single(result.ComplianceDossiers).DossierType);
        Assert.Equal("APPROVED", result.ComplianceDossiers[0].Status);
        Assert.True(result.CanViewGpsTab);
        Assert.Equal(TimeSpan.Zero, result.DepartureAt?.Offset);
        Assert.Equal(f.Segments[^1].PlannedArrivalAt, result.EtaAt?.UtcDateTime);
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Empty(await db.Notifications.ToListAsync());
        Assert.Empty(await db.OutboxMessages.ToListAsync());
    }

    [Fact]
    public async Task Backup_UsesExecutedPrimaryHistoryThenMixedBackupFromBranchAndRetainsCompletedJourney()
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        f.Segments[1].SupersededAt = Fixture.Day(4);
        f.Segments[2].SupersededAt = Fixture.Day(4);
        var backup = f.AddPlan(db, RoutePlanStatus.CONFIRMED);
        var copy = f.AddSegment(db, backup, 1, RouteType.PRIMARY, f.Segments[0].Route);
        var branch = f.AddSegment(db, backup, 2, RouteType.BACKUP, f.Segments[1].Route);
        var tail = f.AddSegment(db, backup, 3, RouteType.PRIMARY, f.Segments[2].Route);
        await db.SaveChangesAsync();
        var before = await f.Service.GetSegmentsAsync(f.Manager.AccountId, f.Transport.TransportId);
        Assert.Equal(f.Segments.Take(2).Select(item => item.RoutePlanRouteId), before.Segments.Select(item => item.RoutePlanRouteId));
        backup.Status = RoutePlanStatus.ACTIVE;
        branch.ActualDepartureAt = Fixture.Day(5);
        await db.SaveChangesAsync();
        var result = await f.Service.GetSegmentsAsync(f.Manager.AccountId, f.Transport.TransportId);
        Assert.Equal(new[] { f.Segments[0].RoutePlanRouteId, f.Segments[1].RoutePlanRouteId, branch.RoutePlanRouteId, tail.RoutePlanRouteId },
            result.Segments.Select(item => item.RoutePlanRouteId));
        Assert.DoesNotContain(result.Segments, item => item.RoutePlanRouteId == copy.RoutePlanRouteId);
        Assert.Equal(new[] { 1, 2, 3, 4 }, result.Segments.Select(item => item.DisplayOrder));
        Assert.Equal(5, result.HandoverBoundaries.Count);
        Assert.Equal(f.Primary.RoutePlanId, result.Segments[1].RoutePlanId);
        Assert.Equal(backup.RoutePlanId, result.Segments[2].RoutePlanId);
        f.Transport.Status = TransportStatus.COMPLETED;
        f.Primary.Status = RoutePlanStatus.COMPLETED;
        backup.Status = RoutePlanStatus.COMPLETED;
        foreach (var segment in new[] { f.Segments[1], branch, tail })
        {
            segment.ActualDepartureAt ??= Fixture.Day(5);
            segment.ActualArrivalAt = Fixture.Day(6);
        }
        await db.SaveChangesAsync();
        result = await f.Service.GetSegmentsAsync(f.Manager.AccountId, f.Transport.TransportId);
        Assert.Equal(tail.RoutePlanRouteId, Assert.Single(result.Segments.Where(item => item.IsCurrent)).RoutePlanRouteId);
        Assert.Equal(4, result.Segments.Count);
        Assert.All(result.HandoverBoundaries, item => Assert.False(item.CanCreate));
    }

    [Fact]
    public async Task Return_AppendsAllReturnSegmentsAndIgnoresDraftOrUnactivatedAlternatives()
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        var draft = f.AddPlan(db, RoutePlanStatus.DRAFT);
        f.AddSegment(db, draft, 1, RouteType.BACKUP, f.Segments[0].Route);
        var returning = f.AddPlan(db, RoutePlanStatus.ACTIVE);
        var segment = f.AddSegment(db, returning, 1, RouteType.RETURN, f.Segments[0].Route);
        segment.ActualDepartureAt = Fixture.Day(5);
        f.Segments[1].ActualArrivalAt = Fixture.Day(4);
        f.Segments[2].SupersededAt = Fixture.Day(4);
        await db.SaveChangesAsync();
        var result = await f.Service.GetOverviewAsync(f.Manager.AccountId, f.Transport.TransportId);
        Assert.Equal(3, result.TotalSegments);
        Assert.Equal("RETURN", result.RouteType);
        Assert.Equal(segment.RoutePlanRouteId, result.CurrentSegment?.RoutePlanRouteId);
        Assert.Equal(returning.RoutePlanId, result.RoutePlan?.RoutePlanId);
    }

    [Fact]
    public async Task CustomerVisibilityAndCrewPermissionsAreCheckedBeforeTransportState()
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        var owner = await f.Service.GetOverviewAsync(f.Customer.AccountId, f.Transport.TransportId);
        Assert.Null(owner.CurrentSegment?.Driver?.Phone);
        Assert.Null(owner.CurrentSegment?.Escort?.Phone);
        Assert.Empty(owner.CurrentSegment!.Vehicles);
        Assert.Null(owner.CurrentSegment.Note);
        Assert.All(owner.ActiveIncidents, item => { Assert.Null(item.Description); Assert.Null(item.Severity); });
        var segments = await f.Service.GetSegmentsAsync(f.Customer.AccountId, f.Transport.TransportId);
        Assert.NotNull(segments.HandoverBoundaries[0].ExistingHandover);
        Assert.Null(segments.HandoverBoundaries[1].ExistingHandover);
        Assert.All(segments.HandoverBoundaries, item => Assert.False(item.CanCreate));
        f.Transport.Status = TransportStatus.CANCELLED;
        await db.SaveChangesAsync();
        var missing = await Assert.ThrowsAsync<NotFoundException>(() => f.Service.GetOverviewAsync(f.OtherCustomer.AccountId, f.Transport.TransportId));
        Assert.Equal("TRANSPORT_NOT_FOUND", missing.ErrorCode);
        var unassigned = await Fixture.EmployeeAsync(db, EmployeeRole.DRIVER);
        var forbidden = await Assert.ThrowsAsync<ForbiddenException>(() => f.Service.GetSegmentsAsync(unassigned.AccountId, f.Transport.TransportId));
        Assert.Equal("FORBIDDEN", forbidden.ErrorCode);
        var conflict = await Assert.ThrowsAsync<ConflictException>(() => f.Service.GetOverviewAsync(f.Customer.AccountId, f.Transport.TransportId));
        Assert.Equal("INVALID_TRANSPORT_STATUS", conflict.ErrorCode);
    }

    [Fact]
    public async Task CrewAssignment_ExcludesUnstartedSupersededSegmentsButKeepsExecutedAssignments()
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        f.Segments[0].DriverEmployeeId = f.OtherDriver.EmployeeId;
        f.Segments[1].DriverEmployeeId = f.OtherDriver.EmployeeId;
        f.Segments[2].DriverEmployeeId = f.Driver.EmployeeId;
        f.Segments[2].SupersededAt = Fixture.Day(4);
        await db.SaveChangesAsync();
        Assert.Empty((await f.Service.GetAssignedAsync(f.Driver.AccountId, new())).Items);
        await Assert.ThrowsAsync<ForbiddenException>(() => f.Service.GetSegmentsAsync(f.Driver.AccountId, f.Transport.TransportId));
        f.Segments[2].ActualDepartureAt = Fixture.Day(5);
        await db.SaveChangesAsync();
        Assert.Equal("DRIVER", Assert.Single((await f.Service.GetAssignedAsync(f.Driver.AccountId, new())).Items).MyRole);
        Assert.False((await f.Service.GetOverviewAsync(f.Driver.AccountId, f.Transport.TransportId)).CanViewGpsTab);
        Assert.Single((await f.Service.GetAssignedAsync(f.Escort.AccountId, new())).Items);
        foreach (var employee in new[] { f.Manager, f.Coordinator, f.Specialist })
        {
            Assert.Single((await f.Service.GetOngoingAsync(employee.AccountId, new())).Items);
            await f.Service.GetOverviewAsync(employee.AccountId, f.Transport.TransportId);
        }
        await Assert.ThrowsAsync<ForbiddenException>(() => f.Service.GetOngoingAsync(f.Driver.AccountId, new()));
        await Assert.ThrowsAsync<ForbiddenException>(() => f.Service.GetAssignedAsync(f.Manager.AccountId, new()));
    }

    [Fact]
    public async Task Boundaries_UseLatestNonCancelledRecordAndRestrictCreationToResponsibleCrew()
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        var cancelled = f.AddHandover(db, HandoverType.TRANSFER, HandoverStatus.CANCELLED, f.Segments[1], f.Segments[2], 3);
        await db.SaveChangesAsync();
        var result = await f.Service.GetSegmentsAsync(f.Driver.AccountId, f.Transport.TransportId);
        Assert.Equal(4, result.HandoverBoundaries.Count);
        Assert.True(result.HandoverBoundaries[2].CanCreate);
        Assert.False(result.HandoverBoundaries[3].CanCreate);
        Assert.Null(result.HandoverBoundaries[2].ExistingHandover);
        Assert.Equal(new[] { "TRANSFER", "BORDER_HANDOVER" }, result.HandoverBoundaries[2].AllowedHandoverTypes);
        Assert.False((await f.Service.GetSegmentsAsync(f.Specialist.AccountId, f.Transport.TransportId)).HandoverBoundaries[2].CanCreate);
        Assert.True((await f.Service.GetSegmentsAsync(f.Coordinator.AccountId, f.Transport.TransportId)).HandoverBoundaries[3].CanCreate);
        var live = f.AddHandover(db, HandoverType.BORDER_HANDOVER, HandoverStatus.IN_PROGRESS, f.Segments[1], f.Segments[2], 4);
        await db.SaveChangesAsync();
        result = await f.Service.GetSegmentsAsync(f.Manager.AccountId, f.Transport.TransportId);
        Assert.Equal(live.HandoverRecordId, result.HandoverBoundaries[2].ExistingHandover?.HandoverRecordId);
        Assert.False(result.HandoverBoundaries[2].CanCreate);
        Assert.NotEqual(cancelled.HandoverRecordId, result.HandoverBoundaries[2].ExistingHandover?.HandoverRecordId);
    }

    [Fact]
    public async Task OutsourcedSegments_HideInternalResourcesAndDisableProviderHandovers()
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        var provider = new TransportProvider { TransportProviderId = Guid.NewGuid(), ProviderCode = "PRV-1", ProviderName = "Carrier",
            Status = ProviderStatus.ACTIVE, SupportedModes = "ROAD", CreatedAt = Fixture.Day(1), UpdatedAt = Fixture.Day(1) };
        db.Add(provider);
        f.Segments[1].TransportProviderId = provider.TransportProviderId;
        f.Segments[1].ServiceType = "FERRY";
        f.Segments[1].ProviderReference = "FERRY-1";
        await db.SaveChangesAsync();
        var result = await f.Service.GetSegmentsAsync(f.Manager.AccountId, f.Transport.TransportId);
        Assert.True(result.Segments[1].IsOutsourced);
        Assert.Equal("FERRY-1", result.Segments[1].Provider?.ProviderReference);
        Assert.Null(result.Segments[1].Driver);
        Assert.Null(result.Segments[1].Escort);
        Assert.Empty(result.Segments[1].Vehicles);
        Assert.All(result.HandoverBoundaries.Skip(1).Take(2), item => { Assert.False(item.CanCreate); Assert.Empty(item.AllowedHandoverTypes); });
    }

    [Theory]
    [InlineData(TransportStatus.CREATED)]
    [InlineData(TransportStatus.PLANNING)]
    [InlineData(TransportStatus.CANCELLED)]
    public async Task Read_RejectsUnsupportedTransportStatuses(TransportStatus status)
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        f.Transport.Status = status;
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ConflictException>(() => f.Service.GetOverviewAsync(f.Manager.AccountId, f.Transport.TransportId));
        await Assert.ThrowsAsync<ConflictException>(() => f.Service.GetSegmentsAsync(f.Manager.AccountId, f.Transport.TransportId));
        Assert.Empty((await f.Service.GetOngoingAsync(f.Manager.AccountId, new())).Items);
    }

    [Fact]
    public async Task Lists_FilterAndPageInDatabaseWithZeroBasedPagesAndReadyFallback()
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        var first = await f.Service.GetOngoingAsync(f.Manager.AccountId, new() { Search = "tEsT cUsToMeR", HasActiveIncident = true, PageSize = 1 });
        Assert.Single(first.Items);
        Assert.Equal(0, first.PageIndex);
        Assert.Equal(1, first.TotalItems);
        Assert.Equal(1, first.TotalPages);
        Assert.Empty((await f.Service.GetOngoingAsync(f.Manager.AccountId, new() { Search = "no-match" })).Items);
        Assert.Empty((await f.Service.GetOngoingAsync(f.Manager.AccountId, new() { Status = "COMPLETED" })).Items);
        var next = await f.Service.GetOngoingAsync(f.Manager.AccountId, new() { PageIndex = 1 });
        Assert.Empty(next.Items);
        Assert.Equal(1, next.TotalItems);
        f.Transport.Status = TransportStatus.READY;
        f.Transport.ActualDepartureAt = null;
        f.Primary.Status = RoutePlanStatus.CONFIRMED;
        foreach (var segment in f.Segments) { segment.ActualArrivalAt = null; segment.ActualDepartureAt = null; }
        await db.SaveChangesAsync();
        var ready = await f.Service.GetOverviewAsync(f.Manager.AccountId, f.Transport.TransportId);
        Assert.Equal(f.Segments[0].RoutePlanRouteId, ready.CurrentSegment?.RoutePlanRouteId);
        Assert.Equal("PLANNED", ready.CurrentSegment?.Status);
        Assert.Equal(f.Segments[0].PlannedDepartureAt, ready.DepartureAt?.UtcDateTime);
        var empty = new Transports.Request.ListRequest { PageIndex = -1, PageSize = 101, Status = "1", SortBy = "updatedAt:desc" };
        var invalid = await Assert.ThrowsAsync<FieldValidationException>(() => f.Service.GetOngoingAsync(f.Manager.AccountId, empty));
        Assert.Equal(new[] { "pageIndex", "pageSize", "sortBy", "status" }, invalid.Errors.Select(item => item.Field).Order());
        await Assert.ThrowsAsync<FieldValidationException>(() => f.Service.GetOngoingAsync(f.Manager.AccountId, new() { PageIndex = int.MaxValue }));
        foreach (var status in new[] { "", "READY,", "CANCELLED", "READY,CREATED", "2", "ready" })
            await Assert.ThrowsAsync<FieldValidationException>(() => f.Service.GetAssignedAsync(f.Driver.AccountId, new() { Status = status }));
    }

    [Theory]
    [InlineData(AccountType.CUSTOMER)]
    [InlineData(AccountType.EMPLOYEE)]
    public async Task Reads_RejectDeletedAccountsAndInactiveProfilesAsync(AccountType type)
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        var accountId = type == AccountType.CUSTOMER ? f.Customer.AccountId : f.Manager.AccountId;
        var account = await db.Accounts.SingleAsync(item => item.AccountId == accountId);
        account.IsDeleted = true;
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<UnauthorizedException>(() => f.Service.GetSegmentsAsync(accountId, f.Transport.TransportId));
        account.IsDeleted = false;
        if (type == AccountType.CUSTOMER) f.Customer.IsDeleted = true;
        else f.Manager.IsActive = false;
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<UnauthorizedException>(() => f.Service.GetOverviewAsync(accountId, f.Transport.TransportId));
    }

    internal sealed class Fixture
    {
        internal required Transports.Service Service { get; init; }
        internal TimeProvider Clock { get; } = new TestClock();
        internal required Customer Customer { get; init; }
        internal required Customer OtherCustomer { get; init; }
        internal required Employee Manager { get; init; }
        internal required Employee Coordinator { get; init; }
        internal required Employee Specialist { get; init; }
        internal required Employee Driver { get; init; }
        internal required Employee OtherDriver { get; init; }
        internal required Employee Escort { get; init; }
        internal required Transport Transport { get; init; }
        internal required TransportPlan Plan { get; init; }
        internal required RoutePlan Primary { get; init; }
        internal List<RoutePlanRoute> Segments { get; } = [];
        internal static DateTime Day(int day) => new(2026, 10, day, 8, 0, 0, DateTimeKind.Unspecified);

        internal static async Task<Fixture> CreateAsync(AppDbContext db)
        {
            var (_, customer) = await TestSupport.AddCustomerAsync(db);
            var (_, otherCustomer) = await TestSupport.AddCustomerAsync(db);
            var (booking, horse) = await TestSupport.AddPendingBookingAsync(db, customer, true);
            var manager = await EmployeeAsync(db, EmployeeRole.LOGISTICS_MANAGER);
            var coordinator = await EmployeeAsync(db, EmployeeRole.FLEET_ROUTE_COORDINATOR);
            var specialist = await EmployeeAsync(db, EmployeeRole.TRANSPORT_SPECIALIST);
            var driver = await EmployeeAsync(db, EmployeeRole.DRIVER);
            var otherDriver = await EmployeeAsync(db, EmployeeRole.DRIVER);
            var escort = await EmployeeAsync(db, EmployeeRole.ESCORT);
            var transport = new Transport { TransportId = Guid.NewGuid(), TransportCode = "TR-JOURNEY-1", Booking = booking,
                BookingId = booking.BookingId, Status = TransportStatus.IN_TRANSIT, ActualDepartureAt = Day(1), CreatedAt = Day(1), UpdatedAt = Day(1) };
            var plan = new TransportPlan { TransportPlanId = Guid.NewGuid(), PlanCode = "TP-JOURNEY-1", Transport = transport,
                TransportId = transport.TransportId, Status = TransportPlanStatus.IN_PROGRESS, CreatedAt = Day(1), UpdatedAt = Day(1) };
            var primary = new RoutePlan { RoutePlanId = Guid.NewGuid(), RoutePlanCode = "RP-JOURNEY-1", TransportPlan = plan,
                TransportPlanId = plan.TransportPlanId, Status = RoutePlanStatus.ACTIVE, CreatedAt = Day(1), UpdatedAt = Day(1) };
            db.AddRange(transport, plan, primary);
            var assignedValidator = new Transports.Validator.AssignedRequestValidator();
            var f = new Fixture { Customer = customer, OtherCustomer = otherCustomer, Manager = manager, Coordinator = coordinator,
                Specialist = specialist, Driver = driver, OtherDriver = otherDriver, Escort = escort, Transport = transport, Plan = plan,
                Primary = primary, Service = new(db, new TestClock(), new Transports.Validator.ListRequestValidator(assignedValidator), assignedValidator) };
            var locations = Enumerable.Range(0, 4).Select(index => new Location { LocationId = Guid.NewGuid(), LocationName = $"Stop {index}",
                Address = $"Address {index}", Country = "France", LocationType = LocationType.REST_STOP, Latitude = 49 + index,
                Longitude = 2 + index, CreatedAt = Day(1), UpdatedAt = Day(1) }).ToArray();
            for (var index = 0; index < 3; index++)
            {
                var route = new Pegaxus.Repository.Entities.Route { RouteId = Guid.NewGuid(), RouteCode = $"R-JOURNEY-{index}",
                    FromLocation = locations[index], FromLocationId = locations[index].LocationId, ToLocation = locations[index + 1],
                    ToLocationId = locations[index + 1].LocationId, TransportMode = TransportMode.ROAD, DistanceKm = 100,
                    CreatedAt = Day(1), UpdatedAt = Day(1) };
                var segment = f.AddSegment(db, primary, index + 1, RouteType.PRIMARY, route);
                if (index < 2) segment.ActualDepartureAt = Day(index + 1);
                if (index == 0) segment.ActualArrivalAt = Day(2);
                if (index == 2) segment.DriverEmployeeId = otherDriver.EmployeeId;
                f.Segments.Add(segment);
            }
            var vehicle = new Vehicle { VehicleId = Guid.NewGuid(), VehicleCode = "V-JOURNEY-1", LicensePlate = "TEST-123",
                TransportMode = TransportMode.ROAD, Status = VehicleStatus.IN_USE, CreatedAt = Day(1), UpdatedAt = Day(1) };
            db.Add(new RoutePlanRouteVehicle { RoutePlanRouteId = f.Segments[1].RoutePlanRouteId, RoutePlanRoute = f.Segments[1],
                VehicleId = vehicle.VehicleId, Vehicle = vehicle });
            var horses = new List<Horse> { horse! };
            for (var index = 0; index < 2; index++)
            {
                var extra = new Horse { HorseId = Guid.NewGuid(), HorseName = $"Horse {index}", CustomerId = customer.CustomerId,
                    Customer = customer, CreatedAt = Day(1), UpdatedAt = Day(1) };
                db.Add(new BookingHorse { BookingId = booking.BookingId, Booking = booking, Horse = extra, HorseId = extra.HorseId, CreatedAt = Day(1) });
                horses.Add(extra);
            }
            foreach (var (index, day, status) in new[] { (0, 1, HorseHealthStatus.NORMAL), (0, 3, HorseHealthStatus.CRITICAL), (1, 2, HorseHealthStatus.OBSERVATION_REQUIRED) })
                db.Add(new HorseHealthLog { HorseHealthLogId = Guid.NewGuid(), HorseId = horses[index].HorseId, Horse = horses[index],
                    TransportId = transport.TransportId, EmployeeId = escort.EmployeeId, CheckedAt = Day(day), HealthStatus = status,
                    LogType = HorseHealthLogType.ROUTINE_CHECK, CreatedAt = Day(day), UpdatedAt = Day(day) });
            foreach (var status in new[] { IncidentStatus.REPORTED, IncidentStatus.PENDING, IncidentStatus.RESOLVED })
                db.Add(new IncidentReport { IncidentReportId = Guid.NewGuid(), IncidentCode = $"INC-JOURNEY-{status}", TransportId = transport.TransportId,
                    EmployeeId = escort.EmployeeId, LocationId = locations[0].LocationId, Title = "Health incident", Description = "Internal incident detail",
                    Category = IncidentCategory.HORSE_HEALTH, Severity = IncidentSeverity.CRITICAL, Status = status,
                    OccurredAt = Day(3), ReportedAt = Day(3), CreatedAt = Day(3), UpdatedAt = Day(3) });
            db.Add(new ComplianceDossier { ComplianceDossierId = Guid.NewGuid(), TransportId = transport.TransportId,
                DossierCode = "CDOS-JOURNEY-1", Status = ComplianceDossierStatus.APPROVED, OpenedAt = Day(1), CreatedAt = Day(1), UpdatedAt = Day(1) });
            f.AddHandover(db, HandoverType.PICKUP, HandoverStatus.COMPLETED, null, f.Segments[0], 1);
            f.AddHandover(db, HandoverType.TRANSFER, HandoverStatus.PLANNED, f.Segments[0], f.Segments[1], 2);
            await db.SaveChangesAsync();
            return f;
        }

        internal static async Task<Employee> EmployeeAsync(AppDbContext db, EmployeeRole role)
        {
            var (_, employee) = await TestSupport.AddLogisticsManagerAsync(db);
            employee.Role = role;
            employee.Phone = "private-phone";
            await db.SaveChangesAsync();
            return employee;
        }

        internal RoutePlan AddPlan(AppDbContext db, RoutePlanStatus status)
        {
            var plan = new RoutePlan { RoutePlanId = Guid.NewGuid(), RoutePlanCode = $"RP-{Guid.NewGuid():N}",
                TransportPlanId = Plan.TransportPlanId, PrimaryRoutePlanId = Primary.RoutePlanId, Status = status,
                CreatedAt = Day(4), UpdatedAt = Day(4) };
            db.Add(plan);
            return plan;
        }

        internal RoutePlanRoute AddSegment(AppDbContext db, RoutePlan plan, int sequence, RouteType type, Pegaxus.Repository.Entities.Route route)
        {
            var segment = new RoutePlanRoute { RoutePlanRouteId = Guid.NewGuid(), RoutePlanId = plan.RoutePlanId, RoutePlan = plan,
                RouteId = route.RouteId, Route = route, SequenceNo = sequence, RouteType = type, DriverEmployeeId = Driver.EmployeeId,
                EscortEmployeeId = Escort.EmployeeId, Note = "Internal segment note", PlannedDepartureAt = Day(sequence),
                PlannedArrivalAt = Day(sequence + 1), CreatedAt = Day(1), UpdatedAt = Day(1) };
            db.Add(segment);
            return segment;
        }

        internal HandoverRecord AddHandover(AppDbContext db, HandoverType type, HandoverStatus status, RoutePlanRoute? from, RoutePlanRoute? to, int sequence)
        {
            var record = new HandoverRecord { HandoverRecordId = Guid.NewGuid(), HandoverCode = $"HO-JOURNEY-{sequence}", TransportId = Transport.TransportId,
                SequenceNo = sequence, HandoverType = type, Status = status, FromRoutePlanRouteId = from?.RoutePlanRouteId,
                ToRoutePlanRouteId = to?.RoutePlanRouteId, FromType = from is null ? HandoverPartyType.CUSTOMER : HandoverPartyType.EMPLOYEE,
                FromCustomerId = from is null ? Customer.CustomerId : null, FromEmployeeId = from?.DriverEmployeeId,
                ToType = to is null ? HandoverPartyType.CUSTOMER : HandoverPartyType.EMPLOYEE,
                ToCustomerId = to is null ? Customer.CustomerId : null, ToEmployeeId = to?.DriverEmployeeId, CreatedAt = Day(sequence), UpdatedAt = Day(sequence) };
            db.Add(record);
            return record;
        }

        private sealed class TestClock : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => new(2026, 10, 16, 8, 0, 0, TimeSpan.Zero);
        }
    }
}
