namespace Pegaxus.Service.AccountService;

public static class Response
{
    public sealed class ProfileResponse
    {
        public Guid AccountId { get; init; }
        public required string Email { get; init; }
        public required string AccountType { get; init; }
        public required string Status { get; init; }
        public required string Role { get; init; }
        public string? FullName { get; init; }
        public string? Phone { get; init; }
        public string? Country { get; init; }
        public string? Address { get; init; }
        public string? AvatarUrl { get; init; }
        public CustomerResponse? Customer { get; init; }
        public EmployeeResponse? Employee { get; init; }
        public DateTimeOffset UpdatedAt { get; init; }
    }

    public sealed class CustomerResponse
    {
        public Guid CustomerId { get; init; }
        public DateOnly? DateOfBirth { get; init; }
    }

    public sealed class EmployeeResponse
    {
        public Guid EmployeeId { get; init; }
        public required string EmployeeCode { get; init; }
    }
}
