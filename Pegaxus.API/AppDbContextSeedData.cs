using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;

namespace Pegaxus.API;

public static class AppDbContextSeedData
{
    private const string DefaultPassword = "pegaxusDemo@123";

    public static async Task SeedAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        var passwordHasher = new PasswordHasher<Account>();
        var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

        var employees = new[]
        {
            new EmployeeSeed("Văng Khánh Khuyên", "EMP-20260927-0001", "diechahien@gmail.com", "0702946163", EmployeeRole.LOGISTICS_MANAGER),
            new EmployeeSeed("Võ Gia Huy", "EMP-20260927-0002", "huyvog6226@gmail.com", "0961002445", EmployeeRole.TRANSPORT_SPECIALIST),
            new EmployeeSeed("Nguyễn Huỳnh Hoàng Uyên", "EMP-20260927-0003", "hoanguyen91011@gmail.com", "0937489883", EmployeeRole.FLEET_ROUTE_COORDINATOR),
            new EmployeeSeed("Ngô Hoàng Thái Dương", "EMP-20260927-0004", "duongngo2209@gmail.com", "0949301121", EmployeeRole.DRIVER),
            new EmployeeSeed("Nguyễn Văn Quốc Bảo", "EMP-20260927-0005", "quocbao.nguyen16102006@gmail.com", "0399556929", EmployeeRole.ESCORT)
        };

        var customers = new[]
        {
            new CustomerSeed("Trinh Ng An Quan", "trinhnganquan@gmail.com"),
            new CustomerSeed("Bat Quy Quan", "batquyquan@gmail.com"),
            new CustomerSeed("Kim Ve Tuan", "kimvetuan@gmail.com"),
            new CustomerSeed("Khuyen Vkse200481", "khuyenvkse200481@fpt.edu.vn")
        };

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        foreach (var seed in employees)
            await SeedEmployeeAsync(db, passwordHasher, seed, now, cancellationToken);
        foreach (var seed in customers)
            await SeedCustomerAsync(db, passwordHasher, seed, now, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task SeedEmployeeAsync(
        AppDbContext db,
        IPasswordHasher<Account> passwordHasher,
        EmployeeSeed seed,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var email = seed.Email.ToLowerInvariant();
        var account = await db.Accounts
            .Include(item => item.EmployeeAccount)
            .SingleOrDefaultAsync(item => item.Email.ToLower() == email, cancellationToken);

        if (account is not null)
        {
            if (account.EmployeeAccount is null)
                throw new InvalidOperationException($"Seed email already belongs to a different account type: {seed.Email}");
            account.EmployeeAccount.EmployeeCode = seed.EmployeeCode;
            return;
        }

        account = new Account
        {
            AccountId = Guid.NewGuid(),
            Email = email,
            AccountType = AccountType.EMPLOYEE,
            Status = AccountStatus.ACTIVE,
            EmailVerifiedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };
        account.PasswordHash = passwordHasher.HashPassword(account, DefaultPassword);

        db.Accounts.Add(account);
        db.Employees.Add(new Employee
        {
            EmployeeId = Guid.NewGuid(),
            AccountId = account.AccountId,
            EmployeeCode = seed.EmployeeCode,
            Role = seed.Role,
            FullName = seed.FullName,
            Phone = seed.Phone,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        });
    }

    private static async Task SeedCustomerAsync(
        AppDbContext db,
        IPasswordHasher<Account> passwordHasher,
        CustomerSeed seed,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var email = seed.Email.ToLowerInvariant();
        var account = await db.Accounts
            .Include(item => item.CustomerAccount)
            .SingleOrDefaultAsync(item => item.Email.ToLower() == email, cancellationToken);

        if (account is not null)
        {
            if (account.CustomerAccount is null)
                throw new InvalidOperationException($"Seed email already belongs to a different account type: {seed.Email}");
            return;
        }

        account = new Account
        {
            AccountId = Guid.NewGuid(),
            Email = email,
            AccountType = AccountType.CUSTOMER,
            Status = AccountStatus.ACTIVE,
            EmailVerifiedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };
        account.PasswordHash = passwordHasher.HashPassword(account, DefaultPassword);

        db.Accounts.Add(account);
        db.Customers.Add(new Customer
        {
            CustomerId = Guid.NewGuid(),
            AccountId = account.AccountId,
            FullName = seed.FullName,
            CreatedAt = now,
            UpdatedAt = now
        });
    }

    private sealed record EmployeeSeed(
        string FullName,
        string EmployeeCode,
        string Email,
        string Phone,
        EmployeeRole Role);

    private sealed record CustomerSeed(string FullName, string Email);
}
