using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;

namespace Pegaxus.Service.RouteService;

public sealed class Service(AppDbContext db, IHttpClientFactory httpClientFactory, IConfiguration configuration) : IService
{
    public async Task<Response.ListResponse> GetListAsync(Request.ListRequest request, CancellationToken cancellationToken = default)
    {
        ValidatePaging(request.PageIndex, request.PageSize);
        var query = db.Routes.AsNoTracking().Where(item => !item.IsDeleted)
            .Include(item => item.FromLocation).Include(item => item.ToLocation).AsQueryable();
        if (request.FromLocationId.HasValue) query = query.Where(item => item.FromLocationId == request.FromLocationId.Value);
        if (request.ToLocationId.HasValue) query = query.Where(item => item.ToLocationId == request.ToLocationId.Value);
        if (!string.IsNullOrWhiteSpace(request.TransportMode))
            query = query.Where(item => item.TransportMode == ParseMode(request.TransportMode));
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToLower();
            query = query.Where(item => item.RouteCode.ToLower().Contains(search) || (item.RouteName != null && item.RouteName.ToLower().Contains(search)));
        }
        var totalItems = await query.CountAsync(cancellationToken);
        var rows = await query.OrderBy(item => item.RouteCode).ThenBy(item => item.RouteId)
            .Skip((request.PageIndex - 1) * request.PageSize).Take(request.PageSize).ToListAsync(cancellationToken);
        return new Response.ListResponse
        {
            Items = rows.Select(Map).ToList(), PageIndex = request.PageIndex, PageSize = request.PageSize,
            TotalItems = totalItems, TotalPages = (int)Math.Ceiling(totalItems / (double)request.PageSize)
        };
    }

    public async Task<Response.DetailResponse> GetAsync(Guid routeId, CancellationToken cancellationToken = default) =>
        Map(await LoadRouteAsync(routeId, cancellationToken));

    public async Task<Response.DetailResponse> CreateAsync(Guid accountId, Request.CreateRequest request, CancellationToken cancellationToken = default)
    {
        await RequireCoordinatorAsync(accountId, cancellationToken);
        ValidateIds(request.FromLocationId, request.ToLocationId);
        var mode = ParseMode(request.TransportMode);
        var locations = await LoadLocationsAsync(request.FromLocationId, request.ToLocationId, cancellationToken);
        await EnsureNotDuplicateAsync(request.FromLocationId, request.ToLocationId, mode, null, cancellationToken);
        var metrics = await CalculateMetricsAsync(locations.from, locations.to, mode, cancellationToken);
        var now = UtcNow();
        var item = new Route
        {
            RouteId = Guid.NewGuid(), RouteCode = await GenerateCodeAsync(cancellationToken),
            FromLocationId = request.FromLocationId, ToLocationId = request.ToLocationId, TransportMode = mode,
            RouteName = EmptyToNull(request.RouteName), RouteNote = EmptyToNull(request.RouteNote),
            DistanceKm = metrics.DistanceKm, EstimatedDurationMinutes = metrics.DurationMinutes,
            CreatedAt = now, UpdatedAt = now, FromLocation = locations.from, ToLocation = locations.to
        };
        db.Routes.Add(item);
        await db.SaveChangesAsync(cancellationToken);
        return Map(item);
    }

    public async Task<Response.UpdateResponse> UpdateAsync(Guid accountId, Guid routeId, Request.UpdateRequest request, CancellationToken cancellationToken = default)
    {
        await RequireCoordinatorAsync(accountId, cancellationToken);
        if (request.LastUpdatedAt == default) throw new BadRequestException("lastUpdatedAt is required", "VALIDATION_ERROR");
        var item = await db.Routes.SingleOrDefaultAsync(item => item.RouteId == routeId && !item.IsDeleted, cancellationToken)
            ?? throw new NotFoundException("Route not found", "ROUTE_NOT_FOUND");
        EnsureConcurrency(item.UpdatedAt, request.LastUpdatedAt);
        var fromId = request.FromLocationId ?? item.FromLocationId;
        var toId = request.ToLocationId ?? item.ToLocationId;
        ValidateIds(fromId, toId);
        var mode = request.TransportMode is null ? item.TransportMode : ParseMode(request.TransportMode);
        if (request.FromLocationId.HasValue || request.ToLocationId.HasValue || request.TransportMode is not null)
        {
            var locations = await LoadLocationsAsync(fromId, toId, cancellationToken);
            await EnsureNotDuplicateAsync(fromId, toId, mode, routeId, cancellationToken);
            var metrics = await CalculateMetricsAsync(locations.from, locations.to, mode, cancellationToken);
            item.FromLocationId = fromId; item.ToLocationId = toId; item.TransportMode = mode;
            item.DistanceKm = metrics.DistanceKm; item.EstimatedDurationMinutes = metrics.DurationMinutes;
        }
        if (request.RouteName is not null) item.RouteName = EmptyToNull(request.RouteName);
        if (request.RouteNote is not null) item.RouteNote = EmptyToNull(request.RouteNote);
        item.UpdatedAt = UtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return new Response.UpdateResponse { RouteId = item.RouteId, UpdatedAt = AsUtc(item.UpdatedAt) };
    }

    public async Task<Response.DeleteResponse> DeleteAsync(Guid accountId, Guid routeId, CancellationToken cancellationToken = default)
    {
        await RequireCoordinatorAsync(accountId, cancellationToken);
        var item = await db.Routes.SingleOrDefaultAsync(item => item.RouteId == routeId && !item.IsDeleted, cancellationToken)
            ?? throw new NotFoundException("Route not found", "ROUTE_NOT_FOUND");
        if (await db.RoutePlanRoutes.AnyAsync(item => item.RouteId == routeId, cancellationToken))
            throw new ConflictException("Route is used by a Route Plan", "ROUTE_IN_USE");
        var now = UtcNow(); item.IsDeleted = true; item.DeletedAt = now; item.DeletedByAccountId = accountId; item.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        return new Response.DeleteResponse { RouteId = item.RouteId, DeletedAt = AsUtc(now) };
    }

    private async Task<Route> LoadRouteAsync(Guid id, CancellationToken token) =>
        await db.Routes.AsNoTracking().Include(item => item.FromLocation).Include(item => item.ToLocation)
            .SingleOrDefaultAsync(item => item.RouteId == id && !item.IsDeleted, token)
        ?? throw new NotFoundException("Route not found", "ROUTE_NOT_FOUND");

    private async Task<(Location from, Location to)> LoadLocationsAsync(Guid fromId, Guid toId, CancellationToken token)
    {
        var locations = await db.Locations.Where(item => (item.LocationId == fromId || item.LocationId == toId) && !item.IsDeleted).ToListAsync(token);
        var from = locations.SingleOrDefault(item => item.LocationId == fromId);
        var to = locations.SingleOrDefault(item => item.LocationId == toId);
        return from is null || to is null ? throw new NotFoundException("Location not found", "LOCATION_NOT_FOUND") : (from, to);
    }

    private async Task EnsureNotDuplicateAsync(Guid from, Guid to, TransportMode mode, Guid? except, CancellationToken token)
    {
        if (await db.Routes.AnyAsync(item => !item.IsDeleted && item.RouteId != except && item.FromLocationId == from && item.ToLocationId == to && item.TransportMode == mode, token))
            throw new ConflictException("A Route with the same locations and transport mode already exists", "ROUTE_DUPLICATED");
    }

    private async Task<(decimal? DistanceKm, int? DurationMinutes)> CalculateMetricsAsync(Location from, Location to, TransportMode mode, CancellationToken token)
    {
        if (!from.Latitude.HasValue || !from.Longitude.HasValue || !to.Latitude.HasValue || !to.Longitude.HasValue)
            throw new BadRequestException("Both Locations must have valid coordinates", "INVALID_COORDINATES");
        if (mode != TransportMode.ROAD)
        {
            var distance = Haversine(from.Latitude.Value, from.Longitude.Value, to.Latitude.Value, to.Longitude.Value);
            var speed = mode == TransportMode.AIR ? 800m : 35m;
            return (Math.Round(distance, 1), (int)Math.Ceiling((double)(distance / speed * 60)));
        }
        var apiKey = configuration["Geoapify:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new Exceptions.UnprocessableEntityException("Geoapify API key is not configured", "MAP_PROVIDER_ERROR");
        var url = $"https://api.geoapify.com/v1/routing?waypoints={from.Latitude.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)},{from.Longitude.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}|{to.Latitude.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)},{to.Longitude.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}&mode=drive&format=json&apiKey={Uri.EscapeDataString(apiKey)}";
        using var response = await httpClientFactory.CreateClient().GetAsync(url, token);
        if (response.StatusCode == HttpStatusCode.TooManyRequests) throw new TooManyRequestsException("Geoapify rate limit exceeded", "MAP_PROVIDER_RATE_LIMITED");
        if (!response.IsSuccessStatusCode) throw new Exceptions.UnprocessableEntityException("Geoapify routing failed", response.StatusCode == HttpStatusCode.UnprocessableEntity ? "ROUTING_FAILED" : "MAP_PROVIDER_ERROR");
        try
        {
            using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(token));
            var properties = json.RootElement.GetProperty("features")[0].GetProperty("properties");
            var distance = properties.GetProperty("distance").GetDecimal() / 1000m;
            var duration = (int)Math.Ceiling(properties.GetProperty("time").GetDouble() / 60d);
            return (Math.Round(distance, 1), duration);
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or JsonException or FormatException)
        {
            throw new Exceptions.UnprocessableEntityException("Geoapify returned an invalid route result", "ROUTING_FAILED");
        }
    }

    private async Task<Employee> RequireCoordinatorAsync(Guid accountId, CancellationToken token) =>
        await db.Employees.Include(item => item.Account).SingleOrDefaultAsync(item => item.AccountId == accountId && item.Role == EmployeeRole.FLEET_ROUTE_COORDINATOR && item.IsActive && !item.IsDeleted && !item.Account.IsDeleted && item.Account.Status == AccountStatus.ACTIVE, token)
        ?? throw new ForbiddenException("Only Fleet Route Coordinators can manage Routes");

    private static TransportMode ParseMode(string? value) => !string.IsNullOrWhiteSpace(value) && Enum.TryParse<TransportMode>(value.Trim(), true, out var mode)
        ? mode : throw new BadRequestException("transportMode must be ROAD, SEA or AIR", "VALIDATION_ERROR");
    private static void ValidateIds(Guid from, Guid to) { if (from == Guid.Empty || to == Guid.Empty || from == to) throw new BadRequestException("fromLocationId and toLocationId are required and must differ", "VALIDATION_ERROR"); }
    private async Task<string> GenerateCodeAsync(CancellationToken token) { for (var i = 1; ; i++) { var code = $"RT-{DateTime.UtcNow:yyyyMMdd}-{i:0000}"; if (!await db.Routes.AnyAsync(item => item.RouteCode == code, token)) return code; } }
    private static Response.DetailResponse Map(Route item) => new() { RouteId = item.RouteId, RouteCode = item.RouteCode, RouteName = item.RouteName, TransportMode = item.TransportMode.ToString(), FromLocation = MapLocation(item.FromLocation), ToLocation = MapLocation(item.ToLocation), DistanceKm = item.DistanceKm, EstimatedDurationMinutes = item.EstimatedDurationMinutes, RouteNote = item.RouteNote, UpdatedAt = AsUtc(item.UpdatedAt) };
    private static Response.LocationResponse MapLocation(Location item) => new() { LocationId = item.LocationId, LocationName = item.LocationName, LocationType = item.LocationType?.ToString(), City = item.City, Country = item.Country, Latitude = item.Latitude, Longitude = item.Longitude };
    private static decimal Haversine(decimal lat1, decimal lon1, decimal lat2, decimal lon2) { const double earth = 6371; var dLat = (double)(lat2 - lat1) * Math.PI / 180; var dLon = (double)(lon2 - lon1) * Math.PI / 180; var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos((double)lat1 * Math.PI / 180) * Math.Cos((double)lat2 * Math.PI / 180) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2); return (decimal)(earth * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a))); }
    private static void ValidatePaging(int page, int size) { if (page < 1 || size is < 1 or > 100) throw new BadRequestException("Invalid pagination", "VALIDATION_ERROR"); }
    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    // The database stores timestamps as `timestamp without time zone`.
    // Keep the UTC instant while writing an unspecified DateTime so Npgsql
    // does not reject the value because of its Kind metadata.
    private static DateTime UtcNow() => DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
    private static DateTimeOffset AsUtc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    private static void EnsureConcurrency(DateTime actual, DateTimeOffset expected) { if (actual != expected.UtcDateTime) throw new ConflictException("Route was modified. Please reload the latest data.", "ROUTE_MODIFIED"); }
}
