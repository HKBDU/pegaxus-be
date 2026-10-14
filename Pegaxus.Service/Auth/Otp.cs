using System.Net;
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
    private async Task<DateTimeOffset> SendOtpAsync(
        string email,
        string purpose,
        string fullName,
        CancellationToken cancellationToken)
    {
        var otp = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(5);
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
                Body = $"""
                    <!doctype html>
                    <html lang="en">
                    <body style="margin:0;background:#f3f5f7;font-family:Arial,sans-serif;color:#17202a;">
                      <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="padding:32px 16px;">
                        <tr>
                          <td align="center">
                            <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="max-width:560px;background:#ffffff;border-radius:16px;overflow:hidden;box-shadow:0 8px 24px rgba(23,32,42,.08);">
                              <tr><td style="background:#12344d;padding:24px 32px;color:#ffffff;font-size:24px;font-weight:700;">PEGAXUS</td></tr>
                              <tr>
                                <td style="padding:32px;">
                                  <h1 style="margin:0 0 16px;font-size:24px;">{(isVerification ? "Verify your email" : "Reset your password")}</h1>
                                  <p style="margin:0 0 12px;line-height:1.6;">Hello {WebUtility.HtmlEncode(fullName)},</p>
                                  <p style="margin:0 0 24px;line-height:1.6;">{(isVerification ? "Use this code to verify your Pegaxus account." : "Use this code to reset your Pegaxus password.")}</p>
                                  <div style="padding:18px;text-align:center;background:#fff4e6;border:1px solid #f5a623;border-radius:12px;font-size:32px;font-weight:700;letter-spacing:8px;color:#12344d;">{otp}</div>
                                  <p style="margin:24px 0 0;line-height:1.6;color:#5d6d7e;">This code expires in 5 minutes. Never share it with anyone.</p>
                                  <p style="margin:12px 0 0;line-height:1.6;color:#5d6d7e;">If you did not request this, you can safely ignore this email.</p>
                                </td>
                              </tr>
                            </table>
                          </td>
                        </tr>
                      </table>
                    </body>
                    </html>
                    """
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
