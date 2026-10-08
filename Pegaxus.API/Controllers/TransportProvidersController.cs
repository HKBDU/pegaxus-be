using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pegaxus.API.Extensions;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.Models;
using Providers = Pegaxus.Service.TransportProviderService;

namespace Pegaxus.API.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/transport-providers")]
public sealed class TransportProvidersController(Providers.IService service) : ControllerBase
{
    /// <summary>
    /// List transport providers.
    /// </summary>
    /// <remarks>Logistics Managers may filter all undeleted providers. Fleet Route Coordinators receive ACTIVE providers only.</remarks>
    /// <param name="request">The status, supported mode, search, sorting and pagination filters.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with a paginated provider catalog, including an empty items array when no providers match.</returns>
    [HttpGet]
    [Authorize(Policy = JwtExtensions.TransportProviderViewerPolicy)]
    public async Task<IActionResult> GetList([FromQuery] Providers.Request.ListRequest request, CancellationToken cancellationToken) =>
        Ok(ResponseBuilder.SuccessResponse(await service.GetListAsync(GetAccountId(), request, cancellationToken),
            "Transport providers retrieved successfully", traceId: HttpContext.TraceIdentifier));

    /// <summary>
    /// Get a transport provider.
    /// </summary>
    /// <remarks>Logistics Managers and Fleet Route Coordinators may read provider master data. Deleted providers return 404 PROVIDER_NOT_FOUND.</remarks>
    /// <param name="providerId">The transport provider identifier (UUID).</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with provider contact details and supported modes.</returns>
    [HttpGet("{providerId:guid}")]
    [Authorize(Policy = JwtExtensions.TransportProviderViewerPolicy)]
    public async Task<IActionResult> Get(Guid providerId, CancellationToken cancellationToken) =>
        Ok(ResponseBuilder.SuccessResponse(await service.GetAsync(GetAccountId(), providerId, cancellationToken),
            "Transport provider retrieved successfully", traceId: HttpContext.TraceIdentifier));

    /// <summary>
    /// Create a transport provider.
    /// </summary>
    /// <remarks>Only Logistics Managers may create providers. Code and tax code must be unique among undeleted providers. Status defaults to ACTIVE.</remarks>
    /// <param name="request">The provider code, name, tax code, supported modes and optional contact details.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>201 Created with provider master data, or 409 when the code or tax code is duplicated.</returns>
    [HttpPost]
    [Authorize(Policy = JwtExtensions.LogisticsManagerPolicy)]
    public async Task<IActionResult> Create([FromBody] Providers.Request.CreateRequest request, CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(GetAccountId(), request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { providerId = result.TransportProviderId }, ResponseBuilder.SuccessResponse(
            result, "Transport provider created successfully", StatusCodes.Status201Created, HttpContext.TraceIdentifier));
    }

    /// <summary>
    /// Update a transport provider.
    /// </summary>
    /// <remarks>Only Logistics Managers may update provider details. ProviderCode is immutable. A stale lastUpdatedAt returns 409 PROVIDER_MODIFIED.</remarks>
    /// <param name="providerId">The transport provider identifier (UUID).</param>
    /// <param name="request">The replacement details and lastUpdatedAt from the latest provider response.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the provider identifier and new updatedAt.</returns>
    [HttpPut("{providerId:guid}")]
    [Authorize(Policy = JwtExtensions.LogisticsManagerPolicy)]
    public async Task<IActionResult> Update(Guid providerId, [FromBody] Providers.Request.UpdateRequest request, CancellationToken cancellationToken) =>
        Ok(ResponseBuilder.SuccessResponse(await service.UpdateAsync(GetAccountId(), providerId, request, cancellationToken),
            "Transport provider updated successfully", traceId: HttpContext.TraceIdentifier));

    /// <summary>
    /// Change a transport provider status.
    /// </summary>
    /// <remarks>Only Logistics Managers may change status. INACTIVE and SUSPENDED require a reason. Supply lastUpdatedAt to prevent stale changes. Historical assignments remain intact.</remarks>
    /// <param name="providerId">The transport provider identifier (UUID).</param>
    /// <param name="request">The target status, reason and latest lastUpdatedAt.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the provider identifier, status and new updatedAt.</returns>
    [HttpPatch("{providerId:guid}/status")]
    [Authorize(Policy = JwtExtensions.LogisticsManagerPolicy)]
    public async Task<IActionResult> UpdateStatus(Guid providerId, [FromBody] Providers.Request.StatusRequest request, CancellationToken cancellationToken) =>
        Ok(ResponseBuilder.SuccessResponse(await service.UpdateStatusAsync(GetAccountId(), providerId, request, cancellationToken),
            "Provider status updated successfully", traceId: HttpContext.TraceIdentifier));

    /// <summary>
    /// Delete a transport provider.
    /// </summary>
    /// <remarks>Only Logistics Managers may soft-delete providers. Assignments to routes that are not COMPLETED or CANCELLED return 409 PROVIDER_IN_USE. Historical references remain intact.</remarks>
    /// <param name="providerId">The transport provider identifier (UUID).</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with a null value after soft deletion.</returns>
    [HttpDelete("{providerId:guid}")]
    [Authorize(Policy = JwtExtensions.LogisticsManagerPolicy)]
    public async Task<IActionResult> Delete(Guid providerId, CancellationToken cancellationToken)
    {
        await service.DeleteAsync(GetAccountId(), providerId, cancellationToken);
        return Ok(ResponseBuilder.SuccessResponse(null, "Transport provider deleted successfully", traceId: HttpContext.TraceIdentifier));
    }

    private Guid GetAccountId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
        ? id : throw new UnauthorizedException("The authenticated account identifier is invalid.");
}
