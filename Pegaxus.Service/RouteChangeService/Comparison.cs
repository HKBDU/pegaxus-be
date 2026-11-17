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

    /// <summary>
    /// Tells the manager, in plain words, how big the change is. Any change can be sent for a decision; a big one (new border
    /// crossing, an extra rest day, 50 km or more off the planned distance, or an hour or more late) is named so it stands out.
    /// </summary>
    private static string ChangeSummary(IReadOnlyList<string> points) => points.Count == 0
        ? "This is a small change: same border crossing, no extra rest day, less than 50 km difference and less than an hour late."
        : $"Please check: {string.Join("; ", points)}.";

    /// <summary>Each big part of the change in plain words; empty for a small change.</summary>
    private static List<string> ChangePoints(Response.Comparison comparison)
    {
        var points = new List<string>();
        if (comparison.BcpChanged)
            points.Add(comparison.NewBcp is { } next ? $"the border crossing changes to {next.LocationName}" : "the border crossing changes");
        if (comparison.Additional24hRest) points.Add("the horses need at least one more full day of rest on the way");
        if (Math.Abs(comparison.DistanceDeltaKm) >= 50)
            points.Add(FormattableString.Invariant($"the route is {Math.Abs(comparison.DistanceDeltaKm):0.#} km {(comparison.DistanceDeltaKm > 0 ? "longer" : "shorter")}"));
        if (comparison.DelayMinutes >= 60) points.Add($"arrival is {Late(comparison.DelayMinutes)} later than planned");
        return points;
    }

    private static string Late(decimal minutes)
    {
        var total = (int)minutes;
        var (hours, rest) = (total / 60, total % 60);
        return rest == 0 ? $"{hours} h" : $"{hours} h {rest} min";
    }
}
