using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Repository.Extensions;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;

using static Pegaxus.Service.Common.DbTime;

namespace Pegaxus.Service.IncidentService;

public sealed partial class Service
{
    public async Task<Response.EscalationResult> EscalateRerouteAsync(Guid incidentReportId, Request.RerouteEscalationRequest request, Guid accountId, CancellationToken cancellationToken = default)
    {
        var ct = cancellationToken;
        Validation.ValidateFields(request, escalationValidator);
        for (var attempt = 0; ; attempt++)
        {
            var committed = false;
            try
            {
                await using var transaction = await db.Database.BeginSerializableAsync(ct);
                var (incident, transport) = await LockIncidentAsync(incidentReportId, ct);
                await RerouteActorAsync(transport, accountId, EmployeeRole.FLEET_ROUTE_COORDINATOR, ct);
                EnsureRerouteWritable(incident, transport);
                var planId = await CurrentPlanIdAsync(transport.TransportId, ct);
                if (await db.RoutePlans.CountAsync(plan => request.BlockedRoutePlanIds.Contains(plan.RoutePlanId) && plan.TransportPlanId == planId, ct) != request.BlockedRoutePlanIds.Count)
                    throw new BadRequestException("Blocked route plans must belong to the current transport plan", "VALIDATION_ERROR");
                var now = Truncate(clock.GetUtcNow());
                incident.Status = IncidentStatus.PENDING; incident.UpdatedAt = NextUpdate(incident);
                var recipients = (await InternalRecipientsAsync(transport.TransportId, ct))
                    .Where(employee => employee.Role is EmployeeRole.LOGISTICS_MANAGER or EmployeeRole.TRANSPORT_SPECIALIST).ToArray();
                var message = $"Incident {incident.IncidentCode}: {request.Reason.Trim()}";
                if (request.SuggestedAction is { } suggestion)
                    message += $"\nSuggested action: {suggestion}" + (request.SuggestedHoldMinutes is { } minutes ? $" for {minutes} minutes" : "");
                if (request.BlockedRoutePlanIds.Count > 0) message += $"\nBlocked route plans: {string.Join(", ", request.BlockedRoutePlanIds)}";
                foreach (var employee in recipients)
                    QueueReroute(incident, employee, NotificationPriority.URGENT, "Primary and backup routes are not feasible", message, false, now);
                var result = new Response.EscalationResult(incidentReportId, incident.Status.ToString(),
                    recipients.Where(employee => employee.Role == EmployeeRole.LOGISTICS_MANAGER).Select(RerouteEmployee).ToArray(), AsUtc(now));
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                committed = true;
                await notifications.FlushAsync(ct);
                return result;
            }
            catch (Exception error) when (!committed && PostgresErrors.IsWriteConflict(error))
            {
                db.ChangeTracker.Clear();
                if (attempt < 2) continue;
                throw new ConflictException("The incident was just modified. Please reload the page", "INCIDENT_MODIFIED");
            }
        }
    }

    public async Task<Response.HoldResult> HoldRouteAsync(Guid incidentReportId, Request.RouteHoldRequest request, Guid accountId, CancellationToken cancellationToken = default)
    {
        var ct = cancellationToken;
        Validation.ValidateFields(request, holdValidator);
        for (var attempt = 0; ; attempt++)
        {
            var committed = false;
            try
            {
                await using var transaction = await db.Database.BeginSerializableAsync(ct);
                var (incident, transport) = await LockIncidentAsync(incidentReportId, ct);
                var actor = await RerouteActorAsync(transport, accountId, EmployeeRole.LOGISTICS_MANAGER, ct);
                EnsureRerouteWritable(incident, transport);
                var existing = await ActiveTransportHoldAsync(transport.TransportId, ct);
                if (existing is not null)
                    throw new ConflictException($"The transport is already on hold until {AsUtc(existing.ExpiresAt):O}", "HOLD_ALREADY_ACTIVE");
                var location = request.SafeStopLocationId is { } locationId
                    ? await db.Locations.AsNoTracking().SingleOrDefaultAsync(item => item.LocationId == locationId && !item.IsDeleted, ct)
                        ?? throw new NotFoundException("Location not found", "LOCATION_NOT_FOUND") : null;
                var now = Truncate(clock.GetUtcNow());
                var hold = new Response.Hold("ACTIVE", AsUtc(now), AsUtc(now.AddMinutes(request.HoldMinutes)), request.HoldMinutes,
                    request.MonitorIntervalMinutes, request.Reason.Trim(), LocationBrief.From(location), RerouteEmployee(actor));
                // The recipient-free notification persists the hold; inbox messages remain human-readable.
                db.Notifications.Add(new Notification
                {
                    NotificationId = Guid.NewGuid(), TransportId = transport.TransportId, NotificationType = NotificationType.ROUTE,
                    Priority = NotificationPriority.URGENT, Title = "Transport hold decision", Message = JsonSerializer.Serialize(hold),
                    ReferenceType = HoldReference, ReferenceId = incidentReportId, CreatedAt = now, ExpiresAt = now.AddMinutes(request.HoldMinutes)
                });
                incident.Status = IncidentStatus.PENDING; incident.UpdatedAt = NextUpdate(incident);
                var current = await CurrentSegmentAsync(transport, ct);
                var crewIds = current is { TransportProviderId: null }
                    ? new[] { current.DriverEmployeeId, current.EscortEmployeeId }.OfType<Guid>().ToArray() : [];
                var crew = await db.Employees.AsNoTracking().Active().Include(employee => employee.Account)
                    .Where(employee => crewIds.Contains(employee.EmployeeId) && (employee.Role == EmployeeRole.DRIVER || employee.Role == EmployeeRole.ESCORT) &&
                        employee.Account.AccountType == AccountType.EMPLOYEE).ToArrayAsync(ct);
                var recipients = crew.Concat((await InternalRecipientsAsync(transport.TransportId, ct))
                    .Where(employee => employee.Role != EmployeeRole.LOGISTICS_MANAGER)).DistinctBy(employee => employee.AccountId).ToArray();
                foreach (var employee in recipients)
                    QueueReroute(incident, employee, Crew(employee) ? NotificationPriority.URGENT : NotificationPriority.HIGH,
                        Crew(employee) ? "Hold at the nearest safe stop" : "Transport on hold, waiting for the backup route",
                        $"Wait until {hold.HoldUntil:O}. Monitor horse vital signs every {hold.MonitorIntervalMinutes} minutes. " +
                        (location is null ? "" : $"Safe stop: {location.LocationName}. ") + hold.Reason, true, now);
                var result = new Response.HoldResult(incidentReportId, incident.Status.ToString(), hold, recipients.Select(RerouteEmployee).ToArray());
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                committed = true;
                await notifications.FlushAsync(ct);
                return result;
            }
            catch (Exception error) when (!committed && PostgresErrors.IsWriteConflict(error))
            {
                db.ChangeTracker.Clear();
                if (attempt < 2) continue;
                throw new ConflictException("The incident was just modified. Please reload the page", "INCIDENT_MODIFIED");
            }
        }
    }
}
