using Pegaxus.Service.Exceptions;

namespace Pegaxus.Service.Common;

public static class DailyCodes
{
    /// <summary>
    /// Next free code for a "PREFIX-yyyyMMdd-" prefix: one above the highest number already used, including deleted rows so a code is never reused.
    /// Two concurrent creators can still pick the same number; the caller's unique index settles that.
    /// </summary>
    public static string Next(string prefix, IEnumerable<string> existingCodes, string exhaustedErrorCode)
    {
        var next = existingCodes.Select(code => int.TryParse(code[prefix.Length..], out var number) ? number : 0).DefaultIfEmpty().Max() + 1;
        if (next > 9999) throw new ConflictException("The daily code range is exhausted", exhaustedErrorCode);
        return prefix + next.ToString("D4");
    }
}
