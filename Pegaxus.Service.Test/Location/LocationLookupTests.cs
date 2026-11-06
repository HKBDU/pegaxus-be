using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;

using Locations = Pegaxus.Service.LocationService;

namespace Pegaxus.Service.Test;

public class LocationLookupTests
{
    [Fact]
    public async Task Lookup_FiltersWithin500MetersBeforePagingAndOrdersNearestFirst()
    {
        await using var db = TestSupport.CreateDb();
        var close = Point("Z close", 50.6293m, 3.0573m);
        var tie = Point("Y tie", 50.6293m, 3.0573m);
        close.LocationId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        tie.LocationId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var north = Point("B north", 50.6328m, 3.0573m);
        var east = Point("A east", 50.6292m, 3.0642m);
        db.Locations.AddRange(close, tie, north, east, Point("Outside circle", 50.6332m, 3.0613m),
            Point("Outside radius", 50.6340m, 3.0573m));
        await db.SaveChangesAsync();
        var service = new Locations.Service(db);
        var request = new Locations.Request.ListRequest { Latitude = 50.6292314m, Longitude = 3.0573129m, PageSize = 2 };

        var first = await service.GetListAsync(request);
        Assert.Equal(new[] { close.LocationId, tie.LocationId }, first.Items.Select(item => item.LocationId));
        Assert.Equal(4, first.TotalItems); Assert.Equal(2, first.TotalPages);
        request.PageIndex = 2;
        var second = await service.GetListAsync(request);
        Assert.Equal(new[] { north.LocationId, east.LocationId }, second.Items.Select(item => item.LocationId));
        Assert.Equal(4, second.TotalItems); Assert.Equal(2, second.PageIndex);
    }

    [Fact]
    public async Task Lookup_PreservesSearchCountryAndTypeFilters()
    {
        await using var db = TestSupport.CreateDb();
        var expected = Point("Rest area", 50.6328m, 3.0573m); expected.City = "Lille";
        var otherCountry = Point("Rest area", 50.6293m, 3.0573m); otherCountry.City = "Lille"; otherCountry.Country = "BE";
        var otherType = Point("Rest area", 50.6293m, 3.0573m); otherType.City = "Lille"; otherType.LocationType = LocationType.CITY;
        db.Locations.AddRange(expected, otherCountry, otherType, Point("Unrelated", 50.6293m, 3.0573m));
        await db.SaveChangesAsync();

        var result = await new Locations.Service(db).GetListAsync(new() { Latitude = 50.6292314m, Longitude = 3.0573129m,
            Search = " LILLE ", Country = " fr ", LocationType = "OTHER" });
        Assert.Equal(expected.LocationId, Assert.Single(result.Items).LocationId);
        Assert.Equal(1, result.TotalItems);
    }

    [Fact]
    public async Task Lookup_ReturnsEmptyForFarDeletedOrMissingCoordinatesWithoutCreatingLocation()
    {
        await using var db = TestSupport.CreateDb();
        var deleted = Point("Deleted", 50.6292m, 3.0573m); deleted.IsDeleted = true;
        db.Locations.AddRange(deleted, Point("Far", 50.6340m, 3.0573m), Point("No latitude", null, 3.0573m),
            Point("No longitude", 50.6292m, null));
        await db.SaveChangesAsync();

        var result = await new Locations.Service(db).GetListAsync(new() { Latitude = 50.6292314m, Longitude = 3.0573129m });
        Assert.Empty(result.Items); Assert.Equal(0, result.TotalItems); Assert.Equal(0, result.TotalPages);
        Assert.Equal(4, await db.Locations.CountAsync());
    }

    [Fact]
    public async Task List_WithoutGpsKeepsNameOrderingPaginationAndLocationsWithoutCoordinates()
    {
        await using var db = TestSupport.CreateDb();
        var alpha = Point("Alpha", 10m, 20m); var beta = Point("Beta", null, null); var zulu = Point("Zulu", 50.6292m, 3.0573m);
        var deleted = Point("A deleted", 50.6292m, 3.0573m); deleted.IsDeleted = true;
        db.Locations.AddRange(zulu, beta, alpha, deleted); await db.SaveChangesAsync();

        var result = await new Locations.Service(db).GetListAsync(new() { PageSize = 2 });
        Assert.Equal(new[] { alpha.LocationId, beta.LocationId }, result.Items.Select(item => item.LocationId));
        Assert.Equal(3, result.TotalItems); Assert.Equal(2, result.TotalPages);
    }

    [Theory]
    [InlineData("missingLatitude")][InlineData("missingLongitude")][InlineData("north")][InlineData("south")]
    [InlineData("east")][InlineData("west")][InlineData("zero")]
    public async Task Lookup_RejectsIncompleteOrInvalidGps(string field)
    {
        await using var db = TestSupport.CreateDb();
        var request = new Locations.Request.ListRequest { Latitude = 50.6292314m, Longitude = 3.0573129m };
        switch (field)
        {
            case "missingLatitude": request.Latitude = null; break;
            case "missingLongitude": request.Longitude = null; break;
            case "north": request.Latitude = 90.0000001m; break;
            case "south": request.Latitude = -90.0000001m; break;
            case "east": request.Longitude = 180.0000001m; break;
            case "west": request.Longitude = -180.0000001m; break;
            case "zero": request.Latitude = 0; request.Longitude = 0; break;
        }
        var error = await Assert.ThrowsAsync<BadRequestException>(() => new Locations.Service(db).GetListAsync(request));
        Assert.Equal("INVALID_COORDINATES", error.ErrorCode);
    }

    [Theory]
    [InlineData("0", "179.9999", "0", "-179.9999")]
    [InlineData("90", "150", "89.9999", "-80")]
    [InlineData("-90", "-150", "-89.9999", "80")]
    public async Task Lookup_HandlesAntimeridianAndPoles(string latitude, string longitude, string storedLatitude, string storedLongitude)
    {
        await using var db = TestSupport.CreateDb();
        var expected = Point("Nearby", decimal.Parse(storedLatitude, CultureInfo.InvariantCulture), decimal.Parse(storedLongitude, CultureInfo.InvariantCulture));
        db.Locations.Add(expected); await db.SaveChangesAsync();
        var result = await new Locations.Service(db).GetListAsync(new() { Latitude = decimal.Parse(latitude, CultureInfo.InvariantCulture),
            Longitude = decimal.Parse(longitude, CultureInfo.InvariantCulture) });
        Assert.Equal(expected.LocationId, Assert.Single(result.Items).LocationId);
    }

    internal static Location Point(string name, decimal? latitude, decimal? longitude) => new()
    {
        LocationId = Guid.NewGuid(), LocationName = name, Country = "FR", LocationType = LocationType.OTHER,
        Latitude = latitude, Longitude = longitude, CreatedAt = DbTime.UtcNow(), UpdatedAt = DbTime.UtcNow()
    };
}
