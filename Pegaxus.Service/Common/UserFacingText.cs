using System.Text.RegularExpressions;

namespace Pegaxus.Service.Common;

/// <summary>
/// One definition of "text a person may read". Messages, validation errors and notifications are shown to users as written,
/// so they must not contain role abbreviations, enum or error codes, entity class names or framework error text.
/// </summary>
public static partial class UserFacingText
{
    /// <summary>
    /// Runtime check. Language is deliberately not checked here: names typed by users (for example Vietnamese customer names)
    /// legitimately appear inside messages.
    /// </summary>
    public static bool ContainsInternalTerms(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        (RoleAbbreviation().IsMatch(value) || InternalCode().IsMatch(value) || EntityName().IsMatch(value) || FrameworkText().IsMatch(value));

    /// <summary>Plain wording for an enum value inside a sentence: READY_FOR_SUBMISSION becomes "ready for submission".</summary>
    public static string Label<T>(T value) where T : struct, Enum => Label(value.ToString());

    public static string Label(string? value) => (value ?? string.Empty).ToLowerInvariant().Replace('_', ' ');

    /// <summary>Static check for message literals written in source: Vietnamese copy must be rewritten in English.</summary>
    public static bool ContainsVietnamese(string? value) => !string.IsNullOrEmpty(value) && VietnameseLetter().IsMatch(value);

    // Employee and reference codes such as "TS-0001" are kept: a hyphen next to the letters means it is a code, not a role.
    [GeneratedRegex(@"(?<![\w-])(?:TS|LM|RC|DRV|ESC)(?![\w-])", RegexOptions.CultureInvariant)]
    private static partial Regex RoleAbbreviation();

    // SUBMIT_FOR_LEGAL_REVIEW, READY_FOR_REVIEW, HOLD_EXPIRED, ...
    [GeneratedRegex(@"\b[A-Z]+(?:_[A-Z0-9]+)+\b", RegexOptions.CultureInvariant)]
    private static partial Regex InternalCode();

    [GeneratedRegex(@"\b(?:DocumentReview|DocumentRequest|DocumentVersion|DossierRequirement|DossierTemplate|ComplianceDossier|ComplianceDocument|ComplianceRequirement)s?\b", RegexOptions.CultureInvariant)]
    private static partial Regex EntityName();

    // Model binding and serializer errors: "The JSON value could not be converted to System.Guid. Path: $.id | LineNumber: 0 ..."
    [GeneratedRegex(@"\bSystem\.\w+|Path: \$|LineNumber|BytePositionInLine|\bException\b|\bat Pegaxus\.", RegexOptions.CultureInvariant)]
    private static partial Regex FrameworkText();

    // Letters that exist in Vietnamese but not in English: ă â ê ô ơ ư đ and every tone-marked vowel.
    [GeneratedRegex(@"[ăâêôơưđĂÂÊÔƠƯĐẠ-ỹ]", RegexOptions.CultureInvariant)]
    private static partial Regex VietnameseLetter();
}
