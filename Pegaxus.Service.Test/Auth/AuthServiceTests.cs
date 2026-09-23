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
        var service = new AuthService(db, new PasswordHasher<Account>(), cache, mailService);

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
        var service = new AuthService(db, new PasswordHasher<Account>(), cache, mailService);

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
        var service = new AuthService(db, new PasswordHasher<Account>(), cache, mailService);

        await service.ForgotPasswordAsync(new AuthRequest.ForgotPasswordRequest { Email = "missing@example.com" });

        Assert.Equal(0, mailService.Calls);
        Assert.NotNull(await cache.GetStringAsync("otp-cooldown:PASSWORD_RESET:missing@example.com"));
    }
}
