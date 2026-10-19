namespace Pegaxus.Service.Common;

/// <summary>A numeric business range plus the most decimal places a value may carry.</summary>
public sealed record DecimalRange(decimal Min, decimal Max, int Scale);

/// <summary>
/// Single source of truth for input limits (see VALIDATION_UX_AUDIT.md section 5). The frontend copies these numbers;
/// change them here and in the api-contract limits table together. Each range sits inside its database column.
/// </summary>
public static class Limits
{
    public static readonly DecimalRange HorseWeightKg = new(20m, 1500m, 1);
    public static readonly DecimalRange HorseHeightCm = new(50m, 220m, 1);
    public static readonly DecimalRange PlanDistanceKm = new(0.1m, 30000m, 2);
    public static readonly DecimalRange TrackingSpeedKmh = new(0m, 1000m, 1);
    public static readonly DecimalRange TrackingAccuracyMeters = new(0m, 10000m, 1);
    /// <summary>Heading is [0, 360), so with one decimal place the largest value is 359.9.</summary>
    public static readonly DecimalRange HeadingDegrees = new(0m, 359.9m, 1);
    public static readonly DecimalRange Latitude = new(-90m, 90m, CoordinateScale);
    public static readonly DecimalRange Longitude = new(-180m, 180m, CoordinateScale);

    public const int CoordinateScale = 4;
    public const int PlanDurationMinutesMax = 60 * 24 * 60;
    public const int InvoiceQuantityMin = 1;
    public const int InvoiceQuantityMax = 5000;
    public const int ValidityPeriodDaysMax = 3650;
    public const int NoteMaxLength = 2000;
    public const int HorseMaxAgeYears = 40;
    public const int AccountMinAgeYears = 16;
    public const int AccountMaxAgeYears = 120;
    public const int DateWindowYears = 10;
    public const int PhoneMinDigits = 8;
    public const int PhoneMaxDigits = 15;
    public static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(5);
}
