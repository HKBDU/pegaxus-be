namespace Pegaxus.Service.ReportService;

public static class Response
{
    public sealed record Period(DateOnly DateFrom, DateOnly DateTo, DateOnly PreviousDateFrom, DateOnly PreviousDateTo);

    /// <summary>A figure with the same figure of the period right before; change is null when either side has no data.</summary>
    public sealed record Kpi<T>(T? Value, T? Previous, T? Change) where T : struct;

    public sealed record Excluded(int Count, IReadOnlyDictionary<string, int> Reasons);

    // ------------------------------------------------------------------ RA1

    public sealed record OnTimeKpi(Kpi<long> TotalTransports, Kpi<decimal> OnTimeRate, Kpi<decimal> AvgDelayMinutes, int SlaTargetRate, bool MeetsSla);
    public sealed record TrendPoint(string Label, DateOnly From, DateOnly To, int Total, decimal? OnTimeRate);
    public sealed record ProviderPoint(Guid? TransportProviderId, string ProviderName, int Total, decimal? OnTimeRate);
    public sealed record DelayCause(string Cause, int Count);
    public sealed record OnTimeItem(Guid TransportId, string TransportCode, string BookingCode, string OriginCountry, string DestinationCountry,
        DateTimeOffset PlannedEndAt, DateTimeOffset ActualArrivalAt, int DelayMinutes, string Bucket, string? MainCause);

    public sealed record OnTimeReport(Period Period, DateTimeOffset GeneratedAt, OnTimeKpi Kpi, IReadOnlyDictionary<string, int> Distribution,
        IReadOnlyList<TrendPoint> Trend, IReadOnlyList<ProviderPoint> ByProvider, IReadOnlyList<DelayCause> DelayCauses, Excluded Excluded,
        IReadOnlyList<OnTimeItem> Items, int PageIndex, int PageSize, int TotalItems, int TotalPages);

    // ------------------------------------------------------------------ RA2

    public sealed record IncidentClaimKpi(Kpi<long> CompletedTransports, Kpi<long> TotalIncidents, Kpi<decimal> IncidentRate, Kpi<long> TotalClaims,
        Kpi<decimal> ClaimRate, Kpi<long> TotalEmergencyCost, string Currency);
    public sealed record CategoryCount(string Category, int Count);
    public sealed record TypeCount(string IncidentType, int Count);
    public sealed record ClaimTypeCount(string ClaimType, int Count);
    public sealed record CostByCategory(string Category, long Amount);
    public sealed record VehicleReliability(Guid VehicleId, string VehicleCode, string? LicensePlate, int IncidentCount, int RepairRequiredCount);
    public sealed record IncidentItem(Guid IncidentReportId, string IncidentCode, DateTimeOffset OccurredAt, string Category, string? IncidentType, string Severity,
        string Status, string? TransportCode, long EmergencyCost, IReadOnlyList<string> ClaimCodes);

    public sealed record IncidentClaimReport(Period Period, DateTimeOffset GeneratedAt, IncidentClaimKpi Kpi, IReadOnlyList<CategoryCount> IncidentsByCategory,
        IReadOnlyDictionary<string, int> IncidentsBySeverity, IReadOnlyList<TypeCount> IncidentsByType, IReadOnlyList<ClaimTypeCount> ClaimsByType,
        IReadOnlyDictionary<string, int> ClaimsByStatus, IReadOnlyList<CostByCategory> EmergencyCostByCategory, IReadOnlyList<VehicleReliability> VehicleReliability,
        IReadOnlyList<IncidentItem> Items, int PageIndex, int PageSize, int TotalItems, int TotalPages);

    // ------------------------------------------------------------------ RA3

    public sealed record TransitKpi(Kpi<long> TotalTransports, Kpi<decimal> AvgActualDays, Kpi<decimal> AvgDelayDays, Kpi<decimal> AccuracyRatio);
    public sealed record RangeCount(string Range, int Count);
    public sealed record RouteBenchmark(string OriginCountry, string DestinationCountry, int Total, decimal AvgActualDays, decimal AvgEstimatedDays, decimal AvgDeltaDays);
    public sealed record TransitItem(Guid TransportId, string TransportCode, string OriginCountry, string DestinationCountry, DateTimeOffset ActualDepartureAt,
        DateTimeOffset ActualArrivalAt, decimal EstimatedDays, decimal ActualDays, decimal DeltaDays);

    public sealed record TransitTimeReport(Period Period, DateTimeOffset GeneratedAt, TransitKpi Kpi, IReadOnlyList<RangeCount> Distribution,
        IReadOnlyList<RouteBenchmark> RouteBenchmark, Excluded Excluded, IReadOnlyList<TransitItem> Items, int PageIndex, int PageSize, int TotalItems, int TotalPages);

    // ------------------------------------------------------------------ RA4

    public sealed record ExportFile(byte[] Content, string ContentType, string FileName);
}
