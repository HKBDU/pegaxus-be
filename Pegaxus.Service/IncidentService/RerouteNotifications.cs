using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.MailService;
using Pegaxus.Service.OutboxService;

namespace Pegaxus.Service.IncidentService;

public sealed partial class Service
{
    private void QueueReroute(IncidentReport incident, Employee employee, NotificationPriority priority,
        string title, string message, bool hold, DateTime now)
    {
        var url = Crew(employee) ? $"/crew/incidents/{incident.IncidentReportId}" : $"/admin/incidents/{incident.IncidentReportId}/reroute";
        notifications.Queue(new()
        {
            AccountId = employee.AccountId, TransportId = incident.TransportId, Type = NotificationType.ROUTE, Priority = priority,
            Title = title, Message = message, ReferenceType = hold ? HoldReference : nameof(IncidentReport),
            ReferenceId = incident.IncidentReportId, ActionUrl = url, CreatedAt = now
        });
        if (priority != NotificationPriority.URGENT || employee.Role != EmployeeRole.LOGISTICS_MANAGER || string.IsNullOrWhiteSpace(employee.Account.Email)) return;
        OutboxQueue.AddEmail(db, new MailContent
        {
            To = employee.Account.Email, Subject = $"Urgent route decision for incident {incident.IncidentCode}",
            Body = EmailBodies.IncidentReportedBody(employee.FullName, incident.IncidentCode, incident.Severity.ToString(), title, message, url)
        }, now);
    }
}
