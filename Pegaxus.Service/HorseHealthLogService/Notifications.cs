using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Repository.Extensions;

namespace Pegaxus.Service.HorseHealthLogService;

public sealed partial class Service
{
    private async Task<bool> QueueEmergencyAsync(HorseHealthLog log, DateTime now, CancellationToken cancellationToken)
    {
        var recipients = await db.Employees.AsNoTracking().Active().Where(employee => employee.Account.AccountType == AccountType.EMPLOYEE &&
                (employee.Role == EmployeeRole.LOGISTICS_MANAGER || employee.Role == EmployeeRole.FLEET_ROUTE_COORDINATOR &&
                    employee.EmployeeTransportPlans.Any(assignment => assignment.TransportPlan.TransportId == log.TransportId)))
            .Select(employee => employee.AccountId).Distinct().ToArrayAsync(cancellationToken);
        foreach (var accountId in recipients)
            notifications.Queue(new()
            {
                AccountId = accountId, TransportId = log.TransportId, Type = NotificationType.HORSE_HEALTH, Priority = NotificationPriority.URGENT,
                Title = "Critical horse health alert", Message = "A horse health check requires urgent attention.",
                ReferenceType = nameof(HorseHealthLog), ReferenceId = log.HorseHealthLogId,
                ActionUrl = $"/admin/ongoing-transports/{log.TransportId}/health", CreatedAt = now
            });
        return recipients.Length > 0;
    }
}
