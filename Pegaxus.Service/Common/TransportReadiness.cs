using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Repository.Extensions;

namespace Pegaxus.Service.Common;

/// <summary>
/// A transport is READY for pickup only when all of these hold:
/// the MAIN compliance dossier is APPROVED; the primary route plan and at least one backup are confirmed, with no primary or
/// backup left in draft; and every internal (not outsourced) primary segment has a driver and an escort.
/// Backup crews are not checked: they take over the primary crew when the backup is activated on the way.
/// </summary>
public static class TransportReadiness
{
    private static readonly RoutePlanStatus[] Decided = [RoutePlanStatus.CONFIRMED, RoutePlanStatus.ACTIVE, RoutePlanStatus.COMPLETED];

    public static bool IsReady(Transport transport)
    {
        if (transport.MainDossier?.Status != ComplianceDossierStatus.APPROVED) return false;
        var plans = transport.TransportPlans.Where(plan => plan.Status != TransportPlanStatus.CANCELLED)
            .SelectMany(plan => plan.RoutePlans)
            .Where(plan => plan.Status is not (RoutePlanStatus.CANCELLED or RoutePlanStatus.REJECTED_BY_MANAGER))
            .ToList();
        var primaries = plans.Where(TransportItinerary.IsPrimaryPlan).ToList();
        var backups = plans.Where(TransportItinerary.IsBackupPlan).ToList();
        return primaries.Count == 1 && backups.Count > 0 &&
            primaries.Concat(backups).All(plan => Decided.Contains(plan.Status)) &&
            primaries[0].RoutePlanRoutes.All(segment => segment.TransportProviderId.HasValue ||
                segment.DriverEmployeeId.HasValue && segment.EscortEmployeeId.HasValue);
    }

    /// <summary>
    /// Moves a transport that has not been picked up between PLANNING and READY to match <see cref="IsReady"/>.
    /// Call it inside the write transaction, after the change and before SaveChanges: the loaded entities are the tracked ones,
    /// so unsaved changes of the caller count.
    /// </summary>
    public static async Task SyncAsync(AppDbContext db, Guid transportId, DateTime now, CancellationToken cancellationToken)
    {
        // Two requests may each supply the last condition (say the dossier approval and the crew): the lock makes the later
        // one wait and then read what the earlier one committed, so neither leaves the transport in PLANNING.
        await db.LockRowsAsync(db.Transports.Where(item => item.TransportId == transportId), item => item.TransportId, cancellationToken);
        var transport = await db.Transports
            .Include(item => item.ComplianceDossiers)
            .Include(item => item.TransportPlans).ThenInclude(plan => plan.RoutePlans).ThenInclude(plan => plan.RoutePlanRoutes)
            .SingleAsync(item => item.TransportId == transportId, cancellationToken);
        if (transport.Status is not (TransportStatus.CREATED or TransportStatus.PLANNING or TransportStatus.READY)) return;
        var status = IsReady(transport) ? TransportStatus.READY
            : transport.Status == TransportStatus.READY ? TransportStatus.PLANNING
            : transport.Status;
        if (status == transport.Status) return;
        transport.Status = status;
        transport.UpdatedAt = now;
    }
}
