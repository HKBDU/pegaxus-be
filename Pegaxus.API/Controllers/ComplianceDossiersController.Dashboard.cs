using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pegaxus.API.Extensions;
using Pegaxus.Service.Exceptions;

using ComplianceDossierService = Pegaxus.Service.ComplianceDossierService;

namespace Pegaxus.API.Controllers;

public sealed partial class ComplianceDossiersController
{
    /// <summary>
    /// Get the compliance dossier dashboard.
    /// </summary>
    /// <remarks>Transport Specialists and Logistics Managers view dossier KPIs, checklist progress, and risk filters across the system.</remarks>
    /// <param name="request">The dashboard filters, sorting, and pagination parameters.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with dashboard metrics and a paginated dossier list.</returns>
    /// <exception cref="BadRequestException">The request fields or query parameters are invalid.</exception>
    /// <exception cref="ForbiddenException">The caller is not permitted to access the resource or perform this operation.</exception>
    [HttpGet("dashboard")]
    [Authorize(Policy = JwtExtensions.TransportSpecialistOrLogisticsManagerPolicy)]
    public async Task<IActionResult> GetDashboard(
        [FromQuery] ComplianceDossierService.Request.DashboardRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _service.GetDashboardAsync(
            AccountId, request, cancellationToken);

        return Success(response, "Dashboard retrieved successfully");
    }

    /// <summary>
    /// Export the compliance dossier dashboard.
    /// </summary>
    /// <remarks>Transport Specialists and Logistics Managers export all matching results as XLSX or PDF. Returns a binary file without a JSON response envelope.</remarks>
    /// <param name="request">The dashboard filters, sorting, and XLSX or PDF export format.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the generated report file.</returns>
    /// <exception cref="BadRequestException">The request fields or query parameters are invalid.</exception>
    /// <exception cref="ForbiddenException">The caller is not permitted to access the resource or perform this operation.</exception>
    [HttpGet("dashboard/export")]
    [Authorize(Policy = JwtExtensions.TransportSpecialistOrLogisticsManagerPolicy)]
    public async Task<IActionResult> ExportDashboard(
        [FromQuery] ComplianceDossierService.Request.DashboardExportRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _service.ExportDashboardAsync(
            AccountId, request, cancellationToken);

        return File(response.Content, response.ContentType, response.FileName);
    }

    /// <summary>
    /// Get compliance dossier audit history.
    /// </summary>
    /// <remarks>Transport Specialists and Logistics Managers view append-only audit events with action, actor, time, and pagination filters.</remarks>
    /// <param name="dossierId">The compliance dossier identifier (UUID).</param>
    /// <param name="request">The audit action, actor, date range, and pagination filters.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the filtered audit history.</returns>
    /// <exception cref="BadRequestException">The request fields or query parameters are invalid.</exception>
    /// <exception cref="ForbiddenException">The caller is not permitted to access the resource or perform this operation.</exception>
    /// <exception cref="NotFoundException">The requested resource does not exist or is not visible to the caller.</exception>
    [HttpGet("{dossierId:guid}/audit-history")]
    [Authorize(Policy = JwtExtensions.TransportSpecialistOrLogisticsManagerPolicy)]
    public async Task<IActionResult> GetAuditHistory(
        Guid dossierId,
        [FromQuery] ComplianceDossierService.Request.AuditHistoryRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _service.GetAuditHistoryAsync(
            AccountId, dossierId, request, cancellationToken);

        return Success(response, "Audit history retrieved successfully");
    }
}
