using Microsoft.AspNetCore.Http;

namespace Pegaxus.Service.AccountService;

public static class Request
{
    public sealed class UpdateProfileRequest
    {
        public string? FullName { get; set; }
        public DateOnly? DateOfBirth { get; set; }
        public string? Phone { get; set; }
        public string? Address { get; set; }
        public IFormFile? Avatar { get; set; }
    }

    public sealed class ChangePasswordRequest
    {
        public string CurrentPassword { get; set; } = string.Empty;
        public string NewPassword { get; set; } = string.Empty;
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
