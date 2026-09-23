using System.Data;
using System.Net;
using System.Security.Claims;
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
using JwtService = Pegaxus.Service.JwtService;
using Claim = System.Security.Claims.Claim;
using AccountEntity = Pegaxus.Repository.Entities.Account;

namespace Pegaxus.Service.Auth;

public class Service : IService
{
    private const string EmailVerification = "EMAIL_VERIFICATION";
    private const string PasswordReset = "PASSWORD_RESET";
    private readonly AppDbContext _db;
    private readonly IPasswordHasher<AccountEntity> _passwordHasher;
    private readonly IDistributedCache _cache;
    private readonly MailService.IService _mailService;
    private readonly JwtService.IJwtService _jwtService;

    public Service(
        AppDbContext db,
        IPasswordHasher<AccountEntity> passwordHasher,
        IDistributedCache cache,
        MailService.IService mailService,
        JwtService.IJwtService jwtService)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _cache = cache;
        _mailService = mailService;
        _jwtService = jwtService;
    }

    public async Task<Response.RegisterResponse> RegisterAsync(
        Request.RegisterRequest request,
        CancellationToken cancellationToken = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        if (await _db.Accounts.AnyAsync(account => !account.IsDeleted && account.Email.ToLower() == email, cancellationToken))
            throw new ConflictException("Email is already in use", "EMAIL_ALREADY_EXISTS");

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
            throw new ConflictException("Email is already in use", "EMAIL_ALREADY_EXISTS");
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
            throw new NotFoundException("Không tìm thấy tài khoản", "ACCOUNT_NOT_FOUND");

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
            await _mailService.SendMail(new MailService.MailContent
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
            });
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
            throw new BadRequestException("Mã OTP đã hết hạn", "OTP_EXPIRED");
        }

        if (MatchesHash(otp, entry.CodeHash))
            return;

        var attempts = entry.Attempts + 1;
        if (attempts >= 5)
        {
            await _cache.RemoveAsync(key, cancellationToken);
            throw new TooManyRequestsException("Nhập sai quá 5 lần, vui lòng yêu cầu mã mới", "OTP_TOO_MANY_ATTEMPTS");
        }

        await _cache.SetStringAsync(
            key,
            JsonSerializer.Serialize(entry with { Attempts = attempts }),
            new DistributedCacheEntryOptions { AbsoluteExpiration = entry.ExpiresAt },
            cancellationToken);
        throw new BadRequestException("Mã OTP không chính xác", "OTP_INVALID");
    }

    private async Task EnsureCooldownExpiredAsync(string email, string purpose, CancellationToken cancellationToken)
    {
        if (await _cache.GetStringAsync(CooldownKey(email, purpose), cancellationToken) is not null)
            throw new TooManyRequestsException("Vui lòng đợi 60 giây trước khi gửi lại mã", "OTP_RESEND_TOO_SOON");
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
        if (account.AccountType == AccountType.CUSTOMER)
        {
            claims.Add(new Claim(ClaimTypes.Role, "Customer"));
        }
        else
        {
            claims.Add(new Claim(ClaimTypes.Role, "Employee"));
            claims.Add(new Claim(ClaimTypes.Role, account.EmployeeAccount!.Role switch
            {
                EmployeeRole.LOGISTICS_MANAGER => "LogisticsManager",
                EmployeeRole.TRANSPORT_SPECIALIST => "TransportSpecialist",
                EmployeeRole.FLEET_ROUTE_COORDINATOR => "FleetRouteCoordinator",
                EmployeeRole.DRIVER => "Driver",
                EmployeeRole.ESCORT => "Escort",
                _ => throw InvalidRefreshToken()
            }));
        }

        return claims;
    }

    private static UnauthorizedException InvalidRefreshToken() =>
        new("Phiên đăng nhập đã hết hạn, vui lòng đăng nhập lại", "REFRESH_TOKEN_INVALID");
    private static DateTime UtcNow() => DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

    private sealed record OtpCacheEntry(string CodeHash, int Attempts, DateTimeOffset ExpiresAt);
}
