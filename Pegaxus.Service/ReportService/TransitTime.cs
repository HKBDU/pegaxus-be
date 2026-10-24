using Pegaxus.Service.Common;

using static Pegaxus.Service.Common.DbTime;

namespace Pegaxus.Service.ReportService;

public sealed partial class Service
{
    private const decimal MinutesPerDay = 1440m;

    private sealed record TransitRow(TransportRow Transport, DateTime Departure, DateTime Arrival, int ActualMinutes, int EstimatedMinutes)
    {
        public int DeltaMinutes => ActualMinutes - EstimatedMinutes;
        public decimal ActualDays => Round1(ActualMinutes / MinutesPerDay);
        public decimal EstimatedDays => Round1(EstimatedMinutes / MinutesPerDay);
        public decimal DeltaDays => Round1(DeltaMinutes / MinutesPerDay);
    }

    private sealed record TransitSet(IReadOnlyList<TransitRow> Rows, int MissingDeparture, int MissingArrival, int MissingEstimate)
    {
        public int Excluded => MissingDeparture + MissingArrival + MissingEstimate;
    }

    public async Task<Response.TransitTimeReport> GetTransitTimeAsync(Guid accountId, Request.TransitTimeRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureManagerAsync(accountId, cancellationToken);
        Validation.ValidateFields(request, TransitTimeValidator);
        var report = await BuildTransitTimeAsync(request, cancellationToken);
        var total = report.Items.Count;
        return report with
        {
            Items = report.Items.Skip(Paging.Offset(request.PageIndex, request.PageSize)).Take(request.PageSize).ToList(),
            PageIndex = request.PageIndex, PageSize = request.PageSize, TotalItems = total, TotalPages = Paging.TotalPages(total, request.PageSize)
        };
    }

    private async Task<TransitSet> TransitSetAsync(Request.ReportFilter filter, Window window, CancellationToken cancellationToken)
    {
        var transports = await CompletedAsync(filter, window, null, cancellationToken);
        var ids = transports.Select(item => item.TransportId).ToArray();
        var plans = await PlansAsync(ids, cancellationToken);
        var deliveries = await DeliveriesAsync(ids, cancellationToken);
        var rows = new List<TransitRow>(); int noDeparture = 0, noArrival = 0, noEstimate = 0;
        foreach (var transport in transports)
        {
            if (transport.ActualDepartureAt is not { } departure) { noDeparture++; continue; }
            var arrival = transport.ActualArrivalAt ?? (deliveries.TryGetValue(transport.TransportId, out var signed) ? signed : null);
            if (arrival is not { } arrivedAt) { noArrival++; continue; }
            if (!plans.TryGetValue(transport.TransportId, out var plan) || plan.EstimatedMinutes is not { } estimate) { noEstimate++; continue; }
            rows.Add(new(transport, departure, arrivedAt, (int)Math.Round((arrivedAt - departure).TotalMinutes, MidpointRounding.AwayFromZero), estimate));
        }
        return new(rows, noDeparture, noArrival, noEstimate);
    }

    private static decimal? AverageDays(IReadOnlyList<TransitRow> rows, Func<TransitRow, int> minutes) =>
        rows.Count == 0 ? null : Round1(rows.Average(item => (decimal)minutes(item)) / MinutesPerDay);

    /// <summary>Total real time against total estimated time (O-F6-16): long trips weigh more than short ones.</summary>
    private static decimal? Accuracy(IReadOnlyList<TransitRow> rows) =>
        rows.Count == 0 ? null : Round1(rows.Sum(item => (decimal)item.ActualMinutes) * 100m / rows.Sum(item => (decimal)item.EstimatedMinutes));

    /// <summary>The whole report with every table row; the caller pages the rows or exports them all.</summary>
    private async Task<Response.TransitTimeReport> BuildTransitTimeAsync(Request.TransitTimeRequest request, CancellationToken cancellationToken)
    {
        var window = WindowOf(request);
        var current = await TransitSetAsync(request, window, cancellationToken);
        var before = await TransitSetAsync(request, window.Previous, cancellationToken);
        var rows = current.Rows;
        var kpi = new Response.TransitKpi(
            CountKpi(rows.Count, before.Rows.Count, before.Rows.Count + before.Excluded > 0),
            RateKpi(AverageDays(rows, item => item.ActualMinutes), AverageDays(before.Rows, item => item.ActualMinutes)),
            RateKpi(AverageDays(rows, item => item.DeltaMinutes), AverageDays(before.Rows, item => item.DeltaMinutes)),
            RateKpi(Accuracy(rows), Accuracy(before.Rows)));

        // Bounds follow the contract: <= -1, (-1, 0], (0, 1], (1, 2], > 2 days against the estimate.
        var distribution = new[]
        {
            new Response.RangeCount("<=-1", rows.Count(item => item.DeltaMinutes <= -MinutesPerDay)),
            new Response.RangeCount("(-1,0]", rows.Count(item => item.DeltaMinutes > -MinutesPerDay && item.DeltaMinutes <= 0)),
            new Response.RangeCount("(0,1]", rows.Count(item => item.DeltaMinutes > 0 && item.DeltaMinutes <= MinutesPerDay)),
            new Response.RangeCount("(1,2]", rows.Count(item => item.DeltaMinutes > MinutesPerDay && item.DeltaMinutes <= 2 * MinutesPerDay)),
            new Response.RangeCount(">2", rows.Count(item => item.DeltaMinutes > 2 * MinutesPerDay))
        };

        var benchmark = rows.GroupBy(item => (item.Transport.OriginCountry, item.Transport.DestinationCountry))
            .Select(group => new Response.RouteBenchmark(group.Key.OriginCountry, group.Key.DestinationCountry, group.Count(),
                AverageDays(group.ToList(), item => item.ActualMinutes)!.Value, AverageDays(group.ToList(), item => item.EstimatedMinutes)!.Value,
                AverageDays(group.ToList(), item => item.DeltaMinutes)!.Value))
            .OrderByDescending(item => item.Total).ThenBy(item => item.OriginCountry, StringComparer.Ordinal).ThenBy(item => item.DestinationCountry, StringComparer.Ordinal).ToList();

        // The delay filter only narrows the table; every figure above still counts all trips.
        var shown = request.DelayFilter?.Trim().ToUpperInvariant() switch
        {
            "LATE_OVER_1_DAY" => rows.Where(item => item.DeltaMinutes > MinutesPerDay),
            "EARLY" => rows.Where(item => item.DeltaMinutes < 0),
            _ => rows.AsEnumerable()
        };
        IOrderedEnumerable<TransitRow> ordered = Sort(request.SortBy) switch
        {
            ["actualdays", "asc"] => shown.OrderBy(item => item.ActualMinutes),
            ["actualdays", "desc"] => shown.OrderByDescending(item => item.ActualMinutes),
            ["deltadays", "asc"] => shown.OrderBy(item => item.DeltaMinutes),
            _ => shown.OrderByDescending(item => item.DeltaMinutes)
        };
        var items = ordered.ThenBy(item => item.Transport.TransportId).Select(item => new Response.TransitItem(item.Transport.TransportId, item.Transport.TransportCode,
            item.Transport.OriginCountry, item.Transport.DestinationCountry, AsUtc(item.Departure), AsUtc(item.Arrival), item.EstimatedDays, item.ActualDays, item.DeltaDays)).ToList();

        return new(PeriodOf(window), clock.GetUtcNow(), kpi, distribution, benchmark,
            new(current.Excluded, new Dictionary<string, int>
            {
                ["MISSING_ACTUAL_DEPARTURE"] = current.MissingDeparture, ["MISSING_ACTUAL_ARRIVAL"] = current.MissingArrival, ["MISSING_ESTIMATE"] = current.MissingEstimate
            }),
            items, 1, Math.Max(items.Count, 1), items.Count, 1);
    }
}
