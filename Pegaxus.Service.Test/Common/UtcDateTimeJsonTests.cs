using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pegaxus.API.Extensions;

namespace Pegaxus.Service.Test;

public class UtcDateTimeJsonTests
{
    private static JsonSerializerOptions ApiOptions()
    {
        var services = new ServiceCollection();
        services.AddLogging(); services.AddApiControllers();
        return services.BuildServiceProvider().GetRequiredService<IOptions<JsonOptions>>().Value.JsonSerializerOptions;
    }

    [Theory]
    [InlineData(DateTimeKind.Unspecified)]
    [InlineData(DateTimeKind.Utc)]
    public void ApiJson_WritesStoredDateTimeAsUtcWithZ(DateTimeKind kind)
    {
        var value = new DateTime(2026, 10, 27, 8, 0, 0, kind);
        var json = JsonSerializer.Serialize(new { at = value, maybe = (DateTime?)value, none = (DateTime?)null }, ApiOptions());
        Assert.Equal("""{"at":"2026-10-27T08:00:00Z","maybe":"2026-10-27T08:00:00Z","none":null}""", json);
    }

    [Fact]
    public void ApiJson_ConvertsLocalDateTimeToUtc()
    {
        var local = new DateTime(2026, 10, 27, 8, 0, 0, DateTimeKind.Local);
        Assert.Equal($"\"{local.ToUniversalTime():yyyy-MM-dd'T'HH:mm:ss'Z'}\"", JsonSerializer.Serialize(local, ApiOptions()));
    }

    [Fact]
    public void ApiJson_LeavesDateTimeOffsetAndDateOnlyAlone()
    {
        var json = JsonSerializer.Serialize(new { at = new DateTimeOffset(2026, 10, 27, 15, 0, 0, TimeSpan.FromHours(7)), day = new DateOnly(2026, 10, 27) }, ApiOptions());
        Assert.Equal("""{"at":"2026-10-27T15:00:00+07:00","day":"2026-10-27"}""", json);
    }

    [Fact]
    public void ApiJson_ReadsDateTimeAsBefore()
    {
        Assert.Equal(DateTimeKind.Utc, JsonSerializer.Deserialize<DateTime>("\"2026-10-27T08:00:00Z\"", ApiOptions()).Kind);
        Assert.Equal(DateTimeKind.Unspecified, JsonSerializer.Deserialize<DateTime>("\"2026-10-27T08:00:00\"", ApiOptions()).Kind);
    }
}
