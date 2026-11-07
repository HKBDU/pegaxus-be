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
        await QueueCustomerAlertAsync(log, now, cancellationToken);
        return recipients.Length > 0;
    }

    /// <summary>
    /// Tells the booking's customer when a horse is critical or needs a veterinarian. The message carries the horse name and its
    /// condition only: the veterinarian note and the escort's details stay internal.
    /// </summary>
    private async Task QueueCustomerAlertAsync(HorseHealthLog log, DateTime now, CancellationToken cancellationToken)
    {
        var trip = await db.Transports.AsNoTracking().Where(transport => transport.TransportId == log.TransportId)
            .Select(transport => new
            {
                transport.TransportCode, transport.BookingId, transport.Booking.BookingCode,
                CustomerAccountId = transport.Booking.Customer.AccountId,
                ContactName = transport.Booking.AssignedEmployee == null ? null : transport.Booking.AssignedEmployee.FullName,
                HorseName = db.Horses.Where(horse => horse.HorseId == log.HorseId).Select(horse => horse.HorseName).FirstOrDefault()
            }).SingleOrDefaultAsync(cancellationToken);
        if (trip is null || trip.HorseName is null) return;
        var critical = log.HealthStatus == HorseHealthStatus.CRITICAL;
        var transportPage = $"/bookings/{trip.BookingId}/transports/{log.TransportId}";
        var condition = (critical, log.VeterinarianRequired) switch
        {
            (true, true) => "is in a critical condition and needs a veterinarian",
            (true, false) => "is in a critical condition",
            _ => "needs a veterinarian"
        };
        notifications.Queue(new()
        {
            AccountId = trip.CustomerAccountId, TransportId = log.TransportId, Type = NotificationType.HORSE_HEALTH, Priority = NotificationPriority.URGENT,
            Title = "Your horse needs urgent attention", Message = $"{trip.HorseName} {condition}. Our team is responding.",
            ReferenceType = nameof(HorseHealthLog), ReferenceId = log.HorseHealthLogId, ActionUrl = transportPage, CreatedAt = now
        });
        await MailService.MailRecipients.QueueAsync(db, [trip.CustomerAccountId], now, recipient => MailService.MailRecipients.Mail(recipient,
            $"Your horse needs urgent attention — {trip.TransportCode}",
            MailService.EmailBodies.HorseHealthCustomerBody(recipient.FullName, trip.BookingCode, trip.TransportCode, trip.HorseName, critical,
                log.VeterinarianRequired, transportPage, trip.ContactName)), cancellationToken);
    }
}
