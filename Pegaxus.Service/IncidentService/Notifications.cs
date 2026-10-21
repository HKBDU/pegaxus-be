using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Repository.Extensions;
using Pegaxus.Service.MailService;
using Pegaxus.Service.OutboxService;

namespace Pegaxus.Service.IncidentService;

public sealed partial class Service
{
    private async Task<Employee[]> InternalRecipientsAsync(Guid? transportId, CancellationToken ct)
    {
        var planId = transportId.HasValue ? await CurrentPlanIdAsync(transportId.Value, ct) : null;
        return await db.Employees.AsNoTracking().Active().Include(employee => employee.Account)
            .Where(employee => employee.Account.AccountType == AccountType.EMPLOYEE &&
                (employee.Role == EmployeeRole.LOGISTICS_MANAGER ||
                 (employee.Role == EmployeeRole.FLEET_ROUTE_COORDINATOR || employee.Role == EmployeeRole.TRANSPORT_SPECIALIST) &&
                    employee.EmployeeTransportPlans.Any(item => item.TransportPlanId == planId))).ToArrayAsync(ct);
    }

    private async Task<Response.Effects> QueueReportedAsync(IncidentReport incident, Transport transport, bool vehicleStatusChanged, DateTime now, CancellationToken ct)
    {
        var priority = incident.Severity is IncidentSeverity.HIGH or IncidentSeverity.CRITICAL || incident.Category == IncidentCategory.BOTH ||
            incident.VehicleIncidentDetail?.IncidentType == VehicleIncidentType.TEMPERATURE_CONTROL_FAILURE ? NotificationPriority.URGENT : NotificationPriority.HIGH;
        var recipients = await InternalRecipientsAsync(transport.TransportId, ct);
        var emailQueued = false;
        foreach (var employee in recipients)
        {
            QueueIncident(incident, employee.AccountId, priority, $"Incident {incident.IncidentCode} reported", incident.Title, $"/admin/incidents/{incident.IncidentReportId}", now);
            if (priority == NotificationPriority.URGENT && employee.Role == EmployeeRole.LOGISTICS_MANAGER && !string.IsNullOrWhiteSpace(employee.Account.Email))
            {
                OutboxQueue.AddEmail(db, new MailContent
                {
                    To = employee.Account.Email, Subject = $"Urgent incident {incident.IncidentCode}",
                    Body = EmailBodies.IncidentReportedBody(employee.FullName, incident.IncidentCode, incident.Severity.ToString(),
                        incident.Title, incident.Description, $"/admin/incidents/{incident.IncidentReportId}")
                }, now);
                emailQueued = true;
            }
        }
        if (incident.Category == IncidentCategory.BOTH)
        {
            var current = await CurrentSegmentAsync(transport, ct);
            if (current is { TransportProviderId: null, EscortEmployeeId: { } escortId })
            {
                var escortAccountId = await db.Employees.AsNoTracking().Active()
                    .Where(employee => employee.EmployeeId == escortId && employee.Role == EmployeeRole.ESCORT && employee.Account.AccountType == AccountType.EMPLOYEE)
                    .Select(employee => (Guid?)employee.AccountId).SingleOrDefaultAsync(ct);
                if (escortAccountId.HasValue)
                    QueueIncident(incident, escortAccountId.Value, NotificationPriority.URGENT, "Add horse incident details",
                        $"Incident {incident.IncidentCode} also affects horses. Add their health details.", $"/crew/incidents/{incident.IncidentReportId}", now);
            }
        }
        if (incident.Severity is IncidentSeverity.HIGH or IncidentSeverity.CRITICAL)
        {
            var customerAccountId = await db.Customers.AsNoTracking().Where(customer => customer.CustomerId == transport.Booking.CustomerId &&
                    !customer.IsDeleted && !customer.Account.IsDeleted && customer.Account.Status == AccountStatus.ACTIVE && customer.Account.AccountType == AccountType.CUSTOMER)
                .Select(customer => (Guid?)customer.AccountId).SingleOrDefaultAsync(ct);
            if (customerAccountId.HasValue)
                QueueIncident(incident, customerAccountId.Value, NotificationPriority.HIGH, "A serious transport incident was reported",
                    "Our team is handling the incident and will keep you updated.",
                    $"/bookings/{transport.BookingId}/transports/{transport.TransportId}/incidents/{incident.IncidentReportId}", now);
        }
        return new(priority.ToString(), emailQueued, VehicleStatusChanged: vehicleStatusChanged, CanAddHorseDetails: incident.Category == IncidentCategory.BOTH);
    }

    private async Task QueueUpdatedAsync(IncidentReport incident, DateTime now, CancellationToken ct)
    {
        foreach (var employee in await InternalRecipientsAsync(incident.TransportId, ct))
            QueueIncident(incident, employee.AccountId, NotificationPriority.NORMAL, $"Incident {incident.IncidentCode} updated",
                "Vehicle incident details were updated.", $"/admin/incidents/{incident.IncidentReportId}", now);
    }

    private void QueueIncident(IncidentReport incident, Guid accountId, NotificationPriority priority, string title, string message, string url, DateTime now) =>
        notifications.Queue(new()
        {
            AccountId = accountId, TransportId = incident.TransportId, Type = NotificationType.INCIDENT, Priority = priority,
            Title = title, Message = message, ReferenceType = nameof(IncidentReport), ReferenceId = incident.IncidentReportId, ActionUrl = url, CreatedAt = now
        });

    private async Task<bool> QueueHorseAlertAsync(IncidentReport incident, Transport transport, bool created, DateTime now, CancellationToken ct)
    {
        var emailQueued = false;
        foreach (var employee in await InternalRecipientsAsync(transport.TransportId, ct))
        {
            QueueHorseNotification(incident, employee.AccountId, NotificationPriority.URGENT,
                $"[CẢNH BÁO NGUY CẤP NGỰA ĐUA] Mã: {incident.IncidentCode}", incident.Title, now);
            if (employee.Role != EmployeeRole.LOGISTICS_MANAGER || string.IsNullOrWhiteSpace(employee.Account.Email)) continue;
            OutboxQueue.AddEmail(db, new MailContent
            {
                To = employee.Account.Email, Subject = $"[EMERGENCY ALERT - PEGAXUS] Horse health - Transport {transport.TransportCode}",
                Body = EmailBodies.IncidentReportedBody(employee.FullName, incident.IncidentCode, incident.Severity.ToString(),
                    incident.Title, incident.Description, $"/admin/incidents/{incident.IncidentReportId}")
            }, now);
            emailQueued = true;
        }
        if (created && incident.Severity is IncidentSeverity.HIGH or IncidentSeverity.CRITICAL)
        {
            var customerAccountId = await db.Customers.AsNoTracking().Where(customer => customer.CustomerId == transport.Booking.CustomerId &&
                    !customer.IsDeleted && !customer.Account.IsDeleted && customer.Account.Status == AccountStatus.ACTIVE && customer.Account.AccountType == AccountType.CUSTOMER)
                .Select(customer => (Guid?)customer.AccountId).SingleOrDefaultAsync(ct);
            if (customerAccountId.HasValue)
                notifications.Queue(new()
                {
                    AccountId = customerAccountId.Value, TransportId = transport.TransportId, Type = NotificationType.INCIDENT,
                    Priority = NotificationPriority.HIGH, Title = $"Chuyến {transport.TransportCode} gặp sự cố nghiêm trọng",
                    Message = "Đội ngũ đang xử lý, sẽ cập nhật.", ReferenceType = nameof(IncidentReport), ReferenceId = incident.IncidentReportId,
                    ActionUrl = $"/bookings/{transport.BookingId}/transports/{transport.TransportId}/incidents/{incident.IncidentReportId}", CreatedAt = now
                });
        }
        return emailQueued;
    }

    private async Task QueueHorseFollowUpAsync(IncidentReport incident, DateTime now, CancellationToken ct)
    {
        foreach (var employee in await InternalRecipientsAsync(incident.TransportId, ct))
            QueueHorseNotification(incident, employee.AccountId, NotificationPriority.HIGH, "Horse incident follow-up",
                $"Veterinary follow-up was added to incident {incident.IncidentCode}.", now);
    }

    private void QueueHorseNotification(IncidentReport incident, Guid accountId, NotificationPriority priority, string title, string message, DateTime now) =>
        notifications.Queue(new()
        {
            AccountId = accountId, TransportId = incident.TransportId, Type = NotificationType.HORSE_HEALTH, Priority = priority,
            Title = title, Message = message, ReferenceType = nameof(IncidentReport), ReferenceId = incident.IncidentReportId,
            ActionUrl = $"/admin/incidents/{incident.IncidentReportId}", CreatedAt = now
        });
}
