using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Common;

using static Pegaxus.Service.Common.DbTime;

namespace Pegaxus.Service.ReportService;

public sealed partial class Service
{
    private static T[] ParseCsv<T>(string? csv) where T : struct, Enum => SplitCsv(csv).Select(part => Enum.Parse<T>(part, true)).ToArray();

    public async Task<Response.IncidentClaimReport> GetIncidentClaimAsync(Guid accountId, Request.IncidentClaimRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureManagerAsync(accountId, cancellationToken);
        Validation.ValidateFields(request, IncidentClaimValidator);
        var report = await BuildIncidentClaimAsync(request, cancellationToken);
        var total = report.Items.Count;
        return report with
        {
            Items = report.Items.Skip(Paging.Offset(request.PageIndex, request.PageSize)).Take(request.PageSize).ToList(),
            PageIndex = request.PageIndex, PageSize = request.PageSize, TotalItems = total, TotalPages = Paging.TotalPages(total, request.PageSize)
        };
    }

    /// <summary>Live incidents with the category, severity, vehicle and horse filters; the window is optional (the rate numerators ignore it).</summary>
    private IQueryable<IncidentReport> Incidents(Request.IncidentClaimRequest filter, Window? window)
    {
        var query = db.IncidentReports.AsNoTracking().Where(item => item.Status != IncidentStatus.CANCELLED);
        if (window is not null)
        {
            var start = window.Start; var until = window.Until;
            query = query.Where(item => item.OccurredAt >= start && item.OccurredAt < until);
        }
        var categories = ParseCsv<IncidentCategory>(filter.IncidentCategory);
        if (categories.Length > 0) query = query.Where(item => categories.Contains(item.Category));
        var severities = ParseCsv<IncidentSeverity>(filter.Severity);
        if (severities.Length > 0) query = query.Where(item => severities.Contains(item.Severity));
        if (filter.VehicleId is { } vehicleId) query = query.Where(item => item.VehicleIncidentDetail != null && item.VehicleIncidentDetail.VehicleId == vehicleId);
        if (filter.HorseId is { } horseId) query = query.Where(item => item.HorseHealthIncidentDetails.Any(detail => detail.HorseId == horseId));
        // An incident without a transport cannot match a route, country or provider, so it only shows when none of those filters is set.
        if (HasTransportFilter(filter))
        {
            var matching = Transports(filter).Select(item => item.TransportId);
            query = query.Where(item => item.TransportId != null && matching.Contains(item.TransportId.Value));
        }
        return query;
    }

    private IQueryable<Claim> Claims(Request.IncidentClaimRequest filter, Window? window)
    {
        var query = db.Claims.AsNoTracking().Where(item => item.Status != ClaimStatus.CANCELLED);
        if (window is not null)
        {
            var start = window.Start; var until = window.Until;
            query = query.Where(item => item.SubmittedAt >= start && item.SubmittedAt < until);
        }
        var types = ParseCsv<ClaimType>(filter.ClaimType);
        if (types.Length > 0) query = query.Where(item => types.Contains(item.ClaimType));
        if (HasTransportFilter(filter))
        {
            var matching = Transports(filter).Select(item => item.TransportId);
            query = query.Where(item => matching.Contains(item.HandoverRecord.TransportId));
        }
        return query;
    }

    private sealed record Figures(int Completed, int Incidents, int TripsWithIncident, int Claims, int TripsWithClaim, long EmergencyCost)
    {
        public bool HasData => Completed + Incidents + Claims > 0;
    }

    /// <summary>The headline numbers of one window. Rates divide the trips that had a live incident (or claim) by the trips completed in the window.</summary>
    private async Task<Figures> FiguresAsync(Request.IncidentClaimRequest filter, Window window, CancellationToken cancellationToken)
    {
        var completedIds = await CompletedQuery(filter, window).Select(item => item.TransportId).ToListAsync(cancellationToken);
        var nullableIds = completedIds.Select(id => (Guid?)id).ToList();
        var incidents = Incidents(filter, window);
        var incidentIds = incidents.Select(item => item.IncidentReportId);
        var cost = await db.EmergencyCostRequests.AsNoTracking().Where(item => item.Status == EmergencyCostStatus.APPROVED && incidentIds.Contains(item.IncidentReportId))
            .SumAsync(item => item.ApprovedAmount, cancellationToken) ?? 0;
        return new(completedIds.Count, await incidents.CountAsync(cancellationToken),
            await Incidents(filter, null).Where(item => nullableIds.Contains(item.TransportId)).Select(item => item.TransportId).Distinct().CountAsync(cancellationToken),
            await Claims(filter, window).CountAsync(cancellationToken),
            await Claims(filter, null).Where(item => completedIds.Contains(item.HandoverRecord.TransportId)).Select(item => item.HandoverRecord.TransportId).Distinct().CountAsync(cancellationToken),
            Vnd.ToLong(cost));
    }

    /// <summary>The whole report with every table row; the caller pages the rows or exports them all.</summary>
    private async Task<Response.IncidentClaimReport> BuildIncidentClaimAsync(Request.IncidentClaimRequest request, CancellationToken cancellationToken)
    {
        var window = WindowOf(request);
        var now = await FiguresAsync(request, window, cancellationToken);
        var before = await FiguresAsync(request, window.Previous, cancellationToken);
        var hasBefore = before.HasData;
        var kpi = new Response.IncidentClaimKpi(
            CountKpi(now.Completed, before.Completed, hasBefore), CountKpi(now.Incidents, before.Incidents, hasBefore),
            RateKpi(Percent(now.TripsWithIncident, now.Completed), Percent(before.TripsWithIncident, before.Completed)),
            CountKpi(now.Claims, before.Claims, hasBefore),
            RateKpi(Percent(now.TripsWithClaim, now.Completed), Percent(before.TripsWithClaim, before.Completed)),
            CountKpi(now.EmergencyCost, before.EmergencyCost, hasBefore), Vnd.Currency);

        var incidents = await Incidents(request, window).Select(item => new { item.IncidentReportId, item.IncidentCode, item.OccurredAt, item.Category, item.Severity, item.Status, item.TransportId })
            .ToListAsync(cancellationToken);
        var incidentIds = incidents.Select(item => item.IncidentReportId).ToArray();
        var transportIds = incidents.Where(item => item.TransportId != null).Select(item => item.TransportId!.Value).Distinct().ToArray();
        var vehicles = await db.VehicleIncidentDetails.AsNoTracking().Where(item => incidentIds.Contains(item.IncidentReportId))
            .Select(item => new { item.IncidentReportId, item.VehicleId, item.Vehicle.VehicleCode, item.Vehicle.LicensePlate, item.IncidentType, item.RepairRequired }).ToListAsync(cancellationToken);
        var horses = await db.HorseHealthIncidentDetails.AsNoTracking().Where(item => incidentIds.Contains(item.IncidentReportId))
            .Select(item => new { item.IncidentReportId, item.IncidentType, item.CreatedAt }).ToListAsync(cancellationToken);
        var costs = await db.EmergencyCostRequests.AsNoTracking().Where(item => item.Status == EmergencyCostStatus.APPROVED && incidentIds.Contains(item.IncidentReportId))
            .Select(item => new { item.IncidentReportId, item.Category, Amount = item.ApprovedAmount ?? 0 }).ToListAsync(cancellationToken);
        var codes = await db.Transports.AsNoTracking().Where(item => transportIds.Contains(item.TransportId))
            .Select(item => new { item.TransportId, item.TransportCode }).ToDictionaryAsync(item => item.TransportId, item => item.TransportCode, cancellationToken);
        var claimCodes = (await Claims(request, null).Where(item => transportIds.Contains(item.HandoverRecord.TransportId))
            .Select(item => new { item.HandoverRecord.TransportId, item.ClaimCode, item.SubmittedAt }).ToListAsync(cancellationToken))
            .GroupBy(item => item.TransportId).ToDictionary(group => group.Key, group => group.OrderBy(item => item.SubmittedAt).ThenBy(item => item.ClaimCode, StringComparer.Ordinal).Select(item => item.ClaimCode).ToList());
        var claims = await Claims(request, window).Select(item => new { item.ClaimType, item.Status }).ToListAsync(cancellationToken);

        // BOTH incidents carry a vehicle and a horse detail, so each distinct type counts once per incident.
        var types = vehicles.Select(item => (item.IncidentReportId, Type: item.IncidentType.ToString()))
            .Concat(horses.Select(item => (item.IncidentReportId, Type: item.IncidentType.ToString()))).Distinct()
            .GroupBy(item => item.Type).Select(group => new Response.TypeCount(group.Key, group.Count()))
            .OrderByDescending(item => item.Count).ThenBy(item => item.IncidentType, StringComparer.Ordinal).ToList();

        var sorted = request.SortBy.Trim().Equals("occurredAt:asc", StringComparison.OrdinalIgnoreCase)
            ? incidents.OrderBy(item => item.OccurredAt) : incidents.OrderByDescending(item => item.OccurredAt);
        var items = sorted.ThenBy(item => item.IncidentCode, StringComparer.Ordinal).Select(item => new Response.IncidentItem(item.IncidentReportId, item.IncidentCode,
            AsUtc(item.OccurredAt), item.Category.ToString(),
            vehicles.FirstOrDefault(detail => detail.IncidentReportId == item.IncidentReportId)?.IncidentType.ToString()
                ?? horses.Where(detail => detail.IncidentReportId == item.IncidentReportId).OrderBy(detail => detail.CreatedAt).FirstOrDefault()?.IncidentType.ToString(),
            item.Severity.ToString(), item.Status.ToString(), item.TransportId is { } id && codes.TryGetValue(id, out var code) ? code : null,
            Vnd.ToLong(costs.Where(cost => cost.IncidentReportId == item.IncidentReportId).Sum(cost => cost.Amount)),
            item.TransportId is { } linked && claimCodes.TryGetValue(linked, out var list) ? list : [])).ToList();

        return new(PeriodOf(window), clock.GetUtcNow(), kpi,
            Enum.GetValues<IncidentCategory>().Select(category => new Response.CategoryCount(category.ToString(), incidents.Count(item => item.Category == category))).ToList(),
            Enum.GetValues<IncidentSeverity>().ToDictionary(severity => severity.ToString(), severity => incidents.Count(item => item.Severity == severity)),
            types,
            claims.GroupBy(item => item.ClaimType).Select(group => new Response.ClaimTypeCount(group.Key.ToString(), group.Count()))
                .OrderByDescending(item => item.Count).ThenBy(item => item.ClaimType, StringComparer.Ordinal).ToList(),
            // Cancelled claims never count (R25), so that key stays at 0; it is kept so the screen has all five columns.
            Enum.GetValues<ClaimStatus>().ToDictionary(status => status.ToString(), status => claims.Count(item => item.Status == status)),
            costs.GroupBy(item => item.Category).Select(group => new Response.CostByCategory(group.Key.ToString(), Vnd.ToLong(group.Sum(item => item.Amount))))
                .Where(item => item.Amount > 0).OrderByDescending(item => item.Amount).ThenBy(item => item.Category, StringComparer.Ordinal).ToList(),
            vehicles.GroupBy(item => (item.VehicleId, item.VehicleCode, item.LicensePlate))
                .Select(group => new Response.VehicleReliability(group.Key.VehicleId, group.Key.VehicleCode, group.Key.LicensePlate, group.Count(), group.Count(item => item.RepairRequired)))
                .OrderByDescending(item => item.IncidentCount).ThenBy(item => item.VehicleCode, StringComparer.Ordinal).ToList(),
            items, 1, Math.Max(items.Count, 1), items.Count, 1);
    }
}
