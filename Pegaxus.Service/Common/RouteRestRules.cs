using Pegaxus.Repository.Enum;

namespace Pegaxus.Service.Common;

public static class RouteRestRules
{
    public sealed record Leg(DateTime? DepartureAt, DateTime? ArrivalAt, TransportMode Mode, LocationType? FromType, LocationType? ToType);
    public sealed record Check(string Code, bool Passed, string Message);

    public static IReadOnlyList<Check> Evaluate(IReadOnlyList<Leg> legs)
    {
        if (legs.Count == 0 || legs.Any(leg => !leg.DepartureAt.HasValue || !leg.ArrivalAt.HasValue || leg.ArrivalAt <= leg.DepartureAt))
            return [new("DRIVING_HOURS_BR033", false, "A complete, valid planned schedule is required"),
                new("REST_STOP_24H_BR034", false, "A complete, valid planned schedule is required")];
        string? drivingError = null;
        if (legs.Any(leg => leg.Mode == TransportMode.ROAD && leg.ArrivalAt - leg.DepartureAt > TimeSpan.FromHours(8)))
            drivingError = "Maximum continuous driving time is 8 hours";
        else
            for (var index = 0; index < legs.Count; index++)
            {
                var leg = legs[index];
                if (leg.Mode == TransportMode.ROAD && leg.ArrivalAt - leg.DepartureAt > TimeSpan.FromMinutes(270) &&
                    (index == legs.Count - 1 || legs[index + 1].DepartureAt - leg.ArrivalAt < TimeSpan.FromMinutes(45)))
                {
                    drivingError = "Road segments exceeding 4.5 hours require a rest stop of at least 45 minutes";
                    break;
                }
            }
        var restPassed = legs[^1].ArrivalAt - legs[0].DepartureAt <= TimeSpan.FromHours(24) ||
            legs.Zip(legs.Skip(1), (first, second) => second.DepartureAt - first.ArrivalAt >= TimeSpan.FromHours(24) &&
                (first.ToType == LocationType.REST_STOP || second.FromType == LocationType.REST_STOP)).Any(rest => rest);
        return [new("DRIVING_HOURS_BR033", drivingError is null, drivingError ?? "Road segments satisfy the 8 hour driving and 45 minute rest limits"),
            new("REST_STOP_24H_BR034", restPassed, restPassed ? "The itinerary satisfies the mandatory 24 hour rest rule"
                : "Trips over 24 hours require a mandatory 24 hour rest")];
    }
}
