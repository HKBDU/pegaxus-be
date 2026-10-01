using Microsoft.AspNetCore.Http;

namespace Pegaxus.Service.MediaService;

public interface IService
{
    public Task<string> UploadImageAsync(IFormFile file);
    Task<(string FileUrl, string PublicId)> UploadFileAsync(IFormFile file);
    Task DeleteFileAsync(string publicId);
    Task<SignedUploadResult> CreateSignedUploadAsync(
        Guid accountId,
        string entity,
        string contentType,
        long sizeBytes);
    Task<ConfirmedUploadResult> ConfirmUploadAsync(Guid accountId, string publicId);
}

public sealed record SignedUploadResult(
    string PublicId,
    string UploadUrl,
    IReadOnlyDictionary<string, string> Parameters);

public sealed record ConfirmedUploadResult(
    string PublicId,
    string FileUrl);
