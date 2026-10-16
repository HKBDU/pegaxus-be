using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Repository.Extensions;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;

using static Pegaxus.Service.Common.Text;

using BookingEntity = Pegaxus.Repository.Entities.Booking;

namespace Pegaxus.Service.BookingService;

public partial class Service : IBookingService
{
    private readonly AppDbContext _db;
    private readonly MailService.IMailService _mailService;
    private readonly NotificationService.INotificationService _notificationService;

    public Service(
        AppDbContext db,
        MailService.IMailService mailService,
        NotificationService.INotificationService notificationService)
    {
        _db = db;
        _mailService = mailService;
        _notificationService = notificationService;
    }

    private async Task<Customer> GetCustomerAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var customer = await _db.Customers.SingleOrDefaultAsync(item =>
            item.AccountId == accountId &&
            !item.IsDeleted &&
            !item.Account.IsDeleted &&
            item.Account.Status == AccountStatus.ACTIVE,
            cancellationToken);
        return customer ?? throw new ForbiddenException("You do not have permission to perform this action");
    }

    private async Task<Employee> GetLogisticsManagerAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var employee = await _db.Employees.Active().SingleOrDefaultAsync(item => item.AccountId == accountId && item.Role == EmployeeRole.LOGISTICS_MANAGER,
            cancellationToken);
        return employee ?? throw new ForbiddenException("You do not have permission to perform this action");
    }

    private void AddHistory(
        Guid bookingId,
        Guid accountId,
        BookingHistoryType type,
        BookingStatus? previousStatus,
        BookingStatus newStatus,
        string? reason,
        DateTime changedAt) => _db.BookingStatusHistories.Add(new BookingStatusHistory
    {
        BookingStatusHistoryId = Guid.NewGuid(),
        BookingId = bookingId,
        Type = type,
        PreviousStatus = previousStatus,
        NewStatus = newStatus,
        ChangedByAccountId = accountId,
        Reason = reason,
        ChangedAt = changedAt
    });

    /// <param name="role">The recipient employee role, or <c>null</c> for the booking customer.</param>
    private void AddNotification(Guid accountId, EmployeeRole? role, Guid? transportId, BookingEntity booking, string title, string message,
        DateTime now, NotificationPriority priority = NotificationPriority.NORMAL)
    {
        _notificationService.Queue(new NotificationService.NotificationCreateRequest
        {
            AccountId = accountId,
            TransportId = transportId,
            Type = NotificationType.BOOKING,
            Priority = priority,
            Title = title,
            Message = message,
            ReferenceType = "Booking",
            ReferenceId = booking.BookingId,
            ActionUrl = NotificationLinks.Booking(role, booking.BookingId),
            CreatedAt = now
        });
    }

    private static string RouteSummary(BookingEntity booking) =>
        $"{booking.OriginCountry} to {booking.DestinationCountry}, departing {booking.RequestedDepartureDate:yyyy-MM-dd}";

    private async Task NotifyManagersAsync(BookingEntity booking, string title, string verb, string followUp, DateTime now, CancellationToken cancellationToken)
    {
        var customerName = await _db.Customers.Where(customer => customer.CustomerId == booking.CustomerId)
            .Select(customer => customer.FullName).SingleAsync(cancellationToken);
        var message = $"{customerName} {verb} booking {booking.BookingCode} ({RouteSummary(booking)}).{followUp}";
        var recipients = await _db.Employees.Active()
            .Where(employee => employee.Role == EmployeeRole.LOGISTICS_MANAGER ||
                employee.EmployeeTransportPlans.Any(assignment => assignment.TransportPlan.Transport.BookingId == booking.BookingId))
            .Select(employee => new { employee.AccountId, employee.Role }).Distinct().ToListAsync(cancellationToken);
        foreach (var recipient in recipients)
            AddNotification(recipient.AccountId, recipient.Role, booking.Transport?.TransportId, booking, title, message, now);
    }

    private Task<bool> HasHorseAsync(Guid bookingId, CancellationToken cancellationToken) =>
        _db.BookingHorses.AnyAsync(item => item.BookingId == bookingId && !item.Horse.IsDeleted, cancellationToken);
}
