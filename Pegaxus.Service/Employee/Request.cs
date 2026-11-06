
namespace Pegaxus.Service.EmployeeService;

public static class Request
{
    public sealed class EmployeeListRequest
    {
        public int PageIndex { get; set; } = 1;
        public int PageSize { get; set; } = 10;
        public string? Search { get; set; }
        public string? Role { get; set; }
        public string? AccountStatus { get; set; }
        public string? WorkingStatus { get; set; }
        public string[]? SortBy { get; set; }
    }

    public sealed class CreateEmployeeRequest
    {
        public string Role { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string? Country { get; set; }
    }

    public sealed class UpdateEmployeeRequest
    {
        public string? Role { get; set; }
        public string? WorkingStatus { get; set; }
        public string? Country { get; set; }
    }

    public sealed class UpdateEmployeeStatusRequest
    {
        public string Status { get; set; } = string.Empty;
    }
}
