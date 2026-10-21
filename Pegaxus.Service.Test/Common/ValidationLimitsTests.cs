using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Pegaxus.API.Middleware;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;

namespace Pegaxus.Service.Test;

public class ValidationLimitsTests
{
    [Theory]
    [InlineData(1, 10, 0)]
    [InlineData(3, 20, 40)]
    [InlineData(int.MaxValue, 100, -1)]
    [InlineData(0, 10, -1)]
    [InlineData(1, 0, -1)]
    public void PagingOffset_RejectsOverflowAndNonPositiveInput(int pageIndex, int pageSize, int expected)
    {
        if (expected >= 0) Assert.Equal(expected, Paging.Offset(pageIndex, pageSize));
        else Assert.Equal("VALIDATION_ERROR", Assert.Throws<BadRequestException>(() => Paging.Offset(pageIndex, pageSize)).ErrorCode);
    }

    [Theory]
    [InlineData("DRIVER", true)]
    [InlineData(" driver ", true)]
    [InlineData("99", false)]
    [InlineData("1", false)]
    [InlineData("DRIVER,ESCORT", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void EnumParser_AcceptsOnlyDeclaredNames(string? value, bool expected)
    {
        Assert.Equal(expected, EnumParser.TryParseDefined<EmployeeRole>(value, out var role));
        if (expected) Assert.Equal(EmployeeRole.DRIVER, role);
    }

    [Theory]
    [InlineData("0.0000000000000000000001", false)]
    [InlineData("19.9", false)]
    [InlineData("20", true)]
    [InlineData("520.1", true)]
    [InlineData("520.10", true)]
    [InlineData("520.15", false)]
    [InlineData("1500", true)]
    [InlineData("1500.1", false)]
    [InlineData("99999", false)]
    public void HorseWeight_EnforcesRangeAndScale(string value, bool valid) =>
        Assert.Equal(valid, InputRules.IsValid(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture), Limits.HorseWeightKg));

    [Theory]
    [InlineData("0", "0", false)]
    [InlineData("10.1234", "20.1234", true)]
    [InlineData("10.12345", "20.1234", false)]
    [InlineData("90.0001", "0", false)]
    [InlineData("90", "180", true)]
    [InlineData("-90", "-180", true)]
    [InlineData("0", "5", true)]
    [InlineData("5", "-180.0001", false)]
    public void Coordinates_AllowFourDecimalsAndRejectTheOrigin(string latitude, string longitude, bool valid) =>
        Assert.Equal(valid, Coordinates.IsValid(
            decimal.Parse(latitude, System.Globalization.CultureInfo.InvariantCulture), decimal.Parse(longitude, System.Globalization.CultureInfo.InvariantCulture)));

    [Theory]
    [InlineData("+33123456789", true)]
    [InlineData("+33 1 23 45 67 89", true)]
    [InlineData("(0033) 123-456-789", false)]
    [InlineData("0900000000", false)]
    [InlineData("1234567", false)]
    [InlineData("1234567890123456", false)]
    [InlineData("abc12345678", false)]
    public void Phones_FollowTheFrontendRule(string value, bool valid) => Assert.Equal(valid, Phones.IsValid(value));

    [Theory]
    [InlineData("a@b.com", true)]
    [InlineData("not-an-email", false)]
    [InlineData("", false)]
    public void Emails_UseTheSharedRule(string value, bool valid)
    {
        if (valid) Emails.EnsureValid(value);
        else Assert.Equal("INVALID_EMAIL", Assert.Throws<BadRequestException>(() => Emails.EnsureValid(value)).ErrorCode);
    }

    [Fact]
    public void Text_NormalizesNamesAndNotes()
    {
        Assert.Equal("Ada Lovelace", Text.NormalizeName("  Ada \t\n  Love\u200blace  "));
        Assert.Equal("Ada Lovelace", Text.NormalizeName(" Ada\u0007   Lovelace "));
        Assert.Equal("", Text.NormalizeName("\u200b\u0007 "));
        Assert.Equal("line1\nline2", Text.NormalizeNote(" line1\n\u200bline2\u0007 "));
    }

    [Theory]
    [InlineData("22003", 422, "VALUE_OUT_OF_RANGE")]
    [InlineData("22001", 400, "VALUE_TOO_LONG")]
    public async Task Middleware_MapsColumnOverflowToClientErrorsWithoutLeakingDatabaseDetails(string sqlState, int status, string code)
    {
        var middleware = new GlobalExceptionHandlerMiddleware(NullLogger<GlobalExceptionHandlerMiddleware>.Instance);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var inner = new PostgresException("secret column detail", "ERROR", "ERROR", sqlState);

        await middleware.InvokeAsync(context, _ => throw new DbUpdateException("save failed", inner));

        Assert.Equal(status, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        var body = JsonDocument.Parse(context.Response.Body).RootElement;
        Assert.Equal(code, body.GetProperty("errorCode").GetString());
        Assert.DoesNotContain("secret", body.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Middleware_Maps413ForOversizedBodies()
    {
        var middleware = new GlobalExceptionHandlerMiddleware(NullLogger<GlobalExceptionHandlerMiddleware>.Instance);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context, _ => throw new BadHttpRequestException("too big", StatusCodes.Status413PayloadTooLarge));

        Assert.Equal(413, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        Assert.Equal("PAYLOAD_TOO_LARGE", JsonDocument.Parse(context.Response.Body).RootElement.GetProperty("errorCode").GetString());
    }

    [Fact]
    public void LimitsClass_MatchesTheSharedLimitsTable()
    {
        var file = FindLimitsFile();
        if (file is null) return; // The table lives in the documentation repository; skip when it is not checked out next to this one.
        var table = JsonDocument.Parse(File.ReadAllText(file)).RootElement;
        var decimals = new Dictionary<string, DecimalRange>
        {
            ["horseWeightKg"] = Limits.HorseWeightKg, ["horseHeightCm"] = Limits.HorseHeightCm, ["planDistanceKm"] = Limits.PlanDistanceKm,
            ["trackingSpeedKmh"] = Limits.TrackingSpeedKmh, ["trackingAccuracyMeters"] = Limits.TrackingAccuracyMeters,
            ["headingDegrees"] = Limits.HeadingDegrees, ["latitude"] = Limits.Latitude, ["longitude"] = Limits.Longitude
        };
        Assert.Equal(decimals.Count, table.GetProperty("decimals").EnumerateObject().Count());
        foreach (var (name, range) in decimals)
        {
            var entry = table.GetProperty("decimals").GetProperty(name);
            Assert.Equal(entry.GetProperty("min").GetDecimal(), range.Min);
            Assert.Equal(entry.GetProperty("max").GetDecimal(), range.Max);
            Assert.Equal(entry.GetProperty("scale").GetInt32(), range.Scale);
        }
        var integers = new Dictionary<string, int>
        {
            ["planDurationMinutesMax"] = Limits.PlanDurationMinutesMax, ["invoiceQuantityMin"] = Limits.InvoiceQuantityMin,
            ["invoiceQuantityMax"] = Limits.InvoiceQuantityMax, ["validityPeriodDaysMax"] = Limits.ValidityPeriodDaysMax,
            ["noteMaxLength"] = Limits.NoteMaxLength, ["horseMaxAgeYears"] = Limits.HorseMaxAgeYears,
            ["accountMinAgeYears"] = Limits.AccountMinAgeYears, ["accountMaxAgeYears"] = Limits.AccountMaxAgeYears,
            ["dateWindowYears"] = Limits.DateWindowYears, ["phoneMinDigits"] = Limits.PhoneMinDigits,
            ["phoneMaxDigits"] = Limits.PhoneMaxDigits, ["coordinateScale"] = Limits.CoordinateScale
        };
        Assert.Equal(integers.Count, table.GetProperty("integers").EnumerateObject().Count());
        foreach (var (name, value) in integers)
            Assert.Equal(table.GetProperty("integers").GetProperty(name).GetInt32(), value);
    }

    private static string? FindLimitsFile()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "pegaxus-document", "api-contract", "validation-limits.json");
            if (File.Exists(candidate)) return candidate;
            candidate = Path.Combine(directory.FullName, "..", "pegaxus-document", "api-contract", "validation-limits.json");
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
        }
        return null;
    }
}
