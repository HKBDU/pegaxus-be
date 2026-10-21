using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Repository.Extensions;
using Pegaxus.Service.Exceptions;

using static Pegaxus.Service.Common.DbTime;

namespace Pegaxus.Service.IncidentService;

public sealed partial class Service
{
    private const string HoldReference = "IncidentHold";
    private const string HoldExpiredReference = "IncidentHoldExpired";

    private IQueryable<Notification> HoldRows() => db.Notifications.AsNoTracking().Where(notification =>
        notification.NotificationType == NotificationType.ROUTE && notification.ReferenceType == HoldReference && notification.ExpiresAt != null);

    private Task<Notification?> ActiveTransportHoldAsync(Guid transportId, CancellationToken ct)
    {
        var now = Truncate(clock.GetUtcNow());
        return HoldRows().Where(hold => hold.TransportId == transportId && hold.ExpiresAt > now)
            .OrderByDescending(hold => hold.CreatedAt).ThenBy(hold => hold.NotificationId).FirstOrDefaultAsync(ct);
    }

    private async Task<Response.Hold?> ReadHoldAsync(Guid incidentReportId, CancellationToken ct)
    {
        var hold = await HoldRows().Where(hold => hold.ReferenceId == incidentReportId)
            .OrderByDescending(hold => hold.CreatedAt).ThenBy(hold => hold.NotificationId).FirstOrDefaultAsync(ct);
        return hold is null ? null : HoldFrom(hold);
    }

    private Response.Hold HoldFrom(Notification hold)
    {
        var value = JsonSerializer.Deserialize<Response.Hold>(hold.Message) ?? throw new InvalidOperationException("Invalid persisted incident hold");
        return value with { Status = hold.ExpiresAt > Truncate(clock.GetUtcNow()) ? "ACTIVE" : "EXPIRED",
            StartedAt = AsUtc(hold.CreatedAt), HoldUntil = AsUtc(hold.ExpiresAt!.Value) };
    }

    public async Task<int> QueueExpiredHoldsAsync(CancellationToken cancellationToken = default)
    {
        var ct = cancellationToken;
        var now = Truncate(clock.GetUtcNow());
        var holds = HoldRows();
        var due = await holds.Where(hold => hold.ExpiresAt <= now &&
                db.IncidentReports.Any(incident => incident.IncidentReportId == hold.ReferenceId &&
                    incident.TransportId == hold.TransportId && (incident.Status == IncidentStatus.REPORTED || incident.Status == IncidentStatus.PENDING)) &&
                db.Transports.Any(transport => transport.TransportId == hold.TransportId && transport.Status == TransportStatus.IN_TRANSIT) &&
                !db.Notifications.Any(marker => marker.ReferenceType == HoldExpiredReference && marker.ReferenceId == hold.NotificationId) &&
                !holds.Any(later => later.TransportId == hold.TransportId && later.CreatedAt > hold.CreatedAt))
            .OrderBy(hold => hold.ExpiresAt).ThenBy(hold => hold.NotificationId)
            .Select(hold => new { hold.NotificationId, IncidentReportId = hold.ReferenceId!.Value }).Take(100).ToArrayAsync(ct);
        var queued = 0;
        foreach (var hold in due)
            if (await QueueHoldExpiryAsync(hold.NotificationId, hold.IncidentReportId, ct)) queued++;
        return queued;
    }

    private async Task<bool> QueueHoldExpiryAsync(Guid notificationId, Guid incidentReportId, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            var committed = false;
            try
            {
                await using var transaction = await db.Database.BeginSerializableAsync(ct);
                var (incident, transport) = await LockIncidentAsync(incidentReportId, ct);
                var now = Truncate(clock.GetUtcNow());
                var hold = await HoldRows().SingleOrDefaultAsync(item => item.NotificationId == notificationId, ct);
                if (hold is null || hold.ExpiresAt > now || transport.Status != TransportStatus.IN_TRANSIT ||
                    incident.Status is not (IncidentStatus.REPORTED or IncidentStatus.PENDING) ||
                    await db.Notifications.AnyAsync(marker => marker.ReferenceType == HoldExpiredReference && marker.ReferenceId == notificationId, ct) ||
                    await HoldRows().AnyAsync(later => later.TransportId == transport.TransportId && later.CreatedAt > hold.CreatedAt, ct))
                    return false;
                foreach (var employee in await InternalRecipientsAsync(transport.TransportId, ct))
                    QueueReroute(incident, employee, NotificationPriority.URGENT, "Hold time is over, decide the next route",
                        $"Hold for incident {incident.IncidentCode} expired at {AsUtc(hold.ExpiresAt):O}. Decide whether to use a backup or return route.", true, now);
                // Commit the deduplication marker with the alerts and outbox messages.
                db.Notifications.Add(new Notification
                {
                    NotificationId = Guid.NewGuid(), TransportId = transport.TransportId, NotificationType = NotificationType.ROUTE,
                    Priority = NotificationPriority.URGENT, Title = "Hold expiry processed", Message = "HOLD_EXPIRED",
                    ReferenceType = HoldExpiredReference, ReferenceId = notificationId, CreatedAt = now
                });
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                committed = true;
                await notifications.FlushAsync(ct);
                return true;
            }
            catch (Exception error) when (!committed && PostgresErrors.IsWriteConflict(error))
            {
                db.ChangeTracker.Clear();
                if (attempt < 2) continue;
                throw new ConflictException("The incident was just modified. Please retry the hold expiry", "INCIDENT_MODIFIED");
            }
        }
    }
}
