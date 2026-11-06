using System.Text.RegularExpressions;
using Pegaxus.Service.Exceptions;

namespace Pegaxus.Service.Common;

/// <summary>Same rule as the frontend: digits with optional "+", spaces, dots, dashes and brackets; 8-15 digits once stripped.</summary>
public static partial class Phones
{
    public const string Message = "Phone must be a valid number (8-15 digits, e.g. +33123456789)";

    [GeneratedRegex(@"^\+?[0-9\s().-]+$")]
    private static partial Regex Allowed();

    [GeneratedRegex(@"^\+?[1-9]\d{7,14}$")]
    private static partial Regex Stripped();

    public static bool IsValid(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed) || !Allowed().IsMatch(trimmed)) return false;
        return Stripped().IsMatch(Regex.Replace(trimmed, @"[\s().-]", ""));
    }

    /// <summary>Blank is allowed (callers enforce "required" themselves).</summary>
    public static void EnsureValid(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value) && !IsValid(value))
            throw new BadRequestException(Message, "INVALID_PHONE");
    }
}
