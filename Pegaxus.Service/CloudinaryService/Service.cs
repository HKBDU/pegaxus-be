using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Pegaxus.Service.MediaService;
using System.Security.Cryptography;
using System.Text;
using Pegaxus.Service.Exceptions;

namespace Pegaxus.Service.CloudinaryService;

public class Service : IService
{
    public const string PreviewHttpClient = "ComplianceDocumentPreview";
    private const long MaxSignedUploadSize = 5 * 1024 * 1024;
    private const string AllowedFormats = "jpg,png,pdf";
    private readonly Cloudinary _cloudinary;
    private readonly CloudinaryOptions _cloudinaryOptions = new();
    private readonly IHttpClientFactory _httpClientFactory;

    public Service(IConfiguration configuration, IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
        configuration.GetSection(nameof(CloudinaryOptions)).Bind(_cloudinaryOptions);
        _cloudinary = new Cloudinary(new Account(
            _cloudinaryOptions.CloudName,
            _cloudinaryOptions.ApiKey,
            _cloudinaryOptions.ApiSecret));
    }

    public async Task<string> UploadImageAsync(IFormFile file)
    {
        if (file == null || file.Length == 0)
        {
            throw new ArgumentException("File is empty or null.", nameof(file));
        }
        if (!IsImageFile(file))
        {
            throw new ArgumentException("File is not a valid image.", nameof(file));
        }

        await using var stream = file.OpenReadStream();
        var uploadParams = new ImageUploadParams()
        {
            File = new FileDescription(file.FileName, stream)
        };
        var uploadResult = await _cloudinary.UploadAsync(uploadParams);
        return uploadResult.SecureUrl.ToString();
    }

    private bool IsImageFile(IFormFile file)
    {
        var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
        var fileExtension = Path.GetExtension(file.FileName).ToLowerInvariant();
        return allowedExtensions.Contains(fileExtension);
    }

    // Upload non-image files such as PDF and DOCX through RawUploadParams.
    public async Task<(string FileUrl, string PublicId)> UploadFileAsync(IFormFile file)
    {
        await using var stream = file.OpenReadStream();

        var uploadParams = new RawUploadParams
        {
            File = new FileDescription(file.FileName, stream),
            Folder = "pegaxus/documents",
            UseFilename = true,
            UniqueFilename = true,
        };

        var uploadResult = await _cloudinary.UploadAsync(uploadParams);

        if (uploadResult.Error != null)
            throw new Exception($"Cloudinary upload failed: {uploadResult.Error.Message}");

        return (uploadResult.SecureUrl.ToString(), uploadResult.PublicId);
    }

    // Delete a Cloudinary file by public ID.
    public async Task DeleteFileAsync(string publicId)
    {
        var deleteParams = new DeletionParams(publicId)
        {
            ResourceType = ResourceType.Raw
        };

        var result = await _cloudinary.DestroyAsync(deleteParams);

        if (result.Error != null)
            throw new Exception($"Cloudinary delete failed: {result.Error.Message}");
    }

    public Task<SignedUploadResult> CreateSignedUploadAsync(
        Guid accountId,
        string entity,
        string contentType,
        long sizeBytes)
    {
        if (!string.Equals(entity?.Trim(), "attachment", StringComparison.OrdinalIgnoreCase))
            throw new BadRequestException("Entity must be attachment", "INVALID_UPLOAD_ENTITY");

        if (sizeBytes <= 0 || sizeBytes > MaxSignedUploadSize)
            throw new Pegaxus.Service.Exceptions.Exceptions(
                "File size must be greater than 0 and must not exceed 5 MB",
                413,
                "FILE_TOO_LARGE");

        var normalizedContentType = contentType.Trim().ToLowerInvariant();
        if (normalizedContentType is not "application/pdf" and not "image/jpeg" and not "image/png")
            throw new BadRequestException(
                "Only PDF, JPG, and PNG files are allowed",
                "INVALID_FILE_TYPE");

        EnsureConfigured();
        var publicId = $"evidence/{accountId:D}/{Guid.NewGuid():N}";
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var parameters = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["allowed_formats"] = AllowedFormats,
            ["public_id"] = publicId,
            ["tags"] = "pending",
            ["timestamp"] = timestamp,
            ["type"] = "upload"
        };
        var signature = CreateSignature(parameters);
        parameters["api_key"] = _cloudinaryOptions.ApiKey;
        parameters["signature"] = signature;

        var orderedResponseParameters = new Dictionary<string, string>
        {
            ["api_key"] = _cloudinaryOptions.ApiKey,
            ["timestamp"] = timestamp,
            ["signature"] = signature,
            ["public_id"] = publicId,
            ["type"] = "upload",
            ["tags"] = "pending",
            ["allowed_formats"] = AllowedFormats
        };

        return Task.FromResult(new SignedUploadResult(
            publicId,
            $"https://api.cloudinary.com/v1_1/{_cloudinaryOptions.CloudName}/auto/upload",
            orderedResponseParameters));
    }

    public async Task<ConfirmedUploadResult> ConfirmUploadAsync(Guid accountId, string publicId)
    {
        if (string.IsNullOrWhiteSpace(publicId) ||
            !publicId.StartsWith($"evidence/{accountId:D}/", StringComparison.OrdinalIgnoreCase))
            throw new BadRequestException("The publicId does not belong to the authenticated account", "INVALID_PUBLIC_ID");

        EnsureConfigured();
        var result = await _cloudinary.GetResourceAsync(new GetResourceParams(publicId)
        {
            ResourceType = ResourceType.Image
        });

        if (result.Error is not null || string.IsNullOrWhiteSpace(result.SecureUrl?.ToString()))
        {
            result = await _cloudinary.GetResourceAsync(new GetResourceParams(publicId)
            {
                ResourceType = ResourceType.Raw
            });
        }

        if (result.Error is not null || string.IsNullOrWhiteSpace(result.SecureUrl?.ToString()))
            throw new NotFoundException("Uploaded file was not found on Cloudinary", "UPLOAD_NOT_FOUND");

        return new ConfirmedUploadResult(publicId, result.SecureUrl.ToString());
    }

    public async Task<PreviewFile> DownloadPreviewAsync(string fileUrl, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        if (!Uri.TryCreate(fileUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(uri.Host, "res.cloudinary.com", StringComparison.OrdinalIgnoreCase) || uri.Port != 443 ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment))
            throw new BadRequestException("Document file storage URL is invalid.", "INVALID_FILE_URL");
        var segments = uri.GetComponents(UriComponents.Path, UriFormat.Unescaped).Split('/');
        if (segments.Length < 4 || segments[0] != _cloudinaryOptions.CloudName ||
            segments[1] is not ("image" or "raw") || segments[2] is not ("upload" or "authenticated" or "private") ||
            segments.Any(segment => segment is "." or ".." || segment.Contains('\\') || segment.Any(char.IsControl)))
            throw new BadRequestException("Document file storage URL is invalid.", "INVALID_FILE_URL");
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            using var client = _httpClientFactory.CreateClient(PreviewHttpClient);
            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                throw new NotFoundException("Document file not found.", "DOCUMENT_FILE_NOT_FOUND");
            if (!response.IsSuccessStatusCode)
                throw new Pegaxus.Service.Exceptions.Exceptions("Document file storage is unavailable.", 502, "FILE_STORAGE_UNAVAILABLE");
            if (response.Content.Headers.ContentLength > MaxSignedUploadSize)
                throw new Pegaxus.Service.Exceptions.Exceptions("Preview files must not exceed 5 MB.", 413, "FILE_TOO_LARGE");
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int bytesRead;
            while ((bytesRead = await stream.ReadAsync(chunk, timeout.Token)) != 0)
            {
                if (buffer.Length + bytesRead > MaxSignedUploadSize)
                    throw new Pegaxus.Service.Exceptions.Exceptions("Preview files must not exceed 5 MB.", 413, "FILE_TOO_LARGE");
                buffer.Write(chunk, 0, bytesRead);
            }
            var content = buffer.ToArray();
            return new PreviewFile(content, PreviewContentType(content));
        }
        catch (HttpRequestException)
        {
            throw new Pegaxus.Service.Exceptions.Exceptions("Document file storage is unavailable.", 502, "FILE_STORAGE_UNAVAILABLE");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new Pegaxus.Service.Exceptions.Exceptions("Document preview timed out.", 504, "FILE_STORAGE_TIMEOUT");
        }
    }

    private static string PreviewContentType(byte[] content)
    {
        if (content.AsSpan().StartsWith("%PDF-"u8))
            return "application/pdf";
        if (content.AsSpan().StartsWith(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }))
            return "image/png";
        if (content.AsSpan().StartsWith(new byte[] { 0xff, 0xd8, 0xff }))
            return "image/jpeg";
        if (content.AsSpan().StartsWith("GIF87a"u8) || content.AsSpan().StartsWith("GIF89a"u8))
            return "image/gif";
        if (content.Length >= 12 && content.AsSpan().StartsWith("RIFF"u8) && content.AsSpan(8).StartsWith("WEBP"u8))
            return "image/webp";
        throw new Pegaxus.Service.Exceptions.Exceptions("Only PDF and image files can be previewed.", 415, "INVALID_FILE_TYPE");
    }

    private string CreateSignature(IReadOnlyDictionary<string, string> parameters)
    {
        var payload = string.Join("&", parameters
            .Where(item => !string.IsNullOrWhiteSpace(item.Value))
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => $"{item.Key}={item.Value}"));
        var hash = SHA1.HashData(Encoding.UTF8.GetBytes(payload + _cloudinaryOptions.ApiSecret));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_cloudinaryOptions.CloudName) ||
            string.IsNullOrWhiteSpace(_cloudinaryOptions.ApiKey) ||
            string.IsNullOrWhiteSpace(_cloudinaryOptions.ApiSecret))
            throw new Pegaxus.Service.Exceptions.Exceptions("Cloudinary is not configured", 503, "CLOUDINARY_NOT_CONFIGURED");
    }
}
