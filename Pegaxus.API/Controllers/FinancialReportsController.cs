using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pegaxus.API.Extensions;
using Pegaxus.Service.Exceptions;

using FinancialReports = Pegaxus.Service.FinancialReportService;

namespace Pegaxus.API.Controllers;

[ApiController]
[Authorize(Policy = JwtExtensions.LogisticsManagerPolicy)]
[Route("api/v1/transports/{transportId:guid}/financial-report")]
public sealed class FinancialReportsController(FinancialReports.IFinancialReportService service) : ApiControllerBase
{
    /// <summary>Get the income and cost summary of a transport.</summary>
    /// <remarks>Only Logistics Managers. A COMPLETED transport returns the saved report (created on first view when missing); isStale tells that an invoice, receipt or emergency cost changed since it was scanned, so refresh it. A transport that is not finished returns 409 TRANSPORT_NOT_COMPLETED unless allowProvisional=true, which returns figures worked out now and saves nothing (isProvisional=true). Every call also recalculates the reconciliation warnings and the suggested invoice lines, which are only a suggestion.</remarks>
    /// <param name="transportId">The transport identifier (UUID).</param>
    /// <param name="allowProvisional">Allow a provisional summary of an unfinished transport. Default false.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the financial report.</returns>
    /// <exception cref="ForbiddenException">The caller is not an active Logistics Manager.</exception>
    /// <exception cref="NotFoundException">The transport does not exist.</exception>
    /// <exception cref="ConflictException">The transport is cancelled, or not completed and allowProvisional is false.</exception>
    [HttpGet]
    public async Task<IActionResult> Get(Guid transportId, [FromQuery] bool allowProvisional = false, CancellationToken cancellationToken = default) =>
        Success(await service.GetAsync(AccountId, transportId, allowProvisional, cancellationToken), "Financial report retrieved successfully");

    /// <summary>Scan the invoices, receipts and emergency costs again.</summary>
    /// <remarks>Only Logistics Managers, only for a COMPLETED transport. Overwrites the four amounts and generatedAt; the report code and the summary note are kept. Creates the report when it does not exist yet.</remarks>
    /// <param name="transportId">The transport identifier (UUID).</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the refreshed financial report.</returns>
    /// <exception cref="ForbiddenException">The caller is not an active Logistics Manager.</exception>
    /// <exception cref="NotFoundException">The transport does not exist.</exception>
    /// <exception cref="ConflictException">The transport is cancelled or not completed, or the report changed at the same time.</exception>
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(Guid transportId, CancellationToken cancellationToken) =>
        Success(await service.RefreshAsync(AccountId, transportId, cancellationToken), "Financial report refreshed successfully");

    /// <summary>Save the evaluation note of the financial report.</summary>
    /// <remarks>Only Logistics Managers. Changes only the note (up to 2000 characters, empty clears it) and updatedAt; no amount is touched. Send the latest updatedAt you hold.</remarks>
    /// <param name="transportId">The transport identifier (UUID).</param>
    /// <param name="request">The latest report timestamp and the note.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the financial report and the new note.</returns>
    /// <exception cref="BadRequestException">The timestamp is missing or the note is longer than 2000 characters.</exception>
    /// <exception cref="ForbiddenException">The caller is not an active Logistics Manager.</exception>
    /// <exception cref="NotFoundException">The transport or its report does not exist; open the summary first.</exception>
    /// <exception cref="ConflictException">The report was modified after the timestamp the caller holds.</exception>
    [HttpPatch]
    public async Task<IActionResult> UpdateNote(Guid transportId, [FromBody] FinancialReports.Request.UpdateNoteRequest request, CancellationToken cancellationToken) =>
        Success(await service.UpdateNoteAsync(AccountId, transportId, request, cancellationToken), "Financial report note saved successfully");
}
