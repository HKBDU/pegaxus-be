using System.Data;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
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
}
