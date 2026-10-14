using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pegaxus.API.Extensions;
using Pegaxus.API.Models;

using Personnel = Pegaxus.Service.PersonnelAssignmentService;

namespace Pegaxus.API.Controllers;

[ApiController]
[Authorize]
[Route("api/v1")]
public sealed class PersonnelAssignmentsController(Personnel.IPersonnelAssignmentService service) : ApiControllerBase
{
    /// <summary>Get transport plan personnel assignments.</summary>
    /// <remarks>Logistics Managers and assigned personnel can view phase 1 and phase 2. Phase 2 opens for a confirmed or active PRIMARY route.</remarks>
    /// <param name="transportPlanId">The transport plan identifier (UUID).</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with phase 1 employees, phase 2 availability, and route assignments.</returns>
    [HttpGet("transport-plans/{transportPlanId:guid}/assignments")]
    [Authorize(Policy = JwtExtensions.PersonnelAssignmentViewerPolicy)]
    public async Task<IActionResult> Get(Guid transportPlanId, CancellationToken cancellationToken)
        => Success(await service.GetAsync(transportPlanId, AccountId, cancellationToken), "Assignments retrieved successfully");

    /// <summary>Assign phase 2 route personnel.</summary>
    /// <remarks>Only Logistics Managers assign Driver and Escort to internal segments of a confirmed PRIMARY route. Provider segments use existing outsourcing. Schedule conflicts fail atomically.</remarks>
    /// <param name="routePlanId">The route plan identifier (UUID).</param>
    /// <param name="request">The route version and internal segment personnel pairs. Every internal segment is required.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>201 Created with assignment completeness and the new route version.</returns>
    [HttpPost("route-plans/{routePlanId:guid}/personnel")]
    [Authorize(Policy = JwtExtensions.LogisticsManagerPolicy)]
    public async Task<IActionResult> Assign(Guid routePlanId, [FromBody] Personnel.Request.AssignRequest request, CancellationToken cancellationToken)
        => StatusCode(201, ResponseBuilder.SuccessResponse(await service.AssignAsync(routePlanId, request, AccountId, cancellationToken),
            "Phase 2 personnel assigned successfully", code: 201, traceId: HttpContext.TraceIdentifier));

    /// <summary>Replace a segment Driver or Escort.</summary>
    /// <remarks>Only Logistics Managers replace personnel before departure. A reason and current route version are required. Started or outsourced segments cannot change.</remarks>
    /// <param name="routePlanId">The route plan identifier (UUID).</param>
    /// <param name="routePlanRouteId">The segment identifier (UUID).</param>
    /// <param name="request">Replacement employee identifiers, reason, and route version.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the segment, assignment completeness, and new route version.</returns>
    [HttpPut("route-plans/{routePlanId:guid}/segments/{routePlanRouteId:guid}/personnel")]
    [Authorize(Policy = JwtExtensions.LogisticsManagerPolicy)]
    public async Task<IActionResult> Replace(Guid routePlanId, Guid routePlanRouteId, [FromBody] Personnel.Request.ReplaceRequest request, CancellationToken cancellationToken)
        => Success(await service.ReplaceAsync(routePlanId, routePlanRouteId, request, AccountId, cancellationToken), "Segment personnel updated successfully");

    /// <summary>List active employees available for assignment.</summary>
    /// <param name="request">Role, schedule, search and pagination.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with available employees.</returns>
    [HttpGet("employees/assignable")]
    [Authorize(Policy = JwtExtensions.LogisticsManagerPolicy)]
    public async Task<IActionResult> GetAssignable([FromQuery] Personnel.Request.AssignableRequest request, CancellationToken cancellationToken)
        => Success(await service.GetAssignableAsync(request, AccountId, cancellationToken), "Assignable employees retrieved successfully");

}
