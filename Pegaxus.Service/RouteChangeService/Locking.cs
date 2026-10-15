using System.Data;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Repository.Extensions;
using Pegaxus.Service.Exceptions;

using static Pegaxus.Service.Common.DbTime;
using static Pegaxus.Service.Common.Validation;

namespace Pegaxus.Service.RouteChangeService;

public sealed partial class Service
{
    private async Task EnsureCoordinatorAsync(RoutePlan plan, Employee actor, CancellationToken token)
    {
        if (!await db.EmployeeTransportPlans.AnyAsync(x => x.TransportPlanId == plan.TransportPlanId &&
            x.EmployeeId == actor.EmployeeId && x.Responsibility == "FLEET_ROUTE_COORDINATOR", token))
            throw new ForbiddenException("Only the Route Coordinator assigned to this Transport Plan can perform this action.");
    }

    private async Task<RoutePlan> PrimaryAsync(RoutePlan plan, CancellationToken token)
    {
        var primaryIds = await db.RoutePlans.AsNoTracking().Where(x => x.TransportPlanId == plan.TransportPlanId &&
            x.Status == RoutePlanStatus.ACTIVE && x.RoutePlanRoutes.Any() &&
            x.RoutePlanRoutes.All(s => s.RouteType == RouteType.PRIMARY)).Select(x => x.RoutePlanId).ToArrayAsync(token);
        if (primaryIds.Length != 1) throw new ConflictException("One active PRIMARY route is required.", "PRIMARY_ROUTE_PLAN_REQUIRED");
        return await LockPlanAsync(primaryIds[0], token);
    }

    private async Task<Employee> ActorAsync(Guid accountId, EmployeeRole[] roles, CancellationToken token)
    {
        var actor = await db.Employees.AsNoTracking().Include(x => x.Account).SingleOrDefaultAsync(x => x.AccountId == accountId, token);
        if (actor is null || !actor.IsActive || actor.IsDeleted || actor.Account.IsDeleted || actor.Account.Status != AccountStatus.ACTIVE ||
            actor.Account.AccountType != AccountType.EMPLOYEE || !roles.Contains(actor.Role))
            throw new ForbiddenException("You do not have permission to perform this action.");
        return actor;
    }

    private async Task<RoutePlan> LockPlanAsync(Guid routePlanId, CancellationToken token)
    {
        var header = await db.RoutePlans.AsNoTracking().Where(x => x.RoutePlanId == routePlanId)
            .Select(x => new { x.TransportPlanId, x.TransportPlan.TransportId }).SingleOrDefaultAsync(token)
            ?? throw new NotFoundException("Route plan not found.", "ROUTE_PLAN_NOT_FOUND");
        if (db.Database.IsRelational())
        {
            // Updating the key to itself holds a row lock until the transaction ends.
            await db.LockRowsAsync(db.Transports.Where(x => x.TransportId == header.TransportId), x => x.TransportId, token);
            await db.LockRowsAsync(db.TransportPlans.Where(x => x.TransportPlanId == header.TransportPlanId), x => x.TransportPlanId, token);
            await db.LockRowsAsync(db.RoutePlans.Where(x => x.RoutePlanId == routePlanId), x => x.RoutePlanId, token);
            await LockSegmentsAsync(routePlanId, token);
        }
        var plan = await db.RoutePlans.Include(x => x.TransportPlan).ThenInclude(x => x.Transport).ThenInclude(x => x.Booking).ThenInclude(x => x.Customer).ThenInclude(x => x.Account)
            .Include(x => x.TransportPlan).ThenInclude(x => x.Transport).ThenInclude(x => x.ComplianceDossier)
            .SingleAsync(x => x.RoutePlanId == routePlanId, token);
        await db.Entry(plan).ReloadAsync(token);
        await db.Entry(plan.TransportPlan).ReloadAsync(token);
        await db.Entry(plan.TransportPlan.Transport).ReloadAsync(token);
        var dossier = plan.TransportPlan.Transport.ComplianceDossier;
        if (dossier is not null && db.Database.IsRelational())
        {
            await db.LockRowsAsync(db.ComplianceDossiers.Where(x => x.ComplianceDossierId == dossier.ComplianceDossierId), x => x.ComplianceDossierId, token);
            await db.Entry(dossier).ReloadAsync(token);
        }
        return plan;
    }

    private async Task LockSegmentsAsync(Guid routePlanId, CancellationToken token)
    {
        // Acquire segment locks in the same order for primary and backup routes to avoid deadlocks.
        var ids = await db.RoutePlanRoutes.Where(x => x.RoutePlanId == routePlanId)
            .OrderBy(x => x.RoutePlanRouteId).Select(x => x.RoutePlanRouteId).ToArrayAsync(token);
        foreach (var id in ids)
            await db.LockRowsAsync(db.RoutePlanRoutes.Where(x => x.RoutePlanRouteId == id), x => x.RoutePlanRouteId, token);
    }

    private IQueryable<RoutePlanRoute> SegmentQuery() => db.RoutePlanRoutes.AsNoTracking()
        .Include(x => x.Route).ThenInclude(x => x.FromLocation).Include(x => x.Route).ThenInclude(x => x.ToLocation)
        .Include(x => x.DriverEmployee).Include(x => x.EscortEmployee).Include(x => x.TransportProvider)
        .Include(x => x.RoutePlanRouteVehicles).ThenInclude(x => x.Vehicle);

    private async Task<List<Response.Segment>> SegmentsAsync(Guid planId, CancellationToken token)
    {
        var rows = await SegmentQuery().Where(x => x.RoutePlanId == planId && x.SupersededAt == null).OrderBy(x => x.SequenceNo).ToListAsync(token);
        return rows.Select((x, i) => Segment(x, i + 1 < rows.Count ? rows[i + 1].PlannedDepartureAt : null)).ToList();
    }
}
