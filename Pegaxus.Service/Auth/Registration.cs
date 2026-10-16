using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Common;
using Pegaxus.Service.OutboxService;

using static Pegaxus.Service.Common.DbTime;

using Claim = System.Security.Claims.Claim;
using AppException = Pegaxus.Service.Exceptions.Exceptions;

namespace Pegaxus.Service.Auth;

public sealed partial class Service
{
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
            CustomerCode = await GenerateCustomerCodeAsync(cancellationToken),
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

    private Task<string> GenerateCustomerCodeAsync(CancellationToken cancellationToken) =>
        CodeGeneration.NextAsync("CUS", DateTime.UtcNow, code => _db.Customers.AnyAsync(item => item.CustomerCode == code, cancellationToken),
            "CUSTOMER_CODE_GENERATION_FAILED", "Could not generate a unique customer code");
}
