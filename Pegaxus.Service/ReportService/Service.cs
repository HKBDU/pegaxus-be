using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.ReportService.Validator;

using static Pegaxus.Service.Common.DbTime;

namespace Pegaxus.Service.ReportService;

/// <summary>
/// Management reports (R25). They only read: every report loads the completed transports of the period, then works the figures out in memory.
/// shortcut: the whole period is held in memory (capped by the 366-day range); upgrade to SQL aggregation if one period ever holds tens of thousands of trips.
/// </summary>
public sealed partial class Service(AppDbContext db, TimeProvider clock) : IReportService
{
    /// <summary>O-F6-16: the share of trips that must arrive on time for the SLA to be met.</summary>
    private const int SlaTargetRate = 92;

    private static readonly OnTimeRequestValidator OnTimeValidator = new();
    private static readonly IncidentClaimRequestValidator IncidentClaimValidator = new();
    private static readonly TransitTimeRequestValidator TransitTimeValidator = new();
    private static readonly ExportRequestValidator ExportValidator = new();

    /// <summary>A whole-day UTC window; the stored times are UTC without a zone, so the bounds are plain dates.</summary>
    private sealed record Window(DateOnly From, DateOnly To)
    {
        public DateTime Start => From.ToDateTime(TimeOnly.MinValue);
        public DateTime Until => To.AddDays(1).ToDateTime(TimeOnly.MinValue);
        public int Days => To.DayNumber - From.DayNumber + 1;
        /// <summary>The window right before this one, of the same length.</summary>
        public Window Previous => new(From.AddDays(-Days), From.AddDays(-1));
    }

    private static Window WindowOf(Request.ReportFilter filter) => new(filter.DateFrom!.Value, filter.DateTo!.Value);

    private static Response.Period PeriodOf(Window window) => new(window.From, window.To, window.Previous.From, window.Previous.To);

    /// <summary>Report figures are for Logistics Managers only.</summary>
    private async Task EnsureManagerAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var allowed = await db.Accounts.AsNoTracking().AnyAsync(account => account.AccountId == accountId && !account.IsDeleted && account.Status == AccountStatus.ACTIVE &&
            account.AccountType == AccountType.EMPLOYEE && account.EmployeeAccount != null && !account.EmployeeAccount.IsDeleted && account.EmployeeAccount.IsActive &&
            account.EmployeeAccount.Role == EmployeeRole.LOGISTICS_MANAGER, cancellationToken);
        if (!allowed) throw new ForbiddenException("Only Logistics Manager can view management reports.");
    }

    private static string[] SplitCsv(string? csv) =>
        string.IsNullOrWhiteSpace(csv) ? [] : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static decimal Round1(decimal value) => Math.Round(value, 1, MidpointRounding.AwayFromZero);

    private static decimal? Percent(int part, int whole) => whole == 0 ? null : Round1(part * 100m / whole);

    private static decimal? Average(IReadOnlyCollection<decimal> values) => values.Count == 0 ? null : Round1(values.Sum() / values.Count);

    private static string[] Sort(string sortBy) => sortBy.Trim().ToLowerInvariant().Split(':');

    /// <summary>A count; its change is null when the period before holds no data at all.</summary>
    private static Response.Kpi<long> CountKpi(long value, long previous, bool previousHasData) => new(value, previous, previousHasData ? value - previous : null);

    /// <summary>A rate or an average; null on either side means that period had nothing to measure.</summary>
    private static Response.Kpi<decimal> RateKpi(decimal? value, decimal? previous) =>
        new(value, previous, value is { } current && previous is { } before ? Round1(current - before) : null);

    // -------------------------------------------------------------- transports

    /// <summary>Transports that match the geography, route and provider filters, whatever their status or date.</summary>
    private IQueryable<Transport> Transports(Request.ReportFilter filter, Guid? driverEmployeeId = null)
    {
        var query = db.Transports.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(filter.OriginCountry))
        {
            var origin = filter.OriginCountry.Trim().ToLower();
            query = query.Where(item => item.Booking.OriginCountry.ToLower() == origin);
        }
        if (!string.IsNullOrWhiteSpace(filter.DestinationCountry))
        {
            var destination = filter.DestinationCountry.Trim().ToLower();
            query = query.Where(item => item.Booking.DestinationCountry.ToLower() == destination);
        }
        // The route of a trip is the route of its primary route plan; a provider or driver counts when any live segment names them.
        if (filter.RouteId is { } routeId)
            query = query.Where(item => item.TransportPlans.Any(plan => plan.RoutePlans.Any(route => route.PrimaryRoutePlanId == null &&
                route.Status != RoutePlanStatus.CANCELLED && route.RoutePlanRoutes.Any(segment => segment.RouteId == routeId))));
        if (filter.TransportProviderId is { } providerId)
            query = query.Where(item => item.TransportPlans.Any(plan => plan.RoutePlans.Any(route => route.Status != RoutePlanStatus.CANCELLED &&
                route.RoutePlanRoutes.Any(segment => segment.TransportProviderId == providerId))));
        if (driverEmployeeId is { } driverId)
            query = query.Where(item => item.TransportPlans.Any(plan => plan.RoutePlans.Any(route => route.Status != RoutePlanStatus.CANCELLED &&
                route.RoutePlanRoutes.Any(segment => segment.DriverEmployeeId == driverId))));
        return query;
    }

    private static bool HasTransportFilter(Request.ReportFilter filter) => !string.IsNullOrWhiteSpace(filter.OriginCountry) ||
        !string.IsNullOrWhiteSpace(filter.DestinationCountry) || filter.RouteId.HasValue || filter.TransportProviderId.HasValue;

    private sealed record TransportRow(Guid TransportId, string TransportCode, string BookingCode, string OriginCountry, string DestinationCountry,
        DateTime? CompletedAt, DateTime? ActualDepartureAt, DateTime? ActualArrivalAt);

    /// <summary>The base set of every report: COMPLETED transports whose completed_at falls in the window.</summary>
    private IQueryable<Transport> CompletedQuery(Request.ReportFilter filter, Window window, Guid? driverEmployeeId = null)
    {
        var start = window.Start; var until = window.Until;
        return Transports(filter, driverEmployeeId).Where(item => item.Status == TransportStatus.COMPLETED && item.CompletedAt >= start && item.CompletedAt < until);
    }

    private Task<List<TransportRow>> CompletedAsync(Request.ReportFilter filter, Window window, Guid? driverEmployeeId, CancellationToken cancellationToken) =>
        CompletedQuery(filter, window, driverEmployeeId).OrderBy(item => item.CompletedAt).ThenBy(item => item.TransportId)
            .Select(item => new TransportRow(item.TransportId, item.TransportCode, item.Booking.BookingCode, item.Booking.OriginCountry, item.Booking.DestinationCountry,
                item.CompletedAt, item.ActualDepartureAt, item.ActualArrivalAt)).ToListAsync(cancellationToken);

    /// <summary>When the DELIVERY handover of each transport was signed; the fallback arrival time when the transport has none.</summary>
    private async Task<Dictionary<Guid, DateTime>> DeliveriesAsync(Guid[] transportIds, CancellationToken cancellationToken)
    {
        var rows = await db.HandoverRecords.AsNoTracking().Where(item => transportIds.Contains(item.TransportId) && item.HandoverType == HandoverType.DELIVERY &&
                item.Status == HandoverStatus.COMPLETED && item.CompletedAt != null)
            .Select(item => new { item.TransportId, At = item.CompletedAt!.Value }).ToListAsync(cancellationToken);
        return rows.GroupBy(item => item.TransportId).ToDictionary(group => group.Key, group => group.Max(item => item.At));
    }

    private sealed record PlanFigures(DateTime? PlannedEndAt, int? EstimatedMinutes);

    /// <summary>
    /// The executed transport plan of each transport (latest that was approved or later) with its planned end and its estimated duration:
    /// the primary route plan's estimate first, then the transport plan's own.
    /// </summary>
    private async Task<Dictionary<Guid, PlanFigures>> PlansAsync(Guid[] transportIds, CancellationToken cancellationToken)
    {
        TransportPlanStatus[] executed = [TransportPlanStatus.APPROVED, TransportPlanStatus.IN_PROGRESS, TransportPlanStatus.COMPLETED];
        var plans = await db.TransportPlans.AsNoTracking().Where(item => transportIds.Contains(item.TransportId) && executed.Contains(item.Status))
            .Select(item => new { item.TransportId, item.TransportPlanId, item.PlannedEndAt, item.EstimatedDurationMinutes, item.CreatedAt }).ToListAsync(cancellationToken);
        var latest = plans.GroupBy(item => item.TransportId)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(item => item.CreatedAt).ThenBy(item => item.TransportPlanId).First());
        var planIds = latest.Values.Select(item => item.TransportPlanId).ToArray();
        var primaries = await db.RoutePlans.AsNoTracking().Where(item => planIds.Contains(item.TransportPlanId) && item.PrimaryRoutePlanId == null &&
                item.Status != RoutePlanStatus.CANCELLED && item.EstimatedDurationMinutes > 0)
            .Select(item => new { item.TransportPlanId, Minutes = item.EstimatedDurationMinutes!.Value, item.CreatedAt }).ToListAsync(cancellationToken);
        var primaryMinutes = primaries.GroupBy(item => item.TransportPlanId)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(item => item.CreatedAt).First().Minutes);
        return latest.ToDictionary(pair => pair.Key, pair => new PlanFigures(pair.Value.PlannedEndAt,
            primaryMinutes.TryGetValue(pair.Value.TransportPlanId, out var minutes) ? minutes
            : pair.Value.EstimatedDurationMinutes > 0 ? pair.Value.EstimatedDurationMinutes : null));
    }
}
