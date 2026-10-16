using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;

using JwtService = Pegaxus.Service.JwtService;
using Claim = System.Security.Claims.Claim;

namespace Pegaxus.Service.Test;

internal static class TestSupport
{
    internal static AppDbContext CreateDb() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    internal static IDistributedCache CreateCache() =>
        new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

    internal static RecordingMailService CreateMailService() => new();

    internal static Pegaxus.Service.NotificationService.Service CreateNotifications(AppDbContext db) =>
        new(db, new Pegaxus.Service.NotificationService.NoopNotificationRealtimeService());
    internal static JwtService.IJwtService CreateJwtService() => new StubJwtService();

    internal static Pegaxus.Service.RoutePlanService.Service CreateRoutePlans(AppDbContext db) =>
        new(db, CreateNotifications(db), new Pegaxus.Service.RouteService.Service(db,
            new NoNetworkRouteClientFactory(), new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build()));

    private sealed class NoNetworkRouteClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("Route tests must provide a routing response before making HTTP calls.");
    }

    internal static async Task<(Account Account, Customer Customer)> AddCustomerAsync(AppDbContext db)
    {
        var now = UtcNow();
        var account = new Account
        {
            AccountId = Guid.NewGuid(),
            Email = $"customer-{Guid.NewGuid():N}@example.com",
            PasswordHash = "hash",
            AccountType = AccountType.CUSTOMER,
            Status = AccountStatus.ACTIVE,
            CreatedAt = now,
            UpdatedAt = now
        };
        var customer = new Customer
        {
            CustomerId = Guid.NewGuid(),
            AccountId = account.AccountId,
            CustomerCode = $"CUS-{DateTime.UtcNow:yyyyMMdd}-{Random.Shared.Next(0, 10_000):D4}",
            Account = account,
            FullName = "Test Customer",
            CreatedAt = now,
            UpdatedAt = now
        };
        account.CustomerAccount = customer;
        db.AddRange(account, customer);
        await db.SaveChangesAsync();
        return (account, customer);
    }

    internal static async Task<(Account Account, Employee Employee)> AddLogisticsManagerAsync(AppDbContext db)
    {
        var now = UtcNow();
        var account = new Account
        {
            AccountId = Guid.NewGuid(),
            Email = $"manager-{Guid.NewGuid():N}@example.com",
            PasswordHash = "hash",
            AccountType = AccountType.EMPLOYEE,
            Status = AccountStatus.ACTIVE,
            CreatedAt = now,
            UpdatedAt = now
        };
        var employee = new Employee
        {
            EmployeeId = Guid.NewGuid(),
            AccountId = account.AccountId,
            Account = account,
            EmployeeCode = $"LM-{Guid.NewGuid():N}",
            Role = EmployeeRole.LOGISTICS_MANAGER,
            FullName = "Test Manager",
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };
        account.EmployeeAccount = employee;
        db.AddRange(account, employee);
        await db.SaveChangesAsync();
        return (account, employee);
    }

    internal static async Task<(Booking Booking, Horse? Horse)> AddPendingBookingAsync(
        AppDbContext db,
        Customer customer,
        bool withHorse = false)
    {
        var now = UtcNow();
        var booking = new Booking
        {
            BookingId = Guid.NewGuid(),
            BookingCode = $"BK-{Guid.NewGuid():N}",
            CustomerId = customer.CustomerId,
            Customer = customer,
            Status = BookingStatus.PENDING,
            OriginAddress = "Old origin",
            OriginCountry = "France",
            DestinationAddress = "Old destination",
            DestinationCountry = "Germany",
            RequestedDepartureDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(21),
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Bookings.Add(booking);

        Horse? horse = null;
        if (withHorse)
        {
            horse = new Horse
            {
                HorseId = Guid.NewGuid(),
                CustomerId = customer.CustomerId,
                Customer = customer,
                HorseName = "Silver Arrow",
                CreatedAt = now,
                UpdatedAt = now
            };
            db.Horses.Add(horse);
            db.BookingHorses.Add(new BookingHorse
            {
                BookingId = booking.BookingId,
                Booking = booking,
                HorseId = horse.HorseId,
                Horse = horse,
                CreatedAt = now
            });
        }

        await db.SaveChangesAsync();
        return (booking, horse);
    }

    private static DateTime UtcNow() => DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

    internal sealed class RecordingMailService : MailService.IMailService
    {
        internal int Calls { get; private set; }

        public Task SendMail(MailService.MailContent mailContent, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.CompletedTask;
        }
    }

    private sealed class StubJwtService : JwtService.IJwtService
    {
        public int AccessTokenExpirationSeconds => 900;
        public string GenerateAccessToken(IEnumerable<Claim> claims) => "access-token";
        public string GenerateRefreshToken() => $"rt_{Guid.NewGuid():N}";
        public ClaimsPrincipal ValidateToken(string token) => throw new NotSupportedException();
    }
}
