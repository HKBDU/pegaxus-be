namespace Pegaxus.Service.Common;

public static class Geo
{
    public static double DistanceMeters(decimal latitude1, decimal longitude1, decimal latitude2, decimal longitude2)
    {
        var latitudeDelta = (double)(latitude2 - latitude1) * Math.PI / 180;
        var longitudeDelta = (double)(longitude2 - longitude1) * Math.PI / 180;
        var a = Math.Pow(Math.Sin(latitudeDelta / 2), 2) + Math.Cos((double)latitude1 * Math.PI / 180) *
            Math.Cos((double)latitude2 * Math.PI / 180) * Math.Pow(Math.Sin(longitudeDelta / 2), 2);
        return 6_371_000 * 2 * Math.Asin(Math.Sqrt(Math.Clamp(a, 0, 1)));
    }
}
