using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;
using BookingRequest = Pegaxus.Service.BookingService.Request;
using BookingServiceImpl = Pegaxus.Service.BookingService.Service;

namespace Pegaxus.Service.Test;

public class BookingServiceTests
{
    [Fact]
    public async Task Create_PersistsBookingHorseAndHistory()
    {
        await using var db = TestSupport.CreateDb();
        var (account, customer) = await TestSupport.AddCustomerAsync(db);
        var horse = new Horse
        {
            HorseId = Guid.NewGuid(),
            CustomerId = customer.CustomerId,
            Customer = customer,
            HorseName = "Silver Arrow",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Horses.Add(horse);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var result = await service.CreateAsync(account.AccountId, new BookingRequest.CreateBookingRequest
        {
            OriginAddress = "Ho Chi Minh City",
            OriginCountry = "Vietnam",
            DestinationAddress = "Melbourne",
            DestinationCountry = "Australia",
            RequestedDepartureDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(21),
            Horses = [new BookingRequest.BookingHorseRequest { HorseId = horse.HorseId }]
        });

        Assert.Equal(result.BookingId, (await db.Bookings.SingleAsync()).BookingId);
        Assert.Equal(horse.HorseId, (await db.BookingHorses.SingleAsync()).HorseId);
        Assert.Equal(BookingStatus.PENDING, (await db.BookingStatusHistories.SingleAsync()).NewStatus);
    }

    [Fact]
    public async Task Update_ChangesOnlyProvidedFields()
    {
        await using var db = TestSupport.CreateDb();
        var (account, customer) = await TestSupport.AddCustomerAsync(db);
        var (booking, _) = await TestSupport.AddPendingBookingAsync(db, customer);
        var service = CreateService(db);

        var result = await service.UpdateAsync(account.AccountId, booking.BookingId, new BookingRequest.UpdateBookingRequest
        {
            LastUpdatedAt = booking.UpdatedAt,
            OriginAddress = "New origin"
        });

        Assert.Equal("New origin", booking.OriginAddress);
        Assert.Equal("Old destination", booking.DestinationAddress);
        Assert.Equal(result.UpdatedAt.UtcDateTime, DateTime.SpecifyKind(booking.UpdatedAt, DateTimeKind.Utc));
        Assert.Contains("originAddress", (await db.BookingStatusHistories.SingleAsync()).Reason);
    }

    [Fact]
    public async Task Update_RejectsStaleVersion()
    {
        await using var db = TestSupport.CreateDb();
        var (account, customer) = await TestSupport.AddCustomerAsync(db);
        var (booking, _) = await TestSupport.AddPendingBookingAsync(db, customer);
        var service = CreateService(db);

        var exception = await Assert.ThrowsAsync<ConflictException>(() => service.UpdateAsync(
            account.AccountId,
            booking.BookingId,
            new BookingRequest.UpdateBookingRequest
            {
                LastUpdatedAt = booking.UpdatedAt.AddSeconds(-1),
                OriginAddress = "New origin"
            }));

        Assert.Equal("BOOKING_MODIFIED", exception.ErrorCode);
    }

    [Fact]
    public async Task Cancel_SetsStatusReasonAndHistory()
    {
        await using var db = TestSupport.CreateDb();
        var (account, customer) = await TestSupport.AddCustomerAsync(db);
        var (booking, _) = await TestSupport.AddPendingBookingAsync(db, customer);
        var service = CreateService(db);

        await service.CancelAsync(
            account.AccountId,
            booking.BookingId,
            new BookingRequest.CancelBookingRequest { Reason = "Event cancelled" });

        Assert.Equal(BookingStatus.CANCELLED, booking.Status);
        Assert.Equal("Event cancelled", booking.CancellationReason);
        Assert.Equal(BookingStatus.CANCELLED, (await db.BookingStatusHistories.SingleAsync()).NewStatus);
    }

    [Fact]
    public async Task Approve_CreatesTransportNotificationAndEmail()
    {
        await using var db = TestSupport.CreateDb();
        var (_, customer) = await TestSupport.AddCustomerAsync(db);
        var (managerAccount, _) = await TestSupport.AddLogisticsManagerAsync(db);
        var (booking, _) = await TestSupport.AddPendingBookingAsync(db, customer, withHorse: true);
        var service = CreateService(db, out var mailService);

        var result = await service.ApproveAsync(managerAccount.AccountId, booking.BookingId);

        Assert.Equal(BookingStatus.APPROVED, booking.Status);
        Assert.Equal(TransportStatus.CREATED, result.Transport.Status);
        Assert.Equal(result.Transport.TransportId, (await db.Transports.SingleAsync()).TransportId);
        Assert.Equal(result.Transport.TransportId, (await db.Notifications.SingleAsync()).TransportId);
        Assert.Equal(1, mailService.Calls);
    }

    [Fact]
    public async Task Approve_RejectsBookingWithoutHorse()
    {
        await using var db = TestSupport.CreateDb();
        var (_, customer) = await TestSupport.AddCustomerAsync(db);
        var (managerAccount, _) = await TestSupport.AddLogisticsManagerAsync(db);
        var (booking, _) = await TestSupport.AddPendingBookingAsync(db, customer);
        var service = CreateService(db);

        var exception = await Assert.ThrowsAsync<UnprocessableEntityException>(() =>
            service.ApproveAsync(managerAccount.AccountId, booking.BookingId));

        Assert.Equal("BOOKING_HAS_NO_HORSE", exception.ErrorCode);
    }

    [Fact]
    public async Task Reject_SetsReasonNotificationAndEmail()
    {
        await using var db = TestSupport.CreateDb();
        var (_, customer) = await TestSupport.AddCustomerAsync(db);
        var (managerAccount, _) = await TestSupport.AddLogisticsManagerAsync(db);
        var (booking, _) = await TestSupport.AddPendingBookingAsync(db, customer);
        var service = CreateService(db, out var mailService);

        await service.RejectAsync(
            managerAccount.AccountId,
            booking.BookingId,
            new BookingRequest.RejectBookingRequest { Reason = "No suitable transport" });

        Assert.Equal(BookingStatus.REJECTED, booking.Status);
        Assert.Equal("No suitable transport", booking.RejectionReason);
        Assert.Null((await db.Notifications.SingleAsync()).TransportId);
        Assert.Equal(1, mailService.Calls);
    }

    private static BookingServiceImpl CreateService(AppDbContext db) =>
        new(db, TestSupport.CreateMailService());

    private static BookingServiceImpl CreateService(AppDbContext db, out TestSupport.RecordingMailService mailService)
    {
        mailService = TestSupport.CreateMailService();
        return new BookingServiceImpl(db, mailService);
    }
}
