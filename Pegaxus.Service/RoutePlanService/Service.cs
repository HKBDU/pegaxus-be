using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using NotificationService = Pegaxus.Service.NotificationService;

namespace Pegaxus.Service.RoutePlanService;

public sealed class Service : IService
{
    private readonly AppDbContext db;
    private readonly NotificationService.IService notificationService;

    public Service(AppDbContext db, NotificationService.IService? notificationService = null)
    {
        this.db = db;
        this.notificationService = notificationService ?? new NotificationService.Service(db, new NotificationService.NoopNotificationRealtimeService());
    }

    public async Task<Response.DetailResponse> CreateAsync(Guid accountId, Guid transportPlanId, Request.SaveRequest request, CancellationToken cancellationToken = default)
    {
        var employee = await RequireEmployeeAsync(accountId, cancellationToken);
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;
        var plan = await LoadPlanAsync(transportPlanId, cancellationToken);
        await EnsurePlanCanBeUsedAsync(plan, cancellationToken);
        var routeType = ParseRouteType(request.RouteType);
        EnsureWriteRole(employee.Role, routeType);
        if (routeType == RouteType.RETURN && !plan.EmployeeTransportPlans.Any(x => x.EmployeeId == employee.EmployeeId &&
                x.Responsibility == "FLEET_ROUTE_COORDINATOR"))
            throw new ForbiddenException("Only the Route Coordinator assigned to this Transport Plan may create RETURN.");
        ValidateSaveRequest(request, routeType);

        var primary = plan.RoutePlans.FirstOrDefault(item => item.Status != RoutePlanStatus.CANCELLED && GetRouteType(item) == RouteType.PRIMARY);
        if (routeType == RouteType.PRIMARY && primary is not null)
            throw new ConflictException("A primary route plan already exists for this transport plan", "PRIMARY_ROUTE_PLAN_EXISTS");
        if (routeType == RouteType.BACKUP && primary is null)
            throw new ConflictException("A backup route plan requires an existing primary route plan", "PRIMARY_ROUTE_PLAN_REQUIRED");
        if (routeType == RouteType.BACKUP)
            EnsureDossierAllowsBackup(plan.Transport.ComplianceDossier);
        if (routeType == RouteType.RETURN)
        {
            if (primary is null) throw new ConflictException("A return route requires an existing PRIMARY.", "PRIMARY_ROUTE_PLAN_REQUIRED");
            if (plan.Transport.Status != TransportStatus.IN_TRANSIT)
                throw new ConflictException("A return route plan can only be created while the transport is in transit", "TRANSPORT_NOT_IN_TRANSIT");
            if (plan.Transport.ComplianceDossier?.Status != ComplianceDossierStatus.APPROVED)
                throw new ConflictException("A return route plan requires an approved compliance dossier (Transport is in transit)", "DOSSIER_NOT_APPROVED");
            if (plan.RoutePlans.Any(item => item.Status != RoutePlanStatus.CANCELLED && GetRouteType(item) == RouteType.RETURN))
                throw new ConflictException("A return route plan already exists for this transport plan", "RETURN_ROUTE_PLAN_EXISTS");
        }

        var materializedSegments = await MaterializeSegmentsAsync(request.Segments, routeType, plan, null, cancellationToken);
        ValidateRouteRules(materializedSegments, plan, routeType, primary);
        var now = UtcNow();
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
                RouteType = routeType,
                PlannedDepartureAt = ToDatabasePrecision(item.Request.PlannedDepartureAt.UtcDateTime),
                PlannedArrivalAt = ToDatabasePrecision(item.Request.PlannedArrivalAt.UtcDateTime),
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

        if (routeType == RouteType.BACKUP && plan.Transport.ComplianceDossier is not null)
        {
            plan.Transport.ComplianceDossier.Status = ComplianceDossierStatus.DRAFT;
            plan.Transport.ComplianceDossier.UpdatedAt = now;
        }

        QueueCreateNotifications(plan, routeType, request.Action, routePlan, now);
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
            if (!Enum.TryParse<RoutePlanStatus>(request.Status, true, out var parsedStatus))
                throw new BadRequestException("status is invalid", "INVALID_ROUTE_PLAN_STATUS");
            status = parsedStatus;
        }
        var items = plan.RoutePlans.AsEnumerable();
        if (!request.IncludeCancelled)
            items = items.Where(item => item.Status != RoutePlanStatus.CANCELLED);
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
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;
        var routePlan = await LoadRoutePlanAsync(routePlanId, cancellationToken);
        if (routePlan is null)
            throw new NotFoundException("Route Plan not found", "ROUTE_PLAN_NOT_FOUND");
        var routeType = GetRouteType(routePlan);
        EnsureWriteRole(employee.Role, routeType);
        if (routeType == RouteType.RETURN && !routePlan.TransportPlan.EmployeeTransportPlans.Any(x => x.EmployeeId == employee.EmployeeId &&
                x.Responsibility == "FLEET_ROUTE_COORDINATOR"))
            throw new ForbiddenException("Only the Route Coordinator assigned to this Transport Plan may edit RETURN.");
        if (routeType == RouteType.RETURN && routePlan.TransportPlan.Transport.Status != TransportStatus.IN_TRANSIT)
            throw new ConflictException("The transport is not in transit", "TRANSPORT_NOT_IN_TRANSIT");
        if (routePlan.TransportPlan.Transport.Status == TransportStatus.IN_TRANSIT && routeType != RouteType.RETURN)
            throw new ConflictException("Transport is currently in transit. Please use Emergency Route Change (UC-48) for in-transit modifications.", "ROUTE_LOCKED");
        if (routePlan.Status != RoutePlanStatus.DRAFT)
            throw new ConflictException("Transport is currently in transit. Please use Emergency Route Change (UC-48) for in-transit modifications.", "ROUTE_LOCKED");
        if (routeType == RouteType.RETURN && routePlan.TransportPlan.Transport.ComplianceDossier?.Status != ComplianceDossierStatus.DRAFT)
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

        var primary = routePlan.TransportPlan.RoutePlans.FirstOrDefault(item => item.Status != RoutePlanStatus.CANCELLED && GetRouteType(item) == RouteType.PRIMARY);
        var materializedSegments = await MaterializeSegmentsAsync(request.Segments, routeType, routePlan.TransportPlan, routePlan.RoutePlanId, cancellationToken);
        ValidateRouteRules(materializedSegments, routePlan.TransportPlan, routeType, primary, routePlan.RoutePlanId);
        db.RoutePlanRouteVehicles.RemoveRange(routePlan.RoutePlanRoutes.SelectMany(item => item.RoutePlanRouteVehicles));
        db.RoutePlanRoutes.RemoveRange(routePlan.RoutePlanRoutes);
        var now = new DateTime(Math.Max(UtcNow().Ticks, routePlan.UpdatedAt.Ticks + 10), DateTimeKind.Unspecified);
        foreach (var item in materializedSegments)
        {
            var segment = new RoutePlanRoute
            {
                RoutePlanRouteId = Guid.NewGuid(),
                RoutePlanId = routePlan.RoutePlanId,
                RouteId = item.Route.RouteId,
                SequenceNo = item.Request.SequenceNo,
                RouteType = routeType,
                PlannedDepartureAt = ToDatabasePrecision(item.Request.PlannedDepartureAt.UtcDateTime),
                PlannedArrivalAt = ToDatabasePrecision(item.Request.PlannedArrivalAt.UtcDateTime),
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
            QueueUpdateNotifications(routePlan, request.UpdateReason.Trim(), now);
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
            UpdatedAt = AsUtc(routePlan.UpdatedAt)
        };
    }

    public async Task<Response.CancelResponse> CancelAsync(Guid accountId, Guid routePlanId, Request.CancelRequest request, CancellationToken cancellationToken = default)
    {
        var employee = await RequireEmployeeAsync(accountId, cancellationToken);
        var routePlan = await LoadRoutePlanAsync(routePlanId, cancellationToken);
        if (routePlan is null)
            throw new NotFoundException("Route Plan not found", "ROUTE_PLAN_NOT_FOUND");
        var routeType = GetRouteType(routePlan);
        EnsureWriteRole(employee.Role, routeType);
        if (request.LastUpdatedAt == default)
            throw new BadRequestException("lastUpdatedAt is required");
        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new BadRequestException("reason is required");
        if (routePlan.Status != RoutePlanStatus.DRAFT)
            throw new ConflictException("Cannot cancel a Confirmed Route Plan. Please contact Logistics Manager if a route change is required.", "INVALID_ROUTE_PLAN_STATUS");
        EnsureConcurrency(routePlan.UpdatedAt, request.LastUpdatedAt);
        var cancelledAt = UtcNow();
        routePlan.Status = RoutePlanStatus.CANCELLED;
        routePlan.RouteNote = string.IsNullOrWhiteSpace(routePlan.RouteNote)
            ? $"Cancelled: {request.Reason.Trim()}"
            : $"{routePlan.RouteNote} | Cancelled: {request.Reason.Trim()}";
        routePlan.UpdatedAt = cancelledAt;
        await db.SaveChangesAsync(cancellationToken);
        return new Response.CancelResponse
        {
            RoutePlanId = routePlan.RoutePlanId,
            Status = routePlan.Status.ToString(),
            CancelledAt = AsUtc(cancelledAt),
            BookingStatus = routePlan.TransportPlan.Transport.Booking.Status.ToString()
        };
    }

    public async Task<Response.ExportResponse> ExportAsync(Guid accountId, Guid routePlanId, string? format, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(format) && !format.Equals("pdf", StringComparison.OrdinalIgnoreCase))
            throw new BadRequestException("Only pdf format is supported");
        var employee = await RequireEmployeeAsync(accountId, cancellationToken);
        var routePlan = await LoadRoutePlanAsync(routePlanId, cancellationToken);
        if (routePlan is null)
            throw new NotFoundException("Route Plan not found", "ROUTE_PLAN_NOT_FOUND");
        if (employee.Role is not (EmployeeRole.LOGISTICS_MANAGER or EmployeeRole.FLEET_ROUTE_COORDINATOR))
            throw new ForbiddenException("Only Logistics Managers and Fleet Route Coordinators can export Route Plans");

        QuestPDF.Settings.License = LicenseType.Community;
        var content = Document.Create(container => container.Page(page =>
        {
            page.Margin(30);
            page.Header().Text($"Route Plan {routePlan.RoutePlanCode}").FontSize(18).Bold();
            page.Content().Column(column =>
            {
                column.Item().Text($"Booking: {routePlan.TransportPlan.Transport.Booking.BookingCode}");
                column.Item().Text($"Type: {GetRouteType(routePlan)} | Status: {routePlan.Status}");
                column.Item().Text($"Note: {routePlan.RouteNote ?? "-"}");
                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.ConstantColumn(35);
                        columns.RelativeColumn();
                        columns.RelativeColumn();
                        columns.ConstantColumn(80);
                    });
                    table.Header(header =>
                    {
                        header.Cell().Text("#").Bold();
                        header.Cell().Text("From").Bold();
                        header.Cell().Text("To").Bold();
                        header.Cell().Text("Mode").Bold();
                    });
                    foreach (var item in routePlan.RoutePlanRoutes.OrderBy(item => item.SequenceNo))
                    {
                        table.Cell().Text(item.SequenceNo.ToString());
                        table.Cell().Text(item.Route.FromLocation.LocationName);
                        table.Cell().Text(item.Route.ToLocation.LocationName);
                        table.Cell().Text(item.Route.TransportMode.ToString());
                    }
                });
            });
        })).GeneratePdf();
        return new Response.ExportResponse
        {
            Content = content,
            FileName = $"{routePlan.RoutePlanCode}.pdf",
            ContentType = "application/pdf"
        };
    }

    public async Task<Response.LegalReviewResultResponse> ReviewLegalAsync(Guid accountId, Guid routePlanId, Request.LegalReviewRequest request, CancellationToken cancellationToken = default)
    {
        var reviewer = await RequireEmployeeAsync(accountId, cancellationToken);
        if (reviewer.Role != EmployeeRole.TRANSPORT_SPECIALIST)
            throw new ForbiddenException("Only Transport Specialists can review Route Plans");
        if (request.LastUpdatedAt == default)
            throw new BadRequestException("lastUpdatedAt is required");
        if (!request.IsFeasible && string.IsNullOrWhiteSpace(request.Note))
            throw new BadRequestException("note is required when isFeasible is false");

        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;
        if (db.Database.IsNpgsql())
        {
            var header = await db.RoutePlans.AsNoTracking().Where(x => x.RoutePlanId == routePlanId)
                .Select(x => new { x.TransportPlanId, x.TransportPlan.TransportId }).SingleOrDefaultAsync(cancellationToken)
                ?? throw new NotFoundException("Route Plan not found", "ROUTE_PLAN_NOT_FOUND");
            await db.Transports.Where(x => x.TransportId == header.TransportId).ExecuteUpdateAsync(setters => setters.SetProperty(x => x.TransportId, x => x.TransportId), cancellationToken);
            await db.TransportPlans.Where(x => x.TransportPlanId == header.TransportPlanId).ExecuteUpdateAsync(setters => setters.SetProperty(x => x.TransportPlanId, x => x.TransportPlanId), cancellationToken);
            await db.RoutePlans.Where(x => x.RoutePlanId == routePlanId).ExecuteUpdateAsync(setters => setters.SetProperty(x => x.RoutePlanId, x => x.RoutePlanId), cancellationToken);
            await db.ComplianceDossiers.Where(x => x.TransportId == header.TransportId).ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ComplianceDossierId, x => x.ComplianceDossierId), cancellationToken);
        }
        var routePlan = await LoadRoutePlanAsync(routePlanId, cancellationToken);
        if (routePlan is null)
            throw new NotFoundException("Route Plan not found", "ROUTE_PLAN_NOT_FOUND");
        if (db.Database.IsRelational())
        {
            await db.Entry(routePlan).ReloadAsync(cancellationToken);
            await db.Entry(routePlan.TransportPlan.Transport).ReloadAsync(cancellationToken);
            if (routePlan.TransportPlan.Transport.ComplianceDossier is { } currentDossier)
                await db.Entry(currentDossier).ReloadAsync(cancellationToken);
        }
        if (GetRouteType(routePlan) == RouteType.RETURN && routePlan.TransportPlan.Transport.Status != TransportStatus.IN_TRANSIT)
            throw new ConflictException("The transport is not in transit", "TRANSPORT_NOT_IN_TRANSIT");
        if (routePlan.Status != RoutePlanStatus.DRAFT)
            throw new ConflictException($"This action cannot be performed while the route plan is in {routePlan.Status} status", "INVALID_ROUTE_PLAN_STATUS");
        if (!routePlan.TransportPlan.EmployeeTransportPlans.Any(item => item.EmployeeId == reviewer.EmployeeId && item.Employee.Role == EmployeeRole.TRANSPORT_SPECIALIST))
            throw new ForbiddenException("Only the Transport Specialist assigned to this Transport Plan can review it");
        EnsureConcurrency(routePlan.UpdatedAt, request.LastUpdatedAt);
        var routeType = GetRouteType(routePlan);
        var validSequences = routePlan.RoutePlanRoutes.Select(item => item.SequenceNo).ToHashSet();
        if (request.BlockedSegments.Any(sequence => !validSequences.Contains(sequence)))
            throw new BadRequestException("blockedSegments contains an unknown sequenceNo");

        var dossier = routePlan.TransportPlan.Transport.ComplianceDossier;
        if (dossier is null)
            throw new ConflictException("Compliance Dossier must be initialized before legal review", "DOSSIER_NOT_INITIALIZED");
        if (routeType == RouteType.RETURN && dossier.Status != ComplianceDossierStatus.DRAFT)
            throw new ConflictException("Route Plan RETURN has not been approved by the Logistics Manager", "RETURN_NOT_APPROVED");
        if (request.IsFeasible && dossier.Status != ComplianceDossierStatus.DRAFT)
            throw new ConflictException("Compliance Dossier must be in DRAFT before legal review", "INVALID_DOSSIER_STATUS");

        var now = new DateTime(Math.Max(UtcNow().Ticks, routePlan.UpdatedAt.Ticks + 10), DateTimeKind.Unspecified);
        AddDossierAudit(dossier, reviewer, "ROUTE_PLAN_LEGAL_REVIEW", routePlan, request, now, "Legal feasibility review recorded.");

        if (request.IsFeasible)
        {
            if (routeType == RouteType.RETURN)
            {
                AddDossierAudit(dossier, reviewer, "ROUTE_RETURN_LEGAL_REVIEW", routePlan, request, now, "Return dossier review started.");
                SetDossierStatus(dossier, ComplianceDossierStatus.IN_PREPARATION, now);
                AddDossierAudit(dossier, reviewer, "COMPLIANCE_DOSSIER_IN_PREPARATION", routePlan, request, now, "Return dossier moved to IN_PREPARATION.");
                SetDossierStatus(dossier, ComplianceDossierStatus.READY_FOR_SUBMISSION, now);
                AddDossierAudit(dossier, reviewer, "COMPLIANCE_DOSSIER_READY_FOR_SUBMISSION", routePlan, request, now, "Return dossier marked ready for submission.");
                SetDossierStatus(dossier, ComplianceDossierStatus.SUBMITTED, now);
                AddDossierAudit(dossier, reviewer, "COMPLIANCE_DOSSIER_SUBMITTED", routePlan, request, now, "Return dossier submitted without customer upload.");
                SetDossierStatus(dossier, ComplianceDossierStatus.APPROVED, now);
                dossier.ApprovedAt = now;
                AddDossierAudit(dossier, reviewer, "COMPLIANCE_DOSSIER_APPROVED", routePlan, request, now, "Return dossier approved.");
            }
            else
            {
                SetDossierStatus(dossier, ComplianceDossierStatus.IN_PREPARATION, now);
                AddDossierAudit(dossier, reviewer, "ROUTE_PLAN_LEGAL_REVIEW_APPROVED", routePlan, request, now, "Route Plan is legally feasible.");
            }
            routePlan.Status = RoutePlanStatus.CONFIRMED;
            foreach (var vehicle in routePlan.RoutePlanRoutes.SelectMany(item => item.RoutePlanRouteVehicles).Select(item => item.Vehicle).DistinctBy(item => item.VehicleId))
                if (vehicle.Status != VehicleStatus.IN_USE) vehicle.Status = VehicleStatus.ASSIGNED;
            if (routeType == RouteType.PRIMARY &&
                routePlan.RoutePlanRoutes.Where(item => !item.TransportProviderId.HasValue)
                    .All(item => item.DriverEmployeeId.HasValue && item.EscortEmployeeId.HasValue))
                routePlan.TransportPlan.Transport.Status = TransportStatus.READY;
        }
        else
        {
            AddDossierAudit(dossier, reviewer, "ROUTE_PLAN_LEGAL_REVIEW_REJECTED", routePlan, request, now, "Route Plan requires revision.");
        }
        routePlan.UpdatedAt = now;
        QueueLegalReviewNotifications(routePlan, reviewer, routeType, request, now);
        await db.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
            await transaction.CommitAsync(cancellationToken);
        await notificationService.FlushAsync(cancellationToken);

        return new Response.LegalReviewResultResponse
        {
            RoutePlanId = routePlan.RoutePlanId,
            Status = routePlan.Status.ToString(),
            LegalReview = CreateLegalReviewResponse(request, reviewer, now),
            CustomerUploadEnabled = request.IsFeasible && routeType != RouteType.RETURN,
            PersonnelAssignmentPhase2Enabled = request.IsFeasible && routeType == RouteType.PRIMARY,
            UpdatedAt = AsUtc(routePlan.UpdatedAt)
        };
    }

    private async Task<Response.LegalReviewResponse?> GetLatestLegalReviewAsync(Guid routePlanId, CancellationToken cancellationToken)
    {
        var audit = await db.AuditDossiers.AsNoTracking()
            .Where(item => item.TargetEntity == "RoutePlan" && item.TargetId == routePlanId && item.ActionType == "ROUTE_PLAN_LEGAL_REVIEW")
            .OrderByDescending(item => item.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (audit?.Details is null)
            return null;
        var snapshot = audit.Details.Deserialize<LegalReviewSnapshot>();
        if (snapshot is null)
            return null;
        var reviewer = await db.Employees.AsNoTracking().SingleOrDefaultAsync(item => item.EmployeeId == snapshot.ReviewedByEmployeeId, cancellationToken);
        if (reviewer is null)
            return null;
        return new Response.LegalReviewResponse
        {
            IsFeasible = snapshot.IsFeasible,
            Note = snapshot.Note,
            BlockedSegments = snapshot.BlockedSegments ?? [],
            ReviewedBy = new Response.ReviewedEmployeeResponse
            {
                EmployeeId = reviewer.EmployeeId,
                EmployeeCode = reviewer.EmployeeCode,
                FullName = reviewer.FullName,
                Role = reviewer.Role.ToString()
            },
            ReviewedAt = AsUtc(snapshot.ReviewedAt)
        };
    }

    private static Response.LegalReviewResponse CreateLegalReviewResponse(Request.LegalReviewRequest request, Employee reviewer, DateTime reviewedAt) => new()
    {
        IsFeasible = request.IsFeasible,
        Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
        BlockedSegments = request.BlockedSegments.Distinct().OrderBy(item => item).ToList(),
        ReviewedBy = new Response.ReviewedEmployeeResponse
        {
            EmployeeId = reviewer.EmployeeId,
            EmployeeCode = reviewer.EmployeeCode,
            FullName = reviewer.FullName,
            Role = reviewer.Role.ToString()
        },
        ReviewedAt = AsUtc(reviewedAt)
    };

    private void AddDossierAudit(ComplianceDossier dossier, Employee reviewer, string action, RoutePlan routePlan, Request.LegalReviewRequest request, DateTime now, string summary)
    {
        db.AuditDossiers.Add(new AuditDossier
        {
            AuditDossierId = Guid.NewGuid(),
            ComplianceDossierId = dossier.ComplianceDossierId,
            ActionType = action,
            TargetEntity = "RoutePlan",
            TargetId = routePlan.RoutePlanId,
            ActorAccountId = reviewer.AccountId,
            Summary = summary,
            Details = JsonSerializer.SerializeToDocument(new LegalReviewSnapshot
            {
                ReviewedByEmployeeId = reviewer.EmployeeId,
                IsFeasible = request.IsFeasible,
                Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
                BlockedSegments = request.BlockedSegments.Distinct().OrderBy(item => item).ToList(),
                ReviewedAt = now
            }),
            Visibility = AuditVisibility.Internal,
            CreatedAt = now
        });
    }

    private static void SetDossierStatus(ComplianceDossier dossier, ComplianceDossierStatus status, DateTime now)
    {
        dossier.Status = status;
        dossier.UpdatedAt = now;
        if (status == ComplianceDossierStatus.IN_PREPARATION)
            dossier.OpenedAt = dossier.OpenedAt == default ? now : dossier.OpenedAt;
        if (status == ComplianceDossierStatus.READY_FOR_SUBMISSION)
            dossier.ReadyForSubmissionAt = now;
    }

    private void QueueLegalReviewNotifications(RoutePlan routePlan, Employee reviewer, RouteType routeType, Request.LegalReviewRequest request, DateTime now)
    {
        var recipients = new HashSet<Guid>();
        if (request.IsFeasible)
        {
            if (routeType != RouteType.RETURN)
                recipients.Add(routePlan.TransportPlan.Transport.Booking.Customer.AccountId);
            foreach (var employee in routePlan.TransportPlan.EmployeeTransportPlans
                         .Where(item => item.Employee.Role is EmployeeRole.FLEET_ROUTE_COORDINATOR or EmployeeRole.LOGISTICS_MANAGER)
                         .Select(item => item.Employee))
                recipients.Add(employee.AccountId);
            if (routeType == RouteType.RETURN)
                foreach (var employee in db.Employees.Where(item => item.Role == EmployeeRole.LOGISTICS_MANAGER && item.IsActive && !item.IsDeleted))
                    recipients.Add(employee.AccountId);
        }
        else
        {
            foreach (var employee in routePlan.TransportPlan.EmployeeTransportPlans.Where(item => item.Employee.Role == EmployeeRole.FLEET_ROUTE_COORDINATOR).Select(item => item.Employee))
                recipients.Add(employee.AccountId);
        }

        foreach (var recipient in recipients.Where(item => item != reviewer.AccountId))
            notificationService.Queue(new NotificationService.NotificationCreateRequest
            {
                AccountId = recipient,
                TransportId = routePlan.TransportPlan.TransportId,
                Type = request.IsFeasible ? NotificationType.ROUTE : NotificationType.COMPLIANCE,
                Priority = request.IsFeasible ? NotificationPriority.NORMAL : NotificationPriority.HIGH,
                Title = request.IsFeasible ? "Route Plan legal review confirmed" : "Route Plan requires revision",
                Message = request.IsFeasible
                    ? routeType == RouteType.RETURN
                        ? $"Return Route Plan {routePlan.RoutePlanCode} is legally feasible and requires Logistics Manager action."
                        : "The route has been confirmed. Please upload the required legal documents."
                    : $"Route Plan {routePlan.RoutePlanCode} requires revision. {request.Note}",
                ReferenceType = "RoutePlan",
                ReferenceId = routePlan.RoutePlanId,
                ActionUrl = $"/admin/route-plans/{routePlan.RoutePlanId}",
                CreatedAt = now
            });
    }

    private sealed class LegalReviewSnapshot
    {
        public Guid ReviewedByEmployeeId { get; set; }
        public bool IsFeasible { get; set; }
        public string? Note { get; set; }
        public List<int>? BlockedSegments { get; set; }
        public DateTime ReviewedAt { get; set; }
    }

    private async Task<Employee> RequireEmployeeAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var employee = await db.Employees.Include(item => item.Account)
            .SingleOrDefaultAsync(item => item.AccountId == accountId && item.IsActive && !item.IsDeleted && item.Account.Status == AccountStatus.ACTIVE && !item.Account.IsDeleted, cancellationToken);
        return employee ?? throw new ForbiddenException("Only active employees can access Route Plans");
    }

    private async Task<TransportPlan> LoadPlanAsync(Guid transportPlanId, CancellationToken cancellationToken)
    {
        var plan = await db.TransportPlans
            .Include(item => item.Transport).ThenInclude(item => item.Booking).ThenInclude(item => item.Customer)
            .Include(item => item.Transport).ThenInclude(item => item.Booking).ThenInclude(item => item.BookingHorses)
            .Include(item => item.Transport).ThenInclude(item => item.ComplianceDossier)
            .Include(item => item.EmployeeTransportPlans).ThenInclude(item => item.Employee)
            .Include(item => item.RoutePlans).ThenInclude(item => item.RoutePlanRoutes).ThenInclude(item => item.Route).ThenInclude(item => item.FromLocation)
            .Include(item => item.RoutePlans).ThenInclude(item => item.RoutePlanRoutes).ThenInclude(item => item.Route).ThenInclude(item => item.ToLocation)
            .Include(item => item.RoutePlans).ThenInclude(item => item.RoutePlanRoutes).ThenInclude(item => item.DriverEmployee)
            .Include(item => item.RoutePlans).ThenInclude(item => item.RoutePlanRoutes).ThenInclude(item => item.EscortEmployee)
            .SingleOrDefaultAsync(item => item.TransportPlanId == transportPlanId, cancellationToken);
        return plan ?? throw new NotFoundException("Transport Plan not found", "TRANSPORT_PLAN_NOT_FOUND");
    }

    private async Task<RoutePlan?> LoadRoutePlanAsync(Guid routePlanId, CancellationToken cancellationToken) =>
        await db.RoutePlans
            .Include(item => item.TransportPlan).ThenInclude(item => item.Transport).ThenInclude(item => item.Booking).ThenInclude(item => item.Customer)
            .Include(item => item.TransportPlan).ThenInclude(item => item.Transport).ThenInclude(item => item.ComplianceDossier)
            .Include(item => item.TransportPlan).ThenInclude(item => item.EmployeeTransportPlans).ThenInclude(item => item.Employee)
            .Include(item => item.TransportPlan).ThenInclude(item => item.RoutePlans).ThenInclude(item => item.RoutePlanRoutes).ThenInclude(item => item.Route).ThenInclude(item => item.FromLocation)
            .Include(item => item.TransportPlan).ThenInclude(item => item.RoutePlans).ThenInclude(item => item.RoutePlanRoutes).ThenInclude(item => item.Route).ThenInclude(item => item.ToLocation)
            .Include(item => item.TransportPlan).ThenInclude(item => item.RoutePlans).ThenInclude(item => item.RoutePlanRoutes).ThenInclude(item => item.RoutePlanRouteVehicles).ThenInclude(item => item.Vehicle)
            .Include(item => item.TransportPlan).ThenInclude(item => item.RoutePlans).ThenInclude(item => item.RoutePlanRoutes).ThenInclude(item => item.TransportProvider)
            .Include(item => item.TransportPlan).ThenInclude(item => item.RoutePlans).ThenInclude(item => item.RoutePlanRoutes).ThenInclude(item => item.DriverEmployee)
            .Include(item => item.TransportPlan).ThenInclude(item => item.RoutePlans).ThenInclude(item => item.RoutePlanRoutes).ThenInclude(item => item.EscortEmployee)
            .Include(item => item.RoutePlanRoutes).ThenInclude(item => item.Route).ThenInclude(item => item.FromLocation)
            .Include(item => item.RoutePlanRoutes).ThenInclude(item => item.Route).ThenInclude(item => item.ToLocation)
            .Include(item => item.RoutePlanRoutes).ThenInclude(item => item.RoutePlanRouteVehicles).ThenInclude(item => item.Vehicle)
            .Include(item => item.RoutePlanRoutes).ThenInclude(item => item.TransportProvider)
            .Include(item => item.RoutePlanRoutes).ThenInclude(item => item.DriverEmployee)
            .Include(item => item.RoutePlanRoutes).ThenInclude(item => item.EscortEmployee)
            .SingleOrDefaultAsync(item => item.RoutePlanId == routePlanId, cancellationToken);

    private static void ValidateSaveRequest(Request.SaveRequest request, RouteType routeType, bool updating = false)
    {
        if (request.Segments.Count == 0)
            throw new BadRequestException("At least one route segment is required");
        if (request.Action is not ("SAVE_DRAFT" or "SUBMIT_FOR_LEGAL_REVIEW"))
            throw new BadRequestException("action must be SAVE_DRAFT or SUBMIT_FOR_LEGAL_REVIEW");
        if (routeType == RouteType.RETURN && !updating && request.Action != "SAVE_DRAFT")
            throw new BadRequestException("RETURN route plans only support SAVE_DRAFT");
        if (routeType is RouteType.BACKUP or RouteType.RETURN && string.IsNullOrWhiteSpace(request.RouteNote) || routeType is RouteType.BACKUP or RouteType.RETURN && request.RouteNote!.Trim().Length < 10)
            throw new BadRequestException("routeNote is required and must contain at least 10 characters");
        var expected = 1;
        foreach (var segment in request.Segments.OrderBy(item => item.SequenceNo))
        {
            if (segment.SequenceNo != expected++)
                throw new BadRequestException("sequenceNo must be contiguous starting from 1");
            if (segment.PlannedDepartureAt == default || segment.PlannedArrivalAt == default || segment.PlannedArrivalAt <= segment.PlannedDepartureAt)
                throw new BadRequestException("plannedArrivalAt must be after plannedDepartureAt");
            if (segment.RouteId is null && (segment.FromLocationId is null || segment.ToLocationId is null || string.IsNullOrWhiteSpace(segment.TransportMode)))
                throw new BadRequestException("routeId or fromLocationId, toLocationId and transportMode is required");
            if (segment.RouteId is not null && (segment.FromLocationId is not null || segment.ToLocationId is not null || !string.IsNullOrWhiteSpace(segment.TransportMode)))
                throw new BadRequestException("Use either routeId or the route location/mode fields, not both");
            if (segment.Outsourcing is not null && segment.VehicleIds.Count > 0)
                throw new UnprocessableEntityException("An outsourced segment cannot have vehicles, driver or escort", "OUTSOURCED_SEGMENT");
        }
    }

    private async Task<List<MaterializedSegment>> MaterializeSegmentsAsync(IReadOnlyCollection<Request.SegmentRequest> requests, RouteType routeType, TransportPlan plan, Guid? excludedRoutePlanId, CancellationToken cancellationToken)
    {
        var result = new List<MaterializedSegment>();
        foreach (var request in requests.OrderBy(item => item.SequenceNo))
        {
            Route route;
            if (request.RouteId.HasValue)
            {
                route = await db.Routes.Include(item => item.FromLocation).Include(item => item.ToLocation)
                    .SingleOrDefaultAsync(item => item.RouteId == request.RouteId.Value && !item.IsDeleted, cancellationToken)
                    ?? throw new NotFoundException("Route not found", "ROUTE_NOT_FOUND");
            }
            else
            {
                var mode = ParseTransportMode(request.TransportMode!);
                var from = await db.Locations.SingleOrDefaultAsync(item => item.LocationId == request.FromLocationId && !item.IsDeleted, cancellationToken)
                    ?? throw new NotFoundException("Location not found", "LOCATION_NOT_FOUND");
                var to = await db.Locations.SingleOrDefaultAsync(item => item.LocationId == request.ToLocationId && !item.IsDeleted, cancellationToken)
                    ?? throw new NotFoundException("Location not found", "LOCATION_NOT_FOUND");
                route = await db.Routes.FirstOrDefaultAsync(item => item.FromLocationId == from.LocationId && item.ToLocationId == to.LocationId && item.TransportMode == mode && !item.IsDeleted, cancellationToken)
                    ?? new Route
                    {
                        RouteId = Guid.NewGuid(),
                        RouteCode = $"RT-AUTO-{Guid.NewGuid():N}"[..20],
                        FromLocationId = from.LocationId,
                        ToLocationId = to.LocationId,
                        TransportMode = mode,
                        RouteName = $"{from.LocationName} - {to.LocationName}",
                        CreatedAt = UtcNow(),
                        UpdatedAt = UtcNow()
                    };
                if (route.RouteId != Guid.Empty && db.Entry(route).State == EntityState.Detached)
                    db.Routes.Add(route);
                route.FromLocation = from;
                route.ToLocation = to;
            }
            await ValidateVehicleAndProviderAsync(request, route, plan, excludedRoutePlanId, cancellationToken);
            result.Add(new MaterializedSegment(request, route));
        }
        return result;
    }

    private async Task ValidateVehicleAndProviderAsync(Request.SegmentRequest request, Route route, TransportPlan plan, Guid? excludedRoutePlanId, CancellationToken cancellationToken)
    {
        if (request.Outsourcing is not null)
        {
            var provider = db.TransportProviders.SingleOrDefault(item => item.TransportProviderId == request.Outsourcing.TransportProviderId && !item.IsDeleted);
            if (provider is null)
                throw new NotFoundException("Transport Provider not found", "TRANSPORT_PROVIDER_NOT_FOUND");
            if (!SupportsMode(provider.SupportedModes, route.TransportMode))
                throw new UnprocessableEntityException("The provider does not support the transport mode of this segment", "PROVIDER_MODE_MISMATCH");
        }
        if (request.VehicleIds.Count == 0)
            return;
        var vehicles = db.Vehicles.Where(item => request.VehicleIds.Contains(item.VehicleId) && !item.IsDeleted).ToList();
        if (vehicles.Count != request.VehicleIds.Distinct().Count())
            throw new NotFoundException("Vehicle not found", "VEHICLE_NOT_FOUND");
        if (vehicles.Any(item => item.TransportMode != route.TransportMode))
            throw new UnprocessableEntityException("The vehicle transport mode does not match the segment", "VEHICLE_MODE_MISMATCH");
        if (vehicles.Any(item => item.Status is not (VehicleStatus.AVAILABLE or VehicleStatus.ASSIGNED)))
            throw new UnprocessableEntityException("The vehicle is not available", "VEHICLE_UNAVAILABLE");
        if (vehicles.Sum(item => item.CapacityHorses ?? 0) < plan.Transport.Booking.BookingHorses.Count)
            throw new UnprocessableEntityException("Total vehicle capacity is lower than the number of horses in the booking", "VEHICLE_CAPACITY_EXCEEDED");
        var departureDate = DateOnly.FromDateTime(request.PlannedDepartureAt.UtcDateTime);
        if (vehicles.Any(item =>
                item.RegistrationExpiryDate.HasValue && item.RegistrationExpiryDate.Value < departureDate ||
                item.InsuranceExpiryDate.HasValue && item.InsuranceExpiryDate.Value < departureDate))
            throw new UnprocessableEntityException("The vehicle registration or insurance is expired for this segment", "VEHICLE_DOCUMENT_EXPIRED");

        var vehicleIds = vehicles.Select(item => item.VehicleId).ToList();
        var overlappingAssignment = await db.RoutePlanRouteVehicles
            .Include(item => item.RoutePlanRoute)
                .ThenInclude(item => item.RoutePlan)
            .Where(item => vehicleIds.Contains(item.VehicleId) &&
                item.RoutePlanRoute.RoutePlan.Status != RoutePlanStatus.CANCELLED &&
                (!excludedRoutePlanId.HasValue || item.RoutePlanRoute.RoutePlanId != excludedRoutePlanId.Value) &&
                item.RoutePlanRoute.PlannedDepartureAt < request.PlannedArrivalAt.UtcDateTime &&
                item.RoutePlanRoute.PlannedArrivalAt > request.PlannedDepartureAt.UtcDateTime)
            .Select(item => item.VehicleId)
            .FirstOrDefaultAsync(cancellationToken);
        if (overlappingAssignment != Guid.Empty)
            throw new ConflictException("The vehicle is already assigned during this time slot", "VEHICLE_SCHEDULE_CONFLICT");
    }

    private static void ValidateRouteRules(IReadOnlyList<MaterializedSegment> segments, TransportPlan plan, RouteType routeType, RoutePlan? primary, Guid? updatingRoutePlanId = null)
    {
        for (var index = 1; index < segments.Count; index++)
        {
            if (segments[index - 1].Route.ToLocationId != segments[index].Route.FromLocationId)
                throw new UnprocessableEntityException("Segments must be continuous: each segment must start where the previous one ends", "SEGMENT_NOT_CONTINUOUS");
            if (segments[index].Request.PlannedDepartureAt < segments[index - 1].Request.PlannedArrivalAt)
                throw new UnprocessableEntityException("A segment cannot depart before the previous segment arrives", "SEGMENT_TIME_OVERLAP");
        }
        if (plan.Transport.Booking.OriginCountry != plan.Transport.Booking.DestinationCountry && !segments.Any(item =>
                item.Route.FromLocation.LocationType is LocationType.BORDER_GATE or LocationType.AIRPORT or LocationType.SEAPORT ||
                item.Route.ToLocation.LocationType is LocationType.BORDER_GATE or LocationType.AIRPORT or LocationType.SEAPORT))
            throw new UnprocessableEntityException("A cross-border transport requires at least one border control point", "BCP_REQUIRED");
        if (segments.Any(item => item.Route.TransportMode == TransportMode.ROAD && item.Request.PlannedArrivalAt - item.Request.PlannedDepartureAt > TimeSpan.FromHours(8)))
            throw new UnprocessableEntityException("Maximum continuous driving time is 8 hours", "REST_RULE_VIOLATION");
        for (var index = 0; index < segments.Count; index++)
        {
            var segment = segments[index];
            if (segment.Route.TransportMode != TransportMode.ROAD || segment.Request.PlannedArrivalAt - segment.Request.PlannedDepartureAt <= TimeSpan.FromMinutes(270))
                continue;
            var restAfter = index < segments.Count - 1
                ? segments[index + 1].Request.PlannedDepartureAt - segment.Request.PlannedArrivalAt
                : TimeSpan.Zero;
            if (restAfter < TimeSpan.FromMinutes(45))
                throw new UnprocessableEntityException("Road segments exceeding 4.5 hours require a rest stop of at least 45 minutes", "REST_RULE_VIOLATION");
        }
        if (segments.Count > 0 && segments[^1].Request.PlannedArrivalAt - segments[0].Request.PlannedDepartureAt > TimeSpan.FromHours(24) &&
            !segments.Zip(segments.Skip(1), (first, second) => new
            {
                Gap = second.Request.PlannedDepartureAt - first.Request.PlannedArrivalAt,
                IsRestStop = first.Route.ToLocation.LocationType == LocationType.REST_STOP || second.Route.FromLocation.LocationType == LocationType.REST_STOP
            }).Any(item => item.Gap >= TimeSpan.FromHours(24) && item.IsRestStop))
            throw new UnprocessableEntityException("Trips over 24 hours require a mandatory 24 hour rest", "REST_RULE_VIOLATION");
        if (routeType == RouteType.BACKUP && primary is not null)
        {
            var primarySegments = primary.RoutePlanRoutes.OrderBy(item => item.SequenceNo).ToList();
            var identical = primarySegments.Count == segments.Count && primarySegments.Zip(segments, (left, right) => left.RouteId == right.Route.RouteId && left.Route.FromLocationId == right.Route.FromLocationId && left.Route.ToLocationId == right.Route.ToLocationId).All(item => item);
            if (identical)
                throw new ConflictException("Contingency route cannot be identical to the primary route. Please select alternative waypoints or border checkpoints.", "IDENTICAL_CONTINGENCY_ROUTE");
            var primaryLocations = primarySegments.SelectMany(item => new[] { item.Route.FromLocationId, item.Route.ToLocationId }).ToHashSet();
            if (!primaryLocations.Contains(segments[0].Route.FromLocationId) || !primaryLocations.Contains(segments[^1].Route.ToLocationId))
                throw new UnprocessableEntityException("The backup route must start and end on locations of the primary route (R16)", "BACKUP_NOT_ANCHORED");
        }
    }

    private static void EnsureDossierAllowsBackup(ComplianceDossier? dossier)
    {
        if (dossier is not null && dossier.Status > ComplianceDossierStatus.IN_PREPARATION)
            throw new ConflictException("A backup route plan cannot be created once the compliance dossier is past IN_PREPARATION", "DOSSIER_LOCKED");
    }

    private static void EnsureWriteRole(EmployeeRole role, RouteType type)
    {
        if (role == EmployeeRole.FLEET_ROUTE_COORDINATOR)
            return;
        if (role == EmployeeRole.LOGISTICS_MANAGER && type == RouteType.BACKUP)
            return;
        throw new ForbiddenException("The current role cannot manage this route plan type");
    }

    private static void EnsureReadRole(EmployeeRole role, TransportPlan plan, Guid employeeId)
    {
        if (role is not (EmployeeRole.LOGISTICS_MANAGER or EmployeeRole.TRANSPORT_SPECIALIST or EmployeeRole.FLEET_ROUTE_COORDINATOR))
            throw new ForbiddenException("Only RC, LM and TS can access Route Plans");
        if (role == EmployeeRole.TRANSPORT_SPECIALIST && !plan.EmployeeTransportPlans.Any(item => item.EmployeeId == employeeId))
            throw new ForbiddenException("You are not assigned to this Transport Plan");
    }

    private static async Task EnsurePlanCanBeUsedAsync(TransportPlan plan, CancellationToken cancellationToken)
    {
        if (plan.Transport.Booking.Status != BookingStatus.APPROVED)
            throw new ConflictException($"This action cannot be performed while the booking is in {plan.Transport.Booking.Status} status", "INVALID_BOOKING_STATUS");
        if (plan.Status != TransportPlanStatus.APPROVED)
            throw new ConflictException("Route Plan requires an approved Transport Plan", "INVALID_TRANSPORT_PLAN_STATUS");
        await Task.CompletedTask;
    }

    private static RouteType ParseRouteType(string value) => Enum.TryParse<RouteType>(value, true, out var result) && result is RouteType.PRIMARY or RouteType.BACKUP or RouteType.RETURN
        ? result
        : throw new BadRequestException("routeType must be PRIMARY, BACKUP or RETURN");

    private static TransportMode ParseTransportMode(string value) => Enum.TryParse<TransportMode>(value, true, out var result)
        ? result
        : throw new BadRequestException("transportMode is invalid");

    private static bool SupportsMode(string? supportedModes, TransportMode mode) => string.IsNullOrWhiteSpace(supportedModes) || supportedModes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any(item => item.Equals(mode.ToString(), StringComparison.OrdinalIgnoreCase));

    private static RouteType GetRouteType(RoutePlan plan)
    {
        var types = plan.RoutePlanRoutes.Select(item => item.RouteType).Distinct().ToList();
        return types.Count == 1 ? types[0] : RouteType.PRIMARY;
    }

    private static Response.SummaryResponse ToSummary(RoutePlan item)
    {
        var segments = item.RoutePlanRoutes.OrderBy(route => route.SequenceNo).ToList();
        var routeType = GetRouteType(item);
        return new Response.SummaryResponse
        {
            RoutePlanId = item.RoutePlanId,
            RoutePlanCode = item.RoutePlanCode,
            TransportPlanId = item.TransportPlanId,
            RouteType = routeType.ToString(),
            Status = item.Status.ToString(),
            TotalDistanceKm = item.TotalDistanceKm ?? 0,
            EstimatedDurationMinutes = item.EstimatedDurationMinutes ?? 0,
            SegmentCount = segments.Count,
            IsPersonnelComplete = segments.Where(segment => !segment.TransportProviderId.HasValue).All(segment => segment.DriverEmployeeId.HasValue && segment.EscortEmployeeId.HasValue),
            ReplacesSequenceNo = routeType == RouteType.BACKUP && segments.Count > 0
                ? new Response.SequenceRangeResponse { From = segments[0].SequenceNo, To = segments[^1].SequenceNo }
                : null,
            RouteNote = item.RouteNote,
            UpdatedAt = AsUtc(item.UpdatedAt)
        };
    }

    private static Response.DetailResponse ToDetail(RoutePlan item, Response.LegalReviewResponse? legalReview = null)
    {
        var segments = item.RoutePlanRoutes.OrderBy(route => route.SequenceNo).ToList();
        var effective = GetRouteType(item) == RouteType.PRIMARY && item.Status == RoutePlanStatus.ACTIVE;
        if (effective)
            segments = item.TransportPlan.RoutePlans.Where(plan => plan.Status == RoutePlanStatus.ACTIVE)
                .SelectMany(plan => plan.RoutePlanRoutes).Where(segment => segment.SupersededAt == null)
                .OrderBy(segment => segment.PlannedDepartureAt).ThenBy(segment => segment.SequenceNo).ToList();
        return new Response.DetailResponse
        {
            RoutePlanId = item.RoutePlanId,
            RoutePlanCode = item.RoutePlanCode,
            TransportPlanId = item.TransportPlanId,
            BookingId = item.TransportPlan.Transport.BookingId,
            BookingCode = item.TransportPlan.Transport.Booking.BookingCode,
            RouteType = GetRouteType(item).ToString(),
            Status = item.Status.ToString(),
            IsCrossBorder = item.TransportPlan.Transport.Booking.OriginCountry != item.TransportPlan.Transport.Booking.DestinationCountry,
            TotalDistanceKm = effective ? segments.Sum(x => x.Route.DistanceKm ?? 0) : item.TotalDistanceKm ?? 0,
            EstimatedDurationMinutes = effective && segments.Count > 0 &&
                segments[0].PlannedDepartureAt.HasValue && segments[^1].PlannedArrivalAt.HasValue
                    ? (int)(segments[^1].PlannedArrivalAt!.Value - segments[0].PlannedDepartureAt!.Value).TotalMinutes
                    : item.EstimatedDurationMinutes ?? 0,
            PlannedDepartureAt = segments.FirstOrDefault()?.PlannedDepartureAt is { } start ? AsUtc(start) : null,
            PlannedArrivalAt = segments.LastOrDefault()?.PlannedArrivalAt is { } end ? AsUtc(end) : null,
            Segments = segments.Select((segment, index) => new Response.SegmentResponse
            {
                RoutePlanRouteId = segment.RoutePlanRouteId,
                SequenceNo = segment.SequenceNo,
                RouteType = segment.RouteType.ToString(),
                Route = new Response.RouteResponse { RouteId = segment.Route.RouteId, RouteCode = segment.Route.RouteCode, RouteName = segment.Route.RouteName, TransportMode = segment.Route.TransportMode.ToString(), DistanceKm = segment.Route.DistanceKm, EstimatedDurationMinutes = segment.Route.EstimatedDurationMinutes },
                FromLocation = ToLocation(segment.Route.FromLocation),
                ToLocation = ToLocation(segment.Route.ToLocation),
                PlannedDepartureAt = segment.PlannedDepartureAt.HasValue ? AsUtc(segment.PlannedDepartureAt.Value) : null,
                PlannedArrivalAt = segment.PlannedArrivalAt.HasValue ? AsUtc(segment.PlannedArrivalAt.Value) : null,
                ActualDepartureAt = segment.ActualDepartureAt.HasValue ? AsUtc(segment.ActualDepartureAt.Value) : null,
                ActualArrivalAt = segment.ActualArrivalAt.HasValue ? AsUtc(segment.ActualArrivalAt.Value) : null,
                Status = segment.ActualArrivalAt.HasValue
                    ? "COMPLETED"
                    : segment.ActualDepartureAt.HasValue
                        ? "IN_PROGRESS"
                        : "PENDING",
                DwellAfterMinutes = index < segments.Count - 1 && segment.PlannedArrivalAt.HasValue && segments[index + 1].PlannedDepartureAt.HasValue ? (int)(segments[index + 1].PlannedDepartureAt.GetValueOrDefault() - segment.PlannedArrivalAt.GetValueOrDefault()).TotalMinutes : null,
                IsOutsourced = segment.TransportProviderId.HasValue,
                Vehicles = segment.RoutePlanRouteVehicles.Select(item => new Response.VehicleResponse { VehicleId = item.Vehicle.VehicleId, VehicleCode = item.Vehicle.VehicleCode, VehicleName = item.Vehicle.VehicleName, LicensePlate = item.Vehicle.LicensePlate, TransportMode = item.Vehicle.TransportMode.ToString(), CapacityHorses = item.Vehicle.CapacityHorses }).ToList(),
                Outsourcing = segment.TransportProvider is null ? null : new Response.ProviderResponse { TransportProviderId = segment.TransportProvider.TransportProviderId, ProviderCode = segment.TransportProvider.ProviderCode, ProviderName = segment.TransportProvider.ProviderName, Status = segment.TransportProvider.Status.ToString(), SupportedModes = ParseSupportedModes(segment.TransportProvider.SupportedModes) },
                Driver = segment.DriverEmployee is null ? null : new Response.EmployeeResponse { EmployeeId = segment.DriverEmployee.EmployeeId, EmployeeCode = segment.DriverEmployee.EmployeeCode, FullName = segment.DriverEmployee.FullName, Role = segment.DriverEmployee.Role.ToString() },
                Escort = segment.EscortEmployee is null ? null : new Response.EmployeeResponse { EmployeeId = segment.EscortEmployee.EmployeeId, EmployeeCode = segment.EscortEmployee.EmployeeCode, FullName = segment.EscortEmployee.FullName, Role = segment.EscortEmployee.Role.ToString() },
                Note = segment.Note
            }).ToList(),
            IsPersonnelComplete = segments.Where(item => !item.TransportProviderId.HasValue).All(item => item.DriverEmployeeId.HasValue && item.EscortEmployeeId.HasValue),
            LegalReview = legalReview,
            RouteNote = item.RouteNote,
            CreatedAt = AsUtc(item.CreatedAt),
            UpdatedAt = AsUtc(item.UpdatedAt)
        };
    }

    private static Response.LocationResponse ToLocation(Location item) => new() { LocationId = item.LocationId, LocationName = item.LocationName, LocationType = item.LocationType?.ToString(), City = item.City, Country = item.Country, Latitude = item.Latitude, Longitude = item.Longitude };

    private static IReadOnlyList<string> ParseSupportedModes(string? supportedModes) =>
        string.IsNullOrWhiteSpace(supportedModes)
            ? []
            : supportedModes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static int CalculateDurationMinutes(IReadOnlyList<MaterializedSegment> segments)
    {
        if (segments.Count == 0)
            return 0;
        var duration = segments[^1].Request.PlannedArrivalAt - segments[0].Request.PlannedDepartureAt;
        return Math.Max(0, (int)duration.TotalMinutes);
    }

    private void QueueCreateNotifications(TransportPlan plan, RouteType routeType, string action, RoutePlan routePlan, DateTime now)
    {
        if ((routeType is RouteType.PRIMARY or RouteType.BACKUP) && action == "SUBMIT_FOR_LEGAL_REVIEW")
            foreach (var accountId in plan.EmployeeTransportPlans.Where(item => item.Employee.Role == EmployeeRole.TRANSPORT_SPECIALIST).Select(item => item.Employee.AccountId).Distinct())
                notificationService.Queue(new NotificationService.NotificationCreateRequest { AccountId = accountId, TransportId = plan.TransportId, Type = NotificationType.COMPLIANCE, Priority = NotificationPriority.NORMAL, Title = "Backup route requires legal review", Message = $"Route Plan {routePlan.RoutePlanCode} requires compliance review.", ReferenceType = "RoutePlan", ReferenceId = routePlan.RoutePlanId, ActionUrl = $"/admin/route-plans/{routePlan.RoutePlanId}", CreatedAt = now });
        if (routeType == RouteType.RETURN)
            foreach (var accountId in db.Employees.Where(item => item.Role == EmployeeRole.LOGISTICS_MANAGER && item.IsActive && !item.IsDeleted).Select(item => item.AccountId))
                notificationService.Queue(new NotificationService.NotificationCreateRequest { AccountId = accountId, TransportId = plan.TransportId, Type = NotificationType.ROUTE, Priority = NotificationPriority.URGENT, Title = "Return route plan created", Message = $"Return Route Plan {routePlan.RoutePlanCode} requires your approval.", ReferenceType = "RoutePlan", ReferenceId = routePlan.RoutePlanId, ActionUrl = $"/admin/route-plans/{routePlan.RoutePlanId}", CreatedAt = now });
    }

    private void QueueUpdateNotifications(RoutePlan routePlan, string reason, DateTime now)
    {
        foreach (var accountId in routePlan.TransportPlan.EmployeeTransportPlans
                     .Where(item => item.Employee.Role == EmployeeRole.TRANSPORT_SPECIALIST)
                     .Select(item => item.Employee.AccountId)
                     .Distinct())
            notificationService.Queue(new NotificationService.NotificationCreateRequest
            {
                AccountId = accountId,
                TransportId = routePlan.TransportPlan.TransportId,
                Type = NotificationType.COMPLIANCE,
                Priority = NotificationPriority.NORMAL,
                Title = "Route Plan requires legal re-review",
                Message = $"Route Plan {routePlan.RoutePlanCode} was updated. Reason: {reason}",
                ReferenceType = "RoutePlan",
                ReferenceId = routePlan.RoutePlanId,
                ActionUrl = $"/admin/route-plans/{routePlan.RoutePlanId}",
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

    private static void EnsureConcurrency(DateTime actual, DateTimeOffset expected)
    {
        if (ToDatabasePrecision(expected.UtcDateTime) != actual)
            throw new ConflictException("The route plan was just modified. Please reload the page", "ROUTE_PLAN_MODIFIED");
    }

    private static DateTime UtcNow() => new(DateTime.UtcNow.Ticks / 10 * 10, DateTimeKind.Unspecified);
    private static DateTime ToDatabasePrecision(DateTime value) => new(value.Ticks / 10 * 10, DateTimeKind.Unspecified);
    private static DateTimeOffset AsUtc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    private static DateTimeOffset? AsUtc(DateTime? value) => value.HasValue ? AsUtc(value.Value) : null;
    private sealed record MaterializedSegment(Request.SegmentRequest Request, Route Route);
}
