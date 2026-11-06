namespace Pegaxus.Service.Common;

public static class Coordinates
{
    /// <summary>In range, at most <see cref="Limits.CoordinateScale"/> decimals, and not the (0, 0) placeholder point.</summary>
    public static bool IsValid(decimal latitude, decimal longitude) =>
        InputRules.IsValid(latitude, Limits.Latitude) && InputRules.IsValid(longitude, Limits.Longitude) && !(latitude == 0 && longitude == 0);

    /// <summary>Great-circle distance in meters.</summary>
    public static double DistanceMeters(decimal latitude1, decimal longitude1, decimal latitude2, decimal longitude2)
    {
        const double earthRadiusMeters = 6_371_000;
        var (lat1, lat2) = (ToRadians(latitude1), ToRadians(latitude2));
        var dLat = lat2 - lat1;
        var dLon = ToRadians(longitude2 - longitude1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(lat1) * Math.Cos(lat2) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return earthRadiusMeters * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private static double ToRadians(decimal degrees) => (double)degrees * Math.PI / 180;
}
