using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Enum;
using Pegaxus.Repository.Extensions;

using static Pegaxus.Service.Common.DbTime;

namespace Pegaxus.Service.LocationTrackingService;

public sealed partial class Service
{
    public async Task<Response.PermissionDenied> PermissionDeniedAsync(Guid transportId, Guid accountId, CancellationToken cancellationToken = default)
    {
        var entry = state.For(transportId);
        await entry.Gate.WaitAsync(cancellationToken);
        try
        {
            await using var transaction = await db.Database.BeginSerializableAsync(cancellationToken);
            await db.LockRowsAsync(db.Transports.Where(transport => transport.TransportId == transportId), transport => transport.TransportId, cancellationToken);
            var (driver, transport) = await DriverTransportAsync(transportId, accountId, cancellationToken);
            var now = clock.GetUtcNow();
            if (entry.PermissionAlerts.TryGetValue(accountId, out var last) && now - last < TimeSpan.FromMinutes(10))
                return new(false);
            var recipients = await db.Employees.AsNoTracking().Active().Where(employee => employee.Role == EmployeeRole.FLEET_ROUTE_COORDINATOR &&
                    employee.Account.AccountType == AccountType.EMPLOYEE && employee.EmployeeTransportPlans.Any(assignment => assignment.TransportPlan.TransportId == transportId))
                .Select(employee => employee.AccountId).Distinct().ToArrayAsync(cancellationToken);
            if (recipients.Length == 0) return new(false);
            foreach (var recipient in recipients)
                notifications.Queue(new()
                {
                    AccountId = recipient, TransportId = transportId, Type = NotificationType.TRANSPORT, Priority = NotificationPriority.HIGH,
                    Title = "Driver GPS sharing is disabled", Message = $"Driver {driver.FullName} has not enabled GPS sharing for transport {transport.TransportCode}.",
                    ReferenceType = "Transport", ReferenceId = transportId, ActionUrl = $"/admin/ongoing-transports/{transportId}", CreatedAt = Truncate(now)
                });
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            entry.PermissionAlerts[accountId] = now;
            await notifications.FlushAsync(cancellationToken);
            return new(true);
        }
        finally { entry.Gate.Release(); }
    }
}
