using System.Net;
using System.Text.Json;
using Pegaxus.Repository.Enum;

namespace Pegaxus.Service.Test;

public class LocationLookupHttpTests
{
    [Fact]
    public async Task Lookup_BindsRawGpsReturnsNearbyIdsAndLeavesCreationToFrontend()
    {
        await using var db = TestSupport.CreateDb();
        var close = LocationLookupTests.Point("Z close", 50.6293m, 3.0573m);
        var north = LocationLookupTests.Point("A north", 50.6328m, 3.0573m);
        db.Locations.AddRange(close, north, LocationLookupTests.Point("Far", 50.6340m, 3.0573m));
        await db.SaveChangesAsync();
        var coordinator = await HorseHealthLogTests.EmployeeAsync(db, EmployeeRole.FLEET_ROUTE_COORDINATOR);
        await using var host = await VehicleIncidentHttpTests.StartAsync(db, new HorseHealthLogTests.Storage());
        using var client = VehicleIncidentHttpTests.Client(host, coordinator.AccountId, EmployeeRole.FLEET_ROUTE_COORDINATOR);

        using var response = await client.GetAsync("/api/v1/locations?latitude=50.6292314&longitude=3.0573129");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var value = body.RootElement.GetProperty("value");
        Assert.Equal(new[] { close.LocationId, north.LocationId }, value.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("locationId").GetGuid()));
        Assert.Equal(2, value.GetProperty("totalItems").GetInt32());

        using var empty = await client.GetAsync("/api/v1/locations?latitude=10.1234567&longitude=20.1234567");
        Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
        using var emptyBody = JsonDocument.Parse(await empty.Content.ReadAsStringAsync());
        Assert.Equal(0, emptyBody.RootElement.GetProperty("value").GetProperty("items").GetArrayLength());
        Assert.Equal(3, db.Locations.Count());

        using var partial = await client.GetAsync("/api/v1/locations?latitude=50.6292314");
        Assert.Equal(HttpStatusCode.BadRequest, partial.StatusCode);
        Assert.Contains("INVALID_COORDINATES", await partial.Content.ReadAsStringAsync());
        using var invalid = await client.GetAsync("/api/v1/locations?latitude=91&longitude=3");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        client.DefaultRequestHeaders.Authorization = null;
        using var anonymous = await client.GetAsync("/api/v1/locations?latitude=50.6292314&longitude=3.0573129");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }
}
