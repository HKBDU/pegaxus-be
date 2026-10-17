using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Repository.Extensions;
using Pegaxus.Service.OutboxService;

namespace Pegaxus.Service.HandoverService;

public sealed partial class Service
{
    private async Task QueueCompletionAsync(HandoverRecord item, Transport transport, RoutePlanRoute[] segments, DateTime now, CancellationToken cancellationToken)
    {
        void Queue(Guid accountId, NotificationType type, NotificationPriority priority, string title, string actionUrl) => notifications.Queue(new()
        {
            AccountId = accountId, TransportId = transport.TransportId, Type = type, Priority = priority, Title = title,
            Message = $"{title}: {transport.TransportCode} ({item.HandoverCode})", ReferenceType = nameof(HandoverRecord),
            ReferenceId = item.HandoverRecordId, ActionUrl = actionUrl, CreatedAt = now
        });
        if (item.HandoverType is HandoverType.TRANSFER or HandoverType.BORDER_HANDOVER)
        {
            var to = segments.Single(segment => segment.RoutePlanRouteId == item.ToRoutePlanRouteId);
            var crew = await db.Employees.AsNoTracking().Active().Where(employee => employee.Account.AccountType == AccountType.EMPLOYEE &&
                    (employee.EmployeeId == to.DriverEmployeeId || employee.EmployeeId == to.EscortEmployeeId))
                .Select(employee => employee.AccountId).Distinct().ToArrayAsync(cancellationToken);
            foreach (var recipient in crew) Queue(recipient, NotificationType.ROUTE, NotificationPriority.HIGH, "You can start your segment", $"/crew/transports/{transport.TransportId}");
        }
        if (item.HandoverType is not (HandoverType.PICKUP or HandoverType.DELIVERY)) return;
        var delivery = item.HandoverType == HandoverType.DELIVERY;
        var type = delivery ? NotificationType.HANDOVER : NotificationType.TRANSPORT;
        var customer = await db.Customers.AsNoTracking().Where(customer => customer.Bookings.Any(booking => booking.BookingId == transport.BookingId) &&
                !customer.IsDeleted && !customer.Account.IsDeleted && customer.Account.Status == AccountStatus.ACTIVE && customer.Account.AccountType == AccountType.CUSTOMER)
            .Select(customer => new { customer.AccountId, customer.FullName, customer.Account.Email }).SingleOrDefaultAsync(cancellationToken);
        if (customer is not null)
        {
            Queue(customer.AccountId, type, NotificationPriority.HIGH, delivery ? "Your horses have been delivered" : "Your horses have been picked up", $"/bookings/{transport.BookingId}/transports/{transport.TransportId}");
            if (delivery)
                OutboxQueue.AddEmail(db, new()
                {
                    To = customer.Email, Subject = $"Transport completed — {transport.TransportCode}",
                    Body = MailService.EmailBodies.TransportDeliveredBody(customer.FullName, transport.TransportCode, now, null)
                }, now);
        }
        var managers = await db.Employees.AsNoTracking().Active().Where(employee => employee.Account.AccountType == AccountType.EMPLOYEE &&
                (employee.Role == EmployeeRole.LOGISTICS_MANAGER || employee.Role == EmployeeRole.FLEET_ROUTE_COORDINATOR))
            .Select(employee => employee.AccountId).Distinct().ToArrayAsync(cancellationToken);
        foreach (var recipient in managers) Queue(recipient, type, NotificationPriority.NORMAL, delivery ? "Transport completed" : "Transport started", $"/admin/ongoing-transports/{transport.TransportId}");
    }
}
