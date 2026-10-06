using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pegaxus.API.Extensions;
using Pegaxus.Service.Models;
using Pegaxus.Service.Exceptions;

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

    /// <summary>
    /// List employee accounts.
    /// </summary>
    /// <remarks>Logistics Managers may search, filter, sort, and paginate employee accounts.</remarks>
    /// <param name="request">The employee search, role, status, sorting, and pagination filters.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with a paginated employee list.</returns>
    /// <exception cref="BadRequestException">The request fields or query parameters are invalid.</exception>
    [HttpGet]
    public async Task<IActionResult> GetList(
        [FromQuery] EmployeeService.Request.EmployeeListRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _service.GetListAsync(request, cancellationToken);
        return Ok(ResponseBuilder.SuccessResponse(response, "Employee accounts retrieved successfully", traceId: HttpContext.TraceIdentifier));
    }

    /// <summary>
    /// Export employee accounts.
    /// </summary>
    /// <remarks>Exports all employees matching the filters as an XLSX file, without a JSON response envelope.</remarks>
    /// <param name="request">The employee filters and sort order used for the export.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the employee workbook.</returns>
    /// <exception cref="BadRequestException">The request fields or query parameters are invalid.</exception>
    [HttpGet("export")]
    public async Task<IActionResult> Export(
        [FromQuery] EmployeeService.Request.EmployeeListRequest request,
        CancellationToken cancellationToken)
    {
        var file = await _service.ExportAsync(request, cancellationToken);
        return File(
            file,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"employees-{DateTime.UtcNow:yyyyMMdd}.xlsx");
    }

    /// <summary>
    /// Get employee account details.
    /// </summary>
    /// <remarks>Available to Logistics Managers.</remarks>
    /// <param name="employeeId">The employee identifier (UUID).</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the employee account details.</returns>
    /// <exception cref="NotFoundException">The requested resource does not exist or is not visible to the caller.</exception>
    [HttpGet("{employeeId:guid}")]
    public async Task<IActionResult> Get(Guid employeeId, CancellationToken cancellationToken)
    {
        var response = await _service.GetAsync(employeeId, cancellationToken);
        return Ok(ResponseBuilder.SuccessResponse(response, "Employee account retrieved successfully", traceId: HttpContext.TraceIdentifier));
    }

    /// <summary>
    /// Create an employee account.
    /// </summary>
    /// <remarks>Logistics Managers create employee accounts with an automatically generated employee code and a unique email address.</remarks>
    /// <param name="request">The employee profile, role, working status, and account credentials.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>201 Created with the new employee account.</returns>
    /// <exception cref="BadRequestException">The request fields or query parameters are invalid.</exception>
    /// <exception cref="ConflictException">The operation conflicts with the current state, a duplicate record, or a concurrent change.</exception>
    /// <exception cref="UnauthorizedAccessException">The authenticated account identifier is invalid.</exception>
    [HttpPost]
    public async Task<IActionResult> Create(EmployeeService.Request.CreateEmployeeRequest request, CancellationToken cancellationToken)
    {
        var response = await _service.CreateAsync(request, GetAccountId(), cancellationToken);
        return StatusCode(StatusCodes.Status201Created, ResponseBuilder.SuccessResponse(
            response, "Employee account created successfully", code: StatusCodes.Status201Created, traceId: HttpContext.TraceIdentifier));
    }

    /// <summary>
    /// Update employee employment details.
    /// </summary>
    /// <remarks>Changes supplied role or working-status fields. Managers cannot update their own employee account.</remarks>
    /// <param name="employeeId">The employee identifier (UUID).</param>
    /// <param name="request">The role or working status to update.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the employment update timestamp.</returns>
    /// <exception cref="BadRequestException">The request fields or query parameters are invalid.</exception>
    /// <exception cref="NotFoundException">The requested resource does not exist or is not visible to the caller.</exception>
    /// <exception cref="ConflictException">The operation conflicts with the current state, a duplicate record, or a concurrent change.</exception>
    /// <exception cref="UnauthorizedAccessException">The authenticated account identifier is invalid.</exception>
    [HttpPatch("{employeeId:guid}")]
    public async Task<IActionResult> Update(Guid employeeId, EmployeeService.Request.UpdateEmployeeRequest request, CancellationToken cancellationToken)
    {
        var response = await _service.UpdateAsync(employeeId, request, GetAccountId(), cancellationToken);
        return Ok(ResponseBuilder.SuccessResponse(response, "Employment information updated successfully", traceId: HttpContext.TraceIdentifier));
    }

    /// <summary>
    /// Change an employee account status.
    /// </summary>
    /// <remarks>Supports account activation, deactivation, and soft deletion through SUSPENDED. Managers cannot change their own account.</remarks>
    /// <param name="employeeId">The employee identifier (UUID).</param>
    /// <param name="request">The target employee account status.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the updated account status.</returns>
    /// <exception cref="BadRequestException">The request fields or query parameters are invalid.</exception>
    /// <exception cref="NotFoundException">The requested resource does not exist or is not visible to the caller.</exception>
    /// <exception cref="ConflictException">The operation conflicts with the current state, a duplicate record, or a concurrent change.</exception>
    /// <exception cref="UnauthorizedAccessException">The authenticated account identifier is invalid.</exception>
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
