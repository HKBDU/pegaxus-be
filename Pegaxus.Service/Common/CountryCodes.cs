using Pegaxus.Service.Exceptions;

namespace Pegaxus.Service.Common;

/// <summary>ISO 3166-1 alpha-2 codes, the format stored on Location and Booking.</summary>
public static class CountryCodes
{
    public static bool IsValid(string? value) => value?.Trim() is { Length: 2 } code && code.All(char.IsAsciiLetter);

    /// <summary>Trims and upper-cases the value; blank becomes null.</summary>
    public static string? Normalize(string? value) => Text.EmptyToNull(value)?.ToUpperInvariant();

    /// <summary>Blank is allowed; anything else must be a 2-letter code.</summary>
    public static void EnsureValid(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value) && !IsValid(value))
            throw new BadRequestException("Country must be a 2-letter ISO code", "INVALID_COUNTRY");
    }
}
