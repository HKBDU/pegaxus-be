using System.Security.Cryptography;
using Pegaxus.Service.Exceptions;

namespace Pegaxus.Service.Common;

/// <summary>Business codes shaped <c>PREFIX-YYYYMMDD-XXXX</c> with four random digits, as the API contracts define.</summary>
public static class CodeGeneration
{
    private const int MaxAttempts = 20;

    /// <param name="exists">Whether the code is already taken, including rows that are added but not saved yet.</param>
    /// <param name="errorCode">Stable error code when every attempt collides; modules keep the code their clients already know.</param>
    public static async Task<string> NextAsync(string prefix, DateTime date, Func<string, Task<bool>> exists,
        string errorCode = "CODE_GENERATION_FAILED", string? failureMessage = null)
    {
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var code = $"{prefix}-{date:yyyyMMdd}-{RandomNumberGenerator.GetInt32(10_000):D4}";
            if (!await exists(code)) return code;
        }
        throw new ConflictException(failureMessage ?? $"Unable to generate a unique {prefix} code. Please try again.", errorCode);
    }
}
