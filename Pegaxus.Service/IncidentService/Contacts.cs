using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Repository.Extensions;

namespace Pegaxus.Service.IncidentService;

public sealed partial class Service
{
    private async Task<IReadOnlyList<Response.EmployeeBrief>> ContactsAsync(Transport transport, CancellationToken ct)
    {
        var planId = await CurrentPlanIdAsync(transport.TransportId, ct);
        var current = await CurrentSegmentAsync(transport, ct);
        var driverId = current?.TransportProviderId is null ? current?.DriverEmployeeId : null;
        var escortId = current?.TransportProviderId is null ? current?.EscortEmployeeId : null;
        var employees = await db.Employees.AsNoTracking().Active().Where(employee => employee.Account.AccountType == AccountType.EMPLOYEE &&
            (employee.Role == EmployeeRole.LOGISTICS_MANAGER ||
             employee.Role == EmployeeRole.FLEET_ROUTE_COORDINATOR && employee.EmployeeTransportPlans.Any(item => item.TransportPlanId == planId) ||
             employee.Role == EmployeeRole.DRIVER && employee.EmployeeId == driverId ||
             employee.Role == EmployeeRole.ESCORT && employee.EmployeeId == escortId)).ToArrayAsync(ct);
        return employees.OrderBy(employee => employee.Role switch
            { EmployeeRole.LOGISTICS_MANAGER => 0, EmployeeRole.FLEET_ROUTE_COORDINATOR => 1, EmployeeRole.DRIVER => 2, _ => 3 })
            .ThenBy(employee => employee.EmployeeCode, StringComparer.Ordinal).Select(EmployeeBrief).ToArray();
    }
}
