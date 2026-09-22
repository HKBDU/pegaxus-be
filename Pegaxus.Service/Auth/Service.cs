using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Npgsql;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;
using MailService = Pegaxus.Service.MailService;

namespace Pegaxus.Service.Auth;

public class Service : IService
{
    private readonly AppDbContext _db;
    private readonly IPasswordHasher<Account> _passwordHasher;
    private readonly IDistributedCache _cache;
    private readonly MailService.IService _mailService;

    public Service(
        AppDbContext db,
        IPasswordHasher<Account> passwordHasher,
        IDistributedCache cache,
        MailService.IService mailService)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _cache = cache;
        _mailService = mailService;
    }

    public async Task<Response.RegisterResponse> RegisterAsync(
        Request.RegisterRequest request,
        CancellationToken cancellationToken = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        if (await _db.Accounts.AnyAsync(account =>
                !account.IsDeleted &&
                account.Status == AccountStatus.ACTIVE &&
                account.Email.ToLower() == email,
                cancellationToken))
            throw new ConflictException("Email is already in use");

        var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        var account = new Account
        {
            AccountId = Guid.NewGuid(),
            Email = email,
            AccountType = AccountType.CUSTOMER,
            Status = AccountStatus.ACTIVE,
            CreatedAt = now,
            UpdatedAt = now
        };
        account.PasswordHash = _passwordHasher.HashPassword(account, request.Password);

        var customer = new Customer
        {
            CustomerId = Guid.NewGuid(),
            AccountId = account.AccountId,
            FullName = request.FullName.Trim(),
            CreatedAt = now,
            UpdatedAt = now
        };

        var otp = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var otpKey = $"otp:EMAIL_VERIFICATION:{email}";
        var cooldownKey = $"otp-cooldown:EMAIL_VERIFICATION:{email}";

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            _db.Accounts.Add(account);
            _db.Customers.Add(customer);
            await _db.SaveChangesAsync(cancellationToken);

            var otpValue = JsonSerializer.Serialize(new OtpCacheEntry(HashOtp(otp), 0));
            await _cache.SetStringAsync(otpKey, otpValue, new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
            }, cancellationToken);
            await _cache.SetStringAsync(cooldownKey, "1", new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1)
            }, cancellationToken);

            var htmlBody = $"""
                <!DOCTYPE html>
                <html lang="en">
                <head>
                    <meta charset="UTF-8">
                    <meta name="viewport" content="width=device-width, initial-scale=1.0">
                    <title>Verify Your Account</title>
                </head>
                <body style="margin:0;padding:0;background-color:#f7f7f8;font-family:Arial,Helvetica,sans-serif;color:#1f2937;">
                    <table width="100%" cellpadding="0" cellspacing="0" border="0" style="background-color:#f7f7f8;padding:40px 16px;">
                        <tr><td align="center">
                            <table width="100%" cellpadding="0" cellspacing="0" border="0" style="max-width:600px;background-color:#ffffff;border-radius:24px;overflow:hidden;box-shadow:0 16px 50px rgba(0,0,0,0.08);">
                                <tr><td style="padding:42px 40px 36px;background:linear-gradient(135deg,#ff7a00 0%,#ff9f43 100%);text-align:center;">
                                    <div style="font-size:34px;margin-bottom:20px;">🔐</div>
                                    <h1 style="margin:0;color:#ffffff;font-size:28px;line-height:1.3;">Verify Your Account</h1>
                                    <p style="margin:10px 0 0;color:rgba(255,255,255,0.88);font-size:15px;">One final step to keep your account secure.</p>
                                </td></tr>
                                <tr><td style="padding:42px 42px 34px;">
                                    <p style="margin:0 0 16px;font-size:17px;line-height:1.7;color:#374151;">
                                        Hello <strong style="color:#111827;">{WebUtility.HtmlEncode(customer.FullName)}</strong>,
                                    </p>
                                    <p style="margin:0 0 28px;font-size:15px;line-height:1.8;color:#6b7280;">
                                        We received a request to verify your account. Please use the one-time password below to continue.
                                    </p>
                                    <table width="100%" cellpadding="0" cellspacing="0" border="0">
                                        <tr><td align="center" style="padding:30px 20px;background-color:#fff7ed;border:2px solid #fed7aa;border-radius:18px;">
                                            <div style="margin-bottom:10px;color:#9a3412;font-size:12px;font-weight:700;letter-spacing:2px;text-transform:uppercase;">Your verification code</div>
                                            <div style="color:#f97316;font-size:42px;line-height:1.2;font-weight:800;letter-spacing:10px;">{otp}</div>
                                            <div style="margin-top:12px;color:#9a3412;font-size:13px;">Valid for <strong>5 minutes</strong></div>
                                        </td></tr>
                                    </table>
                                    <p style="margin:28px 0 0;padding:16px 18px;background-color:#fffaf5;border-left:4px solid #fb923c;border-radius:8px;color:#7c2d12;font-size:13px;line-height:1.6;">
                                        ⚠️ <strong>Security notice:</strong> Never share this OTP with anyone. Our team will never ask you for this code.
                                    </p>
                                    <p style="margin:28px 0 0;color:#9ca3af;font-size:13px;line-height:1.7;text-align:center;">
                                        If you didn't request this verification code, you can safely ignore this email.
                                    </p>
                                </td></tr>
                                <tr><td style="padding:24px 40px 30px;background-color:#111827;text-align:center;">
                                    <div style="color:#fb923c;font-size:18px;font-weight:700;margin-bottom:8px;">Pegaxus</div>
                                    <p style="margin:0;color:#9ca3af;font-size:12px;line-height:1.6;">This is an automated message.<br>Please do not reply to this email.</p>
                                </td></tr>
                            </table>
                            <p style="margin:22px 0 0;color:#9ca3af;font-size:11px;text-align:center;">© {DateTime.UtcNow.Year} Pegaxus. All rights reserved.</p>
                        </td></tr>
                    </table>
                </body>
                </html>
                """;

            await _mailService.SendMail(new MailService.MailContent
            {
                To = email,
                Subject = "Verify your Pegaxus account",
                Body = htmlBody
            });
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ConflictException("Email is already in use");
        }
        catch
        {
            await _cache.RemoveAsync(otpKey, CancellationToken.None);
            await _cache.RemoveAsync(cooldownKey, CancellationToken.None);
            throw;
        }

        return new Response.RegisterResponse
        {
            AccountId = account.AccountId,
            CustomerId = customer.CustomerId,
            Email = account.Email,
            EmailVerified = false
        };
    }

    public async Task ForgotPasswordAsync(
        Request.ForgotPasswordRequest request,
        CancellationToken cancellationToken = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var otpKey = $"otp:PASSWORD_RESET:{email}";
        var cooldownKey = $"otp-cooldown:PASSWORD_RESET:{email}";

        if (await _cache.GetStringAsync(cooldownKey, cancellationToken) is not null)
            throw new TooManyRequestsException("Please wait before requesting another OTP");

        await _cache.SetStringAsync(cooldownKey, "1", new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1)
        }, cancellationToken);

        var accountExists = await _db.Accounts.AnyAsync(account =>
            !account.IsDeleted &&
            account.Status == AccountStatus.ACTIVE &&
            account.Email.ToLower() == email,
            cancellationToken);
        if (!accountExists)
            return;

        var otp = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        await _cache.SetStringAsync(otpKey, JsonSerializer.Serialize(new OtpCacheEntry(HashOtp(otp), 0)),
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
            }, cancellationToken);

        try
        {
            await _mailService.SendMail(new MailService.MailContent
            {
                To = email,
                Subject = "Reset your Pegaxus password",
                Body = $"<p>Your password reset OTP is <strong>{otp}</strong>. It expires in 5 minutes.</p>"
            });
        }
        catch
        {
            await _cache.RemoveAsync(otpKey, CancellationToken.None);
            await _cache.RemoveAsync(cooldownKey, CancellationToken.None);
            throw;
        }
    }

    private static string HashOtp(string otp) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(otp)));

    private sealed record OtpCacheEntry(string CodeHash, int Attempts);
}
