using System.Data;
using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Repository.Extensions;
using Pegaxus.Service.Exceptions;

using Pegaxus.Service.Common;

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
            .Include(x => x.TransportPlan).ThenInclude(x => x.Transport).ThenInclude(x => x.ComplianceDossiers)
            .SingleAsync(x => x.RoutePlanId == routePlanId, token);
        await db.Entry(plan).ReloadAsync(token);
        await db.Entry(plan.TransportPlan).ReloadAsync(token);
        await db.Entry(plan.TransportPlan.Transport).ReloadAsync(token);
        var dossier = plan.TransportPlan.Transport.MainDossier;
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

    private async Task<List<Response.Segment>> EffectiveSegmentsAsync(RoutePlan plan, CancellationToken token)
    {
        var plans = await db.RoutePlans.AsNoTracking().Include(x => x.RoutePlanRoutes)
            .Where(x => x.TransportPlanId == plan.TransportPlanId).ToListAsync(token);
        var ids = TransportItinerary.Effective(plans).Select(x => x.Route.RoutePlanRouteId).ToArray();
        var rows = await SegmentQuery().Where(x => ids.Contains(x.RoutePlanRouteId)).ToListAsync(token);
        var ordered = ids.Select(id => rows.Single(x => x.RoutePlanRouteId == id)).ToList();
        return ordered.Select((x, i) => Segment(x, i + 1 < ordered.Count ? ordered[i + 1].PlannedDepartureAt : null)).ToList();
    }

    private async Task<(ComplianceDossier Dossier, int InheritedRequirementCount)> CreateExtraDossierAsync(
        RoutePlan plan, ComplianceDossier main, Guid accountId, string? note, DateTime now, CancellationToken token)
    {
        var routeNote = await db.RoutePlans.AsNoTracking().Where(x => x.RoutePlanId == plan.RoutePlanId).Select(x => x.RouteNote).SingleAsync(token);
        var extra = new ComplianceDossier
        {
            ComplianceDossierId = Guid.NewGuid(), DossierCode = $"DOS-{now:yyyyMMdd}-{Guid.NewGuid():N}",
            TransportId = main.TransportId, DossierType = DossierType.EXTRA, DossierTemplateId = main.DossierTemplateId,
            Status = ComplianceDossierStatus.DRAFT, OpenedAt = now, Note = routeNote, CreatedAt = now, UpdatedAt = now
        };
        var inherited = await db.DossierRequirements.AsNoTracking()
            .Where(x => x.ComplianceDossierId == main.ComplianceDossierId && !x.IsDeleted).ToListAsync(token);
        foreach (var item in inherited)
            extra.DossierRequirements.Add(new DossierRequirement
            {
                DossierRequirementId = Guid.NewGuid(), ComplianceRequirementId = item.ComplianceRequirementId, HorseId = item.HorseId,
                IsMandatory = item.IsMandatory, Instruction = item.Instruction, ValidUntil = item.ValidUntil,
                Status = DossierRequirementStatus.PENDING, CreatedAt = now, UpdatedAt = now
            });
        db.ComplianceDossiers.Add(extra);
        db.AuditDossiers.Add(new() { AuditDossierId = Guid.NewGuid(), ComplianceDossierId = extra.ComplianceDossierId,
            ActorAccountId = accountId, ActionType = "EXTRA_DOSSIER_CREATED", TargetEntity = "RoutePlan", TargetId = plan.RoutePlanId,
            Summary = note ?? "Return route approved; an extra compliance dossier was created.",
            Details = JsonSerializer.SerializeToDocument(new { dossierType = "EXTRA", inheritedRequirementCount = inherited.Count }),
            Visibility = AuditVisibility.Internal, CreatedAt = now });
        return (extra, inherited.Count);
    }
}
