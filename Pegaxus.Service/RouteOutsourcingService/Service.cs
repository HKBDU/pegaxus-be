using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.NotificationService;
using Pegaxus.Service.Common;

namespace Pegaxus.Service.RouteOutsourcingService;

public sealed class Service(AppDbContext db, IValidator<Request> validator, NotificationService.IService notifications, ILogger<Service> logger) : IService
{
    public async Task<Response> SetAsync(Guid accountId, Guid routePlanId, Guid segmentId, Request request, CancellationToken cancellationToken = default)
    {
        if (!await db.Employees.AnyAsync(employee => employee.AccountId == accountId && employee.IsActive && !employee.IsDeleted &&
            employee.Role == EmployeeRole.LOGISTICS_MANAGER && !employee.Account.IsDeleted &&
            employee.Account.Status == AccountStatus.ACTIVE && employee.Account.AccountType == AccountType.EMPLOYEE, cancellationToken))
            throw new ForbiddenException("Only Logistics Managers may change outsourcing.");
        var validation = validator.Validate(request);
        if (!validation.IsValid)
            throw new BadRequestException(string.Join(" ", validation.Errors.Select(error => error.ErrorMessage)),
                validation.Errors.Any(error => error.ErrorCode == "PROVIDER_REQUIRED") ? "PROVIDER_REQUIRED" : "VALIDATION_ERROR");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var planId = await db.RoutePlans.AsNoTracking().Where(route => route.RoutePlanId == routePlanId)
            .Select(route => (Guid?)route.TransportPlanId).SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Route Plan not found", "ROUTE_PLAN_NOT_FOUND");
        // Serialize provider-link changes across every route in the same transport plan.
        // Updating the key to itself holds a row lock until the transaction ends.
        if (db.Database.IsNpgsql())
            await db.TransportPlans.Where(plan => plan.TransportPlanId == planId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(plan => plan.TransportPlanId, plan => plan.TransportPlanId), cancellationToken);
        var plan = await db.TransportPlans.SingleAsync(plan => plan.TransportPlanId == planId, cancellationToken);
        await db.Entry(plan).ReloadAsync(cancellationToken);
        if (db.Database.IsNpgsql())
            await db.RoutePlans.Where(route => route.RoutePlanId == routePlanId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(route => route.RoutePlanId, route => route.RoutePlanId), cancellationToken);
        var route = await db.RoutePlans.SingleOrDefaultAsync(route => route.RoutePlanId == routePlanId && route.TransportPlanId == planId, cancellationToken)
            ?? throw new NotFoundException("Route Plan not found", "ROUTE_PLAN_NOT_FOUND");
        await db.Entry(route).ReloadAsync(cancellationToken);
        if (route.Status is not (RoutePlanStatus.DRAFT or RoutePlanStatus.CONFIRMED or RoutePlanStatus.PENDING_LEGAL_REVIEW or
            RoutePlanStatus.PENDING_MANAGER_APPROVAL or RoutePlanStatus.REJECTED_BY_MANAGER or RoutePlanStatus.APPROVED))
            throw new ConflictException("The route plan can no longer be modified (use emergency change)", "ROUTE_LOCKED");
        if (DateTime.SpecifyKind(route.UpdatedAt, DateTimeKind.Utc) != request.LastUpdatedAt.UtcDateTime)
            throw Modified();

        if (db.Database.IsNpgsql())
            await db.RoutePlanRoutes.Where(segment => segment.RoutePlanRouteId == segmentId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(segment => segment.RoutePlanRouteId, segment => segment.RoutePlanRouteId), cancellationToken);
        var segment = await db.RoutePlanRoutes.SingleOrDefaultAsync(segment => segment.RoutePlanRouteId == segmentId && segment.RoutePlanId == routePlanId, cancellationToken)
            ?? throw new NotFoundException("Segment not found", "SEGMENT_NOT_FOUND");
        await db.Entry(segment).ReloadAsync(cancellationToken);
        await db.Entry(segment).Reference(item => item.Route).LoadAsync(cancellationToken);
        await db.Entry(segment).Reference(item => item.DriverEmployee).LoadAsync(cancellationToken);
        await db.Entry(segment).Reference(item => item.EscortEmployee).LoadAsync(cancellationToken);
        var vehicles = await db.RoutePlanRouteVehicles.Where(item => item.RoutePlanRouteId == segmentId)
            .Include(item => item.Vehicle).ToListAsync(cancellationToken);
        TransportProvider? provider = null;
        if (request.Outsourced == true)
        {
            // Coordinate with provider status changes and soft deletion (UC-41).
            if (db.Database.IsNpgsql())
                await db.TransportProviders.Where(item => item.TransportProviderId == request.TransportProviderId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.TransportProviderId, item => item.TransportProviderId), cancellationToken);
            provider = await db.TransportProviders.SingleOrDefaultAsync(item => item.TransportProviderId == request.TransportProviderId, cancellationToken);
            if (provider is not null) await db.Entry(provider).ReloadAsync(cancellationToken);
            if (provider is null || provider.IsDeleted)
                throw new BadRequestException("Please select a valid Transport Provider for this outsourced segment.", "PROVIDER_REQUIRED");
            if (provider.Status != ProviderStatus.ACTIVE)
                throw new ConflictException("Provider is not ACTIVE", "PROVIDER_INACTIVE");
            if (!(provider.SupportedModes ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Contains(segment.Route.TransportMode.ToString(), StringComparer.OrdinalIgnoreCase))
                throw new UnprocessableEntityException("The provider does not support the transport mode of this segment", "PROVIDER_MODE_MISMATCH");
        }

        var serviceType = provider is null ? null : Trim(request.ServiceType);
        var reference = provider is null ? null : Trim(request.ProviderReference);
        var note = Trim(request.Note);
        var cost = provider is null ? null : request.CostAmount;
        var currency = provider is null ? null : request.Currency!.Trim().ToUpperInvariant();
        var clearedPersonnel = provider is not null && (segment.DriverEmployeeId.HasValue || segment.EscortEmployeeId.HasValue);
        var clearedVehicles = provider is not null && vehicles.Count > 0;
        var link = provider is null ? null : await db.TransportPlanProviders.SingleOrDefaultAsync(item =>
            item.TransportPlanId == planId && item.TransportProviderId == provider.TransportProviderId, cancellationToken);
        if (segment.TransportProviderId == provider?.TransportProviderId && segment.ServiceType == serviceType &&
            segment.ProviderReference == reference && segment.Note == note && !clearedPersonnel && !clearedVehicles &&
            segment.OutsourcingCostAmount == cost && segment.OutsourcingCurrency == currency &&
            (provider is null || (link is not null && link.ServiceType == serviceType && link.Note == note)))
        {
            var unchanged = await BuildResponseAsync(route, segment, provider, vehicles, false, false, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return unchanged;
        }

        var before = Snapshot(route, segment, vehicles);
        var previousProviderId = segment.TransportProviderId;
        segment.TransportProviderId = provider?.TransportProviderId;
        segment.ServiceType = serviceType;
        segment.ProviderReference = reference;
        segment.Note = note;
        segment.OutsourcingCostAmount = cost;
        segment.OutsourcingCurrency = currency;
        var now = new DateTime(DateTime.UtcNow.Ticks / 10 * 10, DateTimeKind.Unspecified);
        now = now > route.UpdatedAt ? now : route.UpdatedAt.AddTicks(10);
        segment.UpdatedAt = route.UpdatedAt = now;
        plan.UpdatedAt = now > plan.UpdatedAt ? now : plan.UpdatedAt.AddTicks(10);
        if (provider is not null)
        {
            segment.DriverEmployeeId = segment.EscortEmployeeId = null;
            segment.DriverEmployee = segment.EscortEmployee = null;
            db.RoutePlanRouteVehicles.RemoveRange(vehicles);
            vehicles.Clear();
            if (link is null)
            {
                link = new TransportPlanProvider { TransportPlanId = planId, TransportProviderId = provider.TransportProviderId };
                db.TransportPlanProviders.Add(link);
            }
            link.ServiceType = serviceType;
            link.AssignedAt = now;
            link.Note = note;
        }
        if (previousProviderId.HasValue && previousProviderId != segment.TransportProviderId &&
            !await db.RoutePlanRoutes.AnyAsync(item => item.RoutePlanRouteId != segmentId && item.RoutePlan.TransportPlanId == planId &&
                item.TransportProviderId == previousProviderId, cancellationToken))
        {
            var oldLink = await db.TransportPlanProviders.SingleOrDefaultAsync(item => item.TransportPlanId == planId &&
                item.TransportProviderId == previousProviderId, cancellationToken);
            if (oldLink is not null) db.TransportPlanProviders.Remove(oldLink);
        }
        if (route.Status is RoutePlanStatus.CONFIRMED or RoutePlanStatus.PENDING_LEGAL_REVIEW or RoutePlanStatus.PENDING_MANAGER_APPROVAL or RoutePlanStatus.APPROVED)
            route.Status = RoutePlanStatus.PENDING_LEGAL_REVIEW;
        db.CatalogAuditLogs.Add(new CatalogAuditLog
        {
            CatalogAuditLogId = Guid.NewGuid(), EntityType = nameof(RoutePlanRoute), EntityId = segmentId,
            ActionType = provider is null ? "OUTSOURCING_REMOVED" : "OUTSOURCING_ASSIGNED", ActorAccountId = accountId,
            BeforeData = before, AfterData = Snapshot(route, segment, vehicles), CreatedAt = now
        });
        var bookingId = await db.Transports.Where(item => item.TransportId == plan.TransportId).Select(item => item.BookingId).SingleAsync(cancellationToken);
        var recipients = await db.Employees.Where(employee => employee.IsActive && !employee.IsDeleted &&
            employee.Role == EmployeeRole.FLEET_ROUTE_COORDINATOR && employee.Account.Status == AccountStatus.ACTIVE && !employee.Account.IsDeleted &&
            (db.EmployeeTransportPlans.Any(assignment => assignment.TransportPlanId == planId && assignment.EmployeeId == employee.EmployeeId) ||
             db.BookingEmployeeAssignments.Any(assignment => assignment.BookingId == bookingId && assignment.EmployeeId == employee.EmployeeId &&
                 assignment.AssignmentRole == EmployeeRole.FLEET_ROUTE_COORDINATOR && assignment.Status == BookingAssignmentStatus.ACTIVE)))
            .Select(employee => employee.AccountId).Distinct().ToListAsync(cancellationToken);
        foreach (var recipient in recipients)
            notifications.Queue(new NotificationCreateRequest
            {
                AccountId = recipient, TransportId = plan.TransportId, Type = NotificationType.ROUTE,
                Title = "Route outsourcing updated", Message = $"{route.RoutePlanCode}, segment {segment.SequenceNo}: {(provider is null ? "INTERNAL" : provider.ProviderName)}",
                ReferenceType = nameof(RoutePlan), ReferenceId = routePlanId, CreatedAt = now
            });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { throw Modified(); }
        var response = await BuildResponseAsync(route, segment, provider, vehicles, clearedPersonnel, clearedVehicles, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await notifications.FlushAsync(cancellationToken);
        logger.LogInformation("Route {RoutePlanId} segment {SegmentId} outsourcing {Outsourced} provider {ProviderId} by {AccountId}",
            routePlanId, segmentId, request.Outsourced, provider?.TransportProviderId, accountId);
        return response;
    }

    private async Task<Response> BuildResponseAsync(RoutePlan route, RoutePlanRoute segment, TransportProvider? provider,
        IReadOnlyList<RoutePlanRouteVehicle> vehicles, bool clearedPersonnel, bool clearedVehicles, CancellationToken cancellationToken)
    {
        var segments = await db.RoutePlanRoutes.AsNoTracking().Where(item => item.RoutePlan.TransportPlanId == route.TransportPlanId &&
            item.RoutePlan.Status != RoutePlanStatus.CANCELLED).ToArrayAsync(cancellationToken);
        var financialPlan = new Response.FinancialPlanResponse
        {
            TransportPlanId = route.TransportPlanId,
            Routes = segments.Where(item => item.TransportProviderId.HasValue)
                .GroupBy(item => new { item.RoutePlanId, item.OutsourcingCurrency })
                .OrderBy(group => group.Key.RoutePlanId).ThenBy(group => group.Key.OutsourcingCurrency)
                .Select(group => new Response.RouteCostResponse
                {
                    RoutePlanId = group.Key.RoutePlanId, Currency = group.Key.OutsourcingCurrency,
                    TotalOutsourcingCost = group.Any(item => !item.OutsourcingCostAmount.HasValue) ? null : group.Sum(item => item.OutsourcingCostAmount),
                    UnpricedSegments = group.Count(item => !item.OutsourcingCostAmount.HasValue)
                }).ToArray()
        };
        return Map(route, segment, provider, vehicles, clearedPersonnel, clearedVehicles,
            RoutePersonnel.GetStatus(segments.Where(item => item.RoutePlanId == route.RoutePlanId).ToArray()), financialPlan);
    }

    private static Response Map(RoutePlan route, RoutePlanRoute segment, TransportProvider? provider,
        IReadOnlyList<RoutePlanRouteVehicle> vehicles, bool clearedPersonnel, bool clearedVehicles, string personnelStatus,
        Response.FinancialPlanResponse financialPlan) => new()
    {
        RoutePlanId = route.RoutePlanId, RoutePlanStatus = route.Status.ToString(), ClearedPersonnel = clearedPersonnel, ClearedVehicles = clearedVehicles,
        PersonnelStatus = personnelStatus,
        FinancialPlan = financialPlan,
        UpdatedAt = new DateTimeOffset(DateTime.SpecifyKind(route.UpdatedAt, DateTimeKind.Utc)),
        Segment = new Response.SegmentResponse
        {
            RoutePlanRouteId = segment.RoutePlanRouteId, SequenceNo = segment.SequenceNo, ExecutionType = provider is null ? "INTERNAL" : "OUTSOURCED",
            Vehicles = vehicles.Select(item => new Response.VehicleResponse
            {
                VehicleId = item.VehicleId, VehicleCode = item.Vehicle.VehicleCode, VehicleName = item.Vehicle.VehicleName,
                LicensePlate = item.Vehicle.LicensePlate, TransportMode = item.Vehicle.TransportMode.ToString(), CapacityHorses = item.Vehicle.CapacityHorses
            }).ToArray(),
            Outsourcing = provider is null ? null : new Response.OutsourcingResponse
            {
                Provider = new Response.ProviderResponse
                {
                    TransportProviderId = provider.TransportProviderId, ProviderCode = provider.ProviderCode, ProviderName = provider.ProviderName,
                    Status = provider.Status.ToString(), SupportedModes = (provider.SupportedModes ?? "").ToUpperInvariant().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                },
                ServiceType = segment.ServiceType, ProviderReference = segment.ProviderReference,
                CostAmount = segment.OutsourcingCostAmount, Currency = segment.OutsourcingCurrency
            },
            Driver = Employee(segment.DriverEmployee), Escort = Employee(segment.EscortEmployee), Note = segment.Note
        }
    };

    private static Response.EmployeeResponse? Employee(Employee? employee) => employee is null ? null : new()
    {
        EmployeeId = employee.EmployeeId, EmployeeCode = employee.EmployeeCode, FullName = employee.FullName, Role = employee.Role.ToString()
    };
    private static JsonDocument Snapshot(RoutePlan route, RoutePlanRoute segment, IReadOnlyList<RoutePlanRouteVehicle> vehicles) =>
        JsonSerializer.SerializeToDocument(new
        {
            route.RoutePlanId, route.Status, route.UpdatedAt, segment.RoutePlanRouteId, segment.TransportProviderId,
            segment.ServiceType, segment.ProviderReference, segment.Note, segment.DriverEmployeeId, segment.EscortEmployeeId,
            segment.OutsourcingCostAmount, segment.OutsourcingCurrency,
            VehicleIds = vehicles.Select(item => item.VehicleId).ToArray()
        });
    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static ConflictException Modified() => new("The route plan was just modified. Please reload the page", "ROUTE_PLAN_MODIFIED");
}
