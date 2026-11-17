using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;

using Bookings = Pegaxus.Service.BookingService;

namespace Pegaxus.Service.Test;

public class HorseScheduleTests
{
    private static readonly DateOnly Departure = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(3);

    private static Bookings.Service Service(AppDbContext db) => new(db, TestSupport.CreateMailService(), TestSupport.CreateNotifications(db));

    /// <summary>An existing booking of <paramref name="horse"/> from Departure to Departure + 2 days.</summary>
    private static async Task<(Account Account, Customer Customer, Horse Horse, Booking Existing)> ExistingAsync(AppDbContext db)
    {
        var (account, customer) = await TestSupport.AddCustomerAsync(db);
        var (existing, horse) = await TestSupport.AddPendingBookingAsync(db, customer, withHorse: true);
        existing.RequestedDepartureDate = Departure;
        existing.RequestedArrivalDate = Departure.AddDays(2);
        await db.SaveChangesAsync();
        return (account, customer, horse!, existing);
    }

    private static Bookings.Request.CreateBookingRequest Create(Guid horseId, DateOnly departure, DateOnly? arrival = null) => new()
    {
        OriginAddress = "Paris stables", OriginCountry = "France", DestinationAddress = "Cologne track", DestinationCountry = "Germany",
        OriginLatitude = 48.85m, OriginLongitude = 2.35m, DestinationLatitude = 50.94m, DestinationLongitude = 6.96m,
        RequestedDepartureDate = departure, RequestedArrivalDate = arrival, Horses = [new() { HorseId = horseId }]
    };

    [Fact]
    public async Task Create_RefusesAHorseAlreadyBookedOnOverlappingDays_AndAllowsOtherDays()
    {
        await using var db = TestSupport.CreateDb();
        var (account, _, horse, existing) = await ExistingAsync(db);
        var bookings = Service(db);

        var overlap = await Assert.ThrowsAsync<ConflictException>(() => bookings.CreateAsync(account.AccountId, Create(horse.HorseId, Departure.AddDays(1))));
        Assert.Equal("HORSE_SCHEDULE_CONFLICT", overlap.ErrorCode);
        Assert.Contains(existing.BookingCode, overlap.Message);
        Assert.Contains("Silver Arrow", overlap.Message);
        // The last day of the existing trip still counts; the day after is free.
        await Assert.ThrowsAsync<ConflictException>(() => bookings.CreateAsync(account.AccountId, Create(horse.HorseId, Departure.AddDays(-5), Departure)));
        await bookings.CreateAsync(account.AccountId, Create(horse.HorseId, Departure.AddDays(3), Departure.AddDays(4)));
    }

    [Fact]
    public async Task Create_ACancelledOrCompletedBookingOrALeftBehindHorseDoesNotHoldTheHorse()
    {
        await using var db = TestSupport.CreateDb();
        var (account, _, horse, existing) = await ExistingAsync(db);
        var bookings = Service(db);

        existing.Status = BookingStatus.CANCELLED; await db.SaveChangesAsync();
        await bookings.CreateAsync(account.AccountId, Create(horse.HorseId, Departure));

        await using var second = TestSupport.CreateDb();
        var (account2, _, horse2, existing2) = await ExistingAsync(second);
        existing2.Status = BookingStatus.APPROVED;
        existing2.Transport = new Transport { TransportId = Guid.NewGuid(), TransportCode = "TR-DONE", Booking = existing2, Status = TransportStatus.COMPLETED };
        second.Add(existing2.Transport); await second.SaveChangesAsync();
        await Service(second).CreateAsync(account2.AccountId, Create(horse2.HorseId, Departure));

        await using var third = TestSupport.CreateDb();
        var (account3, _, horse3, _) = await ExistingAsync(third);
        (await third.BookingHorses.SingleAsync()).DetachedAt = DateTime.UtcNow; await third.SaveChangesAsync();
        await Service(third).CreateAsync(account3.AccountId, Create(horse3.HorseId, Departure));
    }

    [Fact]
    public async Task Create_ATransportOnTheRoadHoldsItsHorsesPastThePlannedArrival()
    {
        await using var db = TestSupport.CreateDb();
        var (account, _, horse, existing) = await ExistingAsync(db);
        existing.Status = BookingStatus.APPROVED;
        existing.RequestedDepartureDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-10);
        existing.RequestedArrivalDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-8);
        existing.Transport = new Transport { TransportId = Guid.NewGuid(), TransportCode = "TR-LATE", Booking = existing, Status = TransportStatus.IN_TRANSIT };
        db.Add(existing.Transport); await db.SaveChangesAsync();

        var onTheRoad = await Assert.ThrowsAsync<ConflictException>(() => Service(db).CreateAsync(account.AccountId, Create(horse.HorseId, Departure)));

        Assert.Equal("HORSE_SCHEDULE_CONFLICT", onTheRoad.ErrorCode);
        Assert.Contains("on the road", onTheRoad.Message);
    }

    [Fact]
    public async Task Approve_RefusesTheSecondOfTwoPendingBookingsWithTheSameHorseOnTheSameDays()
    {
        await using var db = TestSupport.CreateDb();
        var (_, customer, horse, existing) = await ExistingAsync(db);
        var (manager, _) = await TestSupport.AddLogisticsManagerAsync(db);
        // Seeded directly: two bookings reached review before the rule existed, or raced past it.
        var (twin, _) = await TestSupport.AddPendingBookingAsync(db, customer);
        twin.RequestedDepartureDate = Departure.AddDays(1);
        db.BookingHorses.Add(new BookingHorse { BookingId = twin.BookingId, HorseId = horse.HorseId, CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var bookings = Service(db);

        await bookings.ApproveAsync(manager.AccountId, existing.BookingId);
        var second = await Assert.ThrowsAsync<ConflictException>(() => bookings.ApproveAsync(manager.AccountId, twin.BookingId));

        Assert.Equal("HORSE_SCHEDULE_CONFLICT", second.ErrorCode);
        Assert.Equal(BookingStatus.PENDING, (await db.Bookings.SingleAsync(item => item.BookingId == twin.BookingId)).Status);
    }
}
