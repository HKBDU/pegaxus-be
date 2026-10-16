using Pegaxus.Repository.Enum;

namespace Pegaxus.Service.Common;

public static class RoleLabel
{
    public static string Of(EmployeeRole role) => role switch
    {
        EmployeeRole.LOGISTICS_MANAGER => "Logistics Manager",
        EmployeeRole.TRANSPORT_SPECIALIST => "Transport Specialist",
        EmployeeRole.FLEET_ROUTE_COORDINATOR => "Fleet Route Coordinator",
        EmployeeRole.DRIVER => "Driver",
        EmployeeRole.ESCORT => "Escort",
        _ => role.ToString()
    };
}
