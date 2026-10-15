using Microsoft.EntityFrameworkCore;
using Pegaxus.API;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;

namespace Pegaxus.Service.Test;

public class LocationSeedTests
{
    [Fact]
    public async Task CatalogSeed_PreservesMetadata_UsesStableIds_SkipsExistingAndDeletedLocations()
    {
        await using var db = TestSupport.CreateDb();
        Assert.Equal(200, await AppDbContextSeedData.SeedLocationsAsync(db));
        var seeded = await db.Locations.ToArrayAsync();
        var ids = seeded.ToDictionary(item => (item.LocationName, item.Country), item => item.LocationId);
        Assert.Equal(200, ids.Values.Distinct().Count());
        Assert.DoesNotContain(Guid.Empty, ids.Values);
        var first = seeded.Single(item => item.LocationName == "Eurotunnel Folkestone Border Control Post");
        Assert.Equal("GB", first.Country);
        Assert.Equal(LocationType.BORDER_GATE, first.LocationType);
        Assert.Equal("Ashford Road, Cheriton, Folkestone, Kent CT18 8XX", first.Address);
        Assert.Equal("Folkestone", first.City);
        Assert.Equal("Kent", first.StateProvince);
        Assert.Equal(51.096412m, first.Latitude);
        Assert.Equal(1.134523m, first.Longitude);
        Assert.Contains("DEFRA/APHA", first.LocationNote);
        Assert.All(seeded, item => {
            Assert.Equal(new DateTime(2026, 3, 1, 8, 0, 0), item.CreatedAt);
            Assert.Equal(item.CreatedAt, item.UpdatedAt);
            Assert.Equal(DateTimeKind.Unspecified, item.CreatedAt.Kind);
            Assert.Equal(DateTimeKind.Unspecified, item.UpdatedAt.Kind);
            Assert.False(item.IsDeleted);
            Assert.Null(item.DeletedAt);
            Assert.Null(item.DeletedByAccountId);
        });

        var replacementId = Guid.NewGuid();
        db.Locations.Remove(first);
        db.Locations.Add(new Location { LocationId = replacementId,
            LocationName = " " + first.LocationName.ToUpperInvariant() + " ", Country = "gb",
            IsDeleted = true, LocationNote = "Keep existing location" });
        var edited = seeded.Single(item => item.LocationName == "Dover Western Docks Border Control Post");
        edited.LocationName = "Edited location";
        edited.Address = "Keep edited address";
        edited.IsDeleted = true;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        Assert.Equal(0, await AppDbContextSeedData.SeedLocationsAsync(db));
        Assert.Equal(0, await AppDbContextSeedData.SeedLocationsAsync(db));
        Assert.Equal(200, await db.Locations.CountAsync());
        Assert.False(await db.Locations.AnyAsync(item => item.LocationId == first.LocationId));
        var existing = await db.Locations.SingleAsync(item => item.LocationId == replacementId);
        Assert.True(existing.IsDeleted);
        Assert.Equal("Keep existing location", existing.LocationNote);
        var preserved = await db.Locations.SingleAsync(item => item.LocationId == edited.LocationId);
        Assert.Equal("Edited location", preserved.LocationName);
        Assert.Equal("Keep edited address", preserved.Address);
        Assert.True(preserved.IsDeleted);

        await using var fresh = TestSupport.CreateDb();
        Assert.Equal(200, await AppDbContextSeedData.SeedLocationsAsync(fresh));
        Assert.All(await fresh.Locations.ToArrayAsync(), item =>
            Assert.Equal(ids[(item.LocationName, item.Country)], item.LocationId));
    }
}
