using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pegaxus.API.Extensions;
using Pegaxus.Service.Exceptions;

using Reports = Pegaxus.Service.ReportService;

namespace Pegaxus.API.Controllers;

[ApiController]
[Authorize(Policy = JwtExtensions.LogisticsManagerPolicy)]
[Route("api/v1/reports")]
public sealed class ReportsController(Reports.IReportService service) : ApiControllerBase
{
    /// <summary>On-time delivery report: how many completed trips arrived within 60 minutes of the planned end.</summary>
    /// <remarks>Only Logistics Managers. Read only: nothing is saved. The base set is the COMPLETED transports whose completedAt falls in dateFrom..dateTo (UTC days, both included, at most 366 days). Every KPI carries the same figure of the period right before, of the same length. A trip with no planned end, or with no arrival time (transport arrival, else the signed DELIVERY handover), is left out and counted in excluded. The bucket filter only narrows the table of trips; items lists the late trips unless bucket is given. The delay cause is inferred from the trip's worst incident.</remarks>
    /// <param name="request">The date range, the optional filters, the trend grouping, the table bucket, sort and page.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the KPIs, distribution, trend, per-provider rates, delay causes and the table of trips.</returns>
    /// <exception cref="BadRequestException">A date is missing or wrong, the range is longer than 366 days, or a filter value is not allowed.</exception>
    /// <exception cref="ForbiddenException">The caller is not an active Logistics Manager.</exception>
    [HttpGet("on-time-delivery")]
    public async Task<IActionResult> OnTimeDelivery([FromQuery] Reports.Request.OnTimeRequest request, CancellationToken cancellationToken) =>
        Success(await service.GetOnTimeAsync(AccountId, request, cancellationToken), "On-time delivery report retrieved successfully");

    /// <summary>Incident and claim frequency report.</summary>
    /// <remarks>Only Logistics Managers. Read only. Incident and claim rates are the share of the COMPLETED transports of the period that had at least one live incident or claim. Incidents are counted by occurredAt and claims by submittedAt, both without the cancelled ones. Emergency cost is the approved amount in VND. The category, severity, vehicle, horse and claim type filters narrow the incidents and claims; the country, route and provider filters narrow the trips they belong to.</remarks>
    /// <param name="request">The date range, the optional filters, sort and page.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the KPIs, the breakdowns, the vehicle ranking and the table of incidents.</returns>
    /// <exception cref="BadRequestException">A date is missing or wrong, the range is longer than 366 days, or a filter value is not allowed.</exception>
    /// <exception cref="ForbiddenException">The caller is not an active Logistics Manager.</exception>
    [HttpGet("incident-claim-frequency")]
    public async Task<IActionResult> IncidentClaimFrequency([FromQuery] Reports.Request.IncidentClaimRequest request, CancellationToken cancellationToken) =>
        Success(await service.GetIncidentClaimAsync(AccountId, request, cancellationToken), "Incident and claim frequency report retrieved successfully");

    /// <summary>Transit time report: real travel time against the estimate.</summary>
    /// <remarks>Only Logistics Managers. Read only. Real time is arrival minus departure of the transport; the estimate is the primary route plan's, else the transport plan's. A trip missing its departure, its arrival or an estimate is left out and counted in excluded. The delay filter only narrows the table of trips.</remarks>
    /// <param name="request">The date range, the optional filters, the delay filter, sort and page.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the KPIs, the delay distribution, the route benchmark and the table of trips.</returns>
    /// <exception cref="BadRequestException">A date is missing or wrong, the range is longer than 366 days, or a filter value is not allowed.</exception>
    /// <exception cref="ForbiddenException">The caller is not an active Logistics Manager.</exception>
    [HttpGet("transit-time")]
    public async Task<IActionResult> TransitTime([FromQuery] Reports.Request.TransitTimeRequest request, CancellationToken cancellationToken) =>
        Success(await service.GetTransitTimeAsync(AccountId, request, cancellationToken), "Transit time report retrieved successfully");

    /// <summary>Export one of the reports as an XLSX or PDF file.</summary>
    /// <remarks>Only Logistics Managers. reportType is on-time-delivery, incident-claim-frequency or transit-time. It takes the filters of that report (without paging) and builds the file from exactly the same data, with every matching row, up to 10,000. The answer is the file itself, not the JSON envelope.</remarks>
    /// <param name="reportType">on-time-delivery, incident-claim-frequency or transit-time.</param>
    /// <param name="request">The format (xlsx or pdf) and the filters of the chosen report.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the file and its Content-Disposition file name.</returns>
    /// <exception cref="BadRequestException">The report type or format is wrong, a date is missing or wrong, or a filter value is not allowed.</exception>
    /// <exception cref="ForbiddenException">The caller is not an active Logistics Manager.</exception>
    /// <exception cref="UnprocessableEntityException">NO_DATA_TO_EXPORT when nothing matches, EXPORT_TOO_LARGE when more than 10,000 rows match.</exception>
    [HttpGet("{reportType}/export")]
    public async Task<IActionResult> Export(string reportType, [FromQuery] Reports.Request.ExportRequest request, CancellationToken cancellationToken)
    {
        var file = await service.ExportAsync(AccountId, reportType, request, cancellationToken);
        return File(file.Content, file.ContentType, file.FileName);
    }
}
