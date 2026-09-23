using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Service.Exceptions;

namespace Pegaxus.Service.AccountService;

public sealed class Service : IService
{
    private readonly AppDbContext _appDbContext;
    private readonly MediaService.IService _mediaService;
    private readonly IPasswordHasher<Account> _passwordHasher;

    public Service( AppDbContext appDbContext, MediaService.IService mediaService, IPasswordHasher<Account> passwordHasher)
    {
        _appDbContext = appDbContext;
        _mediaService = mediaService;
        _passwordHasher = passwordHasher;
    }

    public async Task<Response.ProfileResponse> GetCurrentProfileAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        var account = await _appDbContext.Accounts
            .AsNoTracking()
            .Include(item => item.CustomerAccount)
            .Include(item => item.EmployeeAccount)
            .SingleOrDefaultAsync(item =>
                item.AccountId == accountId &&
                !item.IsDeleted,
                cancellationToken);

        if (account is null)
            throw new KeyNotFoundException("Account not found");

        var customer = account.CustomerAccount is { IsDeleted: false } customerData
            ? new Response.CustomerResponse
            {
                CustomerId = customerData.CustomerId,
                DateOfBirth = customerData.DateOfBirth
            }
            : null;
        var employee = account.EmployeeAccount is { IsDeleted: false } employeeData
            ? new Response.EmployeeResponse
            {
                EmployeeId = employeeData.EmployeeId,
                EmployeeCode = employeeData.EmployeeCode
            }
            : null;
        var isEmployee = employee is not null;

        return new Response.ProfileResponse
        {
            AccountId = account.AccountId,
            Email = account.Email,
            AccountType = account.AccountType.ToString(),
            Status = account.Status.ToString(),
            Role = isEmployee ? account.EmployeeAccount!.Role.ToString() : "CUSTOMER",
            FullName = isEmployee ? account.EmployeeAccount!.FullName : account.CustomerAccount?.FullName,
            Phone = isEmployee ? account.EmployeeAccount!.Phone : account.CustomerAccount?.Phone,
            Country = isEmployee ? account.EmployeeAccount!.Country : account.CustomerAccount?.Country,
            Address = isEmployee ? account.EmployeeAccount!.Address : account.CustomerAccount?.Address,
            AvatarUrl = account.AvatarUrl,
            Customer = customer,
            Employee = employee,
            UpdatedAt = new DateTimeOffset(DateTime.SpecifyKind(account.UpdatedAt, DateTimeKind.Utc))
        };
    }

    public async Task<Response.ProfileResponse> UpdateProfileAsync(
        Guid accountId,
        Request.UpdateProfileRequest request,
        CancellationToken cancellationToken = default)
    {
        var account = await _appDbContext.Accounts
            .Include(item => item.CustomerAccount)
            .Include(item => item.EmployeeAccount)
            .SingleOrDefaultAsync(item =>
                item.AccountId == accountId &&
                !item.IsDeleted,
                cancellationToken);

        if (account is null)
            throw new KeyNotFoundException("Account not found");

        ValidateText(request.FullName, 150, "FullName");
        ValidateText(request.Phone, 30, "Phone");
        ValidateText(request.Address, 500, "Address");

        if (request.Avatar is not null)
            ValidateAvatar(request.Avatar);

        if (request.FullName is not null)
        {
            if (account.CustomerAccount is { IsDeleted: false } customer)
                customer.FullName = request.FullName.Trim();
            if (account.EmployeeAccount is { IsDeleted: false } employee)
                employee.FullName = request.FullName.Trim();
        }
        if (request.Phone is not null)
        {
            if (account.CustomerAccount is { IsDeleted: false } customer)
                customer.Phone = request.Phone.Trim();
            if (account.EmployeeAccount is { IsDeleted: false } employee)
                employee.Phone = request.Phone.Trim();
        }
        if (request.Address is not null)
        {
            if (account.CustomerAccount is { IsDeleted: false } customer)
                customer.Address = request.Address.Trim();
            if (account.EmployeeAccount is { IsDeleted: false } employee)
                employee.Address = request.Address.Trim();
        }
        if (request.Avatar is not null)
            account.AvatarUrl = await _mediaService.UploadImageAsync(request.Avatar);

        account.UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        await _appDbContext.SaveChangesAsync(cancellationToken);

        return await GetCurrentProfileAsync(accountId, cancellationToken);
    }

    private static void ValidateText(string? value, int maxLength, string fieldName)
    {
        if (value is not null && value.Length > maxLength)
            throw new BadRequestException($"{fieldName} must not exceed {maxLength} characters");
    }

    private static void ValidateAvatar(IFormFile file)
    {
        const long maxFileSize = 2 * 1024 * 1024;
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var validExtension = extension is ".jpg" or ".jpeg" or ".png";
        var validContentType = file.ContentType is "image/jpeg" or "image/png";

        if (file.Length == 0 || file.Length > maxFileSize || !validExtension || !validContentType)
            throw new BadRequestException("Ảnh phải là JPG/PNG và không quá 2 MB", "INVALID_FILE");
    }

    public async Task ChangePasswordAsync(
        Guid accountId,
        Request.ChangePasswordRequest request,
        CancellationToken cancellationToken = default)
    {
        var account = await _appDbContext.Accounts.SingleOrDefaultAsync(item =>
            item.AccountId == accountId &&
            !item.IsDeleted,
            cancellationToken);

        if (account is null)
            throw new KeyNotFoundException("Account not found");

        if (_passwordHasher.VerifyHashedPassword(
                account,
                account.PasswordHash,
                request.CurrentPassword) is not (PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded))
            throw new BadRequestException(
                "Mật khẩu hiện tại không chính xác",
                "CURRENT_PASSWORD_INCORRECT");

        if (!string.Equals(request.NewPassword, request.ConfirmPassword, StringComparison.Ordinal))
            throw new BadRequestException(
                "Mật khẩu xác nhận không khớp",
                "PASSWORD_MISMATCH");

        ValidatePassword(request.NewPassword);
        account.PasswordHash = _passwordHasher.HashPassword(account, request.NewPassword);
        account.UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

        await _appDbContext.SaveChangesAsync(cancellationToken);
    }

    private static void ValidatePassword(string password)
    {
        if (password.Length is < 6 or > 128 ||
            !password.Any(char.IsUpper) ||
            !password.Any(char.IsLower) ||
            !password.Any(char.IsDigit))
            throw new BadRequestException(
                "Mật khẩu mới phải dài từ 6 đến 128 ký tự và chứa chữ hoa, chữ thường, số");
    }
}
