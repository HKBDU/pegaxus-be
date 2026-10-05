using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pegaxus.Service.Models;
using Pegaxus.Service.Exceptions;

using HorseService = Pegaxus.Service.HorseService;

namespace Pegaxus.API.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/horses")]
public sealed class HorsesController : ControllerBase
{
    private readonly HorseService.IService _service;

    public HorsesController(HorseService.IService service)
    {
        _service = service;
    }

    /// <summary>
    /// List the current customer's horses.
    /// </summary>
    /// <remarks>Only customer accounts may access their horse list.</remarks>
    /// <param name="request">The horse search and pagination parameters.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with a paginated horse list.</returns>
    /// <exception cref="BadRequestException">The request fields or query parameters are invalid.</exception>
    /// <exception cref="ForbiddenException">The caller is not permitted to access the resource or perform this operation.</exception>
    [HttpGet]
    public async Task<IActionResult> GetCustomerHorses(
        [FromQuery] HorseService.Request.HorseListRequest request,
        CancellationToken cancellationToken)
    {
        var accountIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")
            ?? User.FindFirstValue("accountId");

        if (!Guid.TryParse(accountIdValue, out var accountId))
            return Unauthorized(ResponseBuilder.ErrorResponse(
                null,
                "The authenticated account identifier is invalid",
                errorCode: "UNAUTHORIZED",
                code: StatusCodes.Status401Unauthorized,
                traceId: HttpContext.TraceIdentifier));

        var response = await _service.GetCustomerHorsesAsync(accountId, request, cancellationToken);

        return Ok(ResponseBuilder.SuccessResponse(
            response,
            "Horse list retrieved successfully",
            traceId: HttpContext.TraceIdentifier));
    }

    /// <summary>
    /// Get horse details.
    /// </summary>
    /// <remarks>Customers see owned horses; Logistics Managers may view horses across customers.</remarks>
    /// <param name="horseId">The horse identifier (UUID).</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with horse details.</returns>
    /// <exception cref="NotFoundException">The requested resource does not exist or is not visible to the caller.</exception>
    [HttpGet("{horseId:guid}")]
    public async Task<IActionResult> GetHorse(
        Guid horseId,
        CancellationToken cancellationToken)
    {
        var accountIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")
            ?? User.FindFirstValue("accountId");

        if (!Guid.TryParse(accountIdValue, out var accountId))
            return Unauthorized(ResponseBuilder.ErrorResponse(
                null,
                "The authenticated account identifier is invalid",
                errorCode: "UNAUTHORIZED",
                code: StatusCodes.Status401Unauthorized,
                traceId: HttpContext.TraceIdentifier));

        var canViewAll = User.IsInRole("LogisticsManager") || User.IsInRole("LOGISTICS_MANAGER");
        var response = await _service.GetHorseAsync(accountId, horseId, canViewAll, cancellationToken);

        return Ok(ResponseBuilder.SuccessResponse(
            response,
            "Horse information retrieved successfully",
            traceId: HttpContext.TraceIdentifier));
    }

    /// <summary>
    /// Create a horse profile.
    /// </summary>
    /// <remarks>Creates a horse owned by the authenticated customer. Microchip and passport identifiers must be valid and unique.</remarks>
    /// <param name="request">The horse identity, physical details, passport, and microchip information.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>201 Created with the new horse identifier.</returns>
    /// <exception cref="BadRequestException">The request fields or query parameters are invalid.</exception>
    /// <exception cref="ForbiddenException">The caller is not permitted to access the resource or perform this operation.</exception>
    /// <exception cref="ConflictException">The operation conflicts with the current state, a duplicate record, or a concurrent change.</exception>
    [HttpPost]
    public async Task<IActionResult> CreateHorse(
        HorseService.Request.CreateHorseRequest request,
        CancellationToken cancellationToken)
    {
        var accountIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")
            ?? User.FindFirstValue("accountId");

        if (!Guid.TryParse(accountIdValue, out var accountId))
            return Unauthorized(ResponseBuilder.ErrorResponse(
                null,
                "The authenticated account identifier is invalid",
                errorCode: "UNAUTHORIZED",
                code: StatusCodes.Status401Unauthorized,
                traceId: HttpContext.TraceIdentifier));

        var response = await _service.CreateHorseAsync(accountId, request, cancellationToken);

        return StatusCode(StatusCodes.Status201Created, ResponseBuilder.SuccessResponse(
            response,
            "Horse added successfully",
            code: StatusCodes.Status201Created,
            traceId: HttpContext.TraceIdentifier));
    }

    /// <summary>
    /// Update a horse profile.
    /// </summary>
    /// <remarks>Replaces an owned horse's profile. Horses belonging to APPROVED bookings cannot be updated.</remarks>
    /// <param name="horseId">The horse identifier (UUID).</param>
    /// <param name="request">The complete replacement horse profile.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK after the horse profile is updated.</returns>
    /// <exception cref="BadRequestException">The request fields or query parameters are invalid.</exception>
    /// <exception cref="NotFoundException">The requested resource does not exist or is not visible to the caller.</exception>
    /// <exception cref="ConflictException">The operation conflicts with the current state, a duplicate record, or a concurrent change.</exception>
    [HttpPut("{horseId:guid}")]
    public async Task<IActionResult> UpdateHorse(
        Guid horseId,
        HorseService.Request.CreateHorseRequest request,
        CancellationToken cancellationToken)
    {
        var accountIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")
            ?? User.FindFirstValue("accountId");

        if (!Guid.TryParse(accountIdValue, out var accountId))
            return Unauthorized(ResponseBuilder.ErrorResponse(
                null,
                "The authenticated account identifier is invalid",
                errorCode: "UNAUTHORIZED",
                code: StatusCodes.Status401Unauthorized,
                traceId: HttpContext.TraceIdentifier));

        await _service.UpdateHorseAsync(accountId, horseId, request, cancellationToken);

        return Ok(ResponseBuilder.SuccessResponse(
            null,
            "Horse information updated successfully",
            traceId: HttpContext.TraceIdentifier));
    }

    /// <summary>
    /// Delete a horse profile.
    /// </summary>
    /// <remarks>Soft-deletes an owned horse. Horses belonging to APPROVED bookings cannot be deleted.</remarks>
    /// <param name="horseId">The horse identifier (UUID).</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK after the horse is soft-deleted.</returns>
    /// <exception cref="NotFoundException">The requested resource does not exist or is not visible to the caller.</exception>
    /// <exception cref="ConflictException">The operation conflicts with the current state, a duplicate record, or a concurrent change.</exception>
    [HttpDelete("{horseId:guid}")]
    public async Task<IActionResult> DeleteHorse(
        Guid horseId,
        CancellationToken cancellationToken)
    {
        var accountIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")
            ?? User.FindFirstValue("accountId");

        if (!Guid.TryParse(accountIdValue, out var accountId))
            return Unauthorized(ResponseBuilder.ErrorResponse(
                null,
                "The authenticated account identifier is invalid",
                errorCode: "UNAUTHORIZED",
                code: StatusCodes.Status401Unauthorized,
                traceId: HttpContext.TraceIdentifier));

        await _service.DeleteHorseAsync(accountId, horseId, cancellationToken);

        return Ok(ResponseBuilder.SuccessResponse(
            null,
            "Horse removed successfully",
            traceId: HttpContext.TraceIdentifier));
    }
}
