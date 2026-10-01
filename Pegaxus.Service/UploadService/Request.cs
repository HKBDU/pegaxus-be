namespace Pegaxus.Service.UploadService;

public class Request
{
    public sealed class CreateUploadSignatureRequest
    {
        public string Entity { get; set; } = string.Empty;

        public string ContentType { get; set; } = string.Empty;

        public long SizeBytes { get; set; }
    }

    public sealed class ConfirmUploadRequest
    {
        public string PublicId { get; set; } = string.Empty;
    }
}
