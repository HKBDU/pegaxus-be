namespace Pegaxus.Service.Common;

public static class Coordinates
{
    public static bool IsValid(decimal latitude, decimal longitude) =>
        latitude is >= -90 and <= 90 && longitude is >= -180 and <= 180;

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
