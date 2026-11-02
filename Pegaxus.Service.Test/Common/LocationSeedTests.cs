using Microsoft.EntityFrameworkCore;
using Pegaxus.API;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;

namespace Pegaxus.Service.Test;

public class LocationSeedTests
{
    [Fact]
    public async Task CatalogSeed_ExtendsExistingCatalog_WithoutReusingLocationIds()
    {
        await using var db = TestSupport.CreateDb();
        var existingId = Guid.Parse("70eec21b-1122-5500-a69b-bd8529e20ad4");
        db.Locations.Add(new Location
        {
            LocationId = existingId,
            LocationName = "Dublin Port Border Control Post (DAFM Portal Inspection)",
            Country = "IE",
            Address = "Keep edited address",
            IsDeleted = true
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        Assert.Equal(299, await AppDbContextSeedData.SeedLocationsAsync(db));
        Assert.Equal(300, await db.Locations.IgnoreQueryFilters().CountAsync());
        var preserved = await db.Locations.IgnoreQueryFilters().SingleAsync(item => item.LocationId == existingId);
        Assert.Equal("Dublin Port Border Control Post (DAFM Portal Inspection)", preserved.LocationName);
        Assert.Equal("IE", preserved.Country);
        Assert.Equal("Keep edited address", preserved.Address);
        Assert.True(preserved.IsDeleted);
        var added = await db.Locations.SingleAsync(item => item.LocationName == "Ayr Racecourse Stables");
        Assert.NotEqual(existingId, added.LocationId);
        Assert.Equal(0, await AppDbContextSeedData.SeedLocationsAsync(db));
    }

    [Fact]
    public async Task CatalogSeed_PreservesMetadata_UsesStableIds_SkipsExistingAndDeletedLocations()
    {
        await using var db = TestSupport.CreateDb();
        Assert.Equal(300, await AppDbContextSeedData.SeedLocationsAsync(db));
        var seeded = await db.Locations.ToArrayAsync();
        var ids = seeded.ToDictionary(item => (item.LocationName, item.Country), item => item.LocationId);
        Assert.Equal(300, ids.Values.Distinct().Count());
        Assert.DoesNotContain(Guid.Empty, ids.Values);
        var first = seeded.Single(item => item.LocationName == "Eurotunnel Folkestone Terminal");
        Assert.Equal(Guid.Parse("4a8b7ccc-bf50-5945-8cf6-af06f7b1b405"), first.LocationId);
        Assert.Equal("GB", first.Country);
        Assert.Equal(LocationType.BORDER_GATE, first.LocationType);
        Assert.Equal("Ashford Road, Cheriton, Folkestone, Kent CT18 8XX", first.Address);
        Assert.Equal("Folkestone", first.City);
        Assert.Equal("Kent", first.StateProvince);
        Assert.Equal(51.096412m, first.Latitude);
        Assert.Equal(1.134523m, first.Longitude);
        Assert.Contains("Equine import BCP designation is unverified", first.LocationNote);
        Assert.All(seeded, item => {
            Assert.Equal(new DateTime(2026, 3, 1, 8, 0, 0), item.CreatedAt);
            Assert.True(item.UpdatedAt >= item.CreatedAt);
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
        Assert.Equal(300, await db.Locations.CountAsync());
        Assert.False(await db.Locations.AnyAsync(item => item.LocationId == first.LocationId));
        var existing = await db.Locations.SingleAsync(item => item.LocationId == replacementId);
        Assert.True(existing.IsDeleted);
        Assert.Equal("Keep existing location", existing.LocationNote);
        var preserved = await db.Locations.SingleAsync(item => item.LocationId == edited.LocationId);
        Assert.Equal("Edited location", preserved.LocationName);
        Assert.Equal("Keep edited address", preserved.Address);
        Assert.True(preserved.IsDeleted);

        await using var fresh = TestSupport.CreateDb();
        Assert.Equal(300, await AppDbContextSeedData.SeedLocationsAsync(fresh));
        Assert.All(await fresh.Locations.ToArrayAsync(), item =>
            Assert.Equal(ids[(item.LocationName, item.Country)], item.LocationId));
    }

    [Fact]
    public async Task CatalogSeed_UsesPublishedCorrections_WithoutInventingReceivingPointsOrEquineApproval()
    {
        await using var db = TestSupport.CreateDb();
        Assert.Equal(300, await AppDbContextSeedData.SeedLocationsAsync(db));
        var seeded = await db.Locations.ToDictionaryAsync(item => item.LocationName);

        var cairnryan = seeded["Loch Ryan Port, Cairnryan"];
        Assert.Equal(LocationType.SEAPORT, cairnryan.LocationType);
        Assert.Equal("Loch Ryan Port, Belfast Way, Cairnryan, DG9 8RG", cairnryan.Address);
        Assert.Equal(54.985550m, cairnryan.Latitude);
        Assert.Equal(-5.035139m, cairnryan.Longitude);
        Assert.Contains("designation was not found", cairnryan.LocationNote);

        var dover = seeded["Dover Western Docks Border Control Post"];
        Assert.Contains("Union Street", dover.Address);
        Assert.Contains("GBDOV2P", dover.LocationNote);
        Assert.Contains("without live-equine import designation", dover.LocationNote);
        Assert.Null(dover.Latitude);
        Assert.Null(dover.Longitude);
        Assert.Equal(new DateTime(2026, 11, 2), dover.UpdatedAt);

        var wittekindshof = seeded["Gestüt Wittekindshof"];
        Assert.Equal("Gestüt Wittekindshof, Am Römerlager 3, 59602 Rüthen-Kneblinghausen", wittekindshof.Address);
        Assert.Equal("Rüthen", wittekindshof.City);
        Assert.Null(wittekindshof.Latitude);
        Assert.Null(wittekindshof.Longitude);
        Assert.Equal("Højgård Hestehospital, Rugårdsvej 696, 5462 Morud", seeded["Højgård Hestehospital Equine Referral Clinic"].Address);
        Assert.Contains("R51 HK85", seeded["Anglesey Lodge Equine Hospital"].Address);
        Assert.Contains("LA-O(1)", seeded["Stockholm Arlanda Airport Border Control Post"].LocationNote);
        Assert.Equal(LocationType.SEAPORT, seeded["Port of Rotterdam Europoort"].LocationType);
        Assert.Contains("Stationsweg 10", seeded["Animal Centre Hoek van Holland Border Control Post"].Address);
        Assert.Contains("Via Scuderie 37", seeded["Ippodromo Maia di Merano"].Address);
        Assert.DoesNotContain(seeded.Keys, name => name.Contains("Peter O'Sullevan House", StringComparison.Ordinal));
        Assert.Equal(LocationType.OTHER, seeded["National Horseracing Museum"].LocationType);
        Assert.All(seeded.Values, item => Assert.Equal(item.Latitude.HasValue, item.Longitude.HasValue));
        Assert.Equal(35, seeded.Values.Count(item => item.Latitude is null));
        Assert.Equal(0, await AppDbContextSeedData.SeedLocationsAsync(db));
    }
}
