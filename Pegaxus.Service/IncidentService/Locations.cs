using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;

using static Pegaxus.Service.Common.Text;

namespace Pegaxus.Service.IncidentService;

public sealed partial class Service
{
    private async Task<Location> ResolveLocationAsync(Request.ReportHorseHealthRequest request, DateTime now, CancellationToken ct)
    {
        if (request.LocationId.HasValue)
            return await db.Locations.SingleOrDefaultAsync(item => item.LocationId == request.LocationId && !item.IsDeleted, ct)
                ?? throw new NotFoundException("Location not found", "LOCATION_NOT_FOUND");
        var latitude = request.Latitude!.Value;
        var longitude = request.Longitude!.Value;
        var candidates = await db.Locations.AsNoTracking().Where(item => !item.IsDeleted && item.Latitude != null && item.Longitude != null &&
            item.Latitude >= latitude - 0.001m && item.Latitude <= latitude + 0.001m).ToArrayAsync(ct);
        var closest = candidates.Select(item => new { Location = item, Distance = Coordinates.DistanceMeters(latitude, longitude, item.Latitude!.Value, item.Longitude!.Value) })
            .Where(item => item.Distance <= 100).OrderBy(item => item.Distance).ThenBy(item => item.Location.LocationId).FirstOrDefault();
        if (closest is not null)
            return await db.Locations.SingleAsync(item => item.LocationId == closest.Location.LocationId, ct);
        var location = new Location
        {
            LocationId = Guid.NewGuid(), LocationName = $"Incident location {latitude:F4}, {longitude:F4}", LocationType = LocationType.OTHER,
            Latitude = latitude, Longitude = longitude, Address = EmptyToNull(request.LocationDescription),
            Country = EmptyToNull(request.Country) ?? "Unknown", CreatedAt = now, UpdatedAt = now
        };
        db.Locations.Add(location);
        return location;
    }
}
