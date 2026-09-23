using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Pegaxus.Repository.Entities;
using AuthRequest = Pegaxus.Service.Auth.Request;
using AuthService = Pegaxus.Service.Auth.Service;

namespace Pegaxus.Service.Test;

public class AuthServiceTests
{
    [Fact]
    public async Task Register_CreatesAccountCustomerAndOtp()
    {
        await using var db = TestSupport.CreateDb();
        var cache = TestSupport.CreateCache();
        var mailService = TestSupport.CreateMailService();
        var service = CreateService(db, cache, mailService);

        var result = await service.RegisterAsync(new AuthRequest.RegisterRequest
        {
            FullName = "Nguyen Van A",
            Email = "User@Example.com",
            Password = "Password1",
            ConfirmPassword = "Password1"
        });

        var account = await db.Accounts.SingleAsync();
        Assert.Equal("user@example.com", account.Email);
        Assert.Equal(result.AccountId, account.AccountId);
        Assert.Equal(result.CustomerId, (await db.Customers.SingleAsync()).CustomerId);
        Assert.NotNull(await cache.GetStringAsync("otp:EMAIL_VERIFICATION:user@example.com"));
        Assert.Equal(1, mailService.Calls);
    }

    [Fact]
    public async Task ForgotPassword_StoresOtpAndSendsEmail()
    {
        await using var db = TestSupport.CreateDb();
        var (account, _) = await TestSupport.AddCustomerAsync(db);
        var cache = TestSupport.CreateCache();
        var mailService = TestSupport.CreateMailService();
        var service = CreateService(db, cache, mailService);

        await service.ForgotPasswordAsync(new AuthRequest.ForgotPasswordRequest { Email = account.Email });

        Assert.NotNull(await cache.GetStringAsync($"otp:PASSWORD_RESET:{account.Email}"));
        Assert.Equal(1, mailService.Calls);
    }

    [Fact]
    public async Task ForgotPassword_UnknownEmailDoesNotRevealAccount()
    {
        await using var db = TestSupport.CreateDb();
        var cache = TestSupport.CreateCache();
        var mailService = TestSupport.CreateMailService();
        var service = CreateService(db, cache, mailService);

        await service.ForgotPasswordAsync(new AuthRequest.ForgotPasswordRequest { Email = "missing@example.com" });

        Assert.Equal(0, mailService.Calls);
        Assert.NotNull(await cache.GetStringAsync("otp-cooldown:PASSWORD_RESET:missing@example.com"));
    }

    [Fact]
    public async Task VerifyEmail_ValidOtpMarksAccountVerified()
    {
        await using var db = TestSupport.CreateDb();
        var (account, _) = await TestSupport.AddCustomerAsync(db);
        var cache = TestSupport.CreateCache();
        var otp = "482913";
        await cache.SetStringAsync(
            $"otp:EMAIL_VERIFICATION:{account.Email}",
            JsonSerializer.Serialize(new
            {
                CodeHash = Hash(otp),
                Attempts = 0,
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5)
            }));
        var service = CreateService(db, cache, TestSupport.CreateMailService());

        await service.VerifyEmailAsync(new AuthRequest.VerifyEmailRequest { Email = account.Email, Otp = otp });

        Assert.NotNull(account.EmailVerifiedAt);
        Assert.Null(await cache.GetStringAsync($"otp:EMAIL_VERIFICATION:{account.Email}"));
    }

    [Fact]
    public async Task ResendOtp_ReplacesOtpAndReturnsExpiry()
    {
        await using var db = TestSupport.CreateDb();
        var (account, _) = await TestSupport.AddCustomerAsync(db);
        var cache = TestSupport.CreateCache();
        var mailService = TestSupport.CreateMailService();
        var service = CreateService(db, cache, mailService);

        var response = await service.ResendOtpAsync(new AuthRequest.ResendOtpRequest
        {
            Email = account.Email,
            Purpose = "EMAIL_VERIFICATION"
        });

        Assert.True(response.ExpiresAt > DateTimeOffset.UtcNow);
        Assert.NotNull(await cache.GetStringAsync($"otp:EMAIL_VERIFICATION:{account.Email}"));
        Assert.Equal(1, mailService.Calls);
    }

    [Fact]
    public async Task RefreshToken_RotatesStoredToken()
    {
        await using var db = TestSupport.CreateDb();
        var (account, _) = await TestSupport.AddCustomerAsync(db);
        account.EmailVerifiedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        var rawToken = "rt_old";
        var storedToken = new RefreshToken
        {
            RefreshTokenId = Guid.NewGuid(),
            AccountId = account.AccountId,
            Account = account,
            TokenHash = Hash(rawToken),
            CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
            ExpiresAt = DateTime.SpecifyKind(DateTime.UtcNow.AddDays(1), DateTimeKind.Unspecified)
        };
        db.RefreshTokens.Add(storedToken);
        await db.SaveChangesAsync();
        var service = CreateService(db, TestSupport.CreateCache(), TestSupport.CreateMailService());

        var response = await service.RefreshTokenAsync(new AuthRequest.RefreshTokenRequest { RefreshToken = rawToken });

        Assert.Equal("access-token", response.AccessToken);
        Assert.Equal(900, response.ExpiresIn);
        Assert.NotNull(storedToken.RevokedAt);
        Assert.NotNull(storedToken.ReplacedByTokenId);
        Assert.Equal(2, await db.RefreshTokens.CountAsync());
    }

    private static AuthService CreateService(
        Pegaxus.Repository.AppDbContext db,
        IDistributedCache cache,
        TestSupport.RecordingMailService mailService) =>
        new(db, new PasswordHasher<Account>(), cache, mailService, TestSupport.CreateJwtService());

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
