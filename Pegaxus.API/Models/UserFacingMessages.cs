using System.Text.RegularExpressions;
using Pegaxus.Service.Common;

namespace Pegaxus.API.Models;

/// <summary>
/// The API contract exposes <c>errorCode</c> for client logic and a separate, plain-language <c>message</c> for people.
/// A message written at the throw site is shown as it is when it passes <see cref="UserFacingText"/>; otherwise (or when the
/// error did not come from our own code) the user gets a safe English sentence. Exception text is never trusted blindly.
/// </summary>
public static partial class UserFacingMessages
{
    public const string InvalidRequest = "Please check the information provided and try again.";
    public const string InvalidFields = "Please review the highlighted fields and try again.";
    public const string SystemError = "A system error occurred. Please try again later.";

    /// <summary>
    /// Fixed copy for codes raised by the framework or the middleware, where there is no author-written message to keep.
    /// Codes thrown by services carry their own sentence and need no entry here.
    /// </summary>
    private static readonly Dictionary<string, string> Overrides = new(StringComparer.Ordinal)
    {
        ["VALUE_OUT_OF_RANGE"] = "A numeric value is outside the allowed range.",
        ["VALUE_TOO_LONG"] = "A text value is too long.",
        ["PAYLOAD_TOO_LARGE"] = "The request body must not exceed 1 MB.",
        ["CONCURRENT_UPDATE"] = "The record changed. Reload it and try again."
    };

    public static string Success(string? sourceMessage, int statusCode) =>
        !string.IsNullOrWhiteSpace(sourceMessage) && !UserFacingText.ContainsInternalTerms(sourceMessage)
            ? sourceMessage
            : statusCode == StatusCodes.Status201Created
                ? "The item was created successfully."
                : "Your request was completed successfully.";

    /// <param name="errorCode">Stable code for client logic; never shown.</param>
    /// <param name="sourceMessage">The sentence written by our own code, or null when the error is not one of ours.</param>
    public static string Error(string errorCode, string? sourceMessage, int statusCode)
    {
        if (Overrides.TryGetValue(errorCode, out var fixedMessage))
            return fixedMessage;
        return !string.IsNullOrWhiteSpace(sourceMessage) && !UserFacingText.ContainsInternalTerms(sourceMessage)
            ? sourceMessage
            : ErrorByStatus(statusCode);
    }

    /// <summary>Keeps a validation sentence that is safe to read; otherwise names the field in plain words.</summary>
    public static string FieldError(string? field, string? sourceMessage)
    {
        if (!string.IsNullOrWhiteSpace(sourceMessage) && !UserFacingText.ContainsInternalTerms(sourceMessage))
            return sourceMessage;
        var name = Words(field);
        return name.Length == 0 ? "Please enter a valid value." : $"Please enter a valid value for {name}.";
    }

    private static string Words(string? field)
    {
        if (string.IsNullOrWhiteSpace(field) || field == "$")
            return string.Empty;
        var last = field[(field.LastIndexOf('.') + 1)..];
        last = Index().Replace(last, string.Empty).TrimStart('$');
        return CamelBoundary().Replace(last, " $1").Replace('_', ' ').Trim().ToLowerInvariant();
    }

    private static string ErrorByStatus(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => InvalidRequest,
        StatusCodes.Status401Unauthorized => "Your session has expired. Please sign in again.",
        StatusCodes.Status403Forbidden => "You do not have permission to perform this action.",
        StatusCodes.Status404NotFound => "The requested item could not be found.",
        StatusCodes.Status409Conflict => "This item cannot be updated right now. Refresh the page and try again.",
        StatusCodes.Status413PayloadTooLarge => "The submitted file or request is too large.",
        StatusCodes.Status422UnprocessableEntity => "This action cannot be completed with the current information.",
        StatusCodes.Status429TooManyRequests => "Too many requests were made. Please try again shortly.",
        _ => SystemError
    };

    [GeneratedRegex(@"\[\d+\]")]
    private static partial Regex Index();

    [GeneratedRegex(@"(?<=[a-z0-9])([A-Z])")]
    private static partial Regex CamelBoundary();
}
