using System.ComponentModel.DataAnnotations;

namespace Pegaxus.Service.Auth;

public sealed class RegisterRequest
{
    [Required(ErrorMessage = "Full name is required")]
    [StringLength(150, ErrorMessage = "Full name must not exceed 150 characters")]
    public string FullName { get; init; } = string.Empty;

    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "Email format is invalid")]
    [StringLength(255, ErrorMessage = "Email must not exceed 255 characters")]
    public string Email { get; init; } = string.Empty;

    [Required(ErrorMessage = "Password is required")]
    [StringLength(128, MinimumLength = 8, ErrorMessage = "Password must be between 8 and 128 characters")]
    [RegularExpression("^(?=.*[a-z])(?=.*[A-Z])(?=.*\\d).+$", ErrorMessage = "Password must contain an uppercase letter, a lowercase letter, and a number")]
    public string Password { get; init; } = string.Empty;

    [Required(ErrorMessage = "Password confirmation is required")]
    [Compare(nameof(Password), ErrorMessage = "Password confirmation does not match")]
    public string ConfirmPassword { get; init; } = string.Empty;
}
