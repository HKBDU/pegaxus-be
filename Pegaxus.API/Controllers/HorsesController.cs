using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pegaxus.Service.Models;

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
                HttpContext.TraceIdentifier));

        var response = await _service.GetCustomerHorsesAsync(accountId, request, cancellationToken);

        return Ok(ResponseBuilder.SuccessResponse(
            response,
            "Horse list retrieved successfully",
            HttpContext.TraceIdentifier));
    }

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
                HttpContext.TraceIdentifier));

        var canViewAll = User.IsInRole("LogisticsManager") || User.IsInRole("LOGISTICS_MANAGER");
        var response = await _service.GetHorseAsync(accountId, horseId, canViewAll, cancellationToken);

        return Ok(ResponseBuilder.SuccessResponse(
            response,
            "Horse information retrieved successfully",
            HttpContext.TraceIdentifier));
    }

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
                HttpContext.TraceIdentifier));

        var response = await _service.CreateHorseAsync(accountId, request, cancellationToken);

        return StatusCode(StatusCodes.Status201Created, ResponseBuilder.SuccessResponse(
            response,
            "Horse added successfully",
            HttpContext.TraceIdentifier));
    }

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
                HttpContext.TraceIdentifier));

        await _service.UpdateHorseAsync(accountId, horseId, request, cancellationToken);

        return Ok(ResponseBuilder.SuccessResponse(
            null,
            "Horse information updated successfully",
            HttpContext.TraceIdentifier));
    }
}
