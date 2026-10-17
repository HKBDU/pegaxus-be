namespace Pegaxus.Service.Common;

public static class Images
{
    public static bool IsValid(byte[] content, string contentType) => content.Length is > 0 and <= 5 * 1024 * 1024 && (contentType switch
    {
        "image/jpeg" => content.AsSpan().StartsWith(new byte[] { 0xff, 0xd8, 0xff }),
        "image/png" => content.AsSpan().StartsWith(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }),
        _ => false
    });
}
