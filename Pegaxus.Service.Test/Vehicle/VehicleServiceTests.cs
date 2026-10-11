using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.NotificationService;
using Pegaxus.Service.VehicleService;
using Pegaxus.Service.VehicleService.Validator;
using FleetService = Pegaxus.Service.VehicleService.Service;
using Request = Pegaxus.Service.VehicleService.Request;
using Response = Pegaxus.Service.VehicleService.Response;

namespace Pegaxus.Service.Test;

public class VehicleServiceTests
{
    internal sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
    }
    internal static readonly Clock TestClock = new();
    internal static readonly DateOnly Today = new(2026, 10, 8);
    internal static FleetService CreateService(AppDbContext db) => new(db, TestClock,
        new ListRequestValidator(), new CreateRequestValidator(TestClock), new UpdateRequestValidator(TestClock),
        new StatusRequestValidator(), new Pegaxus.Service.NotificationService.Service(db, new NoopNotificationRealtimeService()),
        Microsoft.Extensions.Logging.Abstractions.NullLogger<FleetService>.Instance);
    internal static Request.CreateRequest ValidRequest(string code = "VEH-ROAD-005", string plate = "29C-99988") => new()
    {
        VehicleCode = code, LicensePlate = plate, VehicleName = "Horse Truck 05", VehicleType = "HORSE_TRUCK",
        Manufacturer = "MAN", Model = "TGX", CapacityHorses = 6,
        RegistrationNumber = "REG-005", RegistrationExpiryDate = Today.AddYears(1),
        InsuranceNumber = "INS-005", InsuranceExpiryDate = Today.AddYears(1), SpecialEquipment = "Ventilation, CCTV"
    };
    internal static Request.UpdateRequest UpdateRequest(Response.VehicleResponse vehicle) => new()
    {
        LastUpdatedAt = vehicle.UpdatedAt, VehicleName = "Renamed Truck", LicensePlate = vehicle.LicensePlate!,
        CapacityHorses = 9, Manufacturer = "Volvo", Model = "FH16", VehicleType = "HORSE_TRUCK",
        RegistrationNumber = "REG-RENEWED", RegistrationExpiryDate = Today.AddYears(1),
        InsuranceNumber = "INS-RENEWED", InsuranceExpiryDate = Today.AddYears(1), SpecialEquipment = "New CCTV"
    };
    internal static Request.StatusRequest StatusRequest(DateTimeOffset version, string status = "MAINTENANCE") => new()
    { LastUpdatedAt = version, Status = status, Reason = "Scheduled maintenance" };

    [Fact]
    public async Task Create_DefaultsRoadAvailable_PreservesAllFields_AndAudits()
    {
        await using var db = TestSupport.CreateDb();
        var (account, employee) = await TestSupport.AddLogisticsManagerAsync(db);
        employee.Role = EmployeeRole.FLEET_ROUTE_COORDINATOR;
        await db.SaveChangesAsync();
        var request = ValidRequest("veh-road-005", " 29c-99988 ");
        var result = await CreateService(db).CreateAsync(request, account.AccountId);
        var vehicle = await db.Vehicles.SingleAsync();
        Assert.Equal("ROAD", result.TransportMode);
        Assert.Equal("AVAILABLE", result.Status);
        Assert.Equal("VEH-ROAD-005", result.VehicleCode);
        Assert.Equal("29C-99988", result.LicensePlate);
        Assert.Equal(request.VehicleName, result.VehicleName);
        Assert.Equal(request.VehicleType, result.VehicleType);
        Assert.Equal(request.Manufacturer, result.Manufacturer);
        Assert.Equal(request.Model, result.Model);
        Assert.Equal(request.CapacityHorses, result.CapacityHorses);
        Assert.Equal(request.RegistrationNumber, result.RegistrationNumber);
        Assert.Equal(request.RegistrationExpiryDate, result.RegistrationExpiryDate);
        Assert.Equal(request.InsuranceNumber, result.InsuranceNumber);
        Assert.Equal(request.InsuranceExpiryDate, result.InsuranceExpiryDate);
        Assert.Equal(request.SpecialEquipment, result.SpecialEquipment);
        Assert.False(vehicle.IsDeleted);
        Assert.Equal(result.CreatedAt, result.UpdatedAt);
        Assert.Null(result.IsAvailable);
        Assert.Empty(result.Warnings);
        var audit = await db.CatalogAuditLogs.SingleAsync();
        Assert.Equal(account.AccountId, audit.ActorAccountId);
        Assert.Equal("CREATE", audit.ActionType);
        Assert.Equal("VEH-ROAD-005", audit.AfterData.RootElement.GetProperty("vehicleCode").GetString());
        Assert.Equal(vehicle.VehicleId, (await db.Notifications.SingleAsync()).ReferenceId);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(null)]
    public async Task Capacity_RejectsInvalidValues_OnCreateAndUpdate(int? capacity)
    {
        await using var db = TestSupport.CreateDb();
        var (account, _) = await TestSupport.AddLogisticsManagerAsync(db);
        var service = CreateService(db);
        var result = await service.CreateAsync(ValidRequest(), account.AccountId);
        var create = ValidRequest("INVALID", "BAD-01"); create.CapacityHorses = capacity;
        var update = UpdateRequest(result); update.CapacityHorses = capacity;
        foreach (var error in new[] {
            await Assert.ThrowsAsync<FieldValidationException>(() => service.CreateAsync(create, account.AccountId)),
            await Assert.ThrowsAsync<FieldValidationException>(() => service.UpdateAsync(result.VehicleId, update, account.AccountId)) })
            Assert.Contains(error.Errors, x => x.Field == "capacityHorses");
        Assert.Single(db.Vehicles);
        Assert.Single(db.CatalogAuditLogs);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public async Task Expiry_RejectsPastAndToday_OnCreateAndUpdate(int days)
    {
        await using var db = TestSupport.CreateDb();
        var (account, _) = await TestSupport.AddLogisticsManagerAsync(db);
        var service = CreateService(db);
        var vehicle = await service.CreateAsync(ValidRequest(), account.AccountId);
        var create = ValidRequest("INVALID", "BAD-01");
        create.RegistrationExpiryDate = create.InsuranceExpiryDate = Today.AddDays(days);
        var update = UpdateRequest(vehicle);
        update.RegistrationExpiryDate = update.InsuranceExpiryDate = Today.AddDays(days);
        foreach (var error in new[] {
            await Assert.ThrowsAsync<FieldValidationException>(() => service.CreateAsync(create, account.AccountId)),
            await Assert.ThrowsAsync<FieldValidationException>(() => service.UpdateAsync(vehicle.VehicleId, update, account.AccountId)) })
        {
            Assert.Contains(error.Errors, x => x.Field == "registrationExpiryDate");
            Assert.Contains(error.Errors, x => x.Field == "insuranceExpiryDate");
        }
    }

    [Theory]
    [InlineData(-1, "EXPIRED")]
    [InlineData(0, "EXPIRED")]
    [InlineData(1, "EXPIRING_SOON")]
    [InlineData(6, "EXPIRING_SOON")]
    [InlineData(7, "VALID")]
    [InlineData(8, "VALID")]
    public async Task ExpiryWarnings_UseDateBoundaries_AndRenewalClearsWarnings(int days, string status)
    {
        await using var db = TestSupport.CreateDb();
        var (account, _) = await TestSupport.AddLogisticsManagerAsync(db);
        var service = CreateService(db);
        var result = await service.CreateAsync(ValidRequest(), account.AccountId);
        var vehicle = await db.Vehicles.SingleAsync();
        vehicle.RegistrationExpiryDate = vehicle.InsuranceExpiryDate = Today.AddDays(days);
        await db.SaveChangesAsync();
        var detail = await service.GetAsync(result.VehicleId, account.AccountId);
        var list = await service.GetListAsync(new(), account.AccountId);
        Assert.Equal(status, detail.RegistrationStatus);
        Assert.Equal(status, detail.InsuranceStatus);
        Assert.Equal(detail.Warnings, Assert.Single(list.Items).Warnings);
        if (status != "VALID") Assert.Equal(new[] { "REGISTRATION_" + status, "INSURANCE_" + status }, detail.Warnings);
        var renewed = await service.UpdateAsync(result.VehicleId, UpdateRequest(detail), account.AccountId);
        Assert.Empty(renewed.Warnings);
        Assert.Equal("VALID", renewed.RegistrationStatus);
        Assert.True(renewed.UpdatedAt > detail.UpdatedAt);
        Assert.Equal("Renamed Truck", (await service.GetAsync(result.VehicleId, account.AccountId)).VehicleName);
    }

    [Fact]
    public async Task UniqueFields_RejectDuplicates_AllowSelfAndSoftDeletedReuse()
    {
        await using var db = TestSupport.CreateDb();
        var (account, _) = await TestSupport.AddLogisticsManagerAsync(db);
        var service = CreateService(db);
        var first = await service.CreateAsync(ValidRequest(), account.AccountId);
        var codeError = await Assert.ThrowsAsync<ConflictException>(() => service.CreateAsync(ValidRequest("veh-road-005", "ABC-123"), account.AccountId));
        Assert.Equal("VEHICLE_CODE_DUPLICATED", codeError.ErrorCode);
        var plateError = await Assert.ThrowsAsync<ConflictException>(() => service.CreateAsync(ValidRequest("VEH-NEW", "29c-99988"), account.AccountId));
        Assert.Equal("LICENSE_PLATE_DUPLICATED", plateError.ErrorCode);
        var other = await service.CreateAsync(ValidRequest("VEH-NEW", "ABC-123"), account.AccountId);
        var update = UpdateRequest(other); update.LicensePlate = first.LicensePlate!;
        Assert.Equal("LICENSE_PLATE_DUPLICATED", (await Assert.ThrowsAsync<ConflictException>(() => service.UpdateAsync(other.VehicleId, update, account.AccountId))).ErrorCode);
        await service.UpdateAsync(first.VehicleId, UpdateRequest(first), account.AccountId);
        var old = await db.Vehicles.SingleAsync(x => x.VehicleId == first.VehicleId);
        old.IsDeleted = true; await db.SaveChangesAsync();
        var reused = await service.CreateAsync(ValidRequest(), account.AccountId);
        Assert.NotEqual(first.VehicleId, reused.VehicleId);
        Assert.Equal(2, (await service.GetListAsync(new(), account.AccountId)).TotalItems);
        Assert.Equal("VEHICLE_NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(() => service.GetAsync(first.VehicleId, account.AccountId))).ErrorCode);
    }

    [Theory]
    [InlineData("vehicleId")]
    [InlineData("vehicleCode")]
    [InlineData("status")]
    public async Task Update_RejectsChangedImmutableFields(string field)
    {
        await using var db = TestSupport.CreateDb();
        var (account, _) = await TestSupport.AddLogisticsManagerAsync(db);
        var service = CreateService(db);
        var vehicle = await service.CreateAsync(ValidRequest(), account.AccountId);
        var request = UpdateRequest(vehicle);
        switch (field)
        {
            case "vehicleId": request.VehicleId = Guid.NewGuid(); break;
            case "vehicleCode": request.VehicleCode = "CHANGED"; break;
            case "status": request.Status = "MAINTENANCE"; break;
            case "transportMode": request.TransportMode = "SEA"; break;
        }
        var error = await Assert.ThrowsAsync<FieldValidationException>(() => service.UpdateAsync(vehicle.VehicleId, request, account.AccountId));
        Assert.Contains(error.Errors, x => x.Field == field);
        Assert.Single(db.CatalogAuditLogs);
    }

    [Theory]
    [InlineData("RAIL")]
    [InlineData("1")]
    public async Task Create_RejectsInvalidMode(string mode)
    {
        await using var db = TestSupport.CreateDb();
        var (account, _) = await TestSupport.AddLogisticsManagerAsync(db);
        var request = ValidRequest(); request.TransportMode = mode;
        await Assert.ThrowsAsync<FieldValidationException>(() => CreateService(db).CreateAsync(request, account.AccountId));
        Assert.Empty(db.Vehicles);
    }

    [Theory]
    [InlineData("SEA")]
    [InlineData("AIR")]
    public async Task Mode_CreateListUpdate_AllowOptionalPlate_AndBlockAssignedChanges(string mode)
    {
        await using var db = TestSupport.CreateDb();
        var (account, _) = await TestSupport.AddLogisticsManagerAsync(db);
        var service = CreateService(db);
        var request = ValidRequest(); request.TransportMode = mode; request.LicensePlate = null;
        request.RegistrationExpiryDate = request.InsuranceExpiryDate = null;
        var vehicle = await service.CreateAsync(request, account.AccountId);
        Assert.Equal(mode, vehicle.TransportMode);
        Assert.Null(vehicle.LicensePlate);
        Assert.Equal("VALID", vehicle.RegistrationStatus);
        Assert.Equal("VALID", vehicle.InsuranceStatus);
        Assert.Single((await service.GetListAsync(new() { TransportMode = mode }, account.AccountId)).Items);
        Assert.Empty((await service.GetListAsync(new() { TransportMode = "ROAD" }, account.AccountId)).Items);
        var update = UpdateRequest(vehicle); update.LicensePlate = "NEW-ROAD";
        var changed = await service.UpdateAsync(vehicle.VehicleId, update, account.AccountId);
        Assert.Equal("ROAD", (await service.GetAsync(vehicle.VehicleId, account.AccountId)).TransportMode);
        var entity = await db.Vehicles.SingleAsync();
        await AddAssignmentAsync(db, entity);
        update.LastUpdatedAt = changed.UpdatedAt; update.TransportMode = mode;
        var error = await Assert.ThrowsAsync<ConflictException>(() => service.UpdateAsync(vehicle.VehicleId, update, account.AccountId));
        Assert.Equal("VEHICLE_IN_USE", error.ErrorCode);
        Assert.Equal(TransportMode.ROAD, entity.TransportMode);
    }

    [Theory]
    [InlineData("AVAILABLE")]
    [InlineData("MAINTENANCE")]
    [InlineData("INACTIVE")]
    public async Task ManualStatus_AuditsReasonAndExcludesUnavailableVehicles(string status)
    {
        await using var db = TestSupport.CreateDb();
        var (account, _) = await TestSupport.AddLogisticsManagerAsync(db);
        var service = CreateService(db);
        var vehicle = await service.CreateAsync(ValidRequest(), account.AccountId);
        var entity = await db.Vehicles.SingleAsync(); entity.Status = VehicleStatus.MAINTENANCE;
        await db.SaveChangesAsync();
        var changed = await service.ChangeStatusAsync(vehicle.VehicleId, StatusRequest(vehicle.UpdatedAt, status), account.AccountId);
        Assert.Equal("MAINTENANCE", changed.PreviousStatus);
        Assert.Equal(status, changed.Status);
        Assert.True(changed.UpdatedAt > vehicle.UpdatedAt);
        var audit = await db.CatalogAuditLogs.SingleAsync(x => x.ActionType == "STATUS_CHANGE");
        Assert.Equal("Scheduled maintenance", audit.Summary);
        Assert.Equal("MAINTENANCE", audit.BeforeData.RootElement.GetProperty("status").GetString());
        Assert.Equal(status, audit.AfterData.RootElement.GetProperty("status").GetString());
        var available = await service.GetListAsync(new() { AvailableFrom = Today, AvailableTo = Today.AddDays(2) }, account.AccountId);
        Assert.Equal(status == "AVAILABLE" ? 1 : 0, available.TotalItems);
        Assert.Equal("VEHICLE_MODIFIED", (await Assert.ThrowsAsync<ConflictException>(() => service.ChangeStatusAsync(vehicle.VehicleId, StatusRequest(vehicle.UpdatedAt), account.AccountId))).ErrorCode);
        Assert.Equal("VEHICLE_MODIFIED", (await Assert.ThrowsAsync<ConflictException>(() => service.UpdateAsync(vehicle.VehicleId, UpdateRequest(vehicle), account.AccountId))).ErrorCode);
    }

    [Theory]
    [InlineData("ASSIGNED")]
    [InlineData("IN_USE")]
    [InlineData("IN_TRANSIT")]
    [InlineData("SUSPENDED")]
    [InlineData("OUT_OF_SERVICE")]
    [InlineData("1")]
    [InlineData("")]
    public async Task Status_RejectsAutomaticOrUnknownInputs(string status)
    {
        var result = new StatusRequestValidator().Validate(StatusRequest(TestClock.GetUtcNow(), status));
        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData(VehicleStatus.ASSIGNED)]
    [InlineData(VehicleStatus.IN_USE)]
    public async Task Status_LocksAutomaticCurrentStatus(VehicleStatus status)
    {
        await using var db = TestSupport.CreateDb();
        var (account, _) = await TestSupport.AddLogisticsManagerAsync(db);
        var service = CreateService(db);
        var vehicle = await service.CreateAsync(ValidRequest(), account.AccountId);
        var entity = await db.Vehicles.SingleAsync(); entity.Status = status; await db.SaveChangesAsync();
        Assert.Equal("VEHICLE_STATUS_LOCKED", (await Assert.ThrowsAsync<ConflictException>(() => service.ChangeStatusAsync(vehicle.VehicleId, StatusRequest(vehicle.UpdatedAt, "AVAILABLE"), account.AccountId))).ErrorCode);
        Assert.Single(db.CatalogAuditLogs);
    }

    [Fact]
    public async Task Schedule_ResolvesOverlapsCompletionUnknownDatesExpiry_AndDetailAssignments()
    {
        await using var db = TestSupport.CreateDb();
        var (account, _) = await TestSupport.AddLogisticsManagerAsync(db);
        var service = CreateService(db);
        var vehicle = await service.CreateAsync(ValidRequest(), account.AccountId);
        var entity = await db.Vehicles.SingleAsync(); entity.Status = VehicleStatus.ASSIGNED;
        var segment = await AddAssignmentAsync(db, entity);
        var detail = await service.GetAsync(vehicle.VehicleId, account.AccountId);
        var assignment = Assert.Single(detail.UpcomingAssignments!);
        Assert.Equal("Stable", assignment.FromLocationName);
        Assert.Equal("Airport", assignment.ToLocationName);
        Assert.Equal(segment.RoutePlan.RoutePlanCode, assignment.RoutePlanCode);
        Assert.Equal(segment.RoutePlan.TransportPlan.PlanCode, assignment.PlanCode);
        async Task<Response.ListResponse> Available(int from, int to) => await service.GetListAsync(
            new() { AvailableFrom = Today.AddDays(from), AvailableTo = Today.AddDays(to) }, account.AccountId);
        Assert.Empty((await Available(2, 2)).Items);
        Assert.True(Assert.Single((await Available(3, 4)).Items).IsAvailable);
        segment.PlannedArrivalAt = segment.PlannedDepartureAt!.Value.AddMinutes(-1);
        await db.SaveChangesAsync();
        Assert.Empty((await Available(3, 4)).Items);
        segment.PlannedDepartureAt = Today.AddDays(-1).ToDateTime(new TimeOnly(6, 0));
        segment.PlannedArrivalAt = Today.AddDays(-1).ToDateTime(new TimeOnly(8, 0));
        segment.ActualDepartureAt = segment.PlannedDepartureAt;
        await db.SaveChangesAsync();
        Assert.Empty((await Available(3, 4)).Items);
        Assert.Single((await service.GetAsync(vehicle.VehicleId, account.AccountId)).UpcomingAssignments!);
        segment.ActualDepartureAt = null;
        segment.PlannedDepartureAt = Today.AddDays(2).ToDateTime(new TimeOnly(6, 0));
        segment.PlannedArrivalAt = Today.AddDays(2).ToDateTime(new TimeOnly(8, 0));
        segment.PlannedDepartureAt = null; await db.SaveChangesAsync();
        Assert.Empty((await Available(3, 4)).Items);
        segment.ActualArrivalAt = DateTime.SpecifyKind(TestClock.GetUtcNow().UtcDateTime, DateTimeKind.Unspecified); await db.SaveChangesAsync();
        Assert.Single((await Available(2, 2)).Items);
        Assert.Empty((await service.GetAsync(vehicle.VehicleId, account.AccountId)).UpcomingAssignments!);
        segment.ActualArrivalAt = null; segment.RoutePlan.Status = RoutePlanStatus.CANCELLED;
        await db.SaveChangesAsync();
        Assert.Single((await Available(2, 2)).Items);
        segment.RoutePlan.Status = RoutePlanStatus.COMPLETED; await db.SaveChangesAsync();
        Assert.Single((await Available(2, 2)).Items);
        segment.RoutePlan.Status = RoutePlanStatus.ACTIVE; segment.RoutePlan.TransportPlan.Status = TransportPlanStatus.CANCELLED;
        await db.SaveChangesAsync();
        Assert.Single((await Available(2, 2)).Items);
        segment.RoutePlan.TransportPlan.Status = TransportPlanStatus.APPROVED;
        segment.RoutePlan.TransportPlan.Transport.Status = TransportStatus.COMPLETED;
        await db.SaveChangesAsync();
        Assert.Single((await Available(2, 2)).Items);
        segment.RoutePlan.TransportPlan.Transport.Status = TransportStatus.CREATED;
        segment.RoutePlan.TransportPlan.Status = TransportPlanStatus.CANCELLED;
        entity.RegistrationExpiryDate = Today.AddDays(4); await db.SaveChangesAsync();
        Assert.Empty((await Available(3, 4)).Items);
        entity.RegistrationExpiryDate = Today.AddYears(1); entity.InsuranceExpiryDate = Today;
        await db.SaveChangesAsync();
        Assert.Empty((await Available(3, 4)).Items);
        entity.Status = VehicleStatus.AVAILABLE; segment.RoutePlan.TransportPlan.Status = TransportPlanStatus.APPROVED;
        await db.SaveChangesAsync();
        Assert.Equal("VEHICLE_STATUS_LOCKED", (await Assert.ThrowsAsync<ConflictException>(() => service.ChangeStatusAsync(vehicle.VehicleId, StatusRequest(vehicle.UpdatedAt), account.AccountId))).ErrorCode);
    }

    [Fact]
    public async Task List_FiltersSortsPaginates_AndHidesDeleted()
    {
        await using var db = TestSupport.CreateDb();
        var (account, _) = await TestSupport.AddLogisticsManagerAsync(db);
        var service = CreateService(db);
        await service.CreateAsync(ValidRequest("B", "PLATE-B"), account.AccountId);
        var a = ValidRequest("A", "PLATE-A"); a.CapacityHorses = 2;
        await service.CreateAsync(a, account.AccountId);
        var c = await service.CreateAsync(ValidRequest("C", "PLATE-C"), account.AccountId);
        (await db.Vehicles.SingleAsync(x => x.VehicleId == c.VehicleId)).TransportMode = TransportMode.AIR;
        var d = await service.CreateAsync(ValidRequest("D", "PLATE-D"), account.AccountId);
        (await db.Vehicles.SingleAsync(x => x.VehicleId == d.VehicleId)).IsDeleted = true;
        await db.SaveChangesAsync();
        var list = await service.GetListAsync(new() { PageSize = 1, PageIndex = 2, TransportMode = "ROAD" }, account.AccountId);
        Assert.Equal("B", Assert.Single(list.Items).VehicleCode); Assert.Equal(2, list.TotalItems); Assert.Equal(2, list.TotalPages);
        Assert.Null(list.Items[0].IsAvailable);
        Assert.Equal("B", Assert.Single((await service.GetListAsync(new() { MinCapacity = 4, VehicleType = "horse_truck", Search = "plate-b", Status = "available" }, account.AccountId)).Items).VehicleCode);
        Assert.Equal(6, (await service.GetListAsync(new() { SortBy = "capacityHorses:desc" }, account.AccountId)).Items[0].CapacityHorses);
        Assert.Empty((await service.GetListAsync(new() { Search = "missing" }, account.AccountId)).Items);
        Assert.Equal("AIR", (await service.GetAsync(c.VehicleId, account.AccountId)).TransportMode);
        await service.UpdateAsync(c.VehicleId, UpdateRequest(c), account.AccountId);
        await Assert.ThrowsAsync<NotFoundException>(() => service.ChangeStatusAsync(d.VehicleId, StatusRequest(d.UpdatedAt), account.AccountId));
    }

    [Fact]
    public void Validators_RejectMissingReasonBadFormatDatesSortEnumsAndPagination()
    {
        var list = new ListRequestValidator();
        Assert.False(list.Validate(new Request.ListRequest { Status = "1" }).IsValid);
        Assert.False(list.Validate(new Request.ListRequest { TransportMode = "RAIL" }).IsValid);
        Assert.Equal("INVALID_SORT_PARAMETER", Assert.Single(list.Validate(new Request.ListRequest { SortBy = "name:asc" }).Errors).ErrorCode);
        Assert.Equal("INVALID_DATE_RANGE", Assert.Single(list.Validate(new Request.ListRequest { AvailableFrom = Today }).Errors).ErrorCode);
        Assert.False(list.Validate(new Request.ListRequest { AvailableFrom = Today, AvailableTo = Today.AddDays(-1) }).IsValid);
        Assert.False(list.Validate(new Request.ListRequest { AvailableFrom = DateOnly.MaxValue, AvailableTo = DateOnly.MaxValue }).IsValid);
        Assert.False(list.Validate(new Request.ListRequest { PageIndex = 0 }).IsValid);
        Assert.False(list.Validate(new Request.ListRequest { PageSize = 101 }).IsValid);
        Assert.False(list.Validate(new Request.ListRequest { PageIndex = int.MaxValue, PageSize = 100 }).IsValid);
        var request = ValidRequest(); request.VehicleCode = "bad!"; request.LicensePlate = "<script>";
        Assert.Equal(2, new CreateRequestValidator(TestClock).Validate(request).Errors.Count);
        var status = StatusRequest(default); status.Reason = " ";
        Assert.Equal(2, new StatusRequestValidator().Validate(status).Errors.Count);
    }

    [Theory]
    [InlineData(EmployeeRole.DRIVER, true)]
    [InlineData(EmployeeRole.TRANSPORT_SPECIALIST, true)]
    [InlineData(EmployeeRole.LOGISTICS_MANAGER, false)]
    public async Task Service_RejectsWrongRoleOrInactiveEmployee(EmployeeRole role, bool active)
    {
        await using var db = TestSupport.CreateDb();
        var (account, employee) = await TestSupport.AddLogisticsManagerAsync(db);
        employee.Role = role; employee.IsActive = active; await db.SaveChangesAsync();
        var service = CreateService(db);
        await Assert.ThrowsAsync<ForbiddenException>(() => service.GetListAsync(new(), account.AccountId));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.CreateAsync(ValidRequest(), account.AccountId));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.GetAsync(Guid.NewGuid(), account.AccountId));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.UpdateAsync(Guid.NewGuid(), new(), account.AccountId));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.ChangeStatusAsync(Guid.NewGuid(), new(), account.AccountId));
    }

    internal static async Task<RoutePlanRoute> AddAssignmentAsync(AppDbContext db, Vehicle vehicle)
    {
        var (_, customer) = await TestSupport.AddCustomerAsync(db);
        var (booking, _) = await TestSupport.AddPendingBookingAsync(db, customer);
        var transport = new Transport { TransportId = Guid.NewGuid(), BookingId = booking.BookingId, TransportCode = "TR-" + Guid.NewGuid().ToString("N") };
        var plan = new TransportPlan { TransportPlanId = Guid.NewGuid(), Transport = transport, PlanCode = "TP-" + Guid.NewGuid().ToString("N"), Status = TransportPlanStatus.APPROVED };
        var routePlan = new RoutePlan { RoutePlanId = Guid.NewGuid(), TransportPlan = plan, RoutePlanCode = "RP-" + Guid.NewGuid().ToString("N"), Status = RoutePlanStatus.ACTIVE };
        var route = new Route { RouteId = Guid.NewGuid(), RouteCode = "ROUTE-" + Guid.NewGuid().ToString("N"), TransportMode = TransportMode.ROAD,
            FromLocation = new Location { LocationId = Guid.NewGuid(), LocationName = "Stable", Country = "FR" },
            ToLocation = new Location { LocationId = Guid.NewGuid(), LocationName = "Airport", Country = "FR" } };
        var segment = new RoutePlanRoute { RoutePlanRouteId = Guid.NewGuid(), RoutePlan = routePlan, Route = route, SequenceNo = 1,
            PlannedDepartureAt = Today.AddDays(2).ToDateTime(new TimeOnly(6, 0)), PlannedArrivalAt = Today.AddDays(2).ToDateTime(new TimeOnly(8, 0)) };
        db.RoutePlanRouteVehicles.Add(new() { Vehicle = vehicle, RoutePlanRoute = segment, AssignedAt = DateTime.SpecifyKind(TestClock.GetUtcNow().UtcDateTime, DateTimeKind.Unspecified) });
        await db.SaveChangesAsync();
        return segment;
    }
}
