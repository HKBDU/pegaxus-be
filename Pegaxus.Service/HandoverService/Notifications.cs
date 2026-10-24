using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Repository.Extensions;
using Pegaxus.Service.OutboxService;

namespace Pegaxus.Service.HandoverService;

public sealed partial class Service
{
    private async Task QueueCompletionAsync(HandoverRecord item, Transport transport, RoutePlanRoute[] segments, DateTime now,
        Response.CompletionEffects effects, CancellationToken cancellationToken)
    {
        void Send(Guid accountId, NotificationType type, NotificationPriority priority, string title, string message, string actionUrl) => notifications.Queue(new()
        {
            AccountId = accountId, TransportId = transport.TransportId, Type = type, Priority = priority, Title = title,
            Message = message, ReferenceType = nameof(HandoverRecord), ReferenceId = item.HandoverRecordId, ActionUrl = actionUrl, CreatedAt = now
        });
        void Queue(Guid accountId, NotificationType type, NotificationPriority priority, string title, string actionUrl) =>
            Send(accountId, type, priority, title, $"{title}: {transport.TransportCode} ({item.HandoverCode})", actionUrl);

        if (item.HandoverType is HandoverType.TRANSFER or HandoverType.BORDER_HANDOVER)
        {
            var to = segments.Single(segment => segment.RoutePlanRouteId == item.ToRoutePlanRouteId);
            var crew = await db.Employees.AsNoTracking().Active().Where(employee => employee.Account.AccountType == AccountType.EMPLOYEE &&
                    (employee.EmployeeId == to.DriverEmployeeId || employee.EmployeeId == to.EscortEmployeeId))
                .Select(employee => employee.AccountId).Distinct().ToArrayAsync(cancellationToken);
            foreach (var recipient in crew) Queue(recipient, NotificationType.ROUTE, NotificationPriority.HIGH,
                "Open the trip and turn on location sharing", $"/crew/transports/{transport.TransportId}");
        }
        if (item.HandoverType is not (HandoverType.PICKUP or HandoverType.DELIVERY)) return;
        var delivery = item.HandoverType == HandoverType.DELIVERY;
        var type = delivery ? NotificationType.HANDOVER : NotificationType.TRANSPORT;
        var customer = await db.Customers.AsNoTracking().Where(customer => customer.Bookings.Any(booking => booking.BookingId == transport.BookingId) &&
                !customer.IsDeleted && !customer.Account.IsDeleted && customer.Account.Status == AccountStatus.ACTIVE && customer.Account.AccountType == AccountType.CUSTOMER)
            .Select(customer => new { customer.AccountId, customer.FullName, customer.Account.Email }).SingleOrDefaultAsync(cancellationToken);
        if (customer is not null)
        {
            var transportPage = $"/bookings/{transport.BookingId}/transports/{transport.TransportId}";
            Queue(customer.AccountId, type, NotificationPriority.HIGH, delivery ? "Your horses have been delivered" : "Your horses have been picked up", transportPage);
            // The e-POD needs a signed-in customer, so the email leads to the transport page where it can be downloaded (EP1).
            if (delivery)
                OutboxQueue.AddEmail(db, new()
                {
                    To = customer.Email, Subject = $"Transport completed — {transport.TransportCode}",
                    Body = MailService.EmailBodies.TransportDeliveredBody(customer.FullName, transport.TransportCode, now, transportPage, effects.HasDiscrepancy == true)
                }, now);
        }

        if (!delivery)
        {
            var managers = await db.Employees.AsNoTracking().Active().Where(employee => employee.Account.AccountType == AccountType.EMPLOYEE &&
                    (employee.Role == EmployeeRole.LOGISTICS_MANAGER || employee.Role == EmployeeRole.FLEET_ROUTE_COORDINATOR))
                .Select(employee => employee.AccountId).Distinct().ToArrayAsync(cancellationToken);
            foreach (var recipient in managers) Queue(recipient, type, NotificationPriority.NORMAL, "Transport started", $"/admin/ongoing-transports/{transport.TransportId}");
            return;
        }

        // Flow 6 section 11: after delivery the managers review the money, the coordinators and specialists of this trip close their own side.
        var staff = await db.Employees.AsNoTracking().Active().Where(employee => employee.Account.AccountType == AccountType.EMPLOYEE &&
                (employee.Role == EmployeeRole.LOGISTICS_MANAGER ||
                 (employee.Role == EmployeeRole.FLEET_ROUTE_COORDINATOR || employee.Role == EmployeeRole.TRANSPORT_SPECIALIST) &&
                    employee.EmployeeTransportPlans.Any(link => link.TransportPlan.TransportId == transport.TransportId)))
            .Select(employee => new { employee.AccountId, employee.Role }).Distinct().ToArrayAsync(cancellationToken);
        var vehicles = effects.ReleasedVehicles is { Count: > 0 } list
            ? " Vehicles released: " + string.Join(", ", list.Select(vehicle => $"{vehicle.VehicleCode} ({vehicle.NewStatus})")) + "."
            : "";
        foreach (var recipient in staff)
            switch (recipient.Role)
            {
                case EmployeeRole.LOGISTICS_MANAGER:
                    var review = $"Transport {transport.TransportCode} completed: review the financial summary and create the FINAL invoice.";
                    Send(recipient.AccountId, NotificationType.HANDOVER, NotificationPriority.NORMAL, review,
                        effects.HasDiscrepancy == true ? review + " The handover record has on-site notes, so check them before creating the FINAL invoice." : review,
                        $"/admin/transports/{transport.TransportId}/financial-summary");
                    break;
                case EmployeeRole.FLEET_ROUTE_COORDINATOR:
                    Send(recipient.AccountId, NotificationType.HANDOVER, NotificationPriority.NORMAL, $"Transport {transport.TransportCode} completed",
                        $"Transport {transport.TransportCode} completed ({item.HandoverCode}).{vehicles}", $"/admin/ongoing-transports/{transport.TransportId}");
                    break;
                default:
                    Send(recipient.AccountId, NotificationType.TRANSPORT, NotificationPriority.NORMAL, $"Transport {transport.TransportCode} completed: review the legal dossier",
                        $"Transport {transport.TransportCode} completed ({item.HandoverCode}). Review the legal dossier of this transport.", $"/admin/transports/{transport.TransportId}");
                    break;
            }
    }
}
