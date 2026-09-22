using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;

namespace Pegaxus.Service.Auth;

public class Service : IService
{
    private readonly AppDbContext _db;
    private readonly IPasswordHasher<Account> _passwordHasher;
    private readonly IDistributedCache _cache;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    public Service(
        AppDbContext db,
        IPasswordHasher<Account> passwordHasher,
        IDistributedCache cache,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _cache = cache;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
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

            await SendEmailAsync(
                email,
                "Verify your Pegaxus account",
                $"<p>Hello {WebUtility.HtmlEncode(customer.FullName)},</p><p>Your OTP is <strong>{otp}</strong>. It expires in 5 minutes.</p>",
                cancellationToken);
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
            await SendEmailAsync(
                email,
                "Reset your Pegaxus password",
                $"<p>Your password reset OTP is <strong>{otp}</strong>. It expires in 5 minutes.</p>",
                cancellationToken);
        }
        catch
        {
            await _cache.RemoveAsync(otpKey, CancellationToken.None);
            await _cache.RemoveAsync(cooldownKey, CancellationToken.None);
            throw;
        }
    }

    private async Task SendEmailAsync(
        string recipient,
        string subject,
        string html,
        CancellationToken cancellationToken)
    {
        var apiKey = _configuration["Resend:ApiKey"] ?? _configuration["RESEND_API_KEY"];
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Resend API key is not configured");

        var fromEmail = _configuration["Resend:FromEmail"]
            ?? _configuration["RESEND_FROM_EMAIL"]
            ?? "onboarding@resend.dev";
        var fromName = _configuration["Resend:FromName"]
            ?? _configuration["RESEND_FROM_NAME"]
            ?? "Pegaxus";
        using var message = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails")
        {
            Content = JsonContent.Create(new
            {
                from = $"{fromName} <{fromEmail}>",
                to = new[] { recipient },
                subject,
                html
            })
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await _httpClientFactory.CreateClient().SendAsync(message, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Failed to send OTP email through Resend ({(int)response.StatusCode})");
    }

    private static string HashOtp(string otp) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(otp)));

    private sealed record OtpCacheEntry(string CodeHash, int Attempts);
}
