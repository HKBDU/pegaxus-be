namespace Pegaxus.Service.EmployeeService;

public static class Response
{
    public sealed class EmployeeListResponse
    {
        public required IReadOnlyList<EmployeeItemResponse> Items { get; init; }
        public int PageIndex { get; init; }
        public int PageSize { get; init; }
        public int TotalItems { get; init; }
        public int TotalPages { get; init; }
    }

    public sealed class EmployeeItemResponse
    {
        public Guid EmployeeId { get; init; }
        public Guid AccountId { get; init; }
        public required string EmployeeCode { get; init; }
        public required string FullName { get; init; }
        public required string Email { get; init; }
        public string? Phone { get; init; }
        public required string Role { get; init; }
        public required string AccountStatus { get; init; }
        public required string WorkingStatus { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
    }

    public sealed class EmployeeDetailResponse
    {
        public Guid EmployeeId { get; init; }
        public Guid AccountId { get; init; }
        public required string EmployeeCode { get; init; }
        public required string Role { get; init; }
        public required string WorkingStatus { get; init; }
        public required string AccountStatus { get; init; }
        public required string Email { get; init; }
        public required string FullName { get; init; }
        public string? Phone { get; init; }
        public DateOnly? DateOfBirth { get; init; }
        public string? AvatarUrl { get; init; }
        public DateTimeOffset? LastLoginAt { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset UpdatedAt { get; init; }
    }

    public sealed class EmployeeCreatedResponse
    {
        public Guid AccountId { get; init; }
        public Guid EmployeeId { get; init; }
        public required string EmployeeCode { get; init; }
        public required string AccountType { get; init; }
        public required string AccountStatus { get; init; }
        public required string WorkingStatus { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
    }

    public sealed class EmploymentUpdatedResponse
    {
        public DateTimeOffset UpdatedAt { get; init; }
    }

    public sealed class AccountStatusUpdatedResponse
    {
        public required string Status { get; init; }
        public bool IsDeleted { get; init; }
        public DateTimeOffset? DeletedAt { get; init; }
        public DateTimeOffset UpdatedAt { get; init; }
    }
}
