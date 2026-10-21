using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pegaxus.API.Extensions;
using Pegaxus.API.Models;

using Providers = Pegaxus.Service.TransportProviderService;

namespace Pegaxus.API.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/transport-providers")]
public sealed class TransportProvidersController(Providers.ITransportProviderService service) : ApiControllerBase
{
    /// <summary>
    /// List transport providers.
    /// </summary>
    /// <remarks>Logistics Managers may filter all undeleted providers. Fleet Route Coordinators receive ACTIVE providers only.</remarks>
    /// <param name="request">The status, supported mode, country (2-letter ISO code), search, sorting and pagination filters.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with a paginated provider catalog, including an empty items array when no providers match.</returns>
    [HttpGet]
    [Authorize(Policy = JwtExtensions.TransportProviderViewerPolicy)]
    public async Task<IActionResult> GetList([FromQuery] Providers.Request.ListRequest request, CancellationToken cancellationToken) =>
        Success(await service.GetListAsync(AccountId, request, cancellationToken), "Transport providers retrieved successfully");

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
        Success(await service.GetAsync(AccountId, providerId, cancellationToken), "Transport provider retrieved successfully");

    /// <summary>
    /// Create a transport provider.
    /// </summary>
    /// <remarks>Only Logistics Managers may create providers. The server generates the provider code (PRV-yyyyMMdd-0001); the tax code must be unique among undeleted providers. Status defaults to ACTIVE.</remarks>
    /// <param name="request">The name, tax code, supported modes and optional contact details. The provider code is generated and returned in the response.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>201 Created with provider master data, or 409 TAX_CODE_DUPLICATED when the tax code is already used.</returns>
    [HttpPost]
    [Authorize(Policy = JwtExtensions.LogisticsManagerPolicy)]
    public async Task<IActionResult> Create([FromBody] Providers.Request.CreateRequest request, CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(AccountId, request, cancellationToken);
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
        Success(await service.UpdateAsync(AccountId, providerId, request, cancellationToken), "Transport provider updated successfully");

    /// <summary>
    /// Change a transport provider status.
    /// </summary>
    /// <remarks>Only Logistics Managers may change status. INACTIVE and SUSPENDED require a reason. Optional lastUpdatedAt rejects stale changes. Historical assignments remain intact.</remarks>
    /// <param name="providerId">The transport provider identifier (UUID).</param>
    /// <param name="request">The target status, reason and latest lastUpdatedAt.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the provider identifier, status and new updatedAt.</returns>
    [HttpPatch("{providerId:guid}/status")]
    [Authorize(Policy = JwtExtensions.LogisticsManagerPolicy)]
    public async Task<IActionResult> UpdateStatus(Guid providerId, [FromBody] Providers.Request.StatusRequest request, CancellationToken cancellationToken) =>
        Success(await service.UpdateStatusAsync(AccountId, providerId, request, cancellationToken), "Provider status updated successfully");

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
        await service.DeleteAsync(AccountId, providerId, cancellationToken);
        return Success(null, "Transport provider deleted successfully");
    }

}
