using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;

namespace Pegaxus.Repository.Extensions;

public static class EmployeeQueryExtensions
{
    /// <summary>Employees that are active and not deleted, with an active and not deleted account.</summary>
    public static IQueryable<Employee> Active(this IQueryable<Employee> employees) =>
        employees.Where(employee =>
            !employee.IsDeleted &&
            employee.IsActive &&
            !employee.Account.IsDeleted &&
            employee.Account.Status == AccountStatus.ACTIVE);
}
