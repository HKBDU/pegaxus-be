using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Repository.Extensions;
using Pegaxus.Service.Exceptions;

using static Pegaxus.Service.Common.DbTime;
using static Pegaxus.Service.Common.Text;

using BookingEntity = Pegaxus.Repository.Entities.Booking;
using NotificationService = Pegaxus.Service.NotificationService;

namespace Pegaxus.Service.BookingService;

public partial class Service : IService
{
    private readonly AppDbContext _db;
    private readonly MailService.IService _mailService;
    private readonly NotificationService.IService _notificationService;

    public Service(
        AppDbContext db,
        MailService.IService mailService,
        NotificationService.IService notificationService)
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

    private void AddNotification(Guid accountId, Guid? transportId, BookingEntity booking, string title, DateTime now)
    {
        _notificationService.Queue(new NotificationService.NotificationCreateRequest
        {
            AccountId = accountId,
            TransportId = transportId,
            Type = NotificationType.BOOKING,
            Priority = NotificationPriority.NORMAL,
            Title = title,
            Message = $"Booking {booking.BookingCode}: {title.ToLowerInvariant()}.",
            ReferenceType = "Booking",
            ReferenceId = booking.BookingId,
            ActionUrl = $"/bookings/{booking.BookingId}",
            CreatedAt = now
        });
    }

    private Task<bool> HasHorseAsync(Guid bookingId, CancellationToken cancellationToken) =>
        _db.BookingHorses.AnyAsync(item => item.BookingId == bookingId && !item.Horse.IsDeleted, cancellationToken);
}
