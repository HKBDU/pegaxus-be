using Pegaxus.Repository.Entities;

namespace Pegaxus.Service.Common;

public sealed record LocationBrief(
    Guid LocationId, string LocationName, string? LocationType, string? City, string Country, decimal? Latitude, decimal? Longitude)
{
    public static LocationBrief? From(Location? location) => location is null
        ? null
        : new(location.LocationId, location.LocationName, location.LocationType?.ToString(), location.City, location.Country,
            location.Latitude, location.Longitude);
}
