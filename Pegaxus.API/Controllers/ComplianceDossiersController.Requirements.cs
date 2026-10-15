using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pegaxus.API.Extensions;
using Pegaxus.Service.Exceptions;

using ComplianceDossierService = Pegaxus.Service.ComplianceDossierService;

namespace Pegaxus.API.Controllers;

public sealed partial class ComplianceDossiersController
{
    /// <summary>
    /// Get dossier requirement details.
    /// </summary>
    /// <remarks>Includes the requirement, document, versions, reviews, and correction requests. Customers may only view requirements in their own dossiers.</remarks>
    /// <param name="dossierId">The compliance dossier identifier (UUID).</param>
    /// <param name="requirementId">The dossier requirement identifier (UUID).</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the requirement and associated document details.</returns>
    /// <exception cref="ForbiddenException">The caller is not permitted to access the resource or perform this operation.</exception>
    /// <exception cref="NotFoundException">The requested resource does not exist or is not visible to the caller.</exception>
    [HttpGet("{dossierId:guid}/requirements/{requirementId:guid}")]
    [Authorize(Policy = JwtExtensions.ComplianceDossierViewerPolicy)]
    public async Task<IActionResult> GetRequirement(Guid dossierId, Guid requirementId, CancellationToken cancellationToken) =>
        Success(await _service.GetRequirementAsync(AccountId, dossierId, requirementId, cancellationToken), "DossierRequirement retrieved successfully.");

    /// <summary>
    /// Suggest dossier templates for a booking.
    /// </summary>
    /// <remarks>Transport Specialists retrieve ACTIVE, currently effective templates whose requirements are ACTIVE, ranked by country relevance to the booking.</remarks>
    /// <param name="bookingId">The booking identifier (UUID).</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with available templates and suggestions.</returns>
    /// <exception cref="ForbiddenException">The caller is not permitted to access the resource or perform this operation.</exception>
    /// <exception cref="NotFoundException">The requested resource does not exist or is not visible to the caller.</exception>
    /// <exception cref="ConflictException">The operation conflicts with the current state, a duplicate record, or a concurrent change.</exception>
    [HttpGet("/api/v1/bookings/{bookingId:guid}/dossier-templates")]
    [Authorize(Policy = JwtExtensions.TransportSpecialistPolicy)]
    public async Task<IActionResult> GetTemplateOptions(Guid bookingId, CancellationToken cancellationToken) =>
        Success(await _service.GetTemplateOptionsAsync(AccountId, bookingId, cancellationToken), "Available DossierTemplates retrieved successfully.");

    /// <summary>
    /// Preview a compliance dossier.
    /// </summary>
    /// <remarks>Transport Specialists submit a template or custom items for an approved transport. Generates a prospective checklist without saving a dossier.</remarks>
    /// <param name="transportId">The transport identifier (UUID).</param>
    /// <param name="request">The selected template or custom requirement items and optional booking concurrency timestamp.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the generated dossier preview.</returns>
    /// <exception cref="BadRequestException">The request fields or query parameters are invalid.</exception>
    /// <exception cref="ForbiddenException">The caller is not permitted to access the resource or perform this operation.</exception>
    /// <exception cref="NotFoundException">The requested resource does not exist or is not visible to the caller.</exception>
    /// <exception cref="ConflictException">The operation conflicts with the current state, a duplicate record, or a concurrent change.</exception>
    [HttpPost("/api/v1/transports/{transportId:guid}/compliance-dossier/preview")]
    [Authorize(Policy = JwtExtensions.TransportSpecialistPolicy)]
    public async Task<IActionResult> Preview(Guid transportId, [FromBody] ComplianceDossierService.Request.InitializeRequest request,
        CancellationToken cancellationToken) =>
        Success(await _service.PreviewAsync(AccountId, transportId, request, cancellationToken), "ComplianceDossier preview generated successfully.");

    /// <summary>
    /// Initialize a compliance dossier.
    /// </summary>
    /// <remarks>Transport Specialists initialize a dossier from a template or custom items for an approved booking. Creates the checklist and notifies the customer.</remarks>
    /// <param name="bookingId">The booking identifier (UUID).</param>
    /// <param name="request">The selected template or custom requirement items, optional note, and booking concurrency timestamp.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>201 Created with the dossier and generated requirements.</returns>
    /// <exception cref="BadRequestException">The request fields or query parameters are invalid.</exception>
    /// <exception cref="ForbiddenException">The caller is not permitted to access the resource or perform this operation.</exception>
    /// <exception cref="NotFoundException">The requested resource does not exist or is not visible to the caller.</exception>
    /// <exception cref="ConflictException">The operation conflicts with the current state, a duplicate record, or a concurrent change.</exception>
    [HttpPost("/api/v1/bookings/{bookingId:guid}/compliance-dossier")]
    [Authorize(Policy = JwtExtensions.TransportSpecialistPolicy)]
    public async Task<IActionResult> Initialize(Guid bookingId, [FromBody] ComplianceDossierService.Request.InitializeRequest request,
        CancellationToken cancellationToken) =>
        SuccessCreated(await _service.InitializeAsync(AccountId, bookingId, request, cancellationToken), "ComplianceDossier initialized successfully. DossierRequirements generated for customer.");

    /// <summary>
    /// Add a dossier requirement manually.
    /// </summary>
    /// <remarks>Transport Specialists add an ACTIVE compliance requirement. horseId = null applies to the whole trip; otherwise the horse must belong to the booking. Creates a PENDING requirement and notifies the customer. Errors include REQUIREMENT_INACTIVE, HORSE_NOT_IN_BOOKING, DR_DUPLICATE (including a null horse), and DOSSIER_LOCKED.</remarks>
    /// <param name="dossierId">The compliance dossier identifier (UUID).</param>
    /// <param name="request">The required complianceRequirementId (UUID) and isMandatory; optional horseId, dueDate (yyyy-MM-dd), and instruction. Legacy leadTimeDays and reason remain supported; do not send both dueDate and leadTimeDays.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>201 Created with the new dossier requirement and PENDING status.</returns>
    /// <exception cref="BadRequestException">The request fields or query parameters are invalid.</exception>
    /// <exception cref="ForbiddenException">The caller is not permitted to access the resource or perform this operation.</exception>
    /// <exception cref="NotFoundException">The requested resource does not exist or is not visible to the caller.</exception>
    /// <exception cref="ConflictException">The operation conflicts with the current state, a duplicate record, or a concurrent change.</exception>
    [HttpPost("{dossierId:guid}/requirements")]
    [Authorize(Policy = JwtExtensions.TransportSpecialistPolicy)]
    public async Task<IActionResult> AddRequirement(Guid dossierId, [FromBody] ComplianceDossierService.Request.RequirementRequest request,
        CancellationToken cancellationToken) =>
        SuccessCreated(await _service.AddRequirementAsync(AccountId, dossierId, request, cancellationToken), "DossierRequirement added successfully");

    /// <summary>
    /// Waive a dossier requirement.
    /// </summary>
    /// <remarks>Transport Specialists mark the requirement WAIVED without removing it from the checklist. It counts toward completion; the operation records an audit event and notifies the customer. Errors include WAIVE_REASON_REQUIRED and DOSSIER_LOCKED.</remarks>
    /// <param name="dossierId">The compliance dossier identifier (UUID).</param>
    /// <param name="requirementId">The dossier requirement identifier (UUID).</param>
    /// <param name="request">The required waivedReason, containing at most 2000 characters.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the dossier requirement in WAIVED status.</returns>
    /// <exception cref="BadRequestException">The request fields or query parameters are invalid.</exception>
    /// <exception cref="ForbiddenException">The caller is not permitted to access the resource or perform this operation.</exception>
    /// <exception cref="NotFoundException">The requested resource does not exist or is not visible to the caller.</exception>
    /// <exception cref="ConflictException">The operation conflicts with the current state, a duplicate record, or a concurrent change.</exception>
    [HttpPatch("{dossierId:guid}/requirements/{requirementId:guid}/waive")]
    [Authorize(Policy = JwtExtensions.TransportSpecialistPolicy)]
    public async Task<IActionResult> WaiveRequirement(Guid dossierId, Guid requirementId,
        [FromBody] ComplianceDossierService.Request.WaiveRequirementRequest request, CancellationToken cancellationToken) =>
        Success(await _service.WaiveRequirementAsync(AccountId, dossierId, requirementId, request, cancellationToken), "DossierRequirement waived successfully");

    /// <summary>
    /// Update a dossier requirement.
    /// </summary>
    /// <remarks>Transport Specialists may change supplied fields while the dossier is editable. A mandatory requirement cannot be downgraded to optional. Locked dossiers return 409 DOSSIER_LOCKED.</remarks>
    /// <param name="dossierId">The compliance dossier identifier (UUID).</param>
    /// <param name="requirementId">The dossier requirement identifier (UUID).</param>
    /// <param name="request">At least one of isMandatory, leadTimeDays, or instruction, plus a required reason.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the updated dossier requirement.</returns>
    /// <exception cref="BadRequestException">The request fields or query parameters are invalid.</exception>
    /// <exception cref="ForbiddenException">The caller is not permitted to access the resource or perform this operation.</exception>
    /// <exception cref="NotFoundException">The requested resource does not exist or is not visible to the caller.</exception>
    /// <exception cref="ConflictException">The operation conflicts with the current state, a duplicate record, or a concurrent change.</exception>
    [HttpPatch("{dossierId:guid}/requirements/{requirementId:guid}")]
    [Authorize(Policy = JwtExtensions.TransportSpecialistPolicy)]
    public async Task<IActionResult> UpdateRequirement(Guid dossierId, Guid requirementId,
        [FromBody] ComplianceDossierService.Request.UpdateRequirementRequest request, CancellationToken cancellationToken) =>
        Success(await _service.UpdateRequirementAsync(AccountId, dossierId, requirementId, request, cancellationToken), "DossierRequirement updated successfully.");

    /// <summary>
    /// Remove a dossier requirement.
    /// </summary>
    /// <remarks>Transport Specialists soft-delete optional requirements only. Mandatory requirements cannot be removed. Locked dossiers return 409 DOSSIER_LOCKED; successful changes are audited and the customer is notified.</remarks>
    /// <param name="dossierId">The compliance dossier identifier (UUID).</param>
    /// <param name="requirementId">The dossier requirement identifier (UUID).</param>
    /// <param name="request">The JSON body containing the required removal reason.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the removed requirement identifier and updated completion counts.</returns>
    /// <exception cref="BadRequestException">The request fields or query parameters are invalid.</exception>
    /// <exception cref="ForbiddenException">The caller is not permitted to access the resource or perform this operation.</exception>
    /// <exception cref="NotFoundException">The requested resource does not exist or is not visible to the caller.</exception>
    /// <exception cref="ConflictException">The operation conflicts with the current state, a duplicate record, or a concurrent change.</exception>
    [HttpDelete("{dossierId:guid}/requirements/{requirementId:guid}")]
    [Authorize(Policy = JwtExtensions.TransportSpecialistPolicy)]
    public async Task<IActionResult> RemoveRequirement(Guid dossierId, Guid requirementId,
        [FromBody] ComplianceDossierService.Request.RemoveRequirementRequest request, CancellationToken cancellationToken) =>
        Success(await _service.RemoveRequirementAsync(AccountId, dossierId, requirementId, request, cancellationToken), "DossierRequirement removed successfully.");
}
