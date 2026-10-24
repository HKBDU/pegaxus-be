using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;

using static Pegaxus.Service.ReportService.ReportFiles;

namespace Pegaxus.Service.ReportService;

public sealed partial class Service
{
    private const int MaxExportRows = 10_000;

    public async Task<Response.ExportFile> ExportAsync(Guid accountId, string? reportType, Request.ExportRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureManagerAsync(accountId, cancellationToken);
        var type = reportType?.Trim().ToLowerInvariant();
        if (type is not ("on-time-delivery" or "incident-claim-frequency" or "transit-time"))
            throw new FieldValidationException([new("reportType", "reportType must be on-time-delivery, incident-claim-frequency or transit-time.")]);
        Validation.ValidateFields(request, ExportValidator);

        // The report is built exactly as RA1 to RA3 build it, so the file never disagrees with the screen.
        ReportDocument document; int rows; bool hasData;
        switch (type)
        {
            case "on-time-delivery":
            {
                var typed = Fill(new Request.OnTimeRequest { DriverEmployeeId = request.DriverEmployeeId, Bucket = request.Bucket, TrendBy = request.TrendBy ?? "WEEK" }, request);
                if (request.SortBy is not null) typed.SortBy = request.SortBy;
                Validation.ValidateFields(typed, OnTimeValidator);
                var report = await BuildOnTimeAsync(typed, cancellationToken);
                (document, rows, hasData) = (OnTimeDocument(report), report.Items.Count, report.Kpi.TotalTransports.Value > 0 || report.Excluded.Count > 0);
                break;
            }
            case "incident-claim-frequency":
            {
                var typed = Fill(new Request.IncidentClaimRequest { IncidentCategory = request.IncidentCategory, Severity = request.Severity, VehicleId = request.VehicleId,
                    HorseId = request.HorseId, ClaimType = request.ClaimType }, request);
                if (request.SortBy is not null) typed.SortBy = request.SortBy;
                Validation.ValidateFields(typed, IncidentClaimValidator);
                var report = await BuildIncidentClaimAsync(typed, cancellationToken);
                (document, rows, hasData) = (IncidentClaimDocument(report), report.Items.Count,
                    report.Kpi.CompletedTransports.Value > 0 || report.Kpi.TotalIncidents.Value > 0 || report.Kpi.TotalClaims.Value > 0);
                break;
            }
            default:
            {
                var typed = Fill(new Request.TransitTimeRequest { DelayFilter = request.DelayFilter }, request);
                if (request.SortBy is not null) typed.SortBy = request.SortBy;
                Validation.ValidateFields(typed, TransitTimeValidator);
                var report = await BuildTransitTimeAsync(typed, cancellationToken);
                (document, rows, hasData) = (TransitDocument(report), report.Items.Count, report.Kpi.TotalTransports.Value > 0 || report.Excluded.Count > 0);
                break;
            }
        }
        if (!hasData) throw new UnprocessableEntityException("There is no data to export for the selected filters", "NO_DATA_TO_EXPORT");
        if (rows > MaxExportRows) throw new UnprocessableEntityException("More than 10,000 rows match. Narrow the filters and try again", "EXPORT_TOO_LARGE");

        var pdf = request.Format!.Trim().Equals("pdf", StringComparison.OrdinalIgnoreCase);
        return new(pdf ? Pdf(document) : Xlsx(document), pdf ? PdfType : XlsxType,
            $"{type}-{request.DateFrom:yyyy-MM-dd}_{request.DateTo:yyyy-MM-dd}.{(pdf ? "pdf" : "xlsx")}");
    }

    private static T Fill<T>(T typed, Request.ExportRequest request) where T : Request.PagedFilter
    {
        typed.DateFrom = request.DateFrom; typed.DateTo = request.DateTo; typed.OriginCountry = request.OriginCountry; typed.DestinationCountry = request.DestinationCountry;
        typed.RouteId = request.RouteId; typed.TransportProviderId = request.TransportProviderId;
        return typed;
    }

    // ------------------------------------------------------------------ documents

    private static string Subtitle(Response.Period period, DateTimeOffset generatedAt) =>
        $"Period {period.DateFrom:yyyy-MM-dd} to {period.DateTo:yyyy-MM-dd} (previous {period.PreviousDateFrom:yyyy-MM-dd} to {period.PreviousDateTo:yyyy-MM-dd}) - generated {Show(generatedAt)}";

    private static object?[] Row<T>(string name, Response.Kpi<T> kpi) where T : struct => [name, kpi.Value, kpi.Previous, kpi.Change];

    private static readonly string[] KpiHeaders = ["Metric", "Value", "Previous", "Change"];

    private static ReportDocument OnTimeDocument(Response.OnTimeReport report) => new("On-time delivery report", Subtitle(report.Period, report.GeneratedAt),
    [
        new("KPI", KpiHeaders,
        [
            Row("Transports", report.Kpi.TotalTransports), Row("On-time rate (%)", report.Kpi.OnTimeRate), Row("Average delay (minutes)", report.Kpi.AvgDelayMinutes),
            ["SLA target (%)", report.Kpi.SlaTargetRate, null, null], ["Meets SLA", report.Kpi.MeetsSla, null, null]
        ]),
        new("Distribution", ["Bucket", "Transports"], report.Distribution.Select(pair => new object?[] { pair.Key, pair.Value }).ToList()),
        new("Trend", ["Period", "From", "To", "Transports", "On-time rate (%)"],
            report.Trend.Select(item => new object?[] { item.Label, item.From.ToString("yyyy-MM-dd"), item.To.ToString("yyyy-MM-dd"), item.Total, item.OnTimeRate }).ToList()),
        new("By provider", ["Provider", "Transports", "On-time rate (%)"], report.ByProvider.Select(item => new object?[] { item.ProviderName, item.Total, item.OnTimeRate }).ToList()),
        new("Delay causes", ["Cause", "Transports"], report.DelayCauses.Select(item => new object?[] { item.Cause, item.Count }).ToList()),
        new("Excluded transports", ["Reason", "Transports"], report.Excluded.Reasons.Select(pair => new object?[] { pair.Key, pair.Value }).ToList()),
        new("Transports", ["Transport", "Booking", "Origin", "Destination", "Planned end", "Actual arrival", "Delay (minutes)", "Bucket", "Main cause"],
            report.Items.Select(item => new object?[] { item.TransportCode, item.BookingCode, item.OriginCountry, item.DestinationCountry, Show(item.PlannedEndAt),
                Show(item.ActualArrivalAt), item.DelayMinutes, item.Bucket, item.MainCause }).ToList())
    ]);

    private static ReportDocument IncidentClaimDocument(Response.IncidentClaimReport report) => new("Incident and claim frequency report", Subtitle(report.Period, report.GeneratedAt),
    [
        new("KPI", KpiHeaders,
        [
            Row("Completed transports", report.Kpi.CompletedTransports), Row("Incidents", report.Kpi.TotalIncidents), Row("Incident rate (%)", report.Kpi.IncidentRate),
            Row("Claims", report.Kpi.TotalClaims), Row("Claim rate (%)", report.Kpi.ClaimRate), Row($"Emergency cost ({report.Kpi.Currency})", report.Kpi.TotalEmergencyCost)
        ]),
        new("Incidents by category", ["Category", "Incidents"], report.IncidentsByCategory.Select(item => new object?[] { item.Category, item.Count }).ToList()),
        new("Incidents by severity", ["Severity", "Incidents"], report.IncidentsBySeverity.Select(pair => new object?[] { pair.Key, pair.Value }).ToList()),
        new("Incidents by type", ["Type", "Incidents"], report.IncidentsByType.Select(item => new object?[] { item.IncidentType, item.Count }).ToList()),
        new("Claims by type", ["Type", "Claims"], report.ClaimsByType.Select(item => new object?[] { item.ClaimType, item.Count }).ToList()),
        new("Claims by status", ["Status", "Claims"], report.ClaimsByStatus.Select(pair => new object?[] { pair.Key, pair.Value }).ToList()),
        new($"Emergency cost by category ({report.Kpi.Currency})", ["Category", "Amount"], report.EmergencyCostByCategory.Select(item => new object?[] { item.Category, item.Amount }).ToList()),
        new("Vehicle reliability", ["Vehicle", "License plate", "Incidents", "Repair required"],
            report.VehicleReliability.Select(item => new object?[] { item.VehicleCode, item.LicensePlate, item.IncidentCount, item.RepairRequiredCount }).ToList()),
        new("Incidents", ["Incident", "Occurred at", "Category", "Type", "Severity", "Status", "Transport", $"Emergency cost ({report.Kpi.Currency})", "Claims"],
            report.Items.Select(item => new object?[] { item.IncidentCode, Show(item.OccurredAt), item.Category, item.IncidentType, item.Severity, item.Status, item.TransportCode,
                item.EmergencyCost, string.Join(", ", item.ClaimCodes) }).ToList())
    ]);

    private static ReportDocument TransitDocument(Response.TransitTimeReport report) => new("Transit time report", Subtitle(report.Period, report.GeneratedAt),
    [
        new("KPI", KpiHeaders,
        [
            Row("Transports", report.Kpi.TotalTransports), Row("Average actual days", report.Kpi.AvgActualDays), Row("Average delay (days)", report.Kpi.AvgDelayDays),
            Row("Accuracy ratio (%)", report.Kpi.AccuracyRatio)
        ]),
        new("Delay against the estimate (days)", ["Range", "Transports"], report.Distribution.Select(item => new object?[] { item.Range, item.Count }).ToList()),
        new("Route benchmark", ["Origin", "Destination", "Transports", "Average actual days", "Average estimated days", "Average delta days"],
            report.RouteBenchmark.Select(item => new object?[] { item.OriginCountry, item.DestinationCountry, item.Total, item.AvgActualDays, item.AvgEstimatedDays, item.AvgDeltaDays }).ToList()),
        new("Excluded transports", ["Reason", "Transports"], report.Excluded.Reasons.Select(pair => new object?[] { pair.Key, pair.Value }).ToList()),
        new("Transports", ["Transport", "Origin", "Destination", "Departed", "Arrived", "Estimated days", "Actual days", "Delta days"],
            report.Items.Select(item => new object?[] { item.TransportCode, item.OriginCountry, item.DestinationCountry, Show(item.ActualDepartureAt), Show(item.ActualArrivalAt),
                item.EstimatedDays, item.ActualDays, item.DeltaDays }).ToList())
    ]);
}
