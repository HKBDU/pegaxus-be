using System.Data;
using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Repository.Extensions;
using Pegaxus.Service.Exceptions;
using QuestPDF.Infrastructure;

using Pegaxus.Service.Common;

using static Pegaxus.Service.Common.DbTime;

namespace Pegaxus.Service.RoutePlanService;

public sealed partial class Service
{
    public async Task<Response.DetailResponse> CreateAsync(Guid accountId, Guid transportPlanId, Request.SaveRequest request, CancellationToken cancellationToken = default)
    {
        var employee = await RequireEmployeeAsync(accountId, cancellationToken);
        await using var transaction = await db.Database.BeginSerializableOrNoneAsync(cancellationToken);
        var plan = await LoadPlanAsync(transportPlanId, cancellationToken);
        await EnsurePlanCanBeUsedAsync(plan, cancellationToken);
        var routeType = ParseRouteType(request.RouteType);
        EnsureWriteRole(employee.Role, routeType);
        if (routeType == RouteType.RETURN && employee.Role == EmployeeRole.FLEET_ROUTE_COORDINATOR && !plan.EmployeeTransportPlans.Any(x => x.EmployeeId == employee.EmployeeId &&
                x.Responsibility == "FLEET_ROUTE_COORDINATOR"))
            throw new ForbiddenException("Only the Route Coordinator assigned to this Transport Plan or a Logistics Manager may create RETURN.");
        ValidateSaveRequest(request, routeType);

        var primary = plan.RoutePlans.FirstOrDefault(item => item.Status is not (RoutePlanStatus.CANCELLED or RoutePlanStatus.REJECTED_BY_MANAGER) && GetRouteType(item) == RouteType.PRIMARY);
        if (routeType == RouteType.PRIMARY && primary is not null)
            throw new ConflictException("A primary route plan already exists for this transport plan", "PRIMARY_ROUTE_PLAN_EXISTS");
        if (routeType == RouteType.BACKUP && primary is null)
            throw new ConflictException("A backup route plan requires an existing primary route plan", "PRIMARY_ROUTE_PLAN_REQUIRED");
        if (routeType == RouteType.RETURN)
        {
            if (primary is null) throw new ConflictException("A return route requires an existing PRIMARY.", "PRIMARY_ROUTE_PLAN_REQUIRED");
            if (plan.Transport.Status != TransportStatus.IN_TRANSIT)
                throw new ConflictException("A return route plan can only be created while the transport is in transit", "TRANSPORT_NOT_IN_TRANSIT");
            if (plan.Transport.MainDossier?.Status != ComplianceDossierStatus.APPROVED)
                throw new ConflictException("A return route plan requires an approved compliance dossier (Transport is in transit)", "DOSSIER_NOT_APPROVED");
            if (plan.RoutePlans.Any(item => item.Status is not (RoutePlanStatus.CANCELLED or RoutePlanStatus.REJECTED_BY_MANAGER) && GetRouteType(item) == RouteType.RETURN))
                throw new ConflictException("A return route plan already exists for this transport plan", "RETURN_ROUTE_PLAN_EXISTS");
        }

        var materializedSegments = await MaterializeSegmentsAsync(request.Segments, routeType, plan, null, segment => SegmentType(segment, routeType, null), cancellationToken);
        ValidateRouteRules(materializedSegments, plan, routeType, primary);
        var now = UtcNowTruncated();
        if (request.IncidentReportId is { } incidentId)
            await IncidentLinks.AttachAsync(db, incidentId, plan.TransportId, now, cancellationToken);
        var routePlan = new RoutePlan
        {
            RoutePlanId = Guid.NewGuid(),
            TransportPlanId = transportPlanId,
            RoutePlanCode = await GenerateCodeAsync(now, cancellationToken),
            Status = RoutePlanStatus.DRAFT,
            PrimaryRoutePlanId = routeType == RouteType.PRIMARY ? null : primary?.RoutePlanId,
            TotalDistanceKm = materializedSegments.Sum(item => item.Route.DistanceKm ?? 0),
            EstimatedDurationMinutes = CalculateDurationMinutes(materializedSegments),
            RouteNote = string.IsNullOrWhiteSpace(request.RouteNote) ? null : request.RouteNote.Trim(),
            CreatedAt = now,
            UpdatedAt = now
        };
        db.RoutePlans.Add(routePlan);
        foreach (var item in materializedSegments)
        {
            var route = item.Route;
            var routePlanRoute = new RoutePlanRoute
            {
                RoutePlanRouteId = Guid.NewGuid(),
                RoutePlanId = routePlan.RoutePlanId,
                RouteId = route.RouteId,
                SequenceNo = item.Request.SequenceNo,
                RouteType = item.Type,
                PlannedDepartureAt = Truncate(item.Request.PlannedDepartureAt.UtcDateTime),
                PlannedArrivalAt = Truncate(item.Request.PlannedArrivalAt.UtcDateTime),
                TransportProviderId = item.Request.Outsourcing?.TransportProviderId,
                Note = string.IsNullOrWhiteSpace(item.Request.Note) ? null : item.Request.Note.Trim(),
                CreatedAt = now,
                UpdatedAt = now
            };
            db.RoutePlanRoutes.Add(routePlanRoute);
            foreach (var vehicleId in item.Request.VehicleIds.Distinct())
                db.RoutePlanRouteVehicles.Add(new RoutePlanRouteVehicle
                {
                    RoutePlanRouteId = routePlanRoute.RoutePlanRouteId,
                    VehicleId = vehicleId,
                    AssignedAt = now,
                    Note = "Assigned by Route Plan seed/request."
                });
        }

        await QueueCreateNotificationsAsync(plan, routeType, request.Action, routePlan, employee.AccountId, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
            await transaction.CommitAsync(cancellationToken);
        await notificationService.FlushAsync(cancellationToken);
        return await GetByIdAsync(accountId, routePlan.RoutePlanId, cancellationToken);
    }

    public async Task<Response.ListResponse> GetListAsync(Guid accountId, Guid transportPlanId, Request.ListRequest request, CancellationToken cancellationToken = default)
    {
        var employee = await RequireEmployeeAsync(accountId, cancellationToken);
        var plan = await LoadPlanAsync(transportPlanId, cancellationToken);
        EnsureReadRole(employee.Role, plan, employee.EmployeeId);
        RouteType? routeType = null;
        if (!string.IsNullOrWhiteSpace(request.RouteType))
            routeType = ParseRouteType(request.RouteType);
        RoutePlanStatus? status = null;
        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            if (!EnumParser.TryParseDefined<RoutePlanStatus>(request.Status, out var parsedStatus))
                throw new BadRequestException("status is invalid", "INVALID_ROUTE_PLAN_STATUS");
            status = parsedStatus;
        }
        var items = plan.RoutePlans.AsEnumerable();
        if (!request.IncludeCancelled)
            items = items.Where(item => item.Status is not (RoutePlanStatus.CANCELLED or RoutePlanStatus.REJECTED_BY_MANAGER));
        if (routeType.HasValue)
            items = items.Where(item => GetRouteType(item) == routeType.Value);
        if (status.HasValue)
            items = items.Where(item => item.Status == status.Value);
        return new Response.ListResponse
        {
            Items = items.OrderBy(item => item.CreatedAt).Select(ToSummary).ToList()
        };
    }

    public async Task<Response.DetailResponse> GetByIdAsync(Guid accountId, Guid routePlanId, CancellationToken cancellationToken = default)
    {
        var employee = await RequireEmployeeAsync(accountId, cancellationToken);
        var routePlan = await LoadRoutePlanAsync(routePlanId, cancellationToken);
        if (routePlan is null)
            throw new NotFoundException("Route Plan not found", "ROUTE_PLAN_NOT_FOUND");
        EnsureReadRole(employee.Role, routePlan.TransportPlan, employee.EmployeeId);
        return ToDetail(routePlan, await GetLatestLegalReviewAsync(routePlan.RoutePlanId, cancellationToken));
    }

    public async Task<Response.UpdateResponse> UpdateAsync(Guid accountId, Guid routePlanId, Request.UpdateRequest request, CancellationToken cancellationToken = default)
    {
        var employee = await RequireEmployeeAsync(accountId, cancellationToken);
        await using var transaction = await db.Database.BeginSerializableOrNoneAsync(cancellationToken);
        var routePlan = await LoadRoutePlanAsync(routePlanId, cancellationToken);
        if (routePlan is null)
            throw new NotFoundException("Route Plan not found", "ROUTE_PLAN_NOT_FOUND");
        var routeType = GetRouteType(routePlan);
        EnsureWriteRole(employee.Role, routeType);
        if (routeType == RouteType.RETURN && employee.Role == EmployeeRole.FLEET_ROUTE_COORDINATOR && !routePlan.TransportPlan.EmployeeTransportPlans.Any(x => x.EmployeeId == employee.EmployeeId &&
                x.Responsibility == "FLEET_ROUTE_COORDINATOR"))
            throw new ForbiddenException("Only the Route Coordinator assigned to this Transport Plan or a Logistics Manager may edit RETURN.");
        if (routeType == RouteType.RETURN && routePlan.TransportPlan.Transport.Status != TransportStatus.IN_TRANSIT)
            throw new ConflictException("The transport is not in transit", "TRANSPORT_NOT_IN_TRANSIT");
        if (routePlan.TransportPlan.Transport.Status == TransportStatus.IN_TRANSIT && routeType != RouteType.RETURN)
            throw new ConflictException("Transport is currently in transit. Please use Emergency Route Change (UC-48) for in-transit modifications.", "ROUTE_LOCKED");
        if (routePlan.Status != RoutePlanStatus.DRAFT)
            throw new ConflictException("Transport is currently in transit. Please use Emergency Route Change (UC-48) for in-transit modifications.", "ROUTE_LOCKED");
        if (routeType == RouteType.RETURN && routePlan.TransportPlan.Transport.ExtraDossier?.Status != ComplianceDossierStatus.DRAFT)
            throw new ConflictException("RETURN must be approved by the Logistics Manager before editing.", "RETURN_NOT_APPROVED");
        EnsureConcurrency(routePlan.UpdatedAt, request.LastUpdatedAt);
        if (string.IsNullOrWhiteSpace(request.UpdateReason))
            throw new BadRequestException("updateReason is required");
        ValidateSaveRequest(new Request.SaveRequest
        {
            RouteType = routeType.ToString(),
            Segments = request.Segments,
            RouteNote = request.RouteNote,
            Action = request.Action
        }, routeType, true);

        var primary = routePlan.TransportPlan.RoutePlans.FirstOrDefault(item => item.Status is not (RoutePlanStatus.CANCELLED or RoutePlanStatus.REJECTED_BY_MANAGER) && GetRouteType(item) == RouteType.PRIMARY);
        var existingTypes = routePlan.RoutePlanRoutes.ToDictionary(item => item.SequenceNo, item => item.RouteType);
        var materializedSegments = await MaterializeSegmentsAsync(request.Segments, routeType, routePlan.TransportPlan, routePlan.RoutePlanId,
            segment => SegmentType(segment, routeType, existingTypes), cancellationToken);
        ValidateRouteRules(materializedSegments, routePlan.TransportPlan, routeType, primary, routePlan.RoutePlanId);
        db.RoutePlanRouteVehicles.RemoveRange(routePlan.RoutePlanRoutes.SelectMany(item => item.RoutePlanRouteVehicles));
        db.RoutePlanRoutes.RemoveRange(routePlan.RoutePlanRoutes);
        var now = new DateTime(Math.Max(UtcNowTruncated().Ticks, routePlan.UpdatedAt.Ticks + 10), DateTimeKind.Unspecified);
        foreach (var item in materializedSegments)
        {
            var segment = new RoutePlanRoute
            {
                RoutePlanRouteId = Guid.NewGuid(),
                RoutePlanId = routePlan.RoutePlanId,
                RouteId = item.Route.RouteId,
                SequenceNo = item.Request.SequenceNo,
                RouteType = item.Type,
                Route = item.Route,
                PlannedDepartureAt = Truncate(item.Request.PlannedDepartureAt.UtcDateTime),
                PlannedArrivalAt = Truncate(item.Request.PlannedArrivalAt.UtcDateTime),
                TransportProviderId = item.Request.Outsourcing?.TransportProviderId,
                Note = string.IsNullOrWhiteSpace(item.Request.Note) ? null : item.Request.Note.Trim(),
                CreatedAt = now,
                UpdatedAt = now
            };
            db.RoutePlanRoutes.Add(segment);
            foreach (var vehicleId in item.Request.VehicleIds.Distinct())
                db.RoutePlanRouteVehicles.Add(new RoutePlanRouteVehicle { RoutePlanRouteId = segment.RoutePlanRouteId, VehicleId = vehicleId, AssignedAt = now });
        }
        routePlan.TotalDistanceKm = materializedSegments.Sum(item => item.Route.DistanceKm ?? 0);
        routePlan.EstimatedDurationMinutes = CalculateDurationMinutes(materializedSegments);
        routePlan.RouteNote = string.IsNullOrWhiteSpace(request.RouteNote) ? null : request.RouteNote.Trim();
        routePlan.UpdatedAt = now;
        if (request.Action == "SUBMIT_FOR_LEGAL_REVIEW")
            await QueueUpdateNotificationsAsync(routePlan, request.UpdateReason.Trim(), now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
            await transaction.CommitAsync(cancellationToken);
        await notificationService.FlushAsync(cancellationToken);
        var segments = routePlan.RoutePlanRoutes.ToList();
        return new Response.UpdateResponse
        {
            RoutePlanId = routePlan.RoutePlanId,
            Status = routePlan.Status.ToString(),
            TotalDistanceKm = routePlan.TotalDistanceKm ?? 0,
            EstimatedDurationMinutes = routePlan.EstimatedDurationMinutes ?? 0,
            SegmentCount = segments.Count,
            IsPersonnelComplete = segments.Where(item => !item.TransportProviderId.HasValue).All(item => item.DriverEmployeeId.HasValue && item.EscortEmployeeId.HasValue),
            BranchSequenceNo = TransportItinerary.BranchSequenceNo(routePlan),
            BranchLocation = BranchLocation(routePlan),
            UpdatedAt = AsUtc(routePlan.UpdatedAt)
        };
    }

    public async Task<Response.CancelResponse> CancelAsync(Guid accountId, Guid routePlanId, Request.CancelRequest request, CancellationToken cancellationToken = default)
    {
        var employee = await RequireEmployeeAsync(accountId, cancellationToken);
        await using var transaction = await db.Database.BeginSerializableOrNoneAsync(cancellationToken);
        var routePlan = await LoadRoutePlanAsync(routePlanId, cancellationToken);
        if (routePlan is null)
            throw new NotFoundException("Route Plan not found", "ROUTE_PLAN_NOT_FOUND");
        var routeType = GetRouteType(routePlan);
        EnsureWriteRole(employee.Role, routeType);
        if (request.LastUpdatedAt == default)
            throw new BadRequestException("lastUpdatedAt is required");
        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new BadRequestException("reason is required");
        if (routeType == RouteType.RETURN && routePlan.TransportPlan.Transport.ExtraDossier is not null)
            throw new ConflictException("The RETURN Route Plan has already been approved by the Logistics Manager and cannot be cancelled.", "RETURN_ALREADY_APPROVED");
        if (routePlan.Status != RoutePlanStatus.DRAFT)
            throw new ConflictException($"Cannot cancel a {routePlan.Status} Route Plan. Please contact Logistics Manager if a route change is required.", "INVALID_ROUTE_PLAN_STATUS");
        EnsureConcurrency(routePlan.UpdatedAt, request.LastUpdatedAt);
        var cancelledAt = UtcNowTruncated();
        routePlan.Status = RoutePlanStatus.CANCELLED;
        routePlan.RouteNote = string.IsNullOrWhiteSpace(routePlan.RouteNote)
            ? $"Cancelled: {request.Reason.Trim()}"
            : $"{routePlan.RouteNote} | Cancelled: {request.Reason.Trim()}";
        routePlan.UpdatedAt = cancelledAt;
        await db.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
            await transaction.CommitAsync(cancellationToken);
        return new Response.CancelResponse
        {
            RoutePlanId = routePlan.RoutePlanId,
            Status = routePlan.Status.ToString(),
            CancelledAt = AsUtc(cancelledAt),
            BookingStatus = routePlan.TransportPlan.Transport.Booking.Status.ToString()
        };
    }

    private async Task QueueCreateNotificationsAsync(TransportPlan plan, RouteType routeType, string action, RoutePlan routePlan, Guid creatorAccountId, DateTime now,
        CancellationToken cancellationToken)
    {
        if ((routeType is RouteType.PRIMARY or RouteType.BACKUP) && action == "SUBMIT_FOR_LEGAL_REVIEW")
            foreach (var accountId in plan.EmployeeTransportPlans.Where(item => item.Employee.Role == EmployeeRole.TRANSPORT_SPECIALIST).Select(item => item.Employee.AccountId).Distinct())
                notificationService.Queue(new NotificationService.NotificationCreateRequest { AccountId = accountId, TransportId = plan.TransportId, Type = NotificationType.COMPLIANCE, Priority = NotificationPriority.NORMAL, Title = routeType == RouteType.BACKUP ? "Backup route requires legal review" : "Route plan requires legal review", Message = $"Route plan {routePlan.RoutePlanCode} for transport plan {plan.PlanCode} was submitted for legal review. Check that it is feasible, then confirm it or request changes.", ReferenceType = "RoutePlan", ReferenceId = routePlan.RoutePlanId, ActionUrl = NotificationLinks.RoutePlan(EmployeeRole.TRANSPORT_SPECIALIST, routePlan.RoutePlanId), CreatedAt = now });
        var managerIds = routeType == RouteType.RETURN
            // A Logistics Manager who drafts the RETURN decides on it through the same approval call, so they are not asked to approve their own draft.
            ? await db.Employees.Where(item => item.Role == EmployeeRole.LOGISTICS_MANAGER && item.IsActive && !item.IsDeleted && item.AccountId != creatorAccountId)
                .Select(item => item.AccountId).ToListAsync(cancellationToken)
            : [];
        if (routeType == RouteType.RETURN)
            foreach (var accountId in managerIds)
                notificationService.Queue(new NotificationService.NotificationCreateRequest { AccountId = accountId, TransportId = plan.TransportId, Type = NotificationType.ROUTE, Priority = NotificationPriority.URGENT, Title = "Return route needs your approval", Message = $"Return route plan {routePlan.RoutePlanCode} for transport plan {plan.PlanCode} was created and needs your approval before the transport can head back.", ReferenceType = "RoutePlan", ReferenceId = routePlan.RoutePlanId, ActionUrl = NotificationLinks.RoutePlan(EmployeeRole.LOGISTICS_MANAGER, routePlan.RoutePlanId, "change-decision"), CreatedAt = now });
        await MailService.MailRecipients.QueueAsync(db, managerIds, now, recipient => MailService.MailRecipients.Mail(recipient,
            $"{MailService.EmailBodies.RouteReviewTitle(MailService.EmailBodies.RouteReviewKind.ReturnApproval)} — {routePlan.RoutePlanCode}",
            MailService.EmailBodies.RouteReviewBody(recipient.FullName, routePlan.RoutePlanCode, MailService.EmailBodies.RouteReviewKind.ReturnApproval, null,
                NotificationLinks.RoutePlan(recipient.Role, routePlan.RoutePlanId, "change-decision"))), cancellationToken);
    }

    private async Task QueueUpdateNotificationsAsync(RoutePlan routePlan, string reason, DateTime now, CancellationToken cancellationToken)
    {
        var specialistIds = routePlan.TransportPlan.EmployeeTransportPlans
            .Where(item => item.Employee.Role == EmployeeRole.TRANSPORT_SPECIALIST)
            .Select(item => item.Employee.AccountId)
            .Distinct().ToList();
        await MailService.MailRecipients.QueueAsync(db, specialistIds, now, recipient => MailService.MailRecipients.Mail(recipient,
            $"{MailService.EmailBodies.RouteReviewTitle(MailService.EmailBodies.RouteReviewKind.LegalReviewRequested)} — {routePlan.RoutePlanCode}",
            MailService.EmailBodies.RouteReviewBody(recipient.FullName, routePlan.RoutePlanCode, MailService.EmailBodies.RouteReviewKind.LegalReviewRequested, reason,
                NotificationLinks.RoutePlan(recipient.Role, routePlan.RoutePlanId))), cancellationToken);
        foreach (var accountId in specialistIds)
            notificationService.Queue(new NotificationService.NotificationCreateRequest
            {
                AccountId = accountId,
                TransportId = routePlan.TransportPlan.TransportId,
                Type = NotificationType.COMPLIANCE,
                Priority = NotificationPriority.HIGH,
                Title = "Route plan needs another legal review",
                Message = $"Route plan {routePlan.RoutePlanCode} was updated and needs another legal review. Reason for the update: {reason}",
                ReferenceType = "RoutePlan",
                ReferenceId = routePlan.RoutePlanId,
                ActionUrl = NotificationLinks.RoutePlan(EmployeeRole.TRANSPORT_SPECIALIST, routePlan.RoutePlanId),
                CreatedAt = now
            });
    }

    private async Task<string> GenerateCodeAsync(DateTime now, CancellationToken cancellationToken)
    {
        for (var i = 1; i <= 9999; i++)
        {
            var code = $"RP-{now:yyyyMMdd}-{i:0000}";
            if (!await db.RoutePlans.AnyAsync(item => item.RoutePlanCode == code, cancellationToken))
                return code;
        }
        throw new ConflictException("Unable to generate a unique Route Plan code", "ROUTE_PLAN_CODE_GENERATION_FAILED");
    }
}
