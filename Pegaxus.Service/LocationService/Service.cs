using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;

namespace Pegaxus.Service.LocationService;

public sealed class Service(AppDbContext db) : IService
{
    public async Task<Response.ListResponse> GetListAsync(Request.ListRequest request, CancellationToken cancellationToken = default)
    {
        ValidatePaging(request.PageIndex, request.PageSize);
        var query = db.Locations.AsNoTracking().Where(item => !item.IsDeleted);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToLower();
            query = query.Where(item => item.LocationName.ToLower().Contains(search) ||
                (item.City != null && item.City.ToLower().Contains(search)));
        }
        if (!string.IsNullOrWhiteSpace(request.Country))
            query = query.Where(item => item.Country == request.Country.Trim().ToUpperInvariant());
        if (!string.IsNullOrWhiteSpace(request.LocationType))
        {
            if (!Enum.TryParse<LocationType>(request.LocationType.Trim(), true, out var type))
                throw new BadRequestException("locationType is invalid", "VALIDATION_ERROR");
            query = query.Where(item => item.LocationType == type);
        }

        var totalItems = await query.CountAsync(cancellationToken);
        var rows = await query.OrderBy(item => item.LocationName).ThenBy(item => item.LocationId)
            .Skip((request.PageIndex - 1) * request.PageSize).Take(request.PageSize)
            .ToListAsync(cancellationToken);
        var items = rows.Select(Map).ToList();
        return new Response.ListResponse
        {
            Items = items,
            PageIndex = request.PageIndex,
            PageSize = request.PageSize,
            TotalItems = totalItems,
            TotalPages = (int)Math.Ceiling(totalItems / (double)request.PageSize)
        };
    }

    public async Task<Response.DetailResponse> GetAsync(Guid locationId, CancellationToken cancellationToken = default)
    {
        var item = await db.Locations.AsNoTracking()
            .SingleOrDefaultAsync(item => item.LocationId == locationId && !item.IsDeleted, cancellationToken)
            ?? throw new NotFoundException("Location not found", "LOCATION_NOT_FOUND");
        return Map(item);
    }

    public async Task<Response.DetailResponse> CreateAsync(Guid accountId, Request.CreateRequest request, CancellationToken cancellationToken = default)
    {
        await RequireCoordinatorAsync(accountId, cancellationToken);
        ValidateFields(request.LocationName, request.Country, request.LocationType, request.Latitude, request.Longitude);
        var country = request.Country!.Trim().ToUpperInvariant();
        if (await IsDuplicateAsync(request.LocationName!, request.City, country, null, cancellationToken))
            throw new ConflictException("A Location with the same name already exists", "LOCATION_NAME_DUPLICATED");

        var now = UtcNow();
        var item = new Location
        {
            LocationId = Guid.NewGuid(), LocationName = request.LocationName!.Trim(),
            LocationType = ParseType(request.LocationType), Address = EmptyToNull(request.Address),
            City = EmptyToNull(request.City), StateProvince = EmptyToNull(request.StateProvince), Country = country,
            Latitude = request.Latitude, Longitude = request.Longitude, LocationNote = EmptyToNull(request.LocationNote),
            CreatedAt = now, UpdatedAt = now
        };
        db.Locations.Add(item);
        await db.SaveChangesAsync(cancellationToken);
        return Map(item);
    }

    public async Task<Response.UpdateResponse> UpdateAsync(Guid accountId, Guid locationId, Request.UpdateRequest request, CancellationToken cancellationToken = default)
    {
        await RequireCoordinatorAsync(accountId, cancellationToken);
        if (request.LastUpdatedAt == default)
            throw new BadRequestException("lastUpdatedAt is required", "VALIDATION_ERROR");
        ValidateFields(request.LocationName, request.Country, request.LocationType, request.Latitude, request.Longitude);
        var item = await db.Locations.SingleOrDefaultAsync(item => item.LocationId == locationId && !item.IsDeleted, cancellationToken)
            ?? throw new NotFoundException("Location not found", "LOCATION_NOT_FOUND");
        EnsureConcurrency(item.UpdatedAt, request.LastUpdatedAt);
        var country = request.Country!.Trim().ToUpperInvariant();
        if (await IsDuplicateAsync(request.LocationName!, request.City, country, locationId, cancellationToken))
            throw new ConflictException("A Location with the same name already exists", "LOCATION_NAME_DUPLICATED");

        item.LocationName = request.LocationName!.Trim(); item.LocationType = ParseType(request.LocationType);
        item.Address = EmptyToNull(request.Address); item.City = EmptyToNull(request.City);
        item.StateProvince = EmptyToNull(request.StateProvince); item.Country = country;
        item.Latitude = request.Latitude; item.Longitude = request.Longitude;
        item.LocationNote = EmptyToNull(request.LocationNote); item.UpdatedAt = UtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return new Response.UpdateResponse { LocationId = item.LocationId, UpdatedAt = AsUtc(item.UpdatedAt) };
    }

    public async Task<Response.DeleteResponse> DeleteAsync(Guid accountId, Guid locationId, CancellationToken cancellationToken = default)
    {
        await RequireCoordinatorAsync(accountId, cancellationToken);
        var item = await db.Locations.SingleOrDefaultAsync(item => item.LocationId == locationId && !item.IsDeleted, cancellationToken)
            ?? throw new NotFoundException("Location not found", "LOCATION_NOT_FOUND");
        if (await db.Routes.AnyAsync(route => !route.IsDeleted &&
            (route.FromLocationId == locationId || route.ToLocationId == locationId), cancellationToken))
            throw new ConflictException("Location is used by a Route", "LOCATION_IN_USE");
        var now = UtcNow(); item.IsDeleted = true; item.DeletedAt = now; item.DeletedByAccountId = accountId; item.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        return new Response.DeleteResponse { LocationId = item.LocationId, DeletedAt = AsUtc(now) };
    }

    private async Task<Employee> RequireCoordinatorAsync(Guid accountId, CancellationToken cancellationToken) =>
        await db.Employees.Include(item => item.Account).SingleOrDefaultAsync(item => item.AccountId == accountId &&
            item.Role == EmployeeRole.FLEET_ROUTE_COORDINATOR && item.IsActive && !item.IsDeleted &&
            !item.Account.IsDeleted && item.Account.Status == AccountStatus.ACTIVE, cancellationToken)
        ?? throw new ForbiddenException("Only Fleet Route Coordinators can manage Locations");

    private Task<bool> IsDuplicateAsync(string name, string? city, string country, Guid? exceptId, CancellationToken token) =>
        db.Locations.AnyAsync(item => !item.IsDeleted && item.LocationId != exceptId &&
            item.LocationName.ToLower() == name.Trim().ToLower() && item.Country == country && item.City == city, token);

    private static void ValidateFields(string? name, string? country, string? type, decimal? latitude, decimal? longitude)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200 || string.IsNullOrWhiteSpace(country) ||
            !latitude.HasValue || !longitude.HasValue)
            throw new BadRequestException("locationName, country, latitude and longitude are required", "VALIDATION_ERROR");
        if (latitude < -90 || latitude > 90 || longitude < -180 || longitude > 180)
            throw new BadRequestException("latitude or longitude is invalid", "INVALID_COORDINATES");
        _ = ParseType(type);
    }

    private static LocationType? ParseType(string? value) => string.IsNullOrWhiteSpace(value) ? null :
        Enum.TryParse<LocationType>(value.Trim(), true, out var result)
            ? result : throw new BadRequestException("locationType is invalid", "VALIDATION_ERROR");

    private static Response.DetailResponse Map(Location item) => new()
    {
        LocationId = item.LocationId, LocationName = item.LocationName, LocationType = item.LocationType?.ToString(),
        Address = item.Address, City = item.City, StateProvince = item.StateProvince, Country = item.Country,
        Latitude = item.Latitude, Longitude = item.Longitude, LocationNote = item.LocationNote, UpdatedAt = AsUtc(item.UpdatedAt)
    };

    private static void ValidatePaging(int pageIndex, int pageSize)
    {
        if (pageIndex < 1 || pageSize is < 1 or > 100) throw new BadRequestException("Invalid pagination", "VALIDATION_ERROR");
    }
    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static DateTime UtcNow() => DateTime.UtcNow;
    private static DateTimeOffset AsUtc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    private static void EnsureConcurrency(DateTime actual, DateTimeOffset expected)
    {
        if (actual != expected.UtcDateTime) throw new ConflictException("Location was modified. Please reload the latest data.", "LOCATION_MODIFIED");
    }
}
