using System.Data;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;

using static Pegaxus.Service.Common.DbTime;

using Claim = System.Security.Claims.Claim;
using AppException = Pegaxus.Service.Exceptions.Exceptions;
using AccountEntity = Pegaxus.Repository.Entities.Account;

namespace Pegaxus.Service.Auth;

public sealed partial class Service
{
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
}
