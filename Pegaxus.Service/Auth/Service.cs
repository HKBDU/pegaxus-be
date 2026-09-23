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
    private const string EmailVerification = "EMAIL_VERIFICATION";
    private const string PasswordReset = "PASSWORD_RESET";
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

    private async Task SendOtpAsync(
        string email,
        string purpose,
        string fullName,
        CancellationToken cancellationToken)
    {
        var otp = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        await _cache.SetStringAsync(
            OtpKey(email, purpose),
            JsonSerializer.Serialize(new OtpCacheEntry(Hash(otp), 0)),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5) },
            cancellationToken);
        await SetCooldownAsync(email, purpose, cancellationToken);

        try
        {
            await _mailService.SendMail(new MailService.MailContent
            {
                To = email,
                Subject = purpose == EmailVerification ? "Verify your Pegaxus account" : "Reset your Pegaxus password",
                Body = $"<p>Hello {WebUtility.HtmlEncode(fullName)},</p><p>Your OTP is <strong>{otp}</strong>. It expires in 5 minutes.</p>"
            });
        }
        catch
        {
            await RemoveOtpAsync(email, purpose);
            throw;
        }
    }

    private async Task EnsureCooldownExpiredAsync(string email, string purpose, CancellationToken cancellationToken)
    {
        if (await _cache.GetStringAsync(CooldownKey(email, purpose), cancellationToken) is not null)
            throw new TooManyRequestsException("Please wait 60 seconds before requesting another code", "OTP_RESEND_TOO_SOON");
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
    private static DateTime UtcNow() => DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

    private sealed record OtpCacheEntry(string CodeHash, int Attempts);
}
