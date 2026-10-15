namespace Pegaxus.Service.Common;

public static class Text
{
    /// <summary>Trims the value and returns null when it is null, empty or whitespace.</summary>
    public static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
