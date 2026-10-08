using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.NotificationService;

namespace Pegaxus.Service.VehicleService;

public sealed class Service(AppDbContext db, TimeProvider clock,
    IValidator<Request.ListRequest> listValidator, IValidator<Request.CreateRequest> createValidator,
    IValidator<Request.UpdateRequest> updateValidator, IValidator<Request.StatusRequest> statusValidator,
    NotificationService.IService notifications, ILogger<Service> logger) : IService
{
    public async Task<Response.ListResponse> GetListAsync(Request.ListRequest request, Guid accountId, CancellationToken cancellationToken = default)
    {
        await AuthorizeAsync(accountId, cancellationToken);
        Validate(request, listValidator);
        var query = Vehicles();
        if (request.Status is not null)
        {
            var status = Enum.Parse<VehicleStatus>(request.Status.Trim(), true);
            query = query.Where(x => x.Status == status);
        }
        if (!string.IsNullOrWhiteSpace(request.VehicleType))
        {
            var type = request.VehicleType.Trim().ToUpperInvariant();
            query = query.Where(x => x.VehicleType != null && x.VehicleType.ToUpper() == type);
        }
        if (request.MinCapacity.HasValue)
            query = query.Where(x => x.CapacityHorses >= request.MinCapacity);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToUpperInvariant();
            query = query.Where(x => x.VehicleCode.ToUpper().Contains(search) ||
                x.LicensePlate != null && x.LicensePlate.ToUpper().Contains(search) ||
                x.VehicleName != null && x.VehicleName.ToUpper().Contains(search));
        }
        if (request.AvailableFrom.HasValue)
        {
            var start = request.AvailableFrom.Value.ToDateTime(TimeOnly.MinValue);
            var end = request.AvailableTo!.Value.AddDays(1).ToDateTime(TimeOnly.MinValue);
            var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
            var lastDay = request.AvailableTo.Value > today ? request.AvailableTo.Value : today;
            var assignments = UnfinishedAssignments();
            var now = Now();
            query = query.Where(x => (x.Status == VehicleStatus.AVAILABLE || x.Status == VehicleStatus.ASSIGNED) &&
                (!x.RegistrationExpiryDate.HasValue || x.RegistrationExpiryDate > lastDay) &&
                (!x.InsuranceExpiryDate.HasValue || x.InsuranceExpiryDate > lastDay) &&
                !assignments.Any(a => a.VehicleId == x.VehicleId &&
                    (!a.RoutePlanRoute.PlannedDepartureAt.HasValue || !a.RoutePlanRoute.PlannedArrivalAt.HasValue ||
                     a.RoutePlanRoute.PlannedArrivalAt < a.RoutePlanRoute.PlannedDepartureAt ||
                     a.RoutePlanRoute.ActualDepartureAt.HasValue && a.RoutePlanRoute.PlannedArrivalAt < now ||
                     a.RoutePlanRoute.PlannedDepartureAt < end && a.RoutePlanRoute.PlannedArrivalAt >= start)));
        }
        var count = await query.CountAsync(cancellationToken);
        var ordered = request.SortBy.Trim().ToLowerInvariant() switch
        {
            "vehiclecode:desc" => query.OrderByDescending(x => x.VehicleCode),
            "capacityhorses:asc" => query.OrderBy(x => x.CapacityHorses),
            "capacityhorses:desc" => query.OrderByDescending(x => x.CapacityHorses),
            "updatedat:asc" => query.OrderBy(x => x.UpdatedAt),
            "updatedat:desc" => query.OrderByDescending(x => x.UpdatedAt),
            _ => query.OrderBy(x => x.VehicleCode)
        };
        var items = await ordered.ThenBy(x => x.VehicleId).Skip((request.PageIndex - 1) * request.PageSize)
            .Take(request.PageSize).ToListAsync(cancellationToken);
        return new(items.Select(x => Map(x, request.AvailableFrom.HasValue ? true : null)).ToArray(),
            request.PageIndex, request.PageSize, count, (int)Math.Ceiling(count / (double)request.PageSize));
    }

    public async Task<Response.VehicleResponse> GetAsync(Guid vehicleId, Guid accountId, CancellationToken cancellationToken = default)
    {
        await AuthorizeAsync(accountId, cancellationToken);
        var vehicle = await Vehicles().SingleOrDefaultAsync(x => x.VehicleId == vehicleId, cancellationToken) ?? throw NotFound();
        var now = Now();
        var segments = await UnfinishedAssignments().Where(x => x.VehicleId == vehicleId &&
                (!x.RoutePlanRoute.PlannedDepartureAt.HasValue || !x.RoutePlanRoute.PlannedArrivalAt.HasValue || x.RoutePlanRoute.PlannedArrivalAt >= now ||
                 x.RoutePlanRoute.ActualDepartureAt.HasValue))
            .OrderBy(x => x.RoutePlanRoute.PlannedDepartureAt).ThenBy(x => x.RoutePlanRoute.SequenceNo)
            .ThenBy(x => x.RoutePlanRouteId)
            .Select(x => new
            {
                x.RoutePlanRouteId, x.RoutePlanRoute.RoutePlanId, x.RoutePlanRoute.RoutePlan.RoutePlanCode,
                x.RoutePlanRoute.RoutePlan.TransportPlanId, x.RoutePlanRoute.RoutePlan.TransportPlan.PlanCode,
                x.RoutePlanRoute.SequenceNo, From = x.RoutePlanRoute.Route.FromLocation.LocationName,
                To = x.RoutePlanRoute.Route.ToLocation.LocationName,
                x.RoutePlanRoute.PlannedDepartureAt, x.RoutePlanRoute.PlannedArrivalAt
            }).ToListAsync(cancellationToken);
        var result = Map(vehicle);
        result.UpcomingAssignments = segments.Select(x => new Response.AssignmentResponse(x.RoutePlanRouteId,
            x.RoutePlanId, x.RoutePlanCode, x.TransportPlanId, x.PlanCode, x.SequenceNo, x.From, x.To,
            Offset(x.PlannedDepartureAt), Offset(x.PlannedArrivalAt))).ToArray();
        return result;
    }

    public async Task<Response.VehicleResponse> CreateAsync(Request.CreateRequest request, Guid accountId, CancellationToken cancellationToken = default)
    {
        await AuthorizeAsync(accountId, cancellationToken);
        Normalize(request);
        request.VehicleCode = request.VehicleCode?.Trim().ToUpperInvariant()!;
        Validate(request, createValidator);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (await db.Vehicles.AnyAsync(x => !x.IsDeleted && x.VehicleCode.ToUpper() == request.VehicleCode, cancellationToken))
            throw DuplicateCode();
        await CheckPlateAsync(request.LicensePlate, null, cancellationToken);
        var now = Now();
        var vehicle = new Vehicle { VehicleId = Guid.NewGuid(), VehicleCode = request.VehicleCode,
            TransportMode = TransportMode.ROAD, Status = VehicleStatus.AVAILABLE, CreatedAt = now, UpdatedAt = now };
        Apply(vehicle, request);
        db.Vehicles.Add(vehicle);
        await SaveAsync(vehicle, accountId, "CREATE", JsonSerializer.SerializeToDocument(new { }), null, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Vehicle {VehicleId} created by account {AccountId}", vehicle.VehicleId, accountId);
        await notifications.FlushAsync(cancellationToken);
        return Map(vehicle);
    }

    public async Task<Response.UpdateResponse> UpdateAsync(Guid vehicleId, Request.UpdateRequest request, Guid accountId, CancellationToken cancellationToken = default)
    {
        await AuthorizeAsync(accountId, cancellationToken);
        Normalize(request);
        Validate(request, updateValidator);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var vehicle = await LockAsync(vehicleId, cancellationToken);
        CheckVersion(vehicle, request.LastUpdatedAt);
        if (request.VehicleId.HasValue && request.VehicleId != vehicleId)
            throw Immutable("vehicleId");
        if (request.VehicleCode is not null && request.VehicleCode != vehicle.VehicleCode)
            throw Immutable("vehicleCode");
        if (request.Status is not null && !string.Equals(request.Status, vehicle.Status.ToString(), StringComparison.OrdinalIgnoreCase))
            throw Immutable("status");
        await CheckPlateAsync(request.LicensePlate, vehicleId, cancellationToken);
        var before = Snapshot(vehicle);
        Apply(vehicle, request);
        vehicle.UpdatedAt = NextVersion(vehicle.UpdatedAt);
        await SaveAsync(vehicle, accountId, "UPDATE", before, null, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Vehicle {VehicleId} updated by account {AccountId}", vehicleId, accountId);
        await notifications.FlushAsync(cancellationToken);
        var result = Map(vehicle);
        return new(vehicleId, result.RegistrationStatus, result.InsuranceStatus, result.Warnings, result.UpdatedAt);
    }

    public async Task<Response.StatusResponse> ChangeStatusAsync(Guid vehicleId, Request.StatusRequest request, Guid accountId, CancellationToken cancellationToken = default)
    {
        await AuthorizeAsync(accountId, cancellationToken);
        Validate(request, statusValidator);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var vehicle = await LockAsync(vehicleId, cancellationToken);
        CheckVersion(vehicle, request.LastUpdatedAt);
        if (vehicle.Status is VehicleStatus.ASSIGNED or VehicleStatus.IN_USE ||
            await UnfinishedAssignments().AnyAsync(x => x.VehicleId == vehicleId, cancellationToken))
            throw new ConflictException("Vehicle is currently assigned/IN_USE on an active trip. Status is managed automatically by the trip execution system.", "VEHICLE_STATUS_LOCKED");
        var before = Snapshot(vehicle);
        var previous = vehicle.Status.ToString();
        vehicle.Status = Enum.Parse<VehicleStatus>(request.Status.Trim(), true);
        vehicle.UpdatedAt = NextVersion(vehicle.UpdatedAt);
        await SaveAsync(vehicle, accountId, "STATUS_CHANGE", before, request.Reason.Trim(), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Vehicle {VehicleId} status changed from {PreviousStatus} to {Status} by account {AccountId}", vehicleId, previous, vehicle.Status, accountId);
        await notifications.FlushAsync(cancellationToken);
        return new(vehicleId, previous, vehicle.Status.ToString(), Offset(vehicle.UpdatedAt));
    }

    private IQueryable<Vehicle> Vehicles() => db.Vehicles.AsNoTracking().Where(x => !x.IsDeleted && x.TransportMode == TransportMode.ROAD);

    private IQueryable<RoutePlanRouteVehicle> UnfinishedAssignments() => db.RoutePlanRouteVehicles.AsNoTracking().Where(x =>
        x.RoutePlanRoute.ActualArrivalAt == null &&
        x.RoutePlanRoute.RoutePlan.Status != RoutePlanStatus.COMPLETED && x.RoutePlanRoute.RoutePlan.Status != RoutePlanStatus.CANCELLED &&
        x.RoutePlanRoute.RoutePlan.TransportPlan.Status != TransportPlanStatus.COMPLETED &&
        x.RoutePlanRoute.RoutePlan.TransportPlan.Status != TransportPlanStatus.CANCELLED &&
        x.RoutePlanRoute.RoutePlan.TransportPlan.Transport.Status != TransportStatus.COMPLETED &&
        x.RoutePlanRoute.RoutePlan.TransportPlan.Transport.Status != TransportStatus.CANCELLED);

    private async Task<Vehicle> LockAsync(Guid id, CancellationToken cancellationToken)
    {
        var query = db.Database.IsNpgsql()
            ? db.Vehicles.FromSqlInterpolated($"SELECT * FROM \"Vehicle\" WHERE vehicle_id = {id} FOR UPDATE")
            : db.Vehicles.Where(x => x.VehicleId == id);
        var vehicle = await query.SingleOrDefaultAsync(cancellationToken) ?? throw NotFound();
        // A reused context may already track the vehicle before acquiring its row lock.
        if (db.Database.IsRelational()) await db.Entry(vehicle).ReloadAsync(cancellationToken);
        if (vehicle.IsDeleted || vehicle.TransportMode != TransportMode.ROAD) throw NotFound();
        return vehicle;
    }

    private async Task AuthorizeAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var allowed = await db.Employees.AsNoTracking().AnyAsync(x => x.AccountId == accountId && !x.IsDeleted && x.IsActive &&
            !x.Account.IsDeleted && x.Account.Status == AccountStatus.ACTIVE && x.Account.AccountType == AccountType.EMPLOYEE &&
            (x.Role == EmployeeRole.LOGISTICS_MANAGER || x.Role == EmployeeRole.FLEET_ROUTE_COORDINATOR), cancellationToken);
        if (!allowed) throw new ForbiddenException("Only active Fleet Route Coordinators and Logistics Managers can manage vehicles.");
    }

    private async Task CheckPlateAsync(string plate, Guid? id, CancellationToken cancellationToken)
    {
        if (await db.Vehicles.AnyAsync(x => !x.IsDeleted && x.VehicleId != id && x.LicensePlate != null &&
            x.LicensePlate.ToUpper() == plate, cancellationToken)) throw DuplicatePlate();
    }

    private async Task SaveAsync(Vehicle vehicle, Guid accountId, string action, JsonDocument before, string? reason, CancellationToken cancellationToken)
    {
        db.CatalogAuditLogs.Add(new CatalogAuditLog { CatalogAuditLogId = Guid.NewGuid(), EntityType = nameof(Vehicle),
            EntityId = vehicle.VehicleId, ActionType = action, ActorAccountId = accountId, Summary = reason,
            BeforeData = before, AfterData = Snapshot(vehicle), CreatedAt = vehicle.UpdatedAt });
        var recipients = await db.Employees.AsNoTracking().Where(x => !x.IsDeleted && x.IsActive && !x.Account.IsDeleted &&
            x.Account.Status == AccountStatus.ACTIVE && x.Account.AccountType == AccountType.EMPLOYEE &&
            (x.Role == EmployeeRole.LOGISTICS_MANAGER || x.Role == EmployeeRole.FLEET_ROUTE_COORDINATOR))
            .Select(x => x.AccountId).Distinct().ToListAsync(cancellationToken);
        foreach (var recipient in recipients)
            notifications.Queue(new NotificationCreateRequest { AccountId = recipient, Type = NotificationType.SYSTEM,
                Title = "Vehicle catalog updated", Message = $"Vehicle {vehicle.VehicleCode} ({vehicle.LicensePlate}): {action}. {reason}",
                ReferenceType = nameof(Vehicle), ReferenceId = vehicle.VehicleId, CreatedAt = vehicle.UpdatedAt });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { throw Modified(); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "Vehicle_vehicle_code_key" })
        { throw DuplicateCode(); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "Vehicle_license_plate_key" })
        { throw DuplicatePlate(); }
    }

    private Response.VehicleResponse Map(Vehicle x, bool? available = null)
    {
        var registration = ExpiryStatus(x.RegistrationExpiryDate);
        var insurance = ExpiryStatus(x.InsuranceExpiryDate);
        var warnings = new List<string>();
        if (registration is "EXPIRED" or "EXPIRING_SOON") warnings.Add("REGISTRATION_" + registration);
        if (insurance is "EXPIRED" or "EXPIRING_SOON") warnings.Add("INSURANCE_" + insurance);
        return new() { VehicleId = x.VehicleId, VehicleCode = x.VehicleCode, VehicleName = x.VehicleName,
            TransportMode = x.TransportMode.ToString(), LicensePlate = x.LicensePlate, VehicleType = x.VehicleType,
            Manufacturer = x.Manufacturer, Model = x.Model, CapacityHorses = x.CapacityHorses, Status = x.Status.ToString(),
            RegistrationNumber = x.RegistrationNumber, RegistrationExpiryDate = x.RegistrationExpiryDate,
            RegistrationStatus = registration, InsuranceNumber = x.InsuranceNumber, InsuranceExpiryDate = x.InsuranceExpiryDate,
            InsuranceStatus = insurance, SpecialEquipment = x.SpecialEquipment, Warnings = warnings,
            IsAvailable = available, CreatedAt = Offset(x.CreatedAt), UpdatedAt = Offset(x.UpdatedAt) };
    }

    private string? ExpiryStatus(DateOnly? date)
    {
        if (!date.HasValue) return null;
        var remaining = date.Value.DayNumber - DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime).DayNumber;
        return remaining <= 0 ? "EXPIRED" : remaining <= 7 ? "EXPIRING_SOON" : "VALID";
    }

    private JsonDocument Snapshot(Vehicle vehicle) => JsonSerializer.SerializeToDocument(Map(vehicle), new JsonSerializerOptions(JsonSerializerDefaults.Web));
    private static void Normalize(Request.VehicleFields request) => request.LicensePlate = request.LicensePlate?.Trim().ToUpperInvariant()!;
    private static void Apply(Vehicle vehicle, Request.VehicleFields request)
    {
        vehicle.VehicleName = Trim(request.VehicleName); vehicle.LicensePlate = request.LicensePlate;
        vehicle.VehicleType = Trim(request.VehicleType); vehicle.Manufacturer = Trim(request.Manufacturer);
        vehicle.Model = Trim(request.Model); vehicle.CapacityHorses = request.CapacityHorses;
        vehicle.RegistrationNumber = Trim(request.RegistrationNumber); vehicle.RegistrationExpiryDate = request.RegistrationExpiryDate;
        vehicle.InsuranceNumber = Trim(request.InsuranceNumber); vehicle.InsuranceExpiryDate = request.InsuranceExpiryDate;
        vehicle.SpecialEquipment = Trim(request.SpecialEquipment);
    }
    private static void Validate<T>(T request, IValidator<T> validator)
    {
        var result = validator.Validate(request);
        if (result.IsValid) return;
        var queryError = result.Errors.FirstOrDefault(x => x.ErrorCode is "INVALID_DATE_RANGE" or "INVALID_SORT_PARAMETER");
        throw new FieldValidationException(result.Errors.Select(x => new FieldError(
            JsonNamingPolicy.CamelCase.ConvertName(x.PropertyName), x.ErrorMessage)).ToArray(),
            queryError?.ErrorCode ?? "VALIDATION_ERROR", queryError?.ErrorMessage ?? "The input data is invalid");
    }
    private static void CheckVersion(Vehicle vehicle, DateTimeOffset lastUpdatedAt)
    { if (vehicle.UpdatedAt.Ticks != lastUpdatedAt.UtcDateTime.Ticks) throw Modified(); }
    private DateTime Now() => new(clock.GetUtcNow().Ticks / 10 * 10, DateTimeKind.Unspecified);
    private DateTime NextVersion(DateTime previous) => new(Math.Max(Now().Ticks, previous.Ticks + 10), DateTimeKind.Unspecified);
    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static DateTimeOffset Offset(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    private static DateTimeOffset? Offset(DateTime? value) => value.HasValue ? Offset(value.Value) : null;
    private static NotFoundException NotFound() => new("Vehicle not found", "VEHICLE_NOT_FOUND");
    private static ConflictException Modified() => new("The vehicle was just modified. Please reload the page", "VEHICLE_MODIFIED");
    private static ConflictException DuplicateCode() => new("Vehicle Code or License Plate already exists in the system.", "VEHICLE_CODE_DUPLICATED");
    private static ConflictException DuplicatePlate() => new("License plate is already assigned to another vehicle.", "LICENSE_PLATE_DUPLICATED");
    private static FieldValidationException Immutable(string field) => new([new(field, "This field is read-only.")]);
}
