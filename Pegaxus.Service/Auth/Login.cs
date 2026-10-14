using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;

using static Pegaxus.Service.Common.DbTime;

using Claim = System.Security.Claims.Claim;
using AppException = Pegaxus.Service.Exceptions.Exceptions;

namespace Pegaxus.Service.Auth;

public sealed partial class Service
{
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
                CustomerCode = await GenerateCustomerCodeAsync(cancellationToken),
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

    private sealed record LoginFailureEntry(int Count, DateTimeOffset WindowStartedAt);
}
