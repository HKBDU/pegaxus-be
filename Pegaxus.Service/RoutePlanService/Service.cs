using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Extensions;
using Pegaxus.Service.Exceptions;
using QuestPDF.Infrastructure;

using static Pegaxus.Service.Common.DbTime;

namespace Pegaxus.Service.RoutePlanService;

public sealed partial class Service : IService
{
    private readonly AppDbContext db;
    private readonly NotificationService.IService notificationService;

    public Service(AppDbContext db, NotificationService.IService notificationService)
    {
        this.db = db;
        this.notificationService = notificationService;
    }

    private async Task<Employee> RequireEmployeeAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var employee = await db.Employees.Include(item => item.Account)
            .Active()
            .SingleOrDefaultAsync(item => item.AccountId == accountId, cancellationToken);
        return employee ?? throw new ForbiddenException("Only active employees can access Route Plans");
    }

    private async Task<TransportPlan> LoadPlanAsync(Guid transportPlanId, CancellationToken cancellationToken)
    {
        var plan = await db.TransportPlans
            .Include(item => item.Transport).ThenInclude(item => item.Booking).ThenInclude(item => item.Customer)
            .Include(item => item.Transport).ThenInclude(item => item.Booking).ThenInclude(item => item.BookingHorses)
            .Include(item => item.Transport).ThenInclude(item => item.ComplianceDossier)
            .Include(item => item.EmployeeTransportPlans).ThenInclude(item => item.Employee)
            .Include(item => item.RoutePlans).ThenInclude(item => item.RoutePlanRoutes).ThenInclude(item => item.Route).ThenInclude(item => item.FromLocation)
            .Include(item => item.RoutePlans).ThenInclude(item => item.RoutePlanRoutes).ThenInclude(item => item.Route).ThenInclude(item => item.ToLocation)
            .Include(item => item.RoutePlans).ThenInclude(item => item.RoutePlanRoutes).ThenInclude(item => item.DriverEmployee)
            .Include(item => item.RoutePlans).ThenInclude(item => item.RoutePlanRoutes).ThenInclude(item => item.EscortEmployee)
            .SingleOrDefaultAsync(item => item.TransportPlanId == transportPlanId, cancellationToken);
        return plan ?? throw new NotFoundException("Transport Plan not found", "TRANSPORT_PLAN_NOT_FOUND");
    }

    private async Task<RoutePlan?> LoadRoutePlanAsync(Guid routePlanId, CancellationToken cancellationToken) =>
        await db.RoutePlans
            .Include(item => item.TransportPlan).ThenInclude(item => item.Transport).ThenInclude(item => item.Booking).ThenInclude(item => item.Customer)
            .Include(item => item.TransportPlan).ThenInclude(item => item.Transport).ThenInclude(item => item.ComplianceDossier)
            .Include(item => item.TransportPlan).ThenInclude(item => item.EmployeeTransportPlans).ThenInclude(item => item.Employee)
            .Include(item => item.TransportPlan).ThenInclude(item => item.RoutePlans).ThenInclude(item => item.RoutePlanRoutes).ThenInclude(item => item.Route).ThenInclude(item => item.FromLocation)
            .Include(item => item.TransportPlan).ThenInclude(item => item.RoutePlans).ThenInclude(item => item.RoutePlanRoutes).ThenInclude(item => item.Route).ThenInclude(item => item.ToLocation)
            .Include(item => item.TransportPlan).ThenInclude(item => item.RoutePlans).ThenInclude(item => item.RoutePlanRoutes).ThenInclude(item => item.RoutePlanRouteVehicles).ThenInclude(item => item.Vehicle)
            .Include(item => item.TransportPlan).ThenInclude(item => item.RoutePlans).ThenInclude(item => item.RoutePlanRoutes).ThenInclude(item => item.TransportProvider)
            .Include(item => item.TransportPlan).ThenInclude(item => item.RoutePlans).ThenInclude(item => item.RoutePlanRoutes).ThenInclude(item => item.DriverEmployee)
            .Include(item => item.TransportPlan).ThenInclude(item => item.RoutePlans).ThenInclude(item => item.RoutePlanRoutes).ThenInclude(item => item.EscortEmployee)
            .Include(item => item.RoutePlanRoutes).ThenInclude(item => item.Route).ThenInclude(item => item.FromLocation)
            .Include(item => item.RoutePlanRoutes).ThenInclude(item => item.Route).ThenInclude(item => item.ToLocation)
            .Include(item => item.RoutePlanRoutes).ThenInclude(item => item.RoutePlanRouteVehicles).ThenInclude(item => item.Vehicle)
            .Include(item => item.RoutePlanRoutes).ThenInclude(item => item.TransportProvider)
            .Include(item => item.RoutePlanRoutes).ThenInclude(item => item.DriverEmployee)
            .Include(item => item.RoutePlanRoutes).ThenInclude(item => item.EscortEmployee)
            .SingleOrDefaultAsync(item => item.RoutePlanId == routePlanId, cancellationToken);

    private static void EnsureConcurrency(DateTime actual, DateTimeOffset expected)
    {
        if (Truncate(expected.UtcDateTime) != actual)
            throw new ConflictException("The route plan was just modified. Please reload the page", "ROUTE_PLAN_MODIFIED");
    }
}
