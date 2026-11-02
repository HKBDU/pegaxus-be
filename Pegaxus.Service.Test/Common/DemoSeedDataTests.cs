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

        var bookings = await db.Bookings.Where(item => item.BookingCode.StartsWith("REQ-20261010-")).ToArrayAsync();

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

    [Theory]
    [InlineData("PEGAXUS-DEMO-C1-HORSE-001", "Seeded demo booking for Swagger testing.", "Pegaxus Demo ROAD 001", "Pegaxus Demo Provider 001")]
    [InlineData("PEGAXUS-HRS-20261010-0001", "Please confirm transport arrangements before departure.", "Pegaxus ROAD 001", "Pegaxus Transport 001")]
    public async Task Seed_UpgradesUntouchedLegacyRowsOnce_AndLeavesEditedRowsAlone(
        string passport, string customerNote, string vehicleName, string providerName)
    {
        await using var db = TestSupport.CreateDb();
        await AppDbContextSeedData.SeedAsync(db);
        var legacy = await db.Horses.SingleAsync(item => item.MicrochipNumber == "985112001000001");
        var edited = await db.Horses.SingleAsync(item => item.MicrochipNumber == "985112001000002");
        legacy.HorseName = "Pegaxus Demo Horse 1-1"; legacy.MicrochipNumber = "9851120001000001"; legacy.PassportNumber = passport;
        edited.HorseName = "Renamed by the owner"; edited.PassportNumber = "ABC123456789012";
        var deleted = await db.Horses.SingleAsync(item => item.MicrochipNumber == "985112001000004");
        deleted.PassportNumber = "PEGAXUS-HRS-20261010-0004";
        deleted.IsDeleted = true;
        var vehicle = await db.Vehicles.SingleAsync(item => item.VehicleCode == "VEH-20261010-0001");
        vehicle.VehicleName = vehicleName;
        var provider = await db.TransportProviders.SingleAsync(item => item.ProviderCode == "PRV-20261010-0001");
        provider.ProviderName = providerName;
        var booking = await db.Bookings.SingleAsync(item => item.BookingCode == "REQ-20261010-0004");
        booking.CustomerNote = customerNote; booking.OriginCountry = "DE"; booking.RequestedDepartureDate = new DateOnly(2026, 11, 5);
        booking.UpdatedAt = new DateTime(2026, 3, 1);
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
        Assert.True(fixedBooking.UpdatedAt > new DateTime(2026, 3, 1));
        Assert.Equal("Mercedes-Benz Actros Horse Lorry", (await db.Vehicles.SingleAsync(item => item.VehicleId == vehicle.VehicleId)).VehicleName);
        Assert.Equal("Equitrans Europeenne SAS", (await db.TransportProviders.SingleAsync(item => item.TransportProviderId == provider.TransportProviderId)).ProviderName);
        var preservedDeleted = await db.Horses.IgnoreQueryFilters().SingleAsync(item => item.HorseId == deleted.HorseId);
        Assert.True(preservedDeleted.IsDeleted);
        Assert.Equal("PEGAXUS-HRS-20261010-0004", preservedDeleted.PassportNumber);
        Assert.Equal(16, await db.Horses.IgnoreQueryFilters().CountAsync());

        var upgradedAt = upgraded.UpdatedAt;
        var bookingUpgradedAt = fixedBooking.UpdatedAt;
        db.ChangeTracker.Clear();
        await AppDbContextSeedData.SeedAsync(db);
        Assert.Equal(upgradedAt, (await db.Horses.SingleAsync(item => item.HorseId == legacy.HorseId)).UpdatedAt);
        Assert.Equal(bookingUpgradedAt, (await db.Bookings.SingleAsync(item => item.BookingId == booking.BookingId)).UpdatedAt);
    }
}
