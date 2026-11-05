using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Pegaxus.API.Middleware;
using Pegaxus.API.Models;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.NotificationService;

namespace Pegaxus.Service.Test;

/// <summary>
/// Everything a person reads from the API must be plain English: no role abbreviations, enum or error codes, entity names or
/// framework text. <c>errorCode</c> stays for client logic only.
/// </summary>
public class UserFacingMessageTests
{
    private const string StringBody = "(?:[^\"\\\\\\r\\n]|\\\\.)*";

    private static readonly Regex[] MessageSites =
    [
        // throw new ConflictException("message", "ERROR_CODE")
        new(@"new\s+(?:Exceptions|BadRequestException|UnauthorizedException|ForbiddenException|NotFoundException|ConflictException|UnprocessableEntityException|TooManyRequestsException)\s*\(\s*(?<interp>\$?)""(?<text>" + StringBody + @")""", RegexOptions.Compiled),
        // new FieldError("field", "message")
        new(@"new\s+FieldError\s*\(\s*[^,]+,\s*(?<interp>\$?)""(?<text>" + StringBody + @")""", RegexOptions.Compiled),
        // Title = "..." / Message = "..." on notifications and responses
        new(@"\b(?:Title|Message)\s*=\s*(?<interp>\$?)""(?<text>" + StringBody + @")""", RegexOptions.Compiled),
        // Success(value, "message") / SuccessCreated(value, "message")
        new(@"\bSuccess(?:Created|Response)?\([^;]*?,\s*(?<interp>\$?)""(?<text>" + StringBody + @")""\s*[,)]", RegexOptions.Compiled)
    ];

    private static readonly Regex AnyLiteral = new(@"\$?""(?<text>" + StringBody + @")""", RegexOptions.Compiled);
    private static readonly Regex ErrorCodeLiteral = new(@",\s*""(?<code>[A-Z][A-Z0-9_]+)""\s*[,)]", RegexOptions.Compiled);
    // A single capitalised status such as CONFIRMED or ACTIVE is enum text; real acronyms are listed below.
    private static readonly Regex ShoutedWord = new(@"\b(?!(?:IPAFFS|TRACES|CHED|EORI|IATA|HTTP|HTTPS|HTML|JSON|ISO|VAT|VND|USD|EUR|GBP|PDF|CSV|URL|OTP|GPS|ATA|BCP|APHA|IMO|UUID|JPG|JPEG|PNG|UELN)\b)[A-Z]{4,}\b", RegexOptions.Compiled);
    private static readonly Regex InterpolationHole = new(@"\{[^{}]*\}", RegexOptions.Compiled);

    [Fact]
    public void MessageLiterals_AreWrittenForPeople()
    {
        var offenders = new List<string>();
        foreach (var (file, text) in SourceFiles())
            foreach (var site in MessageSites)
                foreach (Match match in site.Matches(text))
                {
                    var message = Plain(match.Groups["text"].Value, match.Groups["interp"].Length > 0);
                    if (UserFacingText.ContainsInternalTerms(message) || ShoutedWord.IsMatch(message))
                        offenders.Add($"{file}:{LineOf(text, match.Index)}  {message}");
                }

        Assert.True(offenders.Count == 0, "Messages shown to users contain role abbreviations, codes or entity names:\n" + string.Join('\n', offenders.Distinct()));
    }

    [Fact]
    public void StringLiterals_AreNotVietnamese()
    {
        var offenders = new List<string>();
        foreach (var (file, text) in SourceFiles())
        {
            // UserFacingText holds the Vietnamese letter set itself; the seed file holds real Vietnamese personal names.
            if (file.EndsWith("UserFacingText.cs", StringComparison.Ordinal) || file.EndsWith("AppDbContextSeedData.cs", StringComparison.Ordinal)) continue;
            var lines = text.Split('\n');
            for (var index = 0; index < lines.Length; index++)
            {
                if (lines[index].TrimStart().StartsWith("//", StringComparison.Ordinal)) continue;
                foreach (Match literal in AnyLiteral.Matches(lines[index]))
                    if (UserFacingText.ContainsVietnamese(literal.Groups["text"].Value))
                        offenders.Add($"{file}:{index + 1}  {literal.Groups["text"].Value}");
            }
        }

        Assert.True(offenders.Count == 0, "Vietnamese copy must be written in English:\n" + string.Join('\n', offenders));
    }

    [Fact]
    public void EveryErrorCode_HasAReadableMessageEvenWithoutAuthoredText()
    {
        var codes = SourceFiles().SelectMany(file => ErrorCodeLiteral.Matches(file.Text).Select(match => match.Groups["code"].Value))
            .Concat(["VALIDATION_ERROR", "UNAUTHORIZED", "FORBIDDEN", "NOT_FOUND", "CONFLICT", "TOO_MANY_REQUESTS", "SYSTEM_ERROR", "CONCURRENT_UPDATE", "VALUE_OUT_OF_RANGE", "VALUE_TOO_LONG", "PAYLOAD_TOO_LARGE"])
            .Distinct().ToArray();
        Assert.True(codes.Length > 100, "The error code scan found too few codes; the pattern needs updating.");

        foreach (var code in codes)
            foreach (var status in new[] { 400, 401, 403, 404, 409, 413, 422, 429, 500 })
            {
                var message = UserFacingMessages.Error(code, null, status);
                Assert.False(string.IsNullOrWhiteSpace(message), code);
                Assert.False(UserFacingText.ContainsInternalTerms(message), $"{code}: {message}");
                Assert.DoesNotContain(code, message);
            }
    }

    [Theory]
    [InlineData("Please choose Save draft or submit for legal review.", "Please choose Save draft or submit for legal review.")]
    [InlineData("Document SATISFIED by TS", "This action cannot be completed with the current information.")]
    [InlineData("Transport status must be READY_FOR_REVIEW", "This action cannot be completed with the current information.")]
    [InlineData("The ComplianceDocument was not found", "This action cannot be completed with the current information.")]
    public void Error_ShowsAuthoredTextOnlyWhenItIsSafe(string source, string expected) =>
        Assert.Equal(expected, UserFacingMessages.Error("SOME_RULE", source, 422));

    [Fact]
    public void Error_UsesFixedCopyForFrameworkCodes() =>
        Assert.Equal("A text value is too long.", UserFacingMessages.Error("VALUE_TOO_LONG", "string data, right truncated", 400));

    [Theory]
    [InlineData("Segments[0].FromLocationId", "The value is not valid", "The value is not valid")]
    [InlineData("Segments[0].FromLocationId", "The JSON value could not be converted to System.Guid. Path: $.segments[0] | LineNumber: 0", "Please enter a valid value for from location id.")]
    [InlineData("action", "Action must be SAVE_DRAFT or SUBMIT", "Please enter a valid value for action.")]
    [InlineData("$", "Path: $ | LineNumber: 1", "Please enter a valid value.")]
    public void FieldError_KeepsSafeTextAndNamesTheFieldOtherwise(string field, string source, string expected) =>
        Assert.Equal(expected, UserFacingMessages.FieldError(field, source));

    [Fact]
    public void Success_ReplacesInternalWordingWithAPlainSentence()
    {
        Assert.Equal("Document approved successfully.", UserFacingMessages.Success("Document approved successfully.", 200));
        Assert.Equal("Your request was completed successfully.", UserFacingMessages.Success("SATISFIED by TS", 200));
        Assert.Equal("The item was created successfully.", UserFacingMessages.Success("Created DOCUMENT_REVIEW", 201));
    }

    [Fact]
    public async Task Middleware_ShowsOwnTextButHidesTechnicalAndInternalWording()
    {
        async Task<(string Message, string ErrorCode)> RunAsync(Exception exception)
        {
            var context = new DefaultHttpContext();
            context.Response.Body = new MemoryStream();
            await new GlobalExceptionHandlerMiddleware(NullLogger<GlobalExceptionHandlerMiddleware>.Instance)
                .InvokeAsync(context, _ => throw exception);
            context.Response.Body.Position = 0;
            var body = JsonDocument.Parse(context.Response.Body).RootElement;
            return (body.GetProperty("message").GetString()!, body.GetProperty("errorCode").GetString()!);
        }

        var clear = await RunAsync(new UnprocessableEntityException("Segments must be continuous.", "SEGMENT_NOT_CONTINUOUS"));
        Assert.Equal(("Segments must be continuous.", "SEGMENT_NOT_CONTINUOUS"), clear);

        var internalWording = await RunAsync(new UnprocessableEntityException("Dossier must be in READY_FOR_REVIEW", "DOSSIER_STATE"));
        Assert.Equal("This action cannot be completed with the current information.", internalWording.Message);
        Assert.Equal("DOSSIER_STATE", internalWording.ErrorCode);

        var framework = await RunAsync(new ArgumentException("Parameter 'x' of Pegaxus.Service.Thing was null"));
        Assert.Equal("Please check the information provided and try again.", framework.Message);
    }

    [Fact]
    public async Task Middleware_ReplacesInternalWordingInFieldErrors()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var errors = new[] { new FieldError("action", "Use SAVE_DRAFT"), new FieldError("note", "Note is required.") };
        await new GlobalExceptionHandlerMiddleware(NullLogger<GlobalExceptionHandlerMiddleware>.Instance)
            .InvokeAsync(context, _ => throw new FieldValidationException(errors));

        context.Response.Body.Position = 0;
        var items = JsonDocument.Parse(context.Response.Body).RootElement.GetProperty("errors").EnumerateArray()
            .Select(item => item.GetProperty("message").GetString()).ToArray();
        Assert.Equal(["Please enter a valid value for action.", "Note is required."], items);
    }

    [Theory]
    [InlineData("Transport TR-1 moved to READY_FOR_REVIEW")]
    [InlineData("Approved by TS")]
    [InlineData("DocumentReview created")]
    public void NotificationText_NeverShowsInternalWording(string value)
    {
        Assert.Equal("Open this notification to view the latest details.", UserFacingNotificationText.Message(value));
        Assert.DoesNotContain("_", UserFacingNotificationText.Title(NotificationType.TRANSPORT, NotificationPriority.HIGH, value));
    }

    [Fact]
    public void NotificationText_KeepsReferenceCodesAndPeopleNames()
    {
        const string message = "Emergency cost request approved (ECR-20261105-0001) for Nguyễn Văn An";
        Assert.Equal(message, UserFacingNotificationText.Message(message));
    }

    private static string Plain(string text, bool interpolated) => interpolated ? InterpolationHole.Replace(text, string.Empty) : text;

    private static int LineOf(string text, int index) => text.AsSpan(0, index).Count('\n') + 1;

    private static IEnumerable<(string File, string Text)> SourceFiles()
    {
        var root = FindRepositoryRoot();
        Assert.NotNull(root);
        foreach (var project in new[] { "Pegaxus.Service", "Pegaxus.API" })
            foreach (var path in Directory.EnumerateFiles(Path.Combine(root!, project), "*.cs", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(root!, path).Replace('\\', '/');
                if (relative.Contains("/obj/") || relative.Contains("/bin/")) continue;
                yield return (relative, File.ReadAllText(path));
            }
    }

    private static string? FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Pegaxus.sln")))
                return directory.FullName;
        return null;
    }
}
