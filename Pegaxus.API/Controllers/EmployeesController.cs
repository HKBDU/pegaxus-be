using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pegaxus.API.Extensions;
using Pegaxus.Service.Models;

using EmployeeService = Pegaxus.Service.EmployeeService;

namespace Pegaxus.API.Controllers;

[ApiController]
[Authorize(Policy = JwtExtensions.LogisticsManagerPolicy)]
[Route("api/v1/employees")]
public sealed class EmployeesController : ControllerBase
{
    private readonly EmployeeService.IService _service;

    public EmployeesController(EmployeeService.IService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> GetList(
        [FromQuery] EmployeeService.Request.EmployeeListRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _service.GetListAsync(request, cancellationToken);
        return Ok(ResponseBuilder.SuccessResponse(response, "Employee accounts retrieved successfully", traceId: HttpContext.TraceIdentifier));
    }

    [HttpGet("{employeeId:guid}")]
    public async Task<IActionResult> Get(Guid employeeId, CancellationToken cancellationToken)
    {
        var response = await _service.GetAsync(employeeId, cancellationToken);
        return Ok(ResponseBuilder.SuccessResponse(response, "Employee account retrieved successfully", traceId: HttpContext.TraceIdentifier));
    }

    [HttpPost]
    public async Task<IActionResult> Create(EmployeeService.Request.CreateEmployeeRequest request, CancellationToken cancellationToken)
    {
        var response = await _service.CreateAsync(request, GetAccountId(), cancellationToken);
        return StatusCode(StatusCodes.Status201Created, ResponseBuilder.SuccessResponse(
            response, "Employee account created successfully", code: StatusCodes.Status201Created, traceId: HttpContext.TraceIdentifier));
    }

    [HttpPatch("{employeeId:guid}")]
    public async Task<IActionResult> Update(Guid employeeId, EmployeeService.Request.UpdateEmployeeRequest request, CancellationToken cancellationToken)
    {
        var response = await _service.UpdateAsync(employeeId, request, cancellationToken);
        return Ok(ResponseBuilder.SuccessResponse(response, "Employment information updated successfully", traceId: HttpContext.TraceIdentifier));
    }

    [HttpPatch("{employeeId:guid}/status")]
    public async Task<IActionResult> UpdateStatus(Guid employeeId, EmployeeService.Request.UpdateEmployeeStatusRequest request, CancellationToken cancellationToken)
    {
        var response = await _service.UpdateStatusAsync(employeeId, request, GetAccountId(), cancellationToken);
        var message = response.IsDeleted ? "Employee account deleted successfully" : "Account status updated successfully";
        return Ok(ResponseBuilder.SuccessResponse(response, message, traceId: HttpContext.TraceIdentifier));
    }

    private Guid GetAccountId()
    {
        var value = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value
            ?? User.FindFirst("accountId")?.Value;
        if (!Guid.TryParse(value, out var accountId))
            throw new UnauthorizedAccessException("The authenticated account identifier is invalid");
        return accountId;
    }
}
