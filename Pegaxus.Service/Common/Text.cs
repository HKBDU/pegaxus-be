using System.Globalization;
using System.Text;

namespace Pegaxus.Service.Common;

public static class Text
{
    /// <summary>Trims the value and returns null when it is null, empty or whitespace.</summary>
    public static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Names are single-line: control and zero-width characters are dropped and runs of whitespace become one space.</summary>
    public static string NormalizeName(string? value)
    {
        var builder = new StringBuilder();
        foreach (var character in value ?? string.Empty)
        {
            if (IsInvisible(character)) continue;
            if (char.IsWhiteSpace(character)) { if (builder.Length > 0 && builder[^1] != ' ') builder.Append(' '); }
            else builder.Append(character);
        }
        return builder.ToString().Trim();
    }

    /// <summary>Free text keeps its line breaks but loses other control and zero-width characters.</summary>
    public static string NormalizeNote(string? value)
    {
        var builder = new StringBuilder();
        foreach (var character in value ?? string.Empty)
            if (!IsInvisible(character) || character is '\n' or '\r' or '\t') builder.Append(character);
        return builder.ToString().Trim();
    }

    private static bool IsInvisible(char character) =>
        char.IsControl(character) && !char.IsWhiteSpace(character) || CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.Format;
}
