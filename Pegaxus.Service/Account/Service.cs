using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;

using static Pegaxus.Service.Common.DbTime;

namespace Pegaxus.Service.AccountService;

public sealed class Service : IAccountService
{
    private readonly AppDbContext _appDbContext;
    private readonly MediaService.IMediaService _mediaService;
    private readonly IPasswordHasher<Account> _passwordHasher;

    public Service( AppDbContext appDbContext, MediaService.IMediaService mediaService, IPasswordHasher<Account> passwordHasher)
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
                EmployeeCode = employeeData.EmployeeCode,
                DateOfBirth = employeeData.DateOfBirth
            }
            : null;
        var isEmployee = employee is not null;

        return new Response.ProfileResponse
        {
            AccountId = account.AccountId,
            Email = account.Email,
            EmailVerificationStatus = account.EmailVerifiedAt.HasValue ? "VERIFIED" : "UNVERIFIED",
            AccountType = account.AccountType.ToString(),
            Status = account.Status.ToString(),
            Role = isEmployee ? account.EmployeeAccount!.Role.ToString() : "CUSTOMER",
            FullName = isEmployee ? account.EmployeeAccount!.FullName : account.CustomerAccount?.FullName,
            Phone = isEmployee ? account.EmployeeAccount!.Phone : account.CustomerAccount?.Phone,
            Country = isEmployee ? account.EmployeeAccount!.Country : account.CustomerAccount?.Country,
            Address = isEmployee ? account.EmployeeAccount!.Address : account.CustomerAccount?.Address,
            IdentityNumber = account.CustomerAccount?.IdentityNumber,
            AvatarUrl = account.AvatarUrl,
            Customer = customer,
            Employee = employee,
            CreatedAt = AsUtc(account.CreatedAt),
            LastLoginAt = account.LastLoginAt.HasValue
                ? AsUtc(account.LastLoginAt.Value)
                : null,
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

        ValidateText(request.FullName is null ? null : Text.NormalizeName(request.FullName), 150, "FullName");
        ValidateText(request.Phone, 30, "Phone");
        Phones.EnsureValid(request.Phone);
        ValidateText(request.Address, 500, "Address");
        ValidateText(request.Country, 100, "Country");
        // Assignment filters match employees by ISO code, so an employee's country must use it.
        if (account.EmployeeAccount is { IsDeleted: false }) CountryCodes.EnsureValid(request.Country);
        ValidateText(request.IdentityNumber, 100, "IdentityNumber");

        if (request.IdentityNumber is not null && account.CustomerAccount is null)
            throw new BadRequestException("Identity number is only available for customer accounts", "INVALID_PROFILE_FIELD");

        if (request.DateOfBirth is { } dateOfBirth && dateOfBirth > DateOnly.FromDateTime(DateTime.UtcNow))
            throw new BadRequestException("Date of birth cannot be in the future");
        if (request.DateOfBirth is { } birth)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            if (birth > today.AddYears(-Limits.AccountMinAgeYears))
                throw new BadRequestException($"You must be at least {Limits.AccountMinAgeYears} years old");
            if (birth < today.AddYears(-Limits.AccountMaxAgeYears))
                throw new BadRequestException($"Date of birth cannot be more than {Limits.AccountMaxAgeYears} years ago");
        }

        var avatarUrl = request.Avatar is null ? null : await ConfirmAvatarAsync(accountId, request.Avatar, cancellationToken);

        if (request.FullName is not null)
        {
            if (account.CustomerAccount is { IsDeleted: false } customer)
                customer.FullName = Text.NormalizeName(request.FullName);
            if (account.EmployeeAccount is { IsDeleted: false } employee)
                employee.FullName = Text.NormalizeName(request.FullName);
        }
        if (request.DateOfBirth is not null)
        {
            if (account.CustomerAccount is { IsDeleted: false } customer)
                customer.DateOfBirth = request.DateOfBirth;
            if (account.EmployeeAccount is { IsDeleted: false } employee)
                employee.DateOfBirth = request.DateOfBirth;
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
        if (request.Country is not null)
        {
            if (account.CustomerAccount is { IsDeleted: false } customer)
                customer.Country = request.Country.Trim();
            if (account.EmployeeAccount is { IsDeleted: false } employee)
                employee.Country = CountryCodes.Normalize(request.Country);
        }
        if (request.IdentityNumber is not null && account.CustomerAccount is { IsDeleted: false } customerAccount)
            customerAccount.IdentityNumber = request.IdentityNumber.Trim();
        if (avatarUrl is not null)
            account.AvatarUrl = avatarUrl;

        account.UpdatedAt = UtcNow();
        await _appDbContext.SaveChangesAsync(cancellationToken);

        return await GetCurrentProfileAsync(accountId, cancellationToken);
    }

    private static void ValidateText(string? value, int maxLength, string fieldName)
    {
        if (value is not null && value.Length > maxLength)
            throw new BadRequestException($"{fieldName} must not exceed {maxLength} characters");
    }

    private async Task<string> ConfirmAvatarAsync(Guid accountId, UploadService.Request.UploadedAssetReference avatar, CancellationToken cancellationToken)
    {
        var asset = await _mediaService.ConfirmAvatarAsync(accountId, avatar.PublicId, cancellationToken);
        var content = await _mediaService.DownloadPreviewAsync(asset.FileUrl, cancellationToken);
        if (content.Content.Length != asset.SizeBytes || content.ContentType != asset.MimeType)
            throw new BadRequestException("Image must be JPG/PNG and no larger than 2 MB", "INVALID_FILE");
        return asset.FileUrl;
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
                "Current password is incorrect",
                "CURRENT_PASSWORD_INCORRECT");

        if (!string.Equals(request.NewPassword, request.ConfirmPassword, StringComparison.Ordinal))
            throw new BadRequestException(
                "Password confirmation does not match",
                "PASSWORD_MISMATCH");

        ValidatePassword(request.NewPassword);
        account.PasswordHash = _passwordHasher.HashPassword(account, request.NewPassword);
        account.UpdatedAt = UtcNow();

        await _appDbContext.SaveChangesAsync(cancellationToken);
    }

    private static void ValidatePassword(string password)
    {
        if (password.Length is < 6 or > 128 ||
            !password.Any(char.IsUpper) ||
            !password.Any(char.IsLower) ||
            !password.Any(char.IsDigit))
            throw new BadRequestException(
                "New password must be 6 to 128 characters and contain uppercase, lowercase, and numeric characters");
    }
}
