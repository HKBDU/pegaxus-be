using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Repository.Extensions;
using Pegaxus.Service.MailService;
using Pegaxus.Service.OutboxService;

namespace Pegaxus.Service.IncidentService;

public sealed partial class Service
{
    private async Task QueueLifecycleAsync(IncidentReport incident, Transport transport, Employee actor, bool selfResolved,
        Guid[] cancelledRequesters, DateTime now, CancellationToken ct)
    {
        var recipients = await InternalRecipientsAsync(transport.TransportId, ct);
        if (!selfResolved)
        {
            var current = await CurrentSegmentAsync(transport, ct);
            var employeeIds = cancelledRequesters.Concat(new Guid?[]
                { incident.EmployeeId, current?.DriverEmployeeId, current?.EscortEmployeeId }
                .Where(id => id.HasValue).Select(id => id!.Value)).Distinct().ToArray();
            var crewAndReporters = await db.Employees.AsNoTracking().Active().Include(employee => employee.Account)
                .Where(employee => employeeIds.Contains(employee.EmployeeId) && employee.Account.AccountType == AccountType.EMPLOYEE).ToArrayAsync(ct);
            recipients = recipients.Where(employee => employee.Role != EmployeeRole.LOGISTICS_MANAGER).Concat(crewAndReporters)
                .Where(employee => employee.AccountId != actor.AccountId).DistinctBy(employee => employee.AccountId).ToArray();
        }
        var cancelled = incident.Status == IncidentStatus.CANCELLED;
        var verb = cancelled ? "cancelled" : "resolved";
        var priority = selfResolved && incident.Severity is IncidentSeverity.HIGH or IncidentSeverity.CRITICAL
            ? NotificationPriority.HIGH : NotificationPriority.NORMAL;
        foreach (var employee in recipients)
            QueueIncident(incident, employee.AccountId, priority, $"Incident {incident.IncidentCode} {verb}",
                selfResolved ? "The assigned crew resolved the incident." : $"The logistics manager {verb} the incident.",
                $"/{(Crew(employee) ? "crew" : "admin")}/incidents/{incident.IncidentReportId}", now);
        await QueueCustomerIncidentAsync(incident, transport, NotificationPriority.NORMAL, $"Transport incident {verb}",
            cancelled ? "The incident report has been cancelled. Our team will keep you updated."
                : "The transport incident has been resolved. Our team will keep you updated.", now, ct);
    }

    private async Task<Response.Effects> QueueSeverityRaisedAsync(IncidentReport incident, Transport transport,
        IncidentSeverity previousSeverity, DateTime now, CancellationToken ct)
    {
        var priority = incident.Severity is IncidentSeverity.HIGH or IncidentSeverity.CRITICAL
            ? NotificationPriority.URGENT : NotificationPriority.NORMAL;
        var emailQueued = false;
        foreach (var employee in await InternalRecipientsAsync(transport.TransportId, ct))
        {
            QueueIncident(incident, employee.AccountId, priority, $"Incident {incident.IncidentCode} severity raised",
                $"Severity raised from {previousSeverity} to {incident.Severity}.", $"/admin/incidents/{incident.IncidentReportId}", now);
            if (priority != NotificationPriority.URGENT || employee.Role != EmployeeRole.LOGISTICS_MANAGER || string.IsNullOrWhiteSpace(employee.Account.Email)) continue;
            OutboxQueue.AddEmail(db, new MailContent
            {
                To = employee.Account.Email, Subject = $"Urgent incident {incident.IncidentCode}",
                Body = EmailBodies.IncidentReportedBody(employee.FullName, incident.IncidentCode, incident.Severity.ToString(),
                    incident.Title, incident.Description, $"/admin/incidents/{incident.IncidentReportId}")
            }, now);
            emailQueued = true;
        }
        var customerNotified = await QueueCustomerIncidentAsync(incident, transport, NotificationPriority.HIGH,
            "Transport incident update", "Our team is handling a serious transport incident and will keep you updated.", now, ct);
        return new(priority.ToString(), emailQueued, CustomerNotified: customerNotified);
    }

    private async Task<bool> QueueCustomerIncidentAsync(IncidentReport incident, Transport transport, NotificationPriority priority,
        string title, string message, DateTime now, CancellationToken ct)
    {
        if (incident.Severity is not (IncidentSeverity.HIGH or IncidentSeverity.CRITICAL)) return false;
        var accountId = await db.Customers.AsNoTracking().Where(customer => customer.CustomerId == transport.Booking.CustomerId &&
                !customer.IsDeleted && !customer.Account.IsDeleted && customer.Account.Status == AccountStatus.ACTIVE && customer.Account.AccountType == AccountType.CUSTOMER)
            .Select(customer => (Guid?)customer.AccountId).SingleOrDefaultAsync(ct);
        if (accountId is null) return false;
        QueueIncident(incident, accountId.Value, priority, title, message,
            $"/bookings/{transport.BookingId}/transports/{transport.TransportId}/incidents/{incident.IncidentReportId}", now);
        return true;
    }
}
