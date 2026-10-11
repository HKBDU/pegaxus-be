using Pegaxus.Repository.Entities;

namespace Pegaxus.Service.Common;

public static class RoutePersonnel
{
    public static string GetStatus(IReadOnlyCollection<RoutePlanRoute> segments)
    {
        if (segments.Count == 0) return "NOT_ASSIGNED";
        if (segments.All(segment => segment.TransportProviderId.HasValue ||
            (segment.DriverEmployeeId.HasValue && segment.EscortEmployeeId.HasValue))) return "FULLY_ASSIGNED";
        return segments.Any(segment => segment.TransportProviderId.HasValue || segment.DriverEmployeeId.HasValue || segment.EscortEmployeeId.HasValue)
            ? "PARTIALLY_ASSIGNED" : "NOT_ASSIGNED";
    }
}
