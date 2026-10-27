using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Service.Exceptions;

using BookingRequest = Pegaxus.Service.BookingService.Request;
using BookingServiceImpl = Pegaxus.Service.BookingService.Service;

namespace Pegaxus.Service.Test;

/// <summary>Domestic (United Kingdom to United Kingdom) trips need 3 weeks of notice, international ones 2 months; there is no latest date.</summary>
public class BookingDepartureRuleTests
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private static async Task<(Guid AccountId, Horse Horse)> SeedAsync(AppDbContext db)
    {
        var (account, customer) = await TestSupport.AddCustomerAsync(db);
        var horse = new Horse { HorseId = Guid.NewGuid(), CustomerId = customer.CustomerId, Customer = customer, HorseName = "Silver Arrow", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        db.Horses.Add(horse); await db.SaveChangesAsync();
        return (account.AccountId, horse);
    }

    private static BookingRequest.CreateBookingRequest Create(Horse horse, string origin, string destination, DateOnly departure) => new()
    {
        OriginAddress = "Origin", OriginCountry = origin, OriginLatitude = 48.8566m, OriginLongitude = 2.3522m,
        DestinationAddress = "Destination", DestinationCountry = destination, DestinationLatitude = 52.52m, DestinationLongitude = 13.405m,
        RequestedDepartureDate = departure, Horses = [new() { HorseId = horse.HorseId }]
    };

    private static BookingServiceImpl Service(AppDbContext db) => new(db, TestSupport.CreateMailService(), TestSupport.CreateNotifications(db));

    public static TheoryData<string, string, int, int, bool> Routes => new()
    {
        { "United Kingdom", "United Kingdom", 0, 21, true },
        { "United Kingdom", "United Kingdom", 0, 20, false },
        { "United Kingdom", "United Kingdom", 0, 400, true },
        { "France", "Germany", 2, 0, true },
        { "France", "Germany", 1, 28, false },
        { "France", "Germany", 5, 0, true },
        { "France", "United Kingdom", 2, 0, true },
        { "United Kingdom", "France", 1, 28, false },
        { "United Kingdom", "France", 12, 0, true },
    };

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Create_NeedsTheNoticeOfTheRoute_AndHasNoLatestDate(string origin, string destination, int months, int days, bool accepted)
    {
        await using var db = TestSupport.CreateDb(); var (account, horse) = await SeedAsync(db);
        var departure = Today.AddMonths(months).AddDays(days);

        var create = () => Service(db).CreateAsync(account, Create(horse, origin, destination, departure));

        if (accepted) Assert.NotEqual(Guid.Empty, (await create()).BookingId);
        else Assert.Equal("DEPARTURE_DATE_OUT_OF_RANGE", (await Assert.ThrowsAsync<BadRequestException>(create)).ErrorCode);
    }

    [Fact]
    public async Task Create_SaysWhichNoticeIsMissing()
    {
        await using var db = TestSupport.CreateDb(); var (account, horse) = await SeedAsync(db);

        var domestic = await Assert.ThrowsAsync<BadRequestException>(() => Service(db).CreateAsync(account, Create(horse, "United Kingdom", "United Kingdom", Today.AddDays(5))));
        var international = await Assert.ThrowsAsync<BadRequestException>(() => Service(db).CreateAsync(account, Create(horse, "France", "Germany", Today.AddDays(30))));

        Assert.Contains("3 weeks", domestic.Message); Assert.Contains("2 months", international.Message);
    }

    [Fact]
    public async Task Update_ChecksTheDateAgainstTheRouteAfterTheChange()
    {
        await using var db = TestSupport.CreateDb(); var (account, horse) = await SeedAsync(db);
        var created = await Service(db).CreateAsync(account, Create(horse, "United Kingdom", "United Kingdom", Today.AddDays(30)));
        await Service(db).ChangeStatusAsync(account, created.BookingId, new() { Status = "DRAFT" });
        var booking = db.Bookings.Single(item => item.BookingId == created.BookingId);

        var error = await Assert.ThrowsAsync<BadRequestException>(() => Service(db).UpdateAsync(account, booking.BookingId,
            new() { LastUpdatedAt = booking.UpdatedAt, DestinationCountry = "France" }));

        Assert.Equal("DEPARTURE_DATE_OUT_OF_RANGE", error.ErrorCode);
    }

    [Fact]
    public async Task Update_AcceptsAFarDateOnAnInternationalRoute()
    {
        await using var db = TestSupport.CreateDb(); var (account, horse) = await SeedAsync(db);
        var created = await Service(db).CreateAsync(account, Create(horse, "France", "Germany", Today.AddMonths(2)));
        await Service(db).ChangeStatusAsync(account, created.BookingId, new() { Status = "DRAFT" });
        var booking = db.Bookings.Single(item => item.BookingId == created.BookingId);

        await Service(db).UpdateAsync(account, booking.BookingId, new() { LastUpdatedAt = booking.UpdatedAt, RequestedDepartureDate = Today.AddMonths(8) });

        Assert.Equal(Today.AddMonths(8), booking.RequestedDepartureDate);
    }
}
