using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;

namespace Pegaxus.API;

public static class AppDbContextSeedData
{
    private const string DefaultPassword = "pegaxusDemo@123";
    private static readonly DateTime ComplianceRequirementSeedTimestamp =
        new(2026, 10, 4, 0, 0, 0, DateTimeKind.Unspecified);

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
        await SeedComplianceRequirementsAsync(db, cancellationToken);

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

    private static async Task SeedComplianceRequirementsAsync(
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var seeds = ComplianceRequirementSeeds;
        var existingCodes = await db.ComplianceRequirements
            .Where(item => seeds.Select(seed => seed.RequirementCode).Contains(item.RequirementCode))
            .Select(item => item.RequirementCode)
            .ToListAsync(cancellationToken);
        var existingCodeSet = existingCodes.ToHashSet(StringComparer.Ordinal);

        foreach (var seed in seeds)
        {
            if (existingCodeSet.Contains(seed.RequirementCode))
                continue;

            db.ComplianceRequirements.Add(new ComplianceRequirement
            {
                RequirementCode = seed.RequirementCode,
                RequirementName = seed.RequirementName,
                Description = seed.Description,
                Country = seed.Country,
                Category = seed.Category,
                IssuingAuthority = seed.IssuingAuthority,
                BcpCode = seed.BcpCode,
                LegalBasis = seed.LegalBasis,
                ValidityRequired = seed.ValidityRequired,
                ValidityPeriodDays = seed.ValidityPeriodDays,
                Status = seed.Status,
                IsDeleted = false,
                DeletedAt = null,
                DeletedByAccountId = null,
                CreatedAt = ComplianceRequirementSeedTimestamp,
                UpdatedAt = ComplianceRequirementSeedTimestamp
            });
        }
    }

    private static readonly ComplianceRequirementSeed[] ComplianceRequirementSeeds =
    [
        new("EQUINE_PASSPORT", "Equine Passport", "Hộ chiếu định danh ngựa, dùng để đối chiếu tên ngựa, số hộ chiếu và microchip.", "EU", ComplianceRequirementCategory.HorseIdentity, "Weatherbys / France Galop / Approved Passport Issuing Organisation", null, "Equine identification and passport control requirements.", true, 365, ComplianceRequirementStatus.ACTIVE),
        new("EHC_GB_TO_EU_8438", "Export Health Certificate GB to EU — Form 8438", "Chứng thư kiểm dịch thú y xuất khẩu từ GB sang EU.", "GB", ComplianceRequirementCategory.HorseHealth, "APHA / Official Veterinarian", null, "Export health certification for equidae transported from GB to EU.", true, 10, ComplianceRequirementStatus.ACTIVE),
        new("EHC_EU_TO_GB", "Export Health Certificate EU to GB", "Chứng thư kiểm dịch thú y xuất khẩu từ EU vào GB.", "EU", ComplianceRequirementCategory.HorseHealth, "EU Official Veterinarian / TRACES-NT", null, "Export health certification for equidae transported from EU to GB.", true, 10, ComplianceRequirementStatus.ACTIVE),
        new("LAB_TEST_EIA_COGGINS", "Laboratory Test Report — EIA Coggins Test", "Kết quả xét nghiệm Coggins/EIA của ngựa, dùng để chứng minh kết quả âm tính khi tuyến yêu cầu.", null, ComplianceRequirementCategory.HorseHealth, "Approved Veterinary Laboratory", null, "Equine infectious anaemia testing requirement for applicable routes.", true, 90, ComplianceRequirementStatus.ACTIVE),
        new("CHED_A_DOCUMENT", "CHED-A Animal Entry Document", "Tờ khai y tế nhập cảnh động vật trên hệ thống TRACES-NT cho tuyến có kiểm soát BCP.", "EU", ComplianceRequirementCategory.Quarantine, "TRACES-NT / EU Border Control Post", "FRCAL1", "Common Health Entry Document for Animals.", true, 1, ComplianceRequirementStatus.ACTIVE),
        new("IPAFFS_PRE_NOTIF", "IPAFFS Import Pre-notification", "Khai báo trước khi đưa ngựa vào GB qua hệ thống IPAFFS.", "GB", ComplianceRequirementCategory.Quarantine, "UK Defra IPAFFS", "GBHOL1", "Pre-notification requirement for applicable imports into GB.", true, 1, ComplianceRequirementStatus.ACTIVE),
        new("ATA_CARNET_EXPORT", "ATA Carnet — Export Voucher", "Sổ ATA Carnet phục vụ thủ tục tạm xuất ngựa và tài sản liên quan.", "GB", ComplianceRequirementCategory.Customs, "Chamber of Commerce / Customs Authority", null, "Temporary admission and re-export customs procedure.", true, 365, ComplianceRequirementStatus.ACTIVE),
        new("ATA_CARNET_IMPORT", "ATA Carnet — Import Voucher", "Phần chứng từ ATA Carnet phục vụ thủ tục tạm nhập tại nước đến.", "EU", ComplianceRequirementCategory.Customs, "Chamber of Commerce / Customs Authority", null, "Temporary admission customs procedure at destination country.", true, 365, ComplianceRequirementStatus.ACTIVE),
        new("JOURNEY_LOG_EC1_2005", "EU Animal Transport Journey Log", "Nhật ký hành trình ghi nhận kế hoạch, thời gian di chuyển, thời gian nghỉ và các bất thường.", "EU", ComplianceRequirementCategory.Transport, "Transport Operator / Competent Authority", null, "Animal welfare and journey log requirements for transported equidae.", false, null, ComplianceRequirementStatus.ACTIVE),
        new("DRIVER_COMPETENCE_CERT", "Driver Competence Certificate", "Chứng chỉ năng lực của tài xế vận chuyển động vật sống.", null, ComplianceRequirementCategory.Transport, "Competent Transport Authority / Approved Training Body", null, "Driver competence requirement for transport of live animals.", true, 365, ComplianceRequirementStatus.INACTIVE)
    ];

    private sealed record EmployeeSeed(
        string FullName,
        string EmployeeCode,
        string Email,
        string Phone,
        EmployeeRole Role);

    private sealed record CustomerSeed(string FullName, string Email);

    private sealed record ComplianceRequirementSeed(
        string RequirementCode,
        string RequirementName,
        string Description,
        string? Country,
        ComplianceRequirementCategory Category,
        string IssuingAuthority,
        string? BcpCode,
        string LegalBasis,
        bool ValidityRequired,
        int? ValidityPeriodDays,
        ComplianceRequirementStatus Status);
}
