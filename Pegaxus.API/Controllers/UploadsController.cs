using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.MediaService;

using UploadService = Pegaxus.Service.UploadService;

namespace Pegaxus.API.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/uploads")]
public sealed class UploadsController : ApiControllerBase
{
    private readonly IService _mediaService;

    public UploadsController(IService mediaService)
    {
        _mediaService = mediaService;
    }

    /// <summary>
    /// Create an attachment upload signature.
    /// </summary>
    /// <remarks>Returns signed parameters for an authenticated Cloudinary upload of a PDF/JPG/PNG file up to 5 MiB. The frontend uploads directly to Cloudinary with overwrite disabled; this endpoint does not receive the file.</remarks>
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
            AccountId, request.Entity, request.ContentType, request.SizeBytes);

        return Success(new UploadService.Response.CreateUploadSignatureResponse
            {
                PublicId = result.PublicId,
                Upload = new UploadService.Response.UploadSignatureResponse
                {
                    Url = result.UploadUrl,
                    Params = result.Parameters
                }
            }, "Upload signature created successfully");
    }

    /// <summary>
    /// Confirm an attachment upload.
    /// </summary>
    /// <remarks>Verifies ownership, existence, actual size, and format using Cloudinary metadata. Returns trusted file metadata and a signed download URL expiring after five minutes. UC21/UC22 reverify publicIds before saving documents.</remarks>
    /// <param name="request">The publicId of the uploaded attachment.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with publicId, trusted file metadata, fileUrl, and fileUrlExpiresAt.</returns>
    /// <exception cref="BadRequestException">The request fields or query parameters are invalid.</exception>
    /// <exception cref="NotFoundException">The requested resource does not exist or is not visible to the caller.</exception>
    /// <exception cref="UnauthorizedAccessException">The authenticated account identifier is invalid.</exception>
    /// <exception cref="Pegaxus.Service.Exceptions.Exceptions">A business or storage error occurs; its HTTP status and errorCode describe the failure.</exception>
    [HttpPost("confirm")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Confirm(
        [FromBody] UploadService.Request.ConfirmUploadRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _mediaService.ConfirmUploadAsync(
            AccountId, request.PublicId, cancellationToken);
        var signed = await _mediaService.CreateReadUrlAsync(result.FileUrl, cancellationToken);

        return Success(new UploadService.Response.ConfirmUploadResponse
            {
                PublicId = result.PublicId,
                FileUrl = signed.Url,
                FileUrlExpiresAt = signed.ExpiresAt,
                FileName = result.FileName,
                MimeType = result.MimeType,
                SizeBytes = result.SizeBytes
            }, "Upload confirmed successfully");
    }

}
