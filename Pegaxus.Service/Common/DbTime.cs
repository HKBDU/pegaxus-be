namespace Pegaxus.Service.Common;

/// <summary>
/// Time helpers for <c>timestamp without time zone</c> columns: values are UTC instants stored with
/// <see cref="DateTimeKind.Unspecified"/>, optionally truncated to PostgreSQL's microsecond precision.
/// </summary>
public static class DbTime
{
    /// <summary>Current UTC time with Unspecified kind, not truncated.</summary>
    public static DateTime UtcNow() => DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

    /// <summary>Current UTC time truncated to microseconds.</summary>
    public static DateTime UtcNowTruncated() => Truncate(DateTime.UtcNow);

    /// <summary>Drops sub-microsecond ticks and marks the value Unspecified.</summary>
    public static DateTime Truncate(DateTime value) => new(value.Ticks / 10 * 10, DateTimeKind.Unspecified);

    /// <summary>Drops sub-microsecond ticks of a UTC instant and marks the value Unspecified.</summary>
    public static DateTime Truncate(DateTimeOffset value) => new(value.UtcTicks / 10 * 10, DateTimeKind.Unspecified);

    /// <summary>Interprets a stored value as a UTC instant.</summary>
    public static DateTimeOffset AsUtc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    /// <summary>Interprets a nullable stored value as a UTC instant.</summary>
    public static DateTimeOffset? AsUtc(DateTime? value) => value.HasValue ? AsUtc(value.Value) : null;
}
