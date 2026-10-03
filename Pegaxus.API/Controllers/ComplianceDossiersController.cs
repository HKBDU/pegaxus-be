using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pegaxus.API.Extensions;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.Models;

using ComplianceDossierService = Pegaxus.Service.ComplianceDossierService;

namespace Pegaxus.API.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/compliance-dossiers")]
public sealed class ComplianceDossiersController : ControllerBase
{
    private readonly ComplianceDossierService.IService _service;

    public ComplianceDossiersController(ComplianceDossierService.IService service)
    {
        _service = service;
    }

    [HttpGet("{dossierId:guid}/missing-requirements")]
    [Authorize(Policy = JwtExtensions.TransportSpecialistPolicy)]
    public async Task<IActionResult> GetMissingRequirements(Guid dossierId, CancellationToken cancellationToken) =>
        Ok(ResponseBuilder.SuccessResponse(await _service.GetMissingRequirementsAsync(GetAccountId(), dossierId, cancellationToken),
            "Danh sách giấy tờ còn thiếu.", traceId: HttpContext.TraceIdentifier));

    [HttpPost("{dossierId:guid}/missing-document-reminders")]
    [Authorize(Policy = JwtExtensions.TransportSpecialistPolicy)]
    public async Task<IActionResult> RequestMissingDocuments(Guid dossierId,
        [FromBody] ComplianceDossierService.Request.MissingDocumentsRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.RequestMissingDocumentsAsync(GetAccountId(), dossierId, request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, ResponseBuilder.SuccessResponse(result,
            result.EmailSent ? "Đã gửi thông báo nhắc nộp bổ sung giấy tờ thành công cho khách hàng." :
                "Đã lưu thông báo Web; email nhắc nộp giấy tờ đang chờ gửi lại.", StatusCodes.Status201Created, HttpContext.TraceIdentifier));
    }

    [HttpGet("/api/v1/bookings/{bookingId:guid}/compliance-dossier")]
    [Authorize(Policy = JwtExtensions.ComplianceDossierViewerPolicy)]
    public async Task<IActionResult> GetByBooking(Guid bookingId, [FromQuery] ComplianceDossierService.Request.ViewRequest request,
        CancellationToken cancellationToken) =>
        Ok(ResponseBuilder.SuccessResponse(await _service.GetByBookingAsync(GetAccountId(), bookingId, request, cancellationToken),
            "ComplianceDossier retrieved successfully.", traceId: HttpContext.TraceIdentifier));

    [HttpGet("{dossierId:guid}")]
    [Authorize(Policy = JwtExtensions.ComplianceDossierViewerPolicy)]
    public async Task<IActionResult> GetDossier(Guid dossierId, [FromQuery] ComplianceDossierService.Request.ViewRequest request,
        CancellationToken cancellationToken) =>
        Ok(ResponseBuilder.SuccessResponse(await _service.GetDossierAsync(GetAccountId(), dossierId, request, cancellationToken),
            "ComplianceDossier retrieved successfully.", traceId: HttpContext.TraceIdentifier));

    [HttpGet("{dossierId:guid}/requirements/{requirementId:guid}")]
    [Authorize(Policy = JwtExtensions.ComplianceDossierViewerPolicy)]
    public async Task<IActionResult> GetRequirement(Guid dossierId, Guid requirementId, CancellationToken cancellationToken) =>
        Ok(ResponseBuilder.SuccessResponse(await _service.GetRequirementAsync(GetAccountId(), dossierId, requirementId, cancellationToken),
            "DossierRequirement retrieved successfully.", traceId: HttpContext.TraceIdentifier));

    [HttpGet("{dossierId:guid}/requirements/{requirementId:guid}/versions/{versionId:guid}/preview")]
    [HttpGet("{dossierId:guid}/requirements/{requirementId:guid}/versions/{versionId:guid}/attachments/{attachmentId:guid}/preview")]
    [Authorize(Policy = JwtExtensions.ComplianceDossierViewerPolicy)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> GetDocumentPreview(Guid dossierId, Guid requirementId, Guid versionId,
        Guid? attachmentId, CancellationToken cancellationToken)
    {
        var file = await _service.GetDocumentPreviewAsync(GetAccountId(), dossierId, requirementId, versionId, attachmentId, cancellationToken);
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Content-Security-Policy"] = "sandbox";
        Response.Headers["Content-Disposition"] = "inline";
        return File(file.Content, file.ContentType);
    }

    [HttpGet("/api/v1/bookings/{bookingId:guid}/dossier-templates")]
    [Authorize(Policy = JwtExtensions.TransportSpecialistPolicy)]
    public async Task<IActionResult> GetTemplateOptions(Guid bookingId, CancellationToken cancellationToken) =>
        Ok(ResponseBuilder.SuccessResponse(await _service.GetTemplateOptionsAsync(GetAccountId(), bookingId, cancellationToken),
            "Available DossierTemplates retrieved successfully.", traceId: HttpContext.TraceIdentifier));

    [HttpPost("/api/v1/bookings/{bookingId:guid}/compliance-dossier/preview")]
    [Authorize(Policy = JwtExtensions.TransportSpecialistPolicy)]
    public async Task<IActionResult> Preview(Guid bookingId, [FromBody] ComplianceDossierService.Request.InitializeRequest request,
        CancellationToken cancellationToken) =>
        Ok(ResponseBuilder.SuccessResponse(await _service.PreviewAsync(GetAccountId(), bookingId, request, cancellationToken),
            "ComplianceDossier preview generated successfully.", traceId: HttpContext.TraceIdentifier));

    [HttpPost("/api/v1/bookings/{bookingId:guid}/compliance-dossier")]
    [Authorize(Policy = JwtExtensions.TransportSpecialistPolicy)]
    public async Task<IActionResult> Initialize(Guid bookingId, [FromBody] ComplianceDossierService.Request.InitializeRequest request,
        CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created, ResponseBuilder.SuccessResponse(
            await _service.InitializeAsync(GetAccountId(), bookingId, request, cancellationToken),
            "ComplianceDossier initialized successfully. DossierRequirements generated for customer.",
            StatusCodes.Status201Created, HttpContext.TraceIdentifier));

    [HttpPost("{dossierId:guid}/requirements")]
    [Authorize(Policy = JwtExtensions.TransportSpecialistPolicy)]
    public async Task<IActionResult> AddRequirement(Guid dossierId, [FromBody] ComplianceDossierService.Request.RequirementRequest request,
        CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created, ResponseBuilder.SuccessResponse(
            await _service.AddRequirementAsync(GetAccountId(), dossierId, request, cancellationToken),
            "DossierRequirement added to ComplianceDossier successfully.", StatusCodes.Status201Created, HttpContext.TraceIdentifier));

    [HttpPatch("{dossierId:guid}/requirements/{requirementId:guid}")]
    [Authorize(Policy = JwtExtensions.TransportSpecialistPolicy)]
    public async Task<IActionResult> UpdateRequirement(Guid dossierId, Guid requirementId,
        [FromBody] ComplianceDossierService.Request.UpdateRequirementRequest request, CancellationToken cancellationToken) =>
        Ok(ResponseBuilder.SuccessResponse(
            await _service.UpdateRequirementAsync(GetAccountId(), dossierId, requirementId, request, cancellationToken),
            "DossierRequirement updated successfully.", traceId: HttpContext.TraceIdentifier));

    [HttpDelete("{dossierId:guid}/requirements/{requirementId:guid}")]
    [Authorize(Policy = JwtExtensions.TransportSpecialistPolicy)]
    public async Task<IActionResult> RemoveRequirement(Guid dossierId, Guid requirementId,
        [FromBody] ComplianceDossierService.Request.RemoveRequirementRequest request, CancellationToken cancellationToken) =>
        Ok(ResponseBuilder.SuccessResponse(
            await _service.RemoveRequirementAsync(GetAccountId(), dossierId, requirementId, request, cancellationToken),
            "DossierRequirement removed successfully.", traceId: HttpContext.TraceIdentifier));

    [HttpGet("dashboard")]
    [Authorize(Policy = JwtExtensions.TransportSpecialistOrLogisticsManagerPolicy)]
    public async Task<IActionResult> GetDashboard(
        [FromQuery] ComplianceDossierService.Request.DashboardRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _service.GetDashboardAsync(
            GetAccountId(), request, cancellationToken);

        return Ok(ResponseBuilder.SuccessResponse(
            response,
            "Dashboard retrieved successfully",
            traceId: HttpContext.TraceIdentifier));
    }

    [HttpGet("dashboard/export")]
    [Authorize(Policy = JwtExtensions.TransportSpecialistOrLogisticsManagerPolicy)]
    public async Task<IActionResult> ExportDashboard(
        [FromQuery] ComplianceDossierService.Request.DashboardExportRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _service.ExportDashboardAsync(
            GetAccountId(), request, cancellationToken);

        return File(response.Content, response.ContentType, response.FileName);
    }

    [HttpGet("{dossierId:guid}/audit-history")]
    [Authorize(Policy = JwtExtensions.TransportSpecialistOrLogisticsManagerPolicy)]
    public async Task<IActionResult> GetAuditHistory(
        Guid dossierId,
        [FromQuery] ComplianceDossierService.Request.AuditHistoryRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _service.GetAuditHistoryAsync(
            GetAccountId(), dossierId, request, cancellationToken);

        return Ok(ResponseBuilder.SuccessResponse(
            response,
            "Audit history retrieved successfully",
            traceId: HttpContext.TraceIdentifier));
    }

    [HttpGet("{dossierId:guid}/authority-submissions")]
    [Authorize(Policy = JwtExtensions.CustomerOrEmployeePolicy)]
    public async Task<IActionResult> GetAuthoritySubmissions(
        Guid dossierId,
        CancellationToken cancellationToken)
    {
        var response = await _service.GetAuthoritySubmissionsAsync(
            GetAccountId(), dossierId, cancellationToken);

        return Ok(ResponseBuilder.SuccessResponse(
            response,
            "Authority submissions retrieved successfully",
            traceId: HttpContext.TraceIdentifier));
    }

    [HttpPost("{dossierId:guid}/authority-submissions")]
    [Authorize(Policy = JwtExtensions.TransportSpecialistPolicy)]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> RecordAuthoritySubmission(
        Guid dossierId,
        [FromForm] ComplianceDossierService.Request.RecordAuthoritySubmissionRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _service.RecordAuthoritySubmissionAsync(
            GetAccountId(), dossierId, request, cancellationToken);

        return StatusCode(StatusCodes.Status201Created, ResponseBuilder.SuccessResponse(
            response,
            "Authority submission recorded successfully",
            StatusCodes.Status201Created,
            HttpContext.TraceIdentifier));
    }

    [HttpPatch("/api/v1/authority-submissions/{submissionId:guid}/decision")]
    [Authorize(Policy = JwtExtensions.TransportSpecialistPolicy)]
    public async Task<IActionResult> RecordAuthorityDecision(
        Guid submissionId,
        [FromForm] ComplianceDossierService.Request.RecordAuthorityDecisionRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _service.RecordAuthorityDecisionAsync(
            GetAccountId(), submissionId, request, cancellationToken);

        return Ok(ResponseBuilder.SuccessResponse(
            response,
            "Authority decision recorded successfully for ComplianceDossier",
            traceId: HttpContext.TraceIdentifier));
    }

    [HttpPost("/api/v1/compliance-dossiers/{dossierId:guid}/quarantine-proofs")]
    [Authorize(Policy = JwtExtensions.DriverOrEscortPolicy)]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> SubmitQuarantineProof(
        Guid dossierId,
        [FromForm] ComplianceDossierService.Request.SubmitQuarantineProofRequest request,
        CancellationToken cancellationToken)
    {
        var proofId = await _service.SubmitQuarantineProofAsync(
            GetAccountId(), dossierId, request, cancellationToken);

        return StatusCode(StatusCodes.Status201Created, ResponseBuilder.SuccessResponse(
            new { QuarantineProofId = proofId },
            "Quarantine proof submitted successfully. Awaiting TS Verification.",
            StatusCodes.Status201Created,
            HttpContext.TraceIdentifier));
    }

    [HttpGet("/api/v1/quarantine-proofs/{proofId:guid}")]
    [Authorize(Policy = JwtExtensions.CustomerOrEmployeePolicy)]
    public async Task<IActionResult> GetQuarantineProof(
        Guid proofId,
        CancellationToken cancellationToken)
    {
        var response = await _service.GetQuarantineProofAsync(
            GetAccountId(), proofId, cancellationToken);

        return Ok(ResponseBuilder.SuccessResponse(
            response,
            "QuarantineProof retrieved successfully",
            traceId: HttpContext.TraceIdentifier));
    }

    [HttpPatch("/api/v1/quarantine-proofs/{proofId:guid}/finalize")]
    [Authorize(Policy = JwtExtensions.TransportSpecialistPolicy)]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> FinalizeQuarantineProof(
        Guid proofId,
        [FromForm] ComplianceDossierService.Request.FinalizeQuarantineProofRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _service.FinalizeQuarantineProofAsync(
            GetAccountId(), proofId, request, cancellationToken);

        return Ok(ResponseBuilder.SuccessResponse(
            response,
            "Quarantine proof finalized successfully",
            traceId: HttpContext.TraceIdentifier));
    }

    [HttpPost("/api/v1/compliance-dossiers/{dossierId:guid}/customs-clearance-proofs")]
    [Authorize(Policy = JwtExtensions.DriverPolicy)]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> SubmitCustomsClearanceProof(
        Guid dossierId,
        [FromForm] ComplianceDossierService.Request.SubmitCustomsClearanceProofRequest request,
        CancellationToken cancellationToken)
    {
        var proofId = await _service.SubmitCustomsClearanceProofAsync(
            GetAccountId(), dossierId, request, cancellationToken);

        return StatusCode(StatusCodes.Status201Created, ResponseBuilder.SuccessResponse(
            new { CustomsClearanceProofId = proofId },
            "Customs proof submitted successfully. Awaiting TS Verification.",
            StatusCodes.Status201Created,
            HttpContext.TraceIdentifier));
    }

    [HttpGet("/api/v1/customs-clearance-proofs/{proofId:guid}")]
    [Authorize(Policy = JwtExtensions.CustomerOrEmployeePolicy)]
    public async Task<IActionResult> GetCustomsClearanceProof(
        Guid proofId,
        CancellationToken cancellationToken)
    {
        var response = await _service.GetCustomsClearanceProofAsync(
            GetAccountId(), proofId, cancellationToken);

        return Ok(ResponseBuilder.SuccessResponse(
            response,
            "CustomsClearanceProof retrieved successfully",
            traceId: HttpContext.TraceIdentifier));
    }

    [HttpPatch("/api/v1/customs-clearance-proofs/{proofId:guid}/finalize")]
    [Authorize(Policy = JwtExtensions.TransportSpecialistPolicy)]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> FinalizeCustomsClearanceProof(
        Guid proofId,
        [FromForm] ComplianceDossierService.Request.FinalizeCustomsClearanceProofRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _service.FinalizeCustomsClearanceProofAsync(
            GetAccountId(), proofId, request, cancellationToken);

        return Ok(ResponseBuilder.SuccessResponse(
            response,
            "Customs proof finalized successfully",
            traceId: HttpContext.TraceIdentifier));
    }

    private Guid GetAccountId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")
            ?? User.FindFirstValue("accountId");

        if (Guid.TryParse(value, out var accountId))
            return accountId;

        throw new UnauthorizedException("The authentication session is invalid or has expired");
    }
}
