using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.LocationService;
using Xunit;

namespace Pegaxus.Service.Test;

public class LocationServiceTests
{
    [Fact]
    public async Task CreateAsync_AddsLocationSuccessfully()
    {
        await using var db = TestSupport.CreateDb();
        var (_, coordinator) = await AddCoordinatorAsync(db);
        var service = new Pegaxus.Service.LocationService.Service(db);

        var request = new Request.CreateRequest
        {
            LocationName = "Test Stable Paris",
            LocationType = "STABLE",
            Country = "FR",
            City = "Paris",
            Address = "123 Rue de Paris",
            Latitude = 48.8566m,
            Longitude = 2.3522m
        };

        var created = await service.CreateAsync(coordinator.AccountId, request);

        Assert.NotEqual(Guid.Empty, created.LocationId);
        Assert.Equal("Test Stable Paris", created.LocationName);
        Assert.Equal("STABLE", created.LocationType);
        Assert.Equal("FR", created.Country);
        Assert.Equal("Paris", created.City);
        Assert.Equal(48.8566m, created.Latitude);
        Assert.Equal(2.3522m, created.Longitude);
    }

    [Fact]
    public async Task UpdateAsync_SucceedsWithExactOrMillisecondPrecision()
    {
        await using var db = TestSupport.CreateDb();
        var (_, coordinator) = await AddCoordinatorAsync(db);
        var service = new Pegaxus.Service.LocationService.Service(db);

        var created = await service.CreateAsync(coordinator.AccountId, new Request.CreateRequest
        {
            LocationName = "Initial Location",
            LocationType = "CITY",
            Country = "VN",
            Latitude = 10.7769m,
            Longitude = 106.7009m
        });

        // 1. Exact updatedAt
        var update1 = new Request.UpdateRequest
        {
            LocationName = "Updated Location 1",
            LocationType = "CITY",
            Country = "VN",
            Latitude = 10.7769m,
            Longitude = 106.7009m,
            LastUpdatedAt = created.UpdatedAt
        };
        var res1 = await service.UpdateAsync(coordinator.AccountId, created.LocationId, update1);
        Assert.Equal(created.LocationId, res1.LocationId);

        // 2. Millisecond-precision (e.g., from browser JavaScript Date.toISOString())
        var jsDate = new DateTimeOffset(
            res1.UpdatedAt.Year, res1.UpdatedAt.Month, res1.UpdatedAt.Day,
            res1.UpdatedAt.Hour, res1.UpdatedAt.Minute, res1.UpdatedAt.Second,
            res1.UpdatedAt.Millisecond, TimeSpan.Zero);

        var update2 = new Request.UpdateRequest
        {
            LocationName = "Updated Location 2",
            LocationType = "CITY",
            Country = "VN",
            Latitude = 10.7769m,
            Longitude = 106.7009m,
            LastUpdatedAt = jsDate
        };
        var res2 = await service.UpdateAsync(coordinator.AccountId, created.LocationId, update2);
        Assert.Equal(created.LocationId, res2.LocationId);
    }

    [Fact]
    public async Task UpdateAsync_Throws409_WhenConcurrencyConflictOccurs()
    {
        await using var db = TestSupport.CreateDb();
        var (_, coordinator) = await AddCoordinatorAsync(db);
        var service = new Pegaxus.Service.LocationService.Service(db);

        var created = await service.CreateAsync(coordinator.AccountId, new Request.CreateRequest
        {
            LocationName = "Conflict Location",
            LocationType = "CITY",
            Country = "VN",
            Latitude = 10.7769m,
            Longitude = 106.7009m
        });

        var staleTimestamp = created.UpdatedAt.AddMinutes(-5);

        var update = new Request.UpdateRequest
        {
            LocationName = "Stale Update",
            LocationType = "CITY",
            Country = "VN",
            Latitude = 10.7769m,
            Longitude = 106.7009m,
            LastUpdatedAt = staleTimestamp
        };

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            service.UpdateAsync(coordinator.AccountId, created.LocationId, update));
        Assert.Equal("LOCATION_MODIFIED", ex.ErrorCode);
    }

    [Fact]
    public async Task DeleteAsync_SoftDeletesLocation()
    {
        await using var db = TestSupport.CreateDb();
        var (_, coordinator) = await AddCoordinatorAsync(db);
        var service = new Pegaxus.Service.LocationService.Service(db);

        var created = await service.CreateAsync(coordinator.AccountId, new Request.CreateRequest
        {
            LocationName = "To Delete",
            LocationType = "CITY",
            Country = "VN",
            Latitude = 10.7769m,
            Longitude = 106.7009m
        });

        var deleted = await service.DeleteAsync(coordinator.AccountId, created.LocationId);
        Assert.Equal(created.LocationId, deleted.LocationId);

        var inDb = await db.Locations.FindAsync(created.LocationId);
        Assert.NotNull(inDb);
        Assert.True(inDb.IsDeleted);
        Assert.NotNull(inDb.DeletedAt);
        Assert.Equal(coordinator.AccountId, inDb.DeletedByAccountId);
    }

    private static async Task<(Account Account, Employee Employee)> AddCoordinatorAsync(AppDbContext db)
    {
        var (account, employee) = await TestSupport.AddLogisticsManagerAsync(db);
        employee.Role = EmployeeRole.FLEET_ROUTE_COORDINATOR;
        await db.SaveChangesAsync();
        return (account, employee);
    }
}
