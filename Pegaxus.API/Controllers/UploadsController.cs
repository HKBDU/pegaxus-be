using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pegaxus.API.Extensions;
using Pegaxus.Service.MediaService;
using Pegaxus.Service.Models;
using Pegaxus.Service.Exceptions;
using UploadService = Pegaxus.Service.UploadService;

namespace Pegaxus.API.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/uploads")]
public sealed class UploadsController : ControllerBase
{
    private readonly IService _mediaService;

    public UploadsController(IService mediaService)
    {
        _mediaService = mediaService;
    }

    /// <summary>
    /// Create an attachment upload signature.
    /// </summary>
    /// <remarks>Returns signed Cloudinary upload parameters. The frontend uploads the attachment directly to Cloudinary; this endpoint does not receive the file.</remarks>
    /// <param name="request">The attachment entity, content type, and file size in bytes.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with publicId, the upload URL, and signed parameters.</returns>
    /// <exception cref="BadRequestException">The request fields or query parameters are invalid.</exception>
    /// <exception cref="UnauthorizedAccessException">The authenticated account identifier is invalid.</exception>
    /// <exception cref="Pegaxus.Service.Exceptions.Exceptions">A business or storage error occurs; its HTTP status and errorCode describe the failure.</exception>
    [HttpPost("signature")]
    public async Task<IActionResult> CreateSignature(
        [FromBody] UploadService.Request.CreateUploadSignatureRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _mediaService.CreateSignedUploadAsync(
            GetAccountId(), request.Entity, request.ContentType, request.SizeBytes);

        return Ok(ResponseBuilder.SuccessResponse(
            new UploadService.Response.CreateUploadSignatureResponse
            {
                PublicId = result.PublicId,
                Upload = new UploadService.Response.UploadSignatureResponse
                {
                    Url = result.UploadUrl,
                    Params = result.Parameters
                }
            },
            "Upload signature created successfully",
            traceId: HttpContext.TraceIdentifier));
    }

    /// <summary>
    /// Confirm an attachment upload.
    /// </summary>
    /// <remarks>Verifies that the uploaded publicId belongs to the authenticated account.</remarks>
    /// <param name="request">The publicId of the uploaded attachment.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the confirmed publicId and file URL.</returns>
    /// <exception cref="BadRequestException">The request fields or query parameters are invalid.</exception>
    /// <exception cref="NotFoundException">The requested resource does not exist or is not visible to the caller.</exception>
    /// <exception cref="UnauthorizedAccessException">The authenticated account identifier is invalid.</exception>
    /// <exception cref="Pegaxus.Service.Exceptions.Exceptions">A business or storage error occurs; its HTTP status and errorCode describe the failure.</exception>
    [HttpPost("confirm")]
    public async Task<IActionResult> Confirm(
        [FromBody] UploadService.Request.ConfirmUploadRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _mediaService.ConfirmUploadAsync(
            GetAccountId(), request.PublicId);

        return Ok(ResponseBuilder.SuccessResponse(
            new UploadService.Response.ConfirmUploadResponse
            {
                PublicId = result.PublicId,
                FileUrl = result.FileUrl
            },
            "Upload confirmed successfully",
            traceId: HttpContext.TraceIdentifier));
    }

    private Guid GetAccountId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")
            ?? User.FindFirstValue("accountId");

        if (Guid.TryParse(value, out var accountId))
            return accountId;

        throw new UnauthorizedAccessException("The authenticated account identifier is invalid");
    }

}
