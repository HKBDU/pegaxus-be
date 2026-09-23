using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Npgsql;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.JwtService;
using MailService = Pegaxus.Service.MailService;
using AccountEntity = Pegaxus.Repository.Entities.Account;
using JwtClaim = System.Security.Claims.Claim;

namespace Pegaxus.Service.Auth;

public class Service : IService
{
    private readonly AppDbContext _db;
    private readonly IPasswordHasher<AccountEntity> _passwordHasher;
    private readonly IDistributedCache _cache;
    private readonly MailService.IService _mailService;
    private readonly IJwtService _jwtService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    private static readonly TimeSpan LoginAttemptWindow = TimeSpan.FromMinutes(15);
    private const int MaxLoginFailures = 5;

    public Service(
        AppDbContext db,
        IPasswordHasher<AccountEntity> passwordHasher,
        IDistributedCache cache,
        MailService.IService mailService,
        IJwtService jwtService,
        IHttpContextAccessor httpContextAccessor)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _cache = cache;
        _mailService = mailService;
        _jwtService = jwtService;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<Response.LoginResponse> LoginAsync(
        Request.LoginRequest request,
        CancellationToken cancellationToken = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var account = await _db.Accounts
            .Include(a => a.CustomerAccount)
            .Include(a => a.EmployeeAccount)
            .FirstOrDefaultAsync(a => !a.IsDeleted && a.Email.ToLower() == email, cancellationToken);

        if (account is null)
            throw new UnauthorizedException("Email or password incorrect", "INVALID_CREDENTIALS");

        if (account.Status == AccountStatus.SUSPENDED)
            throw new ForbiddenException("Account suspended", "ACCOUNT_SUSPENDED");
        if (account.Status != AccountStatus.ACTIVE)
            throw new ForbiddenException("Account inactive", "ACCOUNT_INACTIVE");

        var failedKey = $"auth:login:failed:{account.AccountId}";
        var lockedKey = $"auth:login:locked:{account.AccountId}";
        if (await _cache.GetStringAsync(lockedKey, cancellationToken) is not null)
            throw new TooManyRequestsException("Too many failed login attempts. Please try again later.");

        var passwordResult = _passwordHasher.VerifyHashedPassword(account, account.PasswordHash, request.Password);
        if (passwordResult == PasswordVerificationResult.Failed)
        {
            await RecordLoginFailureAsync(failedKey, lockedKey, cancellationToken);
            throw new UnauthorizedException("Email or password incorrect", "INVALID_CREDENTIALS");
        }

        if (account.EmailVerifiedAt is null)
            throw new ForbiddenException("Account email has not been verified", "ACCOUNT_NOT_VERIFIED");

        var role = account.AccountType switch
        {
            AccountType.CUSTOMER when account.CustomerAccount is { IsDeleted: false } => "CUSTOMER",
            AccountType.EMPLOYEE when account.EmployeeAccount is { IsDeleted: false, IsActive: true } employee =>
                employee.Role switch
                {
                    EmployeeRole.LOGISTICS_MANAGER or EmployeeRole.TRANSPORT_SPECIALIST or
                        EmployeeRole.FLEET_ROUTE_COORDINATOR or EmployeeRole.DRIVER or EmployeeRole.ESCORT =>
                        employee.Role.ToString(),
                    _ => throw new ForbiddenException("Account role is not permitted to log in")
                },
            _ => throw new ForbiddenException("Account profile is inactive or unavailable")
        };

        await _cache.RemoveAsync(failedKey, cancellationToken);
        await _cache.RemoveAsync(lockedKey, cancellationToken);

        var claims = new List<JwtClaim>
        {
            new(ClaimTypes.NameIdentifier, account.AccountId.ToString()),
            new(ClaimTypes.Email, account.Email),
            new("account_type", account.AccountType.ToString()),
            new(ClaimTypes.Role, account.AccountType == AccountType.CUSTOMER
                ? "Customer"
                : role switch
                {
                    "LOGISTICS_MANAGER" => "LogisticsManager",
                    "TRANSPORT_SPECIALIST" => "TransportSpecialist",
                    "FLEET_ROUTE_COORDINATOR" => "FleetRouteCoordinator",
                    "DRIVER" => "Driver",
                    _ => "Escort"
                })
        };
        if (account.AccountType == AccountType.EMPLOYEE)
            claims.Add(new JwtClaim(ClaimTypes.Role, "Employee"));

        var accessToken = _jwtService.GenerateAccessToken(claims);
        var refreshToken = _jwtService.GenerateRefreshToken();
        var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        var context = _httpContextAccessor.HttpContext;
        var ip = context?.Connection.RemoteIpAddress?.ToString();
        var device = context?.Request.Headers.UserAgent.ToString();

        _db.RefreshTokens.Add(new RefreshToken
        {
            RefreshTokenId = Guid.NewGuid(),
            AccountId = account.AccountId,
            TokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken))),
            CreatedAt = now,
            ExpiresAt = now.AddDays(7),
            CreatedByIp = string.IsNullOrWhiteSpace(ip) ? null : ip[..Math.Min(ip.Length, 64)],
            DeviceInfo = string.IsNullOrWhiteSpace(device) ? null : device[..Math.Min(device.Length, 500)]
        });
        account.LastLoginAt = now;
        await _db.SaveChangesAsync(cancellationToken);

        return new Response.LoginResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresIn = 900,
            User = new Response.UserInfoResponse
            {
                AccountId = account.AccountId,
                CustomerId = account.AccountType == AccountType.CUSTOMER
                    ? account.CustomerAccount!.CustomerId
                    : null,
                EmployeeId = account.AccountType == AccountType.EMPLOYEE
                    ? account.EmployeeAccount!.EmployeeId
                    : null,
                FullName = account.AccountType == AccountType.CUSTOMER
                    ? account.CustomerAccount!.FullName
                    : account.EmployeeAccount!.FullName,
                AccountType = account.AccountType.ToString(),
                Role = role,
                AvatarUrl = account.AvatarUrl
            }
        };
    }

    private async Task RecordLoginFailureAsync(string failedKey, string lockedKey, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var cached = await _cache.GetStringAsync(failedKey, cancellationToken);
        var entry = cached is null ? null : JsonSerializer.Deserialize<LoginFailureEntry>(cached);
        var withinWindow = entry is not null && now - entry.WindowStartedAt < LoginAttemptWindow;
        var windowStart = withinWindow ? entry!.WindowStartedAt : now;
        var count = withinWindow ? entry!.Count + 1 : 1;

        if (count >= MaxLoginFailures)
        {
            await _cache.SetStringAsync(lockedKey, "1", new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = LoginAttemptWindow
            }, cancellationToken);
            await _cache.RemoveAsync(failedKey, cancellationToken);
            throw new TooManyRequestsException("Too many failed login attempts. Please try again later.");
        }

        await _cache.SetStringAsync(failedKey, JsonSerializer.Serialize(new LoginFailureEntry(count, windowStart)),
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = windowStart.Add(LoginAttemptWindow) - now
            }, cancellationToken);
    }

    private sealed record LoginFailureEntry(int Count, DateTimeOffset WindowStartedAt);

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
