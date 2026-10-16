using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.OutboxService;

using static Pegaxus.Service.Common.DbTime;

using Claim = System.Security.Claims.Claim;
using AppException = Pegaxus.Service.Exceptions.Exceptions;

namespace Pegaxus.Service.Auth;

public sealed partial class Service
{
    private const int OtpLifetimeMinutes = 5;

    private async Task<DateTimeOffset> SendOtpAsync(
        string email,
        string purpose,
        string fullName,
        CancellationToken cancellationToken)
    {
        var otp = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(OtpLifetimeMinutes);
        await _cache.SetStringAsync(
            OtpKey(email, purpose),
            JsonSerializer.Serialize(new OtpCacheEntry(Hash(otp), 0, expiresAt)),
            new DistributedCacheEntryOptions { AbsoluteExpiration = expiresAt },
            cancellationToken);
        await SetCooldownAsync(email, purpose, cancellationToken);

        try
        {
            var isVerification = purpose == EmailVerification;
            var mail = new MailService.MailContent
            {
                To = email,
                Subject = isVerification ? "Verify your Pegaxus account" : "Reset your Pegaxus password",
                Body = MailService.EmailBodies.OtpBody(isVerification, fullName, otp, OtpLifetimeMinutes)
            };
            if (OutboxQueue.IsEnabled(_db))
            {
                OutboxQueue.AddEmail(_db, mail, UtcNow());
                await _db.SaveChangesAsync(cancellationToken);
            }
            else
                await _mailService.SendMail(mail, cancellationToken);
        }
        catch
        {
            await RemoveOtpAsync(email, purpose);
            throw;
        }

        return expiresAt;
    }

    private async Task ValidateOtpAsync(
        string email,
        string purpose,
        string otp,
        CancellationToken cancellationToken)
    {
        var key = OtpKey(email, purpose);
        var value = await _cache.GetStringAsync(key, cancellationToken);
        OtpCacheEntry? entry = null;
        if (value is not null)
        {
            try
            {
                entry = JsonSerializer.Deserialize<OtpCacheEntry>(value);
            }
            catch (JsonException)
            {
                // Corrupt cache entries are unusable and treated as expired.
            }
        }

        if (entry is null || entry.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            await _cache.RemoveAsync(key, cancellationToken);
            throw new AppException("OTP has expired", 400, "OTP_EXPIRED");
        }

        if (MatchesHash(otp, entry.CodeHash))
            return;

        var attempts = entry.Attempts + 1;
        if (attempts >= 5)
        {
            await _cache.RemoveAsync(key, cancellationToken);
            throw new AppException("Too many invalid attempts. Please request a new code", 429, "OTP_TOO_MANY_ATTEMPTS");
        }

        await _cache.SetStringAsync(
            key,
            JsonSerializer.Serialize(entry with { Attempts = attempts }),
            new DistributedCacheEntryOptions { AbsoluteExpiration = entry.ExpiresAt },
            cancellationToken);
        throw new AppException("OTP is invalid", 400, "OTP_INVALID");
    }

    private async Task EnsureCooldownExpiredAsync(string email, string purpose, CancellationToken cancellationToken)
    {
        if (await _cache.GetStringAsync(CooldownKey(email, purpose), cancellationToken) is not null)
            throw new AppException("Please wait 60 seconds before requesting another code", 429, "OTP_RESEND_TOO_SOON");
    }

    private async Task SetCooldownAsync(string email, string purpose, CancellationToken cancellationToken) =>
        await _cache.SetStringAsync(
            CooldownKey(email, purpose),
            "1",
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1) },
            cancellationToken);

    private async Task RemoveOtpAsync(string email, string purpose)
    {
        await _cache.RemoveAsync(OtpKey(email, purpose), CancellationToken.None);
        await _cache.RemoveAsync(CooldownKey(email, purpose), CancellationToken.None);
    }

    private static string OtpKey(string email, string purpose) => $"otp:{purpose}:{email}";

    private static string CooldownKey(string email, string purpose) => $"otp-cooldown:{purpose}:{email}";

    private sealed record OtpCacheEntry(string CodeHash, int Attempts, DateTimeOffset ExpiresAt);
}
