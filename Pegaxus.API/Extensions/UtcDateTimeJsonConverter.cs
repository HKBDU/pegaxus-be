using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pegaxus.API.Extensions;

/// <summary>
/// Writes every <see cref="DateTime"/> as a UTC instant with a trailing "Z". Database columns are
/// <c>timestamp without time zone</c> holding UTC, so entity values come back <see cref="DateTimeKind.Unspecified"/> and the
/// stock writer drops the "Z", which clients then read as their own local time. Reading is left to the stock behaviour.
/// </summary>
public sealed class UtcDateTimeJsonConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.GetDateTime();

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
        writer.WriteStringValue(ToUtc(value));

    internal static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
