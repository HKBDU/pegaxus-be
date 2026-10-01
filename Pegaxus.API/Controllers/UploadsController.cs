using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pegaxus.API.Extensions;
using Pegaxus.Service.MediaService;
using Pegaxus.Service.Models;
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
