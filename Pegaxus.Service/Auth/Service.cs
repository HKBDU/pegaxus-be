using System.Data;
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
using Pegaxus.Service.OutboxService;
using MailService = Pegaxus.Service.MailService;
using JwtService = Pegaxus.Service.JwtService;
using Claim = System.Security.Claims.Claim;
using AppException = Pegaxus.Service.Exceptions.Exceptions;
using AccountEntity = Pegaxus.Repository.Entities.Account;

namespace Pegaxus.Service.Auth;

public class Service : IService
{
    private const string EmailVerification = "EMAIL_VERIFICATION";
    private const string PasswordReset = "PASSWORD_RESET";
    private const string GoogleLoginCodePrefix = "auth:google:code:";
    private readonly AppDbContext _db;
    private readonly IPasswordHasher<AccountEntity> _passwordHasher;
    private readonly IDistributedCache _cache;
    private readonly MailService.IService _mailService;
    private readonly JwtService.IJwtService _jwtService;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private static readonly TimeSpan LoginAttemptWindow = TimeSpan.FromMinutes(15);
    private const int MaxLoginFailures = 5;

    public Service(
        AppDbContext db,
        IPasswordHasher<AccountEntity> passwordHasher,
        IDistributedCache cache,
        MailService.IService mailService,
        JwtService.IJwtService jwtService,
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
            .Include(item => item.CustomerAccount)
            .Include(item => item.EmployeeAccount)
            .FirstOrDefaultAsync(item => !item.IsDeleted && item.Email.ToLower() == email, cancellationToken);

        if (account is null)
            throw new UnauthorizedException("Email or password incorrect");
        if (account.Status == AccountStatus.SUSPENDED)
            throw new ForbiddenException("Account suspended");
        if (account.Status != AccountStatus.ACTIVE)
            throw new ForbiddenException("Account inactive");

        var failedKey = $"auth:login:failed:{account.AccountId}";
        var lockedKey = $"auth:login:locked:{account.AccountId}";
        if (await _cache.GetStringAsync(lockedKey, cancellationToken) is not null)
            throw new TooManyRequestsException("Too many failed login attempts. Please try again later.");

        if (_passwordHasher.VerifyHashedPassword(account, account.PasswordHash, request.Password) == PasswordVerificationResult.Failed)
        {
            await RecordLoginFailureAsync(failedKey, lockedKey, cancellationToken);
            throw new UnauthorizedException("Email or password incorrect");
        }

        if (account.EmailVerifiedAt is null)
            throw new ForbiddenException("Account email has not been verified");

        var role = account.AccountType switch
        {
            AccountType.CUSTOMER when account.CustomerAccount is { IsDeleted: false } => "CUSTOMER",
            AccountType.EMPLOYEE when account.EmployeeAccount is { IsDeleted: false, IsActive: true } employee => employee.Role.ToString(),
            _ => throw new ForbiddenException("Account profile is inactive or unavailable")
        };

        await _cache.RemoveAsync(failedKey, cancellationToken);
        await _cache.RemoveAsync(lockedKey, cancellationToken);

        return await IssueTokensAsync(account, role, cancellationToken);
    }

    public async Task<Response.LoginResponse> GoogleLoginAsync(
        Request.GoogleLoginRequest request,
        CancellationToken cancellationToken = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var subject = request.Subject.Trim();
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(email))
            throw new AppException("Google account information is invalid", 400, "GOOGLE_ACCOUNT_INVALID");

        var account = await _db.Accounts
            .Include(item => item.CustomerAccount)
            .Include(item => item.EmployeeAccount)
            .SingleOrDefaultAsync(item => !item.IsDeleted && item.Email.ToLower() == email, cancellationToken);

        var now = UtcNow();
        if (account is null)
        {
            account = new Account
            {
                AccountId = Guid.NewGuid(),
                Email = email,
                AccountType = AccountType.CUSTOMER,
                Status = AccountStatus.ACTIVE,
                EmailVerifiedAt = now,
                CreatedAt = now,
                UpdatedAt = now,
                AvatarUrl = string.IsNullOrWhiteSpace(request.AvatarUrl) ? null : request.AvatarUrl.Trim()
            };
            account.PasswordHash = _passwordHasher.HashPassword(account, Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
            account.CustomerAccount = new Customer
            {
                CustomerId = Guid.NewGuid(),
                AccountId = account.AccountId,
                FullName = string.IsNullOrWhiteSpace(request.FullName) ? email : request.FullName.Trim(),
                CreatedAt = now,
                UpdatedAt = now
            };
            _db.Accounts.Add(account);
        }
        else
        {
            if (account.AccountType != AccountType.CUSTOMER || account.CustomerAccount is null || account.CustomerAccount.IsDeleted)
                throw new ForbiddenException("This email is linked to an unavailable account");
            if (account.Status == AccountStatus.SUSPENDED)
                throw new ForbiddenException("Account suspended");
            if (account.Status != AccountStatus.ACTIVE)
                throw new ForbiddenException("Account inactive");

            account.EmailVerifiedAt ??= now;
            if (!string.IsNullOrWhiteSpace(request.AvatarUrl))
                account.AvatarUrl = request.AvatarUrl.Trim();
            account.UpdatedAt = now;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return await IssueTokensAsync(account, "CUSTOMER", cancellationToken);
    }

    public async Task<string> CreateGoogleLoginCodeAsync(
        Response.LoginResponse response,
        CancellationToken cancellationToken = default)
    {
        var code = $"gl_{Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant()}";
        await _cache.SetStringAsync(
            GoogleLoginCodePrefix + code,
            JsonSerializer.Serialize(response),
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1)
            },
            cancellationToken);
        return code;
    }

    public async Task<Response.LoginResponse> ExchangeGoogleLoginCodeAsync(
        Request.GoogleLoginExchangeRequest request,
        CancellationToken cancellationToken = default)
    {
        var code = request.Code.Trim();
        if (string.IsNullOrWhiteSpace(code))
            throw new AppException("Google login code is required", 400, "GOOGLE_LOGIN_CODE_INVALID");

        var key = GoogleLoginCodePrefix + code;
        var value = await _cache.GetStringAsync(key, cancellationToken);
        await _cache.RemoveAsync(key, cancellationToken);
        if (value is null)
            throw new AppException("Google login code is invalid or expired", 400, "GOOGLE_LOGIN_CODE_INVALID");

        try
        {
            return JsonSerializer.Deserialize<Response.LoginResponse>(value)
                ?? throw new JsonException("Google login code payload is empty");
        }
        catch (JsonException)
        {
            throw new AppException("Google login code is invalid or expired", 400, "GOOGLE_LOGIN_CODE_INVALID");
        }
    }

    private async Task<Response.LoginResponse> IssueTokensAsync(
        AccountEntity account,
        string role,
        CancellationToken cancellationToken)
    {
        var accessToken = _jwtService.GenerateAccessToken(CreateClaims(account));
        var refreshToken = _jwtService.GenerateRefreshToken();
        var now = UtcNow();
        var context = _httpContextAccessor.HttpContext;
        var ip = context?.Connection.RemoteIpAddress?.ToString();
        var device = context?.Request.Headers.UserAgent.ToString();
        _db.RefreshTokens.Add(new RefreshToken
        {
            RefreshTokenId = Guid.NewGuid(),
            AccountId = account.AccountId,
            TokenHash = Hash(refreshToken),
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
            ExpiresIn = _jwtService.AccessTokenExpirationSeconds,
            User = new Response.UserInfoResponse
            {
                AccountId = account.AccountId,
                CustomerId = account.CustomerAccount?.CustomerId,
                EmployeeId = account.EmployeeAccount?.EmployeeId,
                FullName = account.CustomerAccount?.FullName ?? account.EmployeeAccount!.FullName,
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
            await _cache.SetStringAsync(
                lockedKey,
                "1",
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = LoginAttemptWindow },
                cancellationToken);
            await _cache.RemoveAsync(failedKey, cancellationToken);
            throw new TooManyRequestsException("Too many failed login attempts. Please try again later.");
        }

        await _cache.SetStringAsync(
            failedKey,
            JsonSerializer.Serialize(new LoginFailureEntry(count, windowStart)),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = windowStart.Add(LoginAttemptWindow) - now },
            cancellationToken);
    }

    public async Task<Response.RegisterResponse> RegisterAsync(
        Request.RegisterRequest request,
        CancellationToken cancellationToken = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        if (await _db.Accounts.AnyAsync(account => !account.IsDeleted && account.Email.ToLower() == email, cancellationToken))
            throw new AppException("Email is already in use", 409, "EMAIL_ALREADY_EXISTS");

        var now = UtcNow();
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

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            _db.Accounts.Add(account);
            _db.Customers.Add(customer);
            await _db.SaveChangesAsync(cancellationToken);
            await SendOtpAsync(email, EmailVerification, customer.FullName, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new AppException("Email is already in use", 409, "EMAIL_ALREADY_EXISTS");
        }
        catch
        {
            await RemoveOtpAsync(email, EmailVerification);
            throw;
        }

        return new Response.RegisterResponse
        {
            AccountId = account.AccountId,
            CustomerId = customer.CustomerId
        };
    }

    public async Task ForgotPasswordAsync(
        Request.ForgotPasswordRequest request,
        CancellationToken cancellationToken = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        await EnsureCooldownExpiredAsync(email, PasswordReset, cancellationToken);
        var account = await _db.Accounts
            .Include(item => item.CustomerAccount)
            .Include(item => item.EmployeeAccount)
            .SingleOrDefaultAsync(item => !item.IsDeleted && item.Email.ToLower() == email, cancellationToken);
        if (account is null)
        {
            await SetCooldownAsync(email, PasswordReset, cancellationToken);
            return;
        }

        await SendOtpAsync(
            email,
            PasswordReset,
            account.CustomerAccount?.FullName ?? account.EmployeeAccount?.FullName ?? account.Email,
            cancellationToken);
    }

    public async Task VerifyEmailAsync(
        Request.VerifyEmailRequest request,
        CancellationToken cancellationToken = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var account = await _db.Accounts.SingleOrDefaultAsync(
            item => !item.IsDeleted && item.Email.ToLower() == email,
            cancellationToken);
        if (account is null)
            throw new AppException("Account not found", 404, "ACCOUNT_NOT_FOUND");

        await ValidateOtpAsync(email, EmailVerification, request.Otp, cancellationToken);
        var now = UtcNow();
        account.EmailVerifiedAt ??= now;
        account.UpdatedAt = now;
        await _db.SaveChangesAsync(cancellationToken);
        await _cache.RemoveAsync(OtpKey(email, EmailVerification), cancellationToken);
    }

    public async Task<Response.ResendOtpResponse> ResendOtpAsync(
        Request.ResendOtpRequest request,
        CancellationToken cancellationToken = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var purpose = request.Purpose.Trim().ToUpperInvariant();
        await EnsureCooldownExpiredAsync(email, purpose, cancellationToken);

        var account = await _db.Accounts
            .Include(item => item.CustomerAccount)
            .Include(item => item.EmployeeAccount)
            .SingleOrDefaultAsync(item => !item.IsDeleted && item.Email.ToLower() == email, cancellationToken);
        if (account is null || purpose == EmailVerification && account.EmailVerifiedAt is not null)
        {
            await SetCooldownAsync(email, purpose, cancellationToken);
            return new Response.ResendOtpResponse { ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5) };
        }

        return new Response.ResendOtpResponse
        {
            ExpiresAt = await SendOtpAsync(
                email,
                purpose,
                account.CustomerAccount?.FullName ?? account.EmployeeAccount?.FullName ?? account.Email,
                cancellationToken)
        };
    }

    public async Task<Response.RefreshTokenResponse> RefreshTokenAsync(
        Request.RefreshTokenRequest request,
        CancellationToken cancellationToken = default)
    {
        var now = UtcNow();
        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var currentToken = await _db.RefreshTokens
                .Include(item => item.Account)
                .ThenInclude(item => item.EmployeeAccount)
                .SingleOrDefaultAsync(item => item.TokenHash == Hash(request.RefreshToken), cancellationToken);
            if (currentToken is null || currentToken.RevokedAt is not null || currentToken.ExpiresAt <= now ||
                currentToken.Account.IsDeleted || currentToken.Account.Status != AccountStatus.ACTIVE ||
                currentToken.Account.EmailVerifiedAt is null ||
                currentToken.Account.AccountType == AccountType.EMPLOYEE && currentToken.Account.EmployeeAccount?.IsActive != true)
                throw InvalidRefreshToken();

            var rawRefreshToken = _jwtService.GenerateRefreshToken();
            var replacement = new RefreshToken
            {
                RefreshTokenId = Guid.NewGuid(),
                AccountId = currentToken.AccountId,
                TokenHash = Hash(rawRefreshToken),
                CreatedAt = now,
                ExpiresAt = now.AddDays(7)
            };
            currentToken.RevokedAt = now;
            currentToken.ReplacedByTokenId = replacement.RefreshTokenId;
            currentToken.ReplacedByToken = replacement;
            _db.RefreshTokens.Add(replacement);

            var claims = CreateClaims(currentToken.Account);
            var accessToken = _jwtService.GenerateAccessToken(claims);
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new Response.RefreshTokenResponse
            {
                AccessToken = accessToken,
                RefreshToken = rawRefreshToken,
                ExpiresIn = _jwtService.AccessTokenExpirationSeconds
            };
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.SerializationFailure)
        {
            throw InvalidRefreshToken();
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure })
        {
            throw InvalidRefreshToken();
        }
    }

    public async Task LogoutAsync(
        Request.LogoutRequest request,
        CancellationToken cancellationToken = default)
    {
        var context = _httpContextAccessor.HttpContext;
        if (!Guid.TryParse(context?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var accountId))
            throw new UnauthorizedException("Authentication required");

        var now = UtcNow();
        var tokenHash = Hash(request.RefreshToken);
        var token = await _db.RefreshTokens.SingleOrDefaultAsync(item =>
            item.AccountId == accountId && item.TokenHash == tokenHash &&
            item.RevokedAt == null && item.ExpiresAt > now, cancellationToken);

        if (token is null)
            return;

        var ip = context!.Connection.RemoteIpAddress?.ToString();
        token.RevokedAt = now;
        token.RevokedByIp = string.IsNullOrWhiteSpace(ip) ? null : ip[..Math.Min(ip.Length, 64)];
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task ResetPasswordAsync(
        Request.ResetPasswordRequest request,
        CancellationToken cancellationToken = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        await ValidateOtpAsync(email, PasswordReset, request.Otp, cancellationToken);

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var account = await _db.Accounts
            .Include(item => item.RefreshTokens)
            .SingleOrDefaultAsync(item => !item.IsDeleted && item.Email.ToLower() == email, cancellationToken);
        if (account is null)
            throw new AppException("OTP has expired", 400, "OTP_EXPIRED");

        var now = UtcNow();
        account.PasswordHash = _passwordHasher.HashPassword(account, request.NewPassword);
        account.UpdatedAt = now;
        foreach (var refreshToken in account.RefreshTokens.Where(item => item.RevokedAt is null))
            refreshToken.RevokedAt = now;

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await _cache.RemoveAsync(OtpKey(email, PasswordReset), cancellationToken);
    }

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
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static bool MatchesHash(string value, string expectedHash)
    {
        try
        {
            return CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(Encoding.UTF8.GetBytes(value)),
                Convert.FromHexString(expectedHash));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static List<Claim> CreateClaims(AccountEntity account)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, account.AccountId.ToString()),
            new(ClaimTypes.Email, account.Email),
            new("accountType", account.AccountType.ToString())
        };
        claims.Add(new Claim(ClaimTypes.Role, JwtService.JwtServices.GetRoleName(account.AccountType)));
        if (account.AccountType == AccountType.EMPLOYEE)
            claims.Add(new Claim(ClaimTypes.Role, JwtService.JwtServices.GetRoleName(account.EmployeeAccount!.Role)));

        return claims;
    }

    private static AppException InvalidRefreshToken() =>
        new("Session has expired. Please sign in again", 401, "REFRESH_TOKEN_INVALID");
    private static DateTime UtcNow() => DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

    private sealed record OtpCacheEntry(string CodeHash, int Attempts, DateTimeOffset ExpiresAt);
    private sealed record LoginFailureEntry(int Count, DateTimeOffset WindowStartedAt);
}
