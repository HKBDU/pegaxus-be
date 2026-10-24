using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Common;

using static Pegaxus.Service.Common.DbTime;

namespace Pegaxus.Service.ReportService;

public sealed partial class Service
{
    private const string OnTime = "ON_TIME", MinorDelay = "MINOR_DELAY", MajorDelay = "MAJOR_DELAY";
    private const string InternalFleet = "Internal fleet";

    private sealed record OnTimeRow(TransportRow Transport, DateTime PlannedEndAt, DateTime ActualArrivalAt, int DelayMinutes, string Bucket);

    private sealed record OnTimeSet(IReadOnlyList<OnTimeRow> Rows, int MissingPlannedEnd, int MissingActualArrival)
    {
        public int Excluded => MissingPlannedEnd + MissingActualArrival;
    }

    /// <summary>R25: up to 60 minutes late is on time, under 180 is a minor delay, the rest is a major delay.</summary>
    private static string BucketOf(int delayMinutes) => delayMinutes <= 60 ? OnTime : delayMinutes < 180 ? MinorDelay : MajorDelay;

    public async Task<Response.OnTimeReport> GetOnTimeAsync(Guid accountId, Request.OnTimeRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureManagerAsync(accountId, cancellationToken);
        Validation.ValidateFields(request, OnTimeValidator);
        var report = await BuildOnTimeAsync(request, cancellationToken);
        var total = report.Items.Count;
        return report with
        {
            Items = report.Items.Skip(Paging.Offset(request.PageIndex, request.PageSize)).Take(request.PageSize).ToList(),
            PageIndex = request.PageIndex, PageSize = request.PageSize, TotalItems = total, TotalPages = Paging.TotalPages(total, request.PageSize)
        };
    }

    private async Task<OnTimeSet> OnTimeSetAsync(Request.OnTimeRequest filter, Window window, CancellationToken cancellationToken)
    {
        var transports = await CompletedAsync(filter, window, filter.DriverEmployeeId, cancellationToken);
        var ids = transports.Select(item => item.TransportId).ToArray();
        var plans = await PlansAsync(ids, cancellationToken);
        var deliveries = await DeliveriesAsync(ids, cancellationToken);
        var rows = new List<OnTimeRow>(); int noPlannedEnd = 0, noArrival = 0;
        foreach (var transport in transports)
        {
            if (!plans.TryGetValue(transport.TransportId, out var plan) || plan.PlannedEndAt is not { } plannedEnd) { noPlannedEnd++; continue; }
            // Arrival falls back to the signed DELIVERY handover; a trip with neither cannot be judged.
            var arrival = transport.ActualArrivalAt ?? (deliveries.TryGetValue(transport.TransportId, out var signed) ? signed : null);
            if (arrival is not { } arrivedAt) { noArrival++; continue; }
            var delay = (int)Math.Round((arrivedAt - plannedEnd).TotalMinutes, MidpointRounding.AwayFromZero);
            rows.Add(new(transport, plannedEnd, arrivedAt, delay, BucketOf(delay)));
        }
        return new(rows, noPlannedEnd, noArrival);
    }

    /// <summary>The whole report with every table row; the caller pages the rows or exports them all.</summary>
    private async Task<Response.OnTimeReport> BuildOnTimeAsync(Request.OnTimeRequest request, CancellationToken cancellationToken)
    {
        var window = WindowOf(request);
        var current = await OnTimeSetAsync(request, window, cancellationToken);
        var before = await OnTimeSetAsync(request, window.Previous, cancellationToken);
        var rows = current.Rows;
        var rate = Percent(rows.Count(item => item.Bucket == OnTime), rows.Count);
        var previousRate = Percent(before.Rows.Count(item => item.Bucket == OnTime), before.Rows.Count);
        var kpi = new Response.OnTimeKpi(
            CountKpi(rows.Count, before.Rows.Count, before.Rows.Count + before.Excluded > 0),
            RateKpi(rate, previousRate),
            RateKpi(Average(rows.Select(item => (decimal)item.DelayMinutes).ToList()), Average(before.Rows.Select(item => (decimal)item.DelayMinutes).ToList())),
            SlaTargetRate, rate >= SlaTargetRate);

        var distribution = new Dictionary<string, int>
        {
            [OnTime] = rows.Count(item => item.Bucket == OnTime), [MinorDelay] = rows.Count(item => item.Bucket == MinorDelay), [MajorDelay] = rows.Count(item => item.Bucket == MajorDelay)
        };

        var late = rows.Where(item => item.Bucket != OnTime).ToList();
        // The bucket filter only narrows the table below; every figure above still counts all trips.
        var shown = rows.Where(item => request.Bucket is null ? item.Bucket != OnTime : item.Bucket.Equals(request.Bucket.Trim(), StringComparison.OrdinalIgnoreCase));
        IOrderedEnumerable<OnTimeRow> ordered = Sort(request.SortBy) switch
        {
            ["completedat", "asc"] => shown.OrderBy(item => item.Transport.CompletedAt),
            ["completedat", "desc"] => shown.OrderByDescending(item => item.Transport.CompletedAt),
            ["delayminutes", "asc"] => shown.OrderBy(item => item.DelayMinutes),
            _ => shown.OrderByDescending(item => item.DelayMinutes)
        };
        var items = ordered.ThenBy(item => item.Transport.TransportId).ToList();

        var causes = await DelayCausesAsync(late.Concat(items).Select(item => item.Transport.TransportId).Distinct().ToArray(), cancellationToken);
        var providers = await ProvidersAsync(rows.Select(item => item.Transport.TransportId).ToArray(), cancellationToken);

        return new(PeriodOf(window), clock.GetUtcNow(), kpi, distribution,
            Trend(rows, window, request.TrendBy.Trim().Equals("MONTH", StringComparison.OrdinalIgnoreCase)),
            ByProvider(rows, providers),
            late.GroupBy(item => causes[item.Transport.TransportId]).Select(group => new Response.DelayCause(group.Key, group.Count()))
                .OrderByDescending(item => item.Count).ThenBy(item => item.Cause, StringComparer.Ordinal).ToList(),
            new(current.Excluded, new Dictionary<string, int> { ["MISSING_PLANNED_END"] = current.MissingPlannedEnd, ["MISSING_ACTUAL_ARRIVAL"] = current.MissingActualArrival }),
            items.Select(item => new Response.OnTimeItem(item.Transport.TransportId, item.Transport.TransportCode, item.Transport.BookingCode, item.Transport.OriginCountry,
                item.Transport.DestinationCountry, AsUtc(item.PlannedEndAt), AsUtc(item.ActualArrivalAt), item.DelayMinutes, item.Bucket,
                item.Bucket == OnTime ? null : causes[item.Transport.TransportId])).ToList(),
            1, Math.Max(items.Count, 1), items.Count, 1);
    }

    private static List<Response.TrendPoint> Trend(IReadOnlyList<OnTimeRow> rows, Window window, bool monthly)
    {
        var points = new List<Response.TrendPoint>();
        var cursor = monthly ? new DateOnly(window.From.Year, window.From.Month, 1) : window.From.AddDays(-(((int)window.From.DayOfWeek + 6) % 7));
        while (cursor <= window.To)
        {
            var end = monthly ? cursor.AddMonths(1).AddDays(-1) : cursor.AddDays(6);
            var inside = rows.Where(item => DateOnly.FromDateTime(item.Transport.CompletedAt!.Value) >= cursor && DateOnly.FromDateTime(item.Transport.CompletedAt!.Value) <= end).ToList();
            var date = cursor.ToDateTime(TimeOnly.MinValue);
            var label = monthly ? cursor.ToString("yyyy-MM", CultureInfo.InvariantCulture) : $"{ISOWeek.GetYear(date)}-W{ISOWeek.GetWeekOfYear(date):00}";
            points.Add(new(label, cursor, end, inside.Count, Percent(inside.Count(item => item.Bucket == OnTime), inside.Count)));
            cursor = end.AddDays(1);
        }
        return points;
    }

    /// <summary>A trip with several outsourced partners counts once under each of them, so the groups can add up to more than the total.</summary>
    private static List<Response.ProviderPoint> ByProvider(IReadOnlyList<OnTimeRow> rows, Dictionary<Guid, List<(Guid Id, string Name)>> providers)
    {
        var groups = new Dictionary<(Guid? Id, string Name), List<OnTimeRow>>();
        void Add(Guid? id, string name, OnTimeRow row)
        {
            if (!groups.TryGetValue((id, name), out var list)) groups[(id, name)] = list = [];
            list.Add(row);
        }
        foreach (var row in rows)
        {
            if (providers.TryGetValue(row.Transport.TransportId, out var found)) foreach (var provider in found) Add(provider.Id, provider.Name, row);
            else Add(null, InternalFleet, row);
        }
        return groups.Select(group => new Response.ProviderPoint(group.Key.Id, group.Key.Name, group.Value.Count, Percent(group.Value.Count(item => item.Bucket == OnTime), group.Value.Count)))
            .OrderBy(item => item.TransportProviderId is null).ThenByDescending(item => item.Total).ThenBy(item => item.ProviderName, StringComparer.Ordinal).ToList();
    }

    private async Task<Dictionary<Guid, List<(Guid Id, string Name)>>> ProvidersAsync(Guid[] transportIds, CancellationToken cancellationToken)
    {
        var rows = await db.RoutePlanRoutes.AsNoTracking().Where(item => item.TransportProviderId != null && item.RoutePlan.Status != RoutePlanStatus.CANCELLED &&
                transportIds.Contains(item.RoutePlan.TransportPlan.TransportId))
            .Select(item => new { item.RoutePlan.TransportPlan.TransportId, ProviderId = item.TransportProviderId!.Value, item.TransportProvider!.ProviderName })
            .Distinct().ToListAsync(cancellationToken);
        return rows.GroupBy(item => item.TransportId).ToDictionary(group => group.Key, group => group.Select(item => (item.ProviderId, item.ProviderName)).ToList());
    }

    /// <summary>
    /// O-F6-15: a delay has no recorded cause, so it is inferred from the trip's worst live incident (the earliest of equal severity); no incident means UNKNOWN.
    /// </summary>
    private async Task<Dictionary<Guid, string>> DelayCausesAsync(Guid[] transportIds, CancellationToken cancellationToken)
    {
        var nullable = transportIds.Select(id => (Guid?)id).ToArray();
        var incidents = await db.IncidentReports.AsNoTracking().Where(item => item.Status != IncidentStatus.CANCELLED && nullable.Contains(item.TransportId))
            .Select(item => new { TransportId = item.TransportId!.Value, item.Severity, item.Category, item.OccurredAt }).ToListAsync(cancellationToken);
        var worst = incidents.GroupBy(item => item.TransportId)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(item => item.Severity).ThenBy(item => item.OccurredAt).First().Category);
        return transportIds.ToDictionary(id => id, id => worst.TryGetValue(id, out var category) ? category switch
        {
            IncidentCategory.VEHICLE or IncidentCategory.BOTH => "VEHICLE_INCIDENT",
            IncidentCategory.HORSE_HEALTH => "HORSE_HEALTH_INCIDENT",
            _ => "ROUTE_CHANGE"
        } : "UNKNOWN");
    }
}
