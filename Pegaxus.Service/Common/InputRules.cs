using System.Globalization;
using FluentValidation;
using Pegaxus.Service.Exceptions;

namespace Pegaxus.Service.Common;

/// <summary>Reusable limit checks for both the FluentValidation validators and the services that validate by throwing.</summary>
public static class InputRules
{
    public static string Describe(DecimalRange range) =>
        $"between {range.Min.ToString("0.####", CultureInfo.InvariantCulture)} and {range.Max.ToString("0.####", CultureInfo.InvariantCulture)} " +
        (range.Scale == 0 ? "(whole number)" : $"(up to {range.Scale} decimal place{(range.Scale == 1 ? "" : "s")})");

    public static bool IsValid(decimal value, DecimalRange range) =>
        value >= range.Min && value <= range.Max && Decimals.ScaleOf(value) <= range.Scale;

    /// <summary>Throws a 400 when the value is outside the range or has too many decimals; null is left to the required check.</summary>
    public static void Measurement(decimal? value, DecimalRange range, string field)
    {
        if (value is { } number && !IsValid(number, range))
            throw new BadRequestException($"{field} must be {Describe(range)}.");
    }

    public static void MaxLength(string? value, int maxLength, string field)
    {
        if (value is not null && value.Trim().Length > maxLength)
            throw new BadRequestException($"{field} must not exceed {maxLength} characters");
    }

    public static IRuleBuilderOptions<T, decimal> Measurement<T>(this IRuleBuilder<T, decimal> rule, DecimalRange range) =>
        rule.Must(value => IsValid(value, range)).WithMessage($"{{PropertyName}} must be {Describe(range)}.");

    public static IRuleBuilderOptions<T, decimal?> Measurement<T>(this IRuleBuilder<T, decimal?> rule, DecimalRange range) =>
        rule.Must(value => value is null || IsValid(value.Value, range)).WithMessage($"{{PropertyName}} must be {Describe(range)}.");
}
