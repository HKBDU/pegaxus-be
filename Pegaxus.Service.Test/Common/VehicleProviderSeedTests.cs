using Microsoft.EntityFrameworkCore;
using Pegaxus.API;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;

namespace Pegaxus.Service.Test;

public class VehicleProviderSeedTests
{
    [Fact]
    public async Task Seed_CreatesTenUsableVehiclesAndProviders_WithStableIds_WithoutDuplicates()
    {
        await using var db = TestSupport.CreateDb();
        await AppDbContextSeedData.SeedAsync(db);
        var vehicles = await db.Vehicles.OrderBy(item => item.VehicleCode).ToArrayAsync();
        var providers = await db.TransportProviders.OrderBy(item => item.ProviderCode).ToArrayAsync();
        Assert.Equal(10, vehicles.Length);
        Assert.Equal(10, providers.Length);
        Assert.Equal(6, vehicles.Count(item => item.TransportMode == TransportMode.ROAD));
        Assert.Equal(2, vehicles.Count(item => item.TransportMode == TransportMode.AIR));
        Assert.Equal(2, vehicles.Count(item => item.TransportMode == TransportMode.SEA));
        Assert.All(vehicles, item =>
        {
            Assert.Matches(@"^VEH-\d{8}-\d{4}$", item.VehicleCode);
            Assert.Equal(VehicleStatus.AVAILABLE, item.Status);
            Assert.False(item.IsDeleted);
            Assert.Equal(DateTimeKind.Unspecified, item.CreatedAt.Kind);
            Assert.Equal(item.CreatedAt, item.UpdatedAt);
            var result = new Pegaxus.Service.VehicleService.Validator.CreateRequestValidator(TimeProvider.System)
                .Validate(new Pegaxus.Service.VehicleService.Request.CreateRequest
                {
                    VehicleName = item.VehicleName, TransportMode = item.TransportMode.ToString(),
                    LicensePlate = item.LicensePlate, VehicleType = item.VehicleType, CapacityHorses = item.CapacityHorses,
                    RegistrationNumber = item.RegistrationNumber, RegistrationExpiryDate = item.RegistrationExpiryDate,
                    InsuranceNumber = item.InsuranceNumber, InsuranceExpiryDate = item.InsuranceExpiryDate,
                    SpecialEquipment = item.SpecialEquipment, Country = item.Country
                });
            Assert.True(result.IsValid, result.ToString());
        });
        Assert.All(providers, item =>
        {
            Assert.Matches(@"^PRV-\d{8}-\d{4}$", item.ProviderCode);
            Assert.Equal(ProviderStatus.ACTIVE, item.Status);
            Assert.False(item.IsDeleted);
            Assert.Equal(DateTimeKind.Unspecified, item.CreatedAt.Kind);
            Assert.Equal(item.CreatedAt, item.UpdatedAt);
            var result = new Pegaxus.Service.TransportProviderService.Validator.CreateRequestValidator()
                .Validate(new Pegaxus.Service.TransportProviderService.Request.CreateRequest
                {
                    ProviderName = item.ProviderName, ContactName = item.ContactName, Email = item.Email,
                    Country = item.Country, TaxCode = item.TaxCode!, SupportedModes = item.SupportedModes!.Split(',').ToList(),
                    Note = item.Note
                });
            Assert.True(result.IsValid, result.ToString());
        });
        db.ChangeTracker.Clear();
        Assert.Equal((0, 0), await AppDbContextSeedData.SeedVehiclesAndProvidersAsync(db));
        await using var fresh = TestSupport.CreateDb();
        Assert.Equal((10, 10), await AppDbContextSeedData.SeedVehiclesAndProvidersAsync(fresh));
        Assert.Equal(vehicles.Select(item => item.VehicleId), await fresh.Vehicles.OrderBy(item => item.VehicleCode).Select(item => item.VehicleId).ToArrayAsync());
        Assert.Equal(providers.Select(item => item.TransportProviderId), await fresh.TransportProviders.OrderBy(item => item.ProviderCode).Select(item => item.TransportProviderId).ToArrayAsync());
    }

    [Fact]
    public async Task Seed_PreservesEditedAndDeletedRows_AndSkipsConflictingBusinessKeys()
    {
        await using var db = TestSupport.CreateDb();
        await AppDbContextSeedData.SeedVehiclesAndProvidersAsync(db);
        var vehicle = await db.Vehicles.SingleAsync(item => item.VehicleCode == "VEH-20261010-0001");
        vehicle.VehicleCode = "EDITED-VEHICLE";
        vehicle.VehicleName = "Keep edited vehicle";
        vehicle.LicensePlate = "EDITED-PLATE";
        vehicle.IsDeleted = true;
        var provider = await db.TransportProviders.SingleAsync(item => item.ProviderCode == "PRV-20261010-0001");
        provider.ProviderCode = "EDITED-PROVIDER";
        provider.ProviderName = "Keep edited provider";
        provider.TaxCode = "EDITED-TAX";
        provider.IsDeleted = true;

        db.Vehicles.RemoveRange(await db.Vehicles.Where(item => item.VehicleCode == "VEH-20261010-0002" || item.VehicleCode == "VEH-20261010-0003").ToArrayAsync());
        db.TransportProviders.RemoveRange(await db.TransportProviders.Where(item => item.ProviderCode == "PRV-20261010-0002" || item.ProviderCode == "PRV-20261010-0003").ToArrayAsync());
        db.Vehicles.AddRange(
            new Vehicle { VehicleId = Guid.NewGuid(), VehicleCode = "VEH-20261010-0002", IsDeleted = true },
            new Vehicle { VehicleId = Guid.NewGuid(), VehicleCode = "OTHER-VEHICLE", LicensePlate = "EJ21 FXT" });
        db.TransportProviders.AddRange(
            new TransportProvider { TransportProviderId = Guid.NewGuid(), ProviderCode = "PRV-20261010-0002", ProviderName = "Existing provider", IsDeleted = true },
            new TransportProvider { TransportProviderId = Guid.NewGuid(), ProviderCode = "OTHER-PROVIDER", ProviderName = "Tax conflict", TaxCode = "IE-VAT-100003" });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        Assert.Equal((0, 0), await AppDbContextSeedData.SeedVehiclesAndProvidersAsync(db));
        Assert.Equal(10, await db.Vehicles.IgnoreQueryFilters().CountAsync());
        Assert.Equal(10, await db.TransportProviders.IgnoreQueryFilters().CountAsync());
        var preservedVehicle = await db.Vehicles.IgnoreQueryFilters().SingleAsync(item => item.VehicleId == vehicle.VehicleId);
        Assert.Equal("EDITED-VEHICLE", preservedVehicle.VehicleCode);
        Assert.Equal("Keep edited vehicle", preservedVehicle.VehicleName);
        Assert.Equal("EDITED-PLATE", preservedVehicle.LicensePlate);
        Assert.True(preservedVehicle.IsDeleted);
        var preservedProvider = await db.TransportProviders.IgnoreQueryFilters().SingleAsync(item => item.TransportProviderId == provider.TransportProviderId);
        Assert.Equal("EDITED-PROVIDER", preservedProvider.ProviderCode);
        Assert.Equal("Keep edited provider", preservedProvider.ProviderName);
        Assert.Equal("EDITED-TAX", preservedProvider.TaxCode);
        Assert.True(preservedProvider.IsDeleted);
    }
}
