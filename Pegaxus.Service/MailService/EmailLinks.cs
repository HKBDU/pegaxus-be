using System.Text.RegularExpressions;

namespace Pegaxus.Service.MailService;

/// <summary>
/// Turns the app paths that templates put behind their button into absolute links, using the front-end origin
/// (<c>Cors:FrontendUrl</c>). Without a usable origin the button is removed instead of linking to a dead address.
/// </summary>
public static class EmailLinks
{
    private static readonly Regex ButtonBlock = new(
        Regex.Escape(EmailBodies.ButtonStart) + ".*?" + Regex.Escape(EmailBodies.ButtonEnd),
        RegexOptions.Singleline | RegexOptions.Compiled);

    public static string Resolve(string body, string? frontendUrl)
    {
        var origin = Origin(frontendUrl);
        var resolved = origin is null
            ? ButtonBlock.Replace(body, "")
            : body.Replace(EmailBodies.FrontendToken, origin, StringComparison.Ordinal);
        return resolved.Replace(EmailBodies.ButtonStart, "", StringComparison.Ordinal)
            .Replace(EmailBodies.ButtonEnd, "", StringComparison.Ordinal);
    }

    /// <summary>HTTPS only; plain HTTP is accepted for loopback so local development still gets working links.</summary>
    private static string? Origin(string? frontendUrl)
    {
        if (!Uri.TryCreate(frontendUrl?.Trim(), UriKind.Absolute, out var uri) || uri.UserInfo.Length != 0)
            return null;
        if (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))
            return null;
        return uri.GetLeftPart(UriPartial.Authority);
    }
}
