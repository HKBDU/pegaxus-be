using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pegaxus.API.Extensions;
using Pegaxus.Service.Exceptions;

using ComplianceDossierService = Pegaxus.Service.ComplianceDossierService;

namespace Pegaxus.API.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/compliance-dossiers")]
public sealed partial class ComplianceDossiersController : ApiControllerBase
{
    private readonly ComplianceDossierService.IService _service;

    public ComplianceDossiersController(ComplianceDossierService.IService service)
    {
        _service = service;
    }

    /// <summary>
    /// Get a compliance dossier by booking.
    /// </summary>
    /// <remarks>Customers may view only their own bookings. If the dossier has not been initialized, returns an overview explaining that initialization is pending.</remarks>
    /// <param name="bookingId">The booking identifier (UUID).</param>
    /// <param name="request">The optional requirement status and horse filters.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the dossier overview or initialization-pending message.</returns>
    /// <exception cref="BadRequestException">The request fields or query parameters are invalid.</exception>
    /// <exception cref="ForbiddenException">The caller is not permitted to access the resource or perform this operation.</exception>
    /// <exception cref="NotFoundException">The requested resource does not exist or is not visible to the caller.</exception>
    [HttpGet("/api/v1/bookings/{bookingId:guid}/compliance-dossier")]
    [Authorize(Policy = JwtExtensions.ComplianceDossierViewerPolicy)]
    public async Task<IActionResult> GetByBooking(Guid bookingId, [FromQuery] ComplianceDossierService.Request.ViewRequest request,
        CancellationToken cancellationToken) =>
        Success(await _service.GetByBookingAsync(AccountId, bookingId, request, cancellationToken), "ComplianceDossier retrieved successfully.");

    /// <summary>
    /// Get compliance dossier details.
    /// </summary>
    /// <remarks>Customers may view only their own dossiers; Transport Specialists and Logistics Managers may view accessible dossiers. SATISFIED and WAIVED requirements count toward completion.</remarks>
    /// <param name="dossierId">The compliance dossier identifier (UUID).</param>
    /// <param name="request">The optional requirement status and horse filters.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the dossier overview and filtered checklist.</returns>
    /// <exception cref="BadRequestException">The request fields or query parameters are invalid.</exception>
    /// <exception cref="ForbiddenException">The caller is not permitted to access the resource or perform this operation.</exception>
    /// <exception cref="NotFoundException">The requested resource does not exist or is not visible to the caller.</exception>
    [HttpGet("{dossierId:guid}")]
    [Authorize(Policy = JwtExtensions.ComplianceDossierViewerPolicy)]
    public async Task<IActionResult> GetDossier(Guid dossierId, [FromQuery] ComplianceDossierService.Request.ViewRequest request,
        CancellationToken cancellationToken) =>
        Success(await _service.GetDossierAsync(AccountId, dossierId, request, cancellationToken), "ComplianceDossier retrieved successfully.");
}
