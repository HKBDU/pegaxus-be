using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using MailService = Pegaxus.Service.MailService;

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
            OriginCountry = "Vietnam",
            DestinationAddress = "Old destination",
            DestinationCountry = "Australia",
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

    internal sealed class RecordingMailService : MailService.IService
    {
        internal int Calls { get; private set; }

        public Task SendMail(MailService.MailContent mailContent)
        {
            Calls++;
            return Task.CompletedTask;
        }
    }
}
