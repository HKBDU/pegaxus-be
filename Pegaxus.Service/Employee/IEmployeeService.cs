namespace Pegaxus.Service.EmployeeService;

public interface IEmployeeService
{
    Task<Response.EmployeeListResponse> GetListAsync(Request.EmployeeListRequest request, CancellationToken cancellationToken = default);
    Task<byte[]> ExportAsync(Request.EmployeeListRequest request, CancellationToken cancellationToken = default);
    Task<Response.EmployeeDetailResponse> GetAsync(Guid employeeId, CancellationToken cancellationToken = default);
    Task<Response.EmployeeCreatedResponse> CreateAsync(Request.CreateEmployeeRequest request, Guid managerAccountId, CancellationToken cancellationToken = default);
    Task<Response.EmploymentUpdatedResponse> UpdateAsync(Guid employeeId, Request.UpdateEmployeeRequest request, Guid managerAccountId, CancellationToken cancellationToken = default);
    Task<Response.AccountStatusUpdatedResponse> UpdateStatusAsync(Guid employeeId, Request.UpdateEmployeeStatusRequest request, Guid managerAccountId, CancellationToken cancellationToken = default);
}
