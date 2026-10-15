using System.Security.Cryptography;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;

using static Pegaxus.Service.Common.DbTime;

namespace Pegaxus.Service.EmployeeService;

public sealed class Service : IEmployeeService
{
    private const string DefaultPassword = "123@Abc.";
    private readonly AppDbContext _db;
    private readonly IPasswordHasher<Account> _passwordHasher;

    public Service(AppDbContext db, IPasswordHasher<Account> passwordHasher)
    {
        _db = db;
        _passwordHasher = passwordHasher;
    }

    public async Task<Response.EmployeeListResponse> GetListAsync(
        Request.EmployeeListRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidatePaging(request.PageIndex, request.PageSize);
        var query = BuildQuery(request);
        var totalItems = await query.CountAsync(cancellationToken);
        var ordered = ApplySort(query, request.SortBy);
        var items = await ordered
            .Skip((request.PageIndex - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(employee => new Response.EmployeeItemResponse
            {
                EmployeeId = employee.EmployeeId,
                AccountId = employee.AccountId,
                EmployeeCode = employee.EmployeeCode,
                FullName = employee.FullName,
                Email = employee.Account.Email,
                Phone = employee.Phone,
                Role = employee.Role.ToString(),
                AccountStatus = employee.Account.Status.ToString(),
                WorkingStatus = employee.IsActive ? "ACTIVE" : "INACTIVE",
                CreatedAt = AsUtc(employee.CreatedAt)
            })
            .ToListAsync(cancellationToken);

        return new Response.EmployeeListResponse
        {
            Items = items,
            PageIndex = request.PageIndex,
            PageSize = request.PageSize,
            TotalItems = totalItems,
            TotalPages = Paging.TotalPages(totalItems, request.PageSize)
        };
    }

    public async Task<byte[]> ExportAsync(
        Request.EmployeeListRequest request,
        CancellationToken cancellationToken = default)
    {
        // ponytail: in-memory workbook is enough for this admin list; stream if exports become large.
        var employees = await ApplySort(BuildQuery(request), request.SortBy)
            .Select(employee => new
            {
                employee.EmployeeCode,
                employee.FullName,
                employee.Account.Email,
                employee.Phone,
                Role = employee.Role.ToString(),
                AccountStatus = employee.Account.Status.ToString(),
                WorkingStatus = employee.IsActive ? "ACTIVE" : "INACTIVE",
                employee.CreatedAt
            })
            .ToListAsync(cancellationToken);

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Employees");
        var headers = new[] { "Employee Code", "Full Name", "Email", "Phone", "Role", "Account Status", "Working Status", "Created At" };
        for (var column = 0; column < headers.Length; column++)
            sheet.Cell(1, column + 1).Value = headers[column];
        for (var index = 0; index < employees.Count; index++)
        {
            var employee = employees[index];
            var row = index + 2;
            sheet.Cell(row, 1).Value = employee.EmployeeCode;
            sheet.Cell(row, 2).Value = employee.FullName;
            sheet.Cell(row, 3).Value = employee.Email;
            sheet.Cell(row, 4).Value = employee.Phone ?? string.Empty;
            sheet.Cell(row, 5).Value = employee.Role;
            sheet.Cell(row, 6).Value = employee.AccountStatus;
            sheet.Cell(row, 7).Value = employee.WorkingStatus;
            sheet.Cell(row, 8).Value = AsUtc(employee.CreatedAt).UtcDateTime;
        }
        sheet.Row(1).Style.Font.Bold = true;
        sheet.SheetView.FreezeRows(1);
        sheet.Column(8).Style.DateFormat.Format = "yyyy-mm-dd hh:mm:ss";
        sheet.Columns().AdjustToContents();
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public async Task<Response.EmployeeDetailResponse> GetAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        var employee = await GetVisibleEmployeeAsync(employeeId, cancellationToken);
        return ToDetail(employee);
    }

    public async Task<Response.EmployeeCreatedResponse> CreateAsync(
        Request.CreateEmployeeRequest request,
        Guid managerAccountId,
        CancellationToken cancellationToken = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        ValidateText(request.FullName, 150, "FullName", true);
        ValidateText(request.Phone, 30, "Phone", true);
        if (!Enum.TryParse<EmployeeRole>(request.Role.Trim(), true, out var role))
            throw new BadRequestException("Role is invalid", "INVALID_EMPLOYEE_ROLE");
        if (await _db.Accounts.AnyAsync(account => !account.IsDeleted && account.Email.ToLower() == email, cancellationToken))
            throw new ConflictException("The email address is already in use", "EMAIL_ALREADY_EXISTS");

        var now = UtcNow();
        var account = new Account
        {
            AccountId = Guid.NewGuid(),
            Email = email,
            AccountType = AccountType.EMPLOYEE,
            Status = AccountStatus.ACTIVE,
            CreatedAt = now,
            UpdatedAt = now
        };
        account.PasswordHash = _passwordHasher.HashPassword(account, DefaultPassword);
        var employee = new Employee
        {
            EmployeeId = Guid.NewGuid(),
            AccountId = account.AccountId,
            EmployeeCode = await GenerateEmployeeCodeAsync(cancellationToken),
            Role = role,
            FullName = request.FullName.Trim(),
            Phone = request.Phone.Trim(),
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            _db.Accounts.Add(account);
            _db.Employees.Add(employee);
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException postgres &&
            postgres.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new ConflictException("The email address is already in use", "EMAIL_ALREADY_EXISTS");
        }

        return new Response.EmployeeCreatedResponse
        {
            AccountId = account.AccountId,
            EmployeeId = employee.EmployeeId,
            EmployeeCode = employee.EmployeeCode,
            AccountType = account.AccountType.ToString(),
            AccountStatus = account.Status.ToString(),
            WorkingStatus = "ACTIVE",
            CreatedAt = AsUtc(employee.CreatedAt)
        };
    }

    public async Task<Response.EmploymentUpdatedResponse> UpdateAsync(
        Guid employeeId,
        Request.UpdateEmployeeRequest request,
        Guid managerAccountId,
        CancellationToken cancellationToken = default)
    {
        var employee = await GetVisibleEmployeeAsync(employeeId, cancellationToken);
        EnsureNotSelf(employee, managerAccountId);
        if (request.Role is null && request.WorkingStatus is null)
            throw new BadRequestException("At least one field must be provided");
        if (request.Role is not null)
        {
            if (!Enum.TryParse<EmployeeRole>(request.Role.Trim(), true, out var role))
                throw new BadRequestException("Role is invalid", "INVALID_EMPLOYEE_ROLE");
            employee.Role = role;
        }
        if (request.WorkingStatus is not null)
            employee.IsActive = ParseRequiredWorkingStatus(request.WorkingStatus);

        var now = UtcNow();
        employee.UpdatedAt = now;
        employee.Account.UpdatedAt = now;
        await _db.SaveChangesAsync(cancellationToken);
        return new Response.EmploymentUpdatedResponse { UpdatedAt = AsUtc(now) };
    }

    public async Task<Response.AccountStatusUpdatedResponse> UpdateStatusAsync(
        Guid employeeId,
        Request.UpdateEmployeeStatusRequest request,
        Guid managerAccountId,
        CancellationToken cancellationToken = default)
    {
        var employee = await GetVisibleEmployeeAsync(employeeId, cancellationToken);
        EnsureNotSelf(employee, managerAccountId);
        if (!Enum.TryParse<AccountStatus>(request.Status.Trim(), true, out var status))
            throw new BadRequestException("Status is invalid", "INVALID_ACCOUNT_STATUS");

        var now = UtcNow();
        if (status == employee.Account.Status && status != AccountStatus.SUSPENDED)
            return ToStatusResponse(employee, null);

        if (status == AccountStatus.SUSPENDED)
        {
            employee.Account.Status = status;
            employee.Account.IsDeleted = true;
            employee.Account.DeletedAt = now;
            employee.Account.DeletedByAccountId = managerAccountId;
            employee.IsDeleted = true;
            employee.IsActive = false;
            employee.DeletedAt = now;
            employee.DeletedByAccountId = managerAccountId;
            foreach (var token in await _db.RefreshTokens.Where(token => token.AccountId == employee.AccountId && token.RevokedAt == null).ToListAsync(cancellationToken))
                token.RevokedAt = now;
        }
        else
        {
            employee.Account.Status = status;
            employee.Account.UpdatedAt = now;
        }
        employee.UpdatedAt = now;
        await _db.SaveChangesAsync(cancellationToken);
        return ToStatusResponse(employee, status == AccountStatus.SUSPENDED ? now : null);
    }

    private async Task<Employee> GetVisibleEmployeeAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        var employee = await _db.Employees
            .Include(item => item.Account)
            .SingleOrDefaultAsync(item => item.EmployeeId == employeeId &&
                !item.IsDeleted && !item.Account.IsDeleted && item.Account.Status != AccountStatus.SUSPENDED, cancellationToken);
        return employee ?? throw new NotFoundException("Employee not found", "EMPLOYEE_NOT_FOUND");
    }

    private IQueryable<Employee> BuildQuery(Request.EmployeeListRequest request)
    {
        var role = ParseOptionalRole(request.Role);
        var accountStatus = ParseAccountStatusFilter(request.AccountStatus);
        var workingStatus = ParseWorkingStatus(request.WorkingStatus);
        var query = _db.Employees.AsNoTracking().Where(employee =>
            !employee.IsDeleted && !employee.Account.IsDeleted && employee.Account.Status != AccountStatus.SUSPENDED);
        if (role is not null) query = query.Where(employee => employee.Role == role.Value);
        if (accountStatus is not null) query = query.Where(employee => employee.Account.Status == accountStatus.Value);
        if (workingStatus is not null) query = query.Where(employee => employee.IsActive == workingStatus.Value);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToLower();
            query = query.Where(employee =>
                EF.Functions.Like(employee.FullName.ToLower(), $"%{search}%") ||
                EF.Functions.Like(employee.Account.Email.ToLower(), $"%{search}%") ||
                (employee.Phone != null && EF.Functions.Like(employee.Phone.ToLower(), $"%{search}%")) ||
                EF.Functions.Like(employee.EmployeeCode.ToLower(), $"%{search}%"));
        }
        return query;
    }

    private static IQueryable<Employee> ApplySort(IQueryable<Employee> query, string[]? values)
    {
        var sorts = (values ?? ["createdAt:desc"])
            .SelectMany(value => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToArray();
        if (sorts.Length == 0)
            sorts = ["createdAt:desc"];
        IOrderedQueryable<Employee>? ordered = null;
        foreach (var sort in sorts)
        {
            var parts = sort.Split(':', StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || parts[1].ToLowerInvariant() is not ("asc" or "desc"))
                throw new BadRequestException("The sort parameter is invalid", "INVALID_SORT_PARAMETER");
            var ascending = parts[1].Equals("asc", StringComparison.OrdinalIgnoreCase);
            ordered = parts[0].ToLowerInvariant() switch
            {
                "createdat" => ordered is null ? (ascending ? query.OrderBy(item => item.CreatedAt) : query.OrderByDescending(item => item.CreatedAt)) : (ascending ? ordered.ThenBy(item => item.CreatedAt) : ordered.ThenByDescending(item => item.CreatedAt)),
                "fullname" => ordered is null ? (ascending ? query.OrderBy(item => item.FullName) : query.OrderByDescending(item => item.FullName)) : (ascending ? ordered.ThenBy(item => item.FullName) : ordered.ThenByDescending(item => item.FullName)),
                "employeecode" => ordered is null ? (ascending ? query.OrderBy(item => item.EmployeeCode) : query.OrderByDescending(item => item.EmployeeCode)) : (ascending ? ordered.ThenBy(item => item.EmployeeCode) : ordered.ThenByDescending(item => item.EmployeeCode)),
                _ => throw new BadRequestException("The sort parameter is invalid", "INVALID_SORT_PARAMETER")
            };
        }
        return ordered!;
    }

    private async Task<string> GenerateEmployeeCodeAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var code = $"EMP-{DateTime.UtcNow:yyyyMMdd}-{RandomNumberGenerator.GetInt32(0, 10_000):D4}";
            if (!await _db.Employees.AnyAsync(item => item.EmployeeCode == code, cancellationToken))
                return code;
        }
        throw new ConflictException("Could not generate a unique employee code", "EMPLOYEE_CODE_GENERATION_FAILED");
    }

    private static Response.EmployeeDetailResponse ToDetail(Employee employee) => new()
    {
        EmployeeId = employee.EmployeeId,
        AccountId = employee.AccountId,
        EmployeeCode = employee.EmployeeCode,
        Role = employee.Role.ToString(),
        WorkingStatus = employee.IsActive ? "ACTIVE" : "INACTIVE",
        AccountStatus = employee.Account.Status.ToString(),
        Email = employee.Account.Email,
        FullName = employee.FullName,
        Phone = employee.Phone,
        DateOfBirth = employee.DateOfBirth,
        AvatarUrl = employee.Account.AvatarUrl,
        LastLoginAt = employee.Account.LastLoginAt.HasValue ? AsUtc(employee.Account.LastLoginAt.Value) : null,
        CreatedAt = AsUtc(employee.CreatedAt),
        UpdatedAt = AsUtc(employee.UpdatedAt)
    };

    private static Response.AccountStatusUpdatedResponse ToStatusResponse(Employee employee, DateTime? deletedAt) => new()
    {
        Status = employee.Account.Status.ToString(),
        IsDeleted = employee.IsDeleted,
        DeletedAt = deletedAt.HasValue ? AsUtc(deletedAt.Value) : null,
        UpdatedAt = AsUtc(employee.UpdatedAt)
    };

    private static void EnsureNotSelf(Employee employee, Guid managerAccountId)
    {
        if (employee.AccountId == managerAccountId)
            throw new ConflictException("You cannot update your own employee account", "CANNOT_UPDATE_OWN_ACCOUNT");
    }

    private static EmployeeRole? ParseOptionalRole(string? value) => string.IsNullOrWhiteSpace(value)
        ? null
        : Enum.TryParse<EmployeeRole>(value.Trim(), true, out var role)
            ? role
            : throw new BadRequestException("Role is invalid", "INVALID_EMPLOYEE_ROLE");

    private static AccountStatus? ParseAccountStatusFilter(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!Enum.TryParse<AccountStatus>(value.Trim(), true, out var status) || status == AccountStatus.SUSPENDED)
            throw new BadRequestException("Account status must be ACTIVE or INACTIVE", "INVALID_ACCOUNT_STATUS");
        return status;
    }

    private static bool? ParseWorkingStatus(string? value) => string.IsNullOrWhiteSpace(value)
        ? null
        : value.Trim().Equals("ACTIVE", StringComparison.OrdinalIgnoreCase)
            ? true
            : value.Trim().Equals("INACTIVE", StringComparison.OrdinalIgnoreCase)
                ? false
                : throw new BadRequestException("Working status must be ACTIVE or INACTIVE", "INVALID_WORKING_STATUS");

    private static bool ParseRequiredWorkingStatus(string value) => ParseWorkingStatus(value) ?? throw new BadRequestException("Working status is required", "INVALID_WORKING_STATUS");

    private static void ValidatePaging(int pageIndex, int pageSize)
    {
        if (pageIndex < 1) throw new BadRequestException("PageIndex must be greater than or equal to 1");
        if (pageSize < 1 || pageSize > 100) throw new BadRequestException("PageSize must be between 1 and 100");
    }

    private static void ValidateText(string? value, int maxLength, string field, bool required)
    {
        if (required && string.IsNullOrWhiteSpace(value)) throw new BadRequestException($"{field} is required");
        if (value?.Length > maxLength) throw new BadRequestException($"{field} must not exceed {maxLength} characters");
    }

}
