using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Pegaxus.API;
using Pegaxus.Service.Common;

namespace Pegaxus.Service.Test;

public class DemoSeedDataTests
{
    [Fact]
    public async Task SeededHorses_FollowTheRulesTheHorseFormEnforces_AndLookDifferentFromEachOther()
    {
        await using var db = TestSupport.CreateDb();
        await AppDbContextSeedData.SeedAsync(db);
        var horses = await db.Horses.ToArrayAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        Assert.Equal(16, horses.Length);
        Assert.All(horses, horse =>
        {
            Assert.Matches(@"^\d{15}$", horse.MicrochipNumber!);
            Assert.Matches(@"^[A-Za-z0-9]{15}$", horse.PassportNumber!);
            Assert.DoesNotContain("Demo", horse.HorseName);
            Assert.InRange(horse.WeightKg!.Value, Limits.HorseWeightKg.Min, Limits.HorseWeightKg.Max);
            Assert.InRange(horse.HeightCm!.Value, Limits.HorseHeightCm.Min, Limits.HorseHeightCm.Max);
            Assert.InRange(horse.DateOfBirth!.Value, today.AddYears(-Limits.HorseMaxAgeYears), today);
        });
        Assert.Equal(16, horses.Select(item => item.HorseName).Distinct().Count());
        Assert.Equal(16, horses.Select(item => item.MicrochipNumber).Distinct().Count());
        Assert.True(horses.Select(item => item.WeightKg).Distinct().Count() > 8);
        Assert.True(horses.Select(item => item.Breed).Distinct().Count() > 6);
    }

    [Fact]
    public async Task SeededBookings_KeepTheDepartureRule_HaveCoordinates_AndRouteBetweenTwoDifferentPlaces()
    {
        await using var db = TestSupport.CreateDb();
        await AppDbContextSeedData.SeedAsync(db);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var bookings = await db.Bookings.Where(item => item.BookingCode.StartsWith("REQ-20261010-DEMO-")).ToArrayAsync();

        Assert.Equal(6, bookings.Length);
        Assert.All(bookings, booking =>
        {
            var domestic = booking.OriginCountry == booking.DestinationCountry;
            Assert.True(booking.RequestedDepartureDate >= (domestic ? today.AddDays(21) : today.AddMonths(2)), booking.BookingCode);
            Assert.True(booking.RequestedArrivalDate > booking.RequestedDepartureDate, booking.BookingCode);
            Assert.NotEqual(booking.OriginAddress, booking.DestinationAddress);
            Assert.NotNull(booking.OriginLatitude); Assert.NotNull(booking.OriginLongitude);
            Assert.NotNull(booking.DestinationLatitude); Assert.NotNull(booking.DestinationLongitude);
        });
    }

    [Fact]
    public async Task Seed_UpgradesUntouchedLegacyRowsOnce_AndLeavesEditedRowsAlone()
    {
        await using var db = TestSupport.CreateDb();
        await AppDbContextSeedData.SeedAsync(db);
        var legacy = await db.Horses.SingleAsync(item => item.MicrochipNumber == "985112001000001");
        var edited = await db.Horses.SingleAsync(item => item.MicrochipNumber == "985112001000002");
        legacy.HorseName = "Pegaxus Demo Horse 1-1"; legacy.MicrochipNumber = "9851120001000001"; legacy.PassportNumber = "PEGAXUS-DEMO-C1-HORSE-001";
        edited.HorseName = "Renamed by the owner"; edited.PassportNumber = "ABC123456789012";
        var booking = await db.Bookings.SingleAsync(item => item.BookingCode == "REQ-20261010-DEMO-0004");
        booking.CustomerNote = "Seeded demo booking for Swagger testing."; booking.OriginCountry = "DE"; booking.RequestedDepartureDate = new DateOnly(2026, 11, 5);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await AppDbContextSeedData.SeedAsync(db);

        var upgraded = await db.Horses.SingleAsync(item => item.HorseId == legacy.HorseId);
        Assert.Equal("Eclipse Valor", upgraded.HorseName); Assert.Equal("985112001000001", upgraded.MicrochipNumber);
        Assert.Matches(@"^[A-Za-z0-9]{15}$", upgraded.PassportNumber!);
        var kept = await db.Horses.SingleAsync(item => item.HorseId == edited.HorseId);
        Assert.Equal("Renamed by the owner", kept.HorseName); Assert.Equal("ABC123456789012", kept.PassportNumber);
        var fixedBooking = await db.Bookings.SingleAsync(item => item.BookingId == booking.BookingId);
        Assert.Equal("IE", fixedBooking.OriginCountry); Assert.True(fixedBooking.RequestedDepartureDate > DateOnly.FromDateTime(DateTime.UtcNow));
        Assert.Equal(16, await db.Horses.CountAsync());
    }
}
