using System.Security.Cryptography;
using System.Text;
using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.MediaService;

namespace Pegaxus.Service.CloudinaryService;

public partial class Service : IMediaService
{
    public const string PreviewHttpClient = "ComplianceDocumentPreview";
    private const long MaxSignedUploadSize = DocumentStorageService.Service.MaxFileSize;
    private const string AllowedFormats = "jpg,jpeg,png,pdf";
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

        var normalizedContentType = contentType?.Trim().ToLowerInvariant();
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
            ["type"] = "authenticated",
            ["overwrite"] = "false"
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
            ["type"] = "authenticated",
            ["overwrite"] = "false",
            ["tags"] = "pending",
            ["allowed_formats"] = AllowedFormats
        };

        return Task.FromResult(new SignedUploadResult(
            publicId,
            $"https://api.cloudinary.com/v1_1/{_cloudinaryOptions.CloudName}/auto/upload",
            orderedResponseParameters));
    }

    protected virtual Task<GetResourceResult> GetResourceAsync(GetResourceParams parameters, CancellationToken cancellationToken) =>
        _cloudinary.GetResourceAsync(parameters, cancellationToken);

    public async Task<ConfirmedUploadResult> ConfirmUploadAsync(Guid accountId, string publicId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(publicId) || publicId.Length > 255 ||
            !publicId.StartsWith($"evidence/{accountId:D}/", StringComparison.Ordinal) ||
            publicId.Split('/').Any(segment => segment is "" or "." or ".." || segment.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_' or '.'))))
            throw new BadRequestException("The publicId does not belong to the authenticated account", "INVALID_PUBLIC_ID");

        EnsureConfigured();
        GetResourceResult? result = null;
        try
        {
            foreach (var resourceType in new[] { ResourceType.Image, ResourceType.Raw })
            {
                result = await GetResourceAsync(new GetResourceParams(publicId)
                {
                    ResourceType = resourceType, Type = "authenticated"
                }, cancellationToken);
                if (result.StatusCode == System.Net.HttpStatusCode.NotFound) continue;
                if (result.Error is not null || string.IsNullOrWhiteSpace(result.SecureUrl?.ToString()))
                    throw new Pegaxus.Service.Exceptions.Exceptions("Cloudinary is unavailable", 502, "FILE_STORAGE_UNAVAILABLE");
                break;
            }
        }
        catch (HttpRequestException)
        {
            throw new Pegaxus.Service.Exceptions.Exceptions("Cloudinary is unavailable", 502, "FILE_STORAGE_UNAVAILABLE");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new Pegaxus.Service.Exceptions.Exceptions("Cloudinary confirmation timed out", 504, "FILE_STORAGE_TIMEOUT");
        }
        if (result is null || result.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new NotFoundException("Uploaded file was not found on Cloudinary", "UPLOAD_NOT_FOUND");
        if (result.PublicId != publicId || result.Type != "authenticated" || result.ResourceType is not (ResourceType.Image or ResourceType.Raw))
            throw new BadRequestException("Uploaded file metadata is invalid", "INVALID_FILE_TYPE");
        if (result.Bytes <= 0 || result.Bytes > MaxSignedUploadSize)
            throw new Pegaxus.Service.Exceptions.Exceptions("File must not be empty or exceed 5 MB", 413, "FILE_TOO_LARGE");
        var format = (string.IsNullOrWhiteSpace(result.Format) ? Path.GetExtension(publicId).TrimStart('.') : result.Format).ToLowerInvariant();
        var mimeType = format switch
        {
            "pdf" => "application/pdf",
            "jpg" or "jpeg" => "image/jpeg",
            "png" => "image/png",
            _ => throw new BadRequestException("Only PDF, JPG, and PNG files are allowed", "INVALID_FILE_TYPE")
        };
        var fileUrl = result.SecureUrl;
        var location = StorageLocation(fileUrl);
        if (location.PublicId != publicId || location.DeliveryType != "authenticated" || location.ResourceType != result.ResourceType.ToString().ToLowerInvariant())
            throw new BadRequestException("Uploaded file storage URL is invalid", "INVALID_FILE_URL");
        var name = Path.GetFileName((result.OriginalFilename ?? publicId.Split('/').Last()).Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(name) || name.Any(char.IsControl) || name.Length > 240)
            throw new BadRequestException("Uploaded file name is invalid", "INVALID_FILE_TYPE");
        if (!string.Equals(Path.GetExtension(name).TrimStart('.'), format, StringComparison.OrdinalIgnoreCase))
            name += "." + format;
        return new ConfirmedUploadResult(publicId, fileUrl, name, mimeType, result.Bytes);
    }

    public Task<DocumentStorageService.SignedDocumentUrl> CreateReadUrlAsync(string fileUrl, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var location = StorageLocation(fileUrl);
        cancellationToken.ThrowIfCancellationRequested();
        var expiresAt = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds());
        var url = _cloudinary.DownloadPrivate(location.PublicId, attachment: false, format: location.Format,
            type: location.DeliveryType, expiresAt: expiresAt.ToUnixTimeSeconds(), resourceType: location.ResourceType);
        return Task.FromResult(new DocumentStorageService.SignedDocumentUrl(url, expiresAt));
    }

    public async Task<PreviewFile> DownloadPreviewAsync(string fileUrl, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var location = StorageLocation(fileUrl);
        var downloadUrl = location.DeliveryType == "upload" ? fileUrl : (await CreateReadUrlAsync(fileUrl, cancellationToken)).Url;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            using var client = _httpClientFactory.CreateClient(PreviewHttpClient);
            using var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                throw new NotFoundException("Document file not found.", "DOCUMENT_FILE_NOT_FOUND");
            if (!response.IsSuccessStatusCode)
                throw new Pegaxus.Service.Exceptions.Exceptions("Document file storage is unavailable.", 502, "FILE_STORAGE_UNAVAILABLE");
            if (response.Content.Headers.ContentLength > MaxSignedUploadSize)
                throw new Pegaxus.Service.Exceptions.Exceptions("Preview file exceeds the size limit.", 413, "FILE_TOO_LARGE");
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int bytesRead;
            while ((bytesRead = await stream.ReadAsync(chunk, timeout.Token)) != 0)
            {
                if (buffer.Length + bytesRead > MaxSignedUploadSize)
                    throw new Pegaxus.Service.Exceptions.Exceptions("Preview file exceeds the size limit.", 413, "FILE_TOO_LARGE");
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

    private (string ResourceType, string DeliveryType, string PublicId, string? Format)
    StorageLocation(string fileUrl)
{
    if (
        !Uri.TryCreate(fileUrl, UriKind.Absolute, out var uri) ||
        uri.Scheme != Uri.UriSchemeHttps ||
        !string.Equals(uri.Host, "res.cloudinary.com", StringComparison.OrdinalIgnoreCase) ||
        uri.Port != 443 ||
        !string.IsNullOrEmpty(uri.UserInfo) ||
        !string.IsNullOrEmpty(uri.Fragment) ||
        !string.IsNullOrEmpty(uri.Query)
    )
    {
        throw new BadRequestException(
            "Document file storage URL is invalid.",
            "INVALID_FILE_URL"
        );
    }

    var segments = uri
        .GetComponents(UriComponents.Path, UriFormat.Unescaped)
        .Split('/');

    if (
        segments.Length < 4 ||
        segments[0] != _cloudinaryOptions.CloudName ||
        segments[1] is not ("image" or "raw") ||
        segments[2] is not ("upload" or "authenticated" or "private") ||
        segments.Any(segment =>
            segment is "." or ".." ||
            segment.Contains('\\') ||
            segment.Any(char.IsControl))
    )
    {
        throw new BadRequestException(
            "Document file storage URL is invalid.",
            "INVALID_FILE_URL"
        );
    }

    var offset = 3;
    if (
        offset < segments.Length &&
        segments[offset].StartsWith("s--", StringComparison.Ordinal) &&
        segments[offset].EndsWith("--", StringComparison.Ordinal)
    )
    {
        offset++;
    }
    if (
        offset < segments.Length &&
        segments[offset].StartsWith('v') &&
        long.TryParse(segments[offset][1..], out _)
    )
    {
        offset++;
    }

    var publicId = string.Join('/', segments.Skip(offset));

    if (string.IsNullOrWhiteSpace(publicId))
    {
        throw new BadRequestException(
            "Document file storage URL is invalid.",
            "INVALID_FILE_URL"
        );
    }

    var format = segments[1] == "image"
        ? Path.GetExtension(publicId).TrimStart('.')
        : null;

    if (!string.IsNullOrEmpty(format))
    {
        publicId = publicId[..^(format.Length + 1)];
    }

    return (
        segments[1],
        segments[2],
        publicId,
        format
    );
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
