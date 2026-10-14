using System.Data;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Repository.Extensions;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.RouteChangeService.Validator;

using static Pegaxus.Service.Common.DbTime;
using static Pegaxus.Service.Common.Validation;

namespace Pegaxus.Service.RouteChangeService;

public sealed partial class Service
{
    private static Response.Comparison Compare(List<Response.Segment> active, List<Response.Segment> current, List<Response.Segment> proposed, DateTimeOffset eta)
    {
        static Guid[] Bcps(List<Response.Segment> list) => list.SelectMany(x => new[] { x.FromLocation, x.ToLocation })
            .Where(x => x.LocationType == "BORDER_GATE").Select(x => x.LocationId).Distinct().ToArray();
        Dictionary<Guid, int> Rest(List<Response.Segment> list)
        {
            var rest = list.Where(x => x.DwellAfterMinutes >= 1440).GroupBy(x => x.ToLocation.LocationId)
                .ToDictionary(x => x.Key, x => x.Sum(s => (int)(s.DwellAfterMinutes!.Value / 1440)));
            if (active.LastOrDefault(x => x.ActualArrivalAt.HasValue)?.ActualArrivalAt is { } arrival)
            {
                var days = (int)((list[0].PlannedDepartureAt - arrival).TotalMinutes / 1440);
                if (days > 0) rest[list[0].FromLocation.LocationId] = rest.GetValueOrDefault(list[0].FromLocation.LocationId) + days;
            }
            return rest;
        }
        var oldRest = Rest(current);
        return new(proposed.Sum(x => x.Route.DistanceKm!.Value) - current.Sum(x => x.Route.DistanceKm!.Value),
            (decimal)(eta - current[^1].PlannedArrivalAt).TotalMinutes, !Bcps(current).SequenceEqual(Bcps(proposed)),
            Rest(proposed).Any(x => x.Value > oldRest.GetValueOrDefault(x.Key)),
            current.SelectMany(x => new[] { x.FromLocation, x.ToLocation }).FirstOrDefault(x => x.LocationType == "BORDER_GATE"),
            proposed.SelectMany(x => new[] { x.FromLocation, x.ToLocation }).FirstOrDefault(x => x.LocationType == "BORDER_GATE"), current[^1].PlannedArrivalAt, eta);
    }

    private static string[] ApprovalReasons(Response.Comparison comparison)
    {
        var reasons = new List<string>();
        if (comparison.BcpChanged) reasons.Add("BCP_CHANGED");
        if (comparison.Additional24hRest) reasons.Add("ADDITIONAL_24H_REST");
        if (Math.Abs(comparison.DistanceDeltaKm) >= 50) reasons.Add("DEVIATION_OVER_50KM");
        if (comparison.DelayMinutes >= 60) reasons.Add("DELAY_OVER_1H");
        return reasons.ToArray();
    }
}
