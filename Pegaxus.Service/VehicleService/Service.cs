using System.Data;
using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Repository.Extensions;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.NotificationService;

using static Pegaxus.Service.Common.DbTime;
using static Pegaxus.Service.Common.Text;

namespace Pegaxus.Service.VehicleService;

public sealed class Service(AppDbContext db, TimeProvider clock,
    IValidator<Request.ListRequest> listValidator, IValidator<Request.CreateRequest> createValidator,
    IValidator<Request.UpdateRequest> updateValidator, IValidator<Request.StatusRequest> statusValidator,
    NotificationService.INotificationService notifications, ILogger<Service> logger) : IVehicleService
{
    public async Task<Response.ListResponse> GetListAsync(Request.ListRequest request, Guid accountId, CancellationToken cancellationToken = default)
    {
        await AuthorizeAsync(accountId, cancellationToken);
        Validate(request, listValidator);
        var query = Vehicles();
        if (request.TransportMode is not null)
        {
            var mode = Enum.Parse<TransportMode>(request.TransportMode.Trim(), true);
            query = query.Where(x => x.TransportMode == mode);
        }
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
        if (CountryCodes.Normalize(request.Country) is { } country)
            query = query.Where(x => x.Country == country);
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
                    (request.TransportPlanId == null || a.RoutePlanRoute.RoutePlan.TransportPlanId != request.TransportPlanId) &&
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
        var items = await ordered.ThenBy(x => x.VehicleId).Skip(Paging.Offset(request.PageIndex, request.PageSize))
            .Take(request.PageSize).ToListAsync(cancellationToken);
        return new(items.Select(x => Map(x, request.AvailableFrom.HasValue ? true : null)).ToArray(),
            request.PageIndex, request.PageSize, count, Paging.TotalPages(count, request.PageSize));
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
            AsUtc(x.PlannedDepartureAt), AsUtc(x.PlannedArrivalAt))).ToArray();
        return result;
    }

    public async Task<Response.VehicleResponse> CreateAsync(Request.CreateRequest request, Guid accountId, CancellationToken cancellationToken = default)
    {
        await AuthorizeAsync(accountId, cancellationToken);
        Normalize(request);
        Validate(request, createValidator);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await CheckPlateAsync(request.LicensePlate, null, cancellationToken);
        var now = Now();
        var prefix = $"VEH-{now:yyyyMMdd}-";
        var vehicle = new Vehicle { VehicleId = Guid.NewGuid(),
            VehicleCode = DailyCodes.Next(prefix, await db.Vehicles.AsNoTracking().Where(x => x.VehicleCode.StartsWith(prefix)).Select(x => x.VehicleCode).ToListAsync(cancellationToken), "VEHICLE_CODE_EXHAUSTED"),
            Status = VehicleStatus.AVAILABLE, CreatedAt = now, UpdatedAt = now };
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
        await using var transaction = await db.Database.BeginSerializableAsync(cancellationToken);
        var vehicle = await LockAsync(vehicleId, cancellationToken);
        CheckVersion(vehicle, request.LastUpdatedAt);
        if (request.VehicleId.HasValue && request.VehicleId != vehicleId)
            throw Immutable("vehicleId");
        if (request.VehicleCode is not null && request.VehicleCode != vehicle.VehicleCode)
            throw Immutable("vehicleCode");
        if (request.Status is not null && !string.Equals(request.Status, vehicle.Status.ToString(), StringComparison.OrdinalIgnoreCase))
            throw Immutable("status");
        if (vehicle.TransportMode != Enum.Parse<TransportMode>(request.TransportMode.Trim(), true) &&
            await UnfinishedAssignments().AnyAsync(x => x.VehicleId == vehicleId, cancellationToken))
            throw new ConflictException("Cannot change transport mode while the vehicle has unfinished assignments.", "VEHICLE_IN_USE");
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
        // shortcut: future assignment writers must validate the vehicle within a Serializable transaction.
        await using var transaction = await db.Database.BeginSerializableAsync(cancellationToken);
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
        return new(vehicleId, previous, vehicle.Status.ToString(), AsUtc(vehicle.UpdatedAt));
    }

    private IQueryable<Vehicle> Vehicles() => db.Vehicles.AsNoTracking().Where(x => !x.IsDeleted);

    private IQueryable<RoutePlanRouteVehicle> UnfinishedAssignments() => db.RoutePlanRouteVehicles.AsNoTracking().Where(x =>
        x.RoutePlanRoute.ActualArrivalAt == null &&
        x.RoutePlanRoute.RoutePlan.Status != RoutePlanStatus.COMPLETED && x.RoutePlanRoute.RoutePlan.Status != RoutePlanStatus.CANCELLED &&
        x.RoutePlanRoute.RoutePlan.TransportPlan.Status != TransportPlanStatus.COMPLETED &&
        x.RoutePlanRoute.RoutePlan.TransportPlan.Status != TransportPlanStatus.CANCELLED &&
        x.RoutePlanRoute.RoutePlan.TransportPlan.Transport.Status != TransportStatus.COMPLETED &&
        x.RoutePlanRoute.RoutePlan.TransportPlan.Transport.Status != TransportStatus.CANCELLED);

    private async Task<Vehicle> LockAsync(Guid id, CancellationToken cancellationToken)
    {
        var query = db.Vehicles.Where(x => x.VehicleId == id);
        // Updating the key to itself holds a row lock until the transaction ends.
        if (db.Database.IsNpgsql())
        {
            try { await db.LockRowsAsync(query, x => x.VehicleId, cancellationToken); }
            catch (Exception error) when (error.GetBaseException() is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected })
            { throw Modified(); }
        }
        var vehicle = await query.SingleOrDefaultAsync(cancellationToken) ?? throw NotFound();
        // A reused context may already track the vehicle before acquiring its row lock.
        if (db.Database.IsRelational()) await db.Entry(vehicle).ReloadAsync(cancellationToken);
        if (vehicle.IsDeleted) throw NotFound();
        return vehicle;
    }

    private async Task AuthorizeAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var allowed = await db.Employees.AsNoTracking().Active().AnyAsync(x => x.AccountId == accountId && x.Account.AccountType == AccountType.EMPLOYEE && (x.Role == EmployeeRole.LOGISTICS_MANAGER || x.Role == EmployeeRole.FLEET_ROUTE_COORDINATOR), cancellationToken);
        if (!allowed) throw new ForbiddenException("Only active Fleet Route Coordinators and Logistics Managers can manage vehicles.");
    }

    private async Task CheckPlateAsync(string? plate, Guid? id, CancellationToken cancellationToken)
    {
        if (plate is not null && await db.Vehicles.AnyAsync(x => !x.IsDeleted && x.VehicleId != id && x.LicensePlate != null &&
            x.LicensePlate.ToUpper() == plate, cancellationToken)) throw DuplicatePlate();
    }

    private async Task SaveAsync(Vehicle vehicle, Guid accountId, string action, JsonDocument before, string? reason, CancellationToken cancellationToken)
    {
        db.CatalogAuditLogs.Add(new CatalogAuditLog { CatalogAuditLogId = Guid.NewGuid(), EntityType = nameof(Vehicle),
            EntityId = vehicle.VehicleId, ActionType = action, ActorAccountId = accountId, Summary = reason,
            BeforeData = before, AfterData = Snapshot(vehicle), CreatedAt = vehicle.UpdatedAt });
        var recipients = await db.Employees.AsNoTracking().Active().Where(x => x.AccountId != accountId && x.Account.AccountType == AccountType.EMPLOYEE && (x.Role == EmployeeRole.LOGISTICS_MANAGER || x.Role == EmployeeRole.FLEET_ROUTE_COORDINATOR))
            .Select(x => new { x.AccountId, x.Role }).Distinct().ToListAsync(cancellationToken);
        var (title, outcome) = action switch
        {
            "CREATE" => ("Vehicle added", "was added to the catalog"),
            "STATUS_CHANGE" => ("Vehicle status changed", $"changed status to {vehicle.Status}"),
            _ => ("Vehicle updated", "was updated")
        };
        var message = $"Vehicle {vehicle.VehicleCode} ({vehicle.LicensePlate}) {outcome}.{(string.IsNullOrWhiteSpace(reason) ? "" : " Reason: " + reason)}";
        foreach (var recipient in recipients)
            notifications.Queue(new NotificationCreateRequest { AccountId = recipient.AccountId, Type = NotificationType.SYSTEM,
                Title = title, Message = message, ReferenceType = nameof(Vehicle), ReferenceId = vehicle.VehicleId,
                ActionUrl = NotificationLinks.Vehicle(recipient.Role, vehicle.VehicleId), CreatedAt = vehicle.UpdatedAt });
        if (action == "STATUS_CHANGE" && vehicle.Status is VehicleStatus.MAINTENANCE or VehicleStatus.INACTIVE or VehicleStatus.SUSPENDED or VehicleStatus.OUT_OF_SERVICE)
            await MailService.MailRecipients.QueueAsync(db, recipients.Select(item => item.AccountId), vehicle.UpdatedAt,
                recipient => MailService.MailRecipients.Mail(recipient, $"Vehicle status changed — {vehicle.VehicleCode}",
                    MailService.EmailBodies.CatalogChangeBody(recipient.FullName, "Vehicle", vehicle.VehicleCode, vehicle.VehicleName,
                        $"is now {vehicle.Status} and may be unavailable for assignments", string.IsNullOrWhiteSpace(reason) ? null : reason,
                        NotificationLinks.Vehicle(recipient.Role, vehicle.VehicleId))), cancellationToken);
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { throw Modified(); }
        catch (Exception ex) when (ex.GetBaseException() is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected })
        { throw Modified(); }
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
            InsuranceStatus = insurance, SpecialEquipment = x.SpecialEquipment, Country = x.Country, Warnings = warnings,
            IsAvailable = available, CreatedAt = AsUtc(x.CreatedAt), UpdatedAt = AsUtc(x.UpdatedAt) };
    }

    private string ExpiryStatus(DateOnly? date)
    {
        if (!date.HasValue) return "VALID";
        var remaining = date.Value.DayNumber - DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime).DayNumber;
        return remaining <= 0 ? "EXPIRED" : remaining < 7 ? "EXPIRING_SOON" : "VALID";
    }

    private JsonDocument Snapshot(Vehicle vehicle) => JsonSerializer.SerializeToDocument(Map(vehicle), new JsonSerializerOptions(JsonSerializerDefaults.Web));
    private static void Normalize(Request.VehicleFields request) => request.LicensePlate = EmptyToNull(request.LicensePlate)?.ToUpperInvariant();
    private static void Apply(Vehicle vehicle, Request.VehicleFields request)
    {
        vehicle.TransportMode = Enum.Parse<TransportMode>(request.TransportMode.Trim(), true);
        vehicle.VehicleName = EmptyToNull(request.VehicleName); vehicle.LicensePlate = request.LicensePlate;
        vehicle.VehicleType = EmptyToNull(request.VehicleType); vehicle.Manufacturer = EmptyToNull(request.Manufacturer);
        vehicle.Model = EmptyToNull(request.Model); vehicle.CapacityHorses = request.CapacityHorses;
        vehicle.RegistrationNumber = EmptyToNull(request.RegistrationNumber); vehicle.RegistrationExpiryDate = request.RegistrationExpiryDate;
        vehicle.InsuranceNumber = EmptyToNull(request.InsuranceNumber); vehicle.InsuranceExpiryDate = request.InsuranceExpiryDate;
        vehicle.SpecialEquipment = EmptyToNull(request.SpecialEquipment);
        vehicle.Country = CountryCodes.Normalize(request.Country);
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
    private static NotFoundException NotFound() => new("Vehicle not found", "VEHICLE_NOT_FOUND");
    private static ConflictException Modified() => new("The vehicle was just modified. Please reload the page", "VEHICLE_MODIFIED");
    private static ConflictException DuplicateCode() => new("Vehicle Code or License Plate already exists in the system.", "VEHICLE_CODE_DUPLICATED");
    private static ConflictException DuplicatePlate() => new("License plate is already assigned to another vehicle.", "LICENSE_PLATE_DUPLICATED");
    private static FieldValidationException Immutable(string field) => new([new(field, "This field is read-only.")]);
}
