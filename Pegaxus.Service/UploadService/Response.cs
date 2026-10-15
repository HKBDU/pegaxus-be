namespace Pegaxus.Service.UploadService;

public class Response
{
    public sealed class CreateUploadSignatureResponse
    {
        public string PublicId { get; init; } = string.Empty;

        public UploadSignatureResponse Upload { get; init; } = null!;
    }

    public sealed class UploadSignatureResponse
    {
        public string Url { get; init; } = string.Empty;

        public IReadOnlyDictionary<string, string> Params { get; init; } =
            new Dictionary<string, string>();
    }

    public sealed class ConfirmUploadResponse
    {
        public string PublicId { get; init; } = string.Empty;

        public string FileUrl { get; init; } = string.Empty;
        public DateTimeOffset FileUrlExpiresAt { get; init; }
        public string FileName { get; init; } = string.Empty;
        public string MimeType { get; init; } = string.Empty;
        public long SizeBytes { get; init; }
    }
}
