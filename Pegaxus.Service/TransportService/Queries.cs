using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Enum;

using static Pegaxus.Service.Common.DbTime;

namespace Pegaxus.Service.TransportService;

public sealed partial class Service
{
    public async Task<Response.OverviewResponse> GetOverviewAsync(Guid accountId, Guid transportId, CancellationToken cancellationToken = default)
    {
        var actor = await GetActorAsync(accountId, cancellationToken);
        var transport = await GetVisibleTransportAsync(actor, transportId, cancellationToken);
        var segments = EffectiveSegments(await ReadSegmentsAsync([transportId], actor, cancellationToken));
        var current = CurrentSegment(segments, transport.Status);
        var tracking = ToTrackingSegments(segments, transport.Status);
        var health = await db.BookingHorses.AsNoTracking().Where(item => item.BookingId == transport.BookingId)
            .Select(item => db.HorseHealthLogs.Where(log => log.TransportId == transportId && log.HorseId == item.HorseId)
                .OrderByDescending(log => log.CheckedAt).ThenByDescending(log => log.CreatedAt).ThenByDescending(log => log.HorseHealthLogId)
                .Select(log => (HorseHealthStatus?)log.HealthStatus).FirstOrDefault()).ToListAsync(cancellationToken);
        var incidents = await db.IncidentReports.AsNoTracking().Where(item => item.TransportId == transportId &&
                (item.Status == IncidentStatus.REPORTED || item.Status == IncidentStatus.PENDING))
            .OrderByDescending(item => item.OccurredAt).ThenBy(item => item.IncidentReportId)
            .Select(item => new Response.ActiveIncident(item.IncidentReportId, item.IncidentCode, item.Category.ToString(),
                item.Status.ToString(), item.Title, AsUtc(item.OccurredAt), actor.IsCustomer ? null : item.Severity.ToString(),
                actor.IsCustomer ? null : item.Description)).ToListAsync(cancellationToken);
        // shortcut: dossierType stays null until B-07 adds MAIN/EXTRA and removes transport uniqueness.
        var dossiers = await db.ComplianceDossiers.AsNoTracking().Where(item => item.TransportId == transportId)
            .OrderBy(item => item.OpenedAt).ThenBy(item => item.ComplianceDossierId)
            .Select(item => new Response.DossierBrief(item.ComplianceDossierId, item.DossierCode, null, item.Status.ToString()))
            .ToListAsync(cancellationToken);
        return new(clock.GetUtcNow(),
            new(transport.TransportId, transport.TransportCode, transport.Status.ToString(), AsUtc(transport.ActualDepartureAt),
                AsUtc(transport.ActualArrivalAt), AsUtc(transport.CompletedAt)),
            new(transport.BookingId, transport.BookingCode, transport.CustomerName, transport.OriginAddress, transport.OriginCountry,
                transport.DestinationAddress, transport.DestinationCountry),
            current?.Segment.RouteType,
            current is null ? null : new(current.Segment.RoutePlanId, current.Segment.RoutePlanCode, current.PlanStatus.ToString()),
            tracking.FirstOrDefault(item => item.IsCurrent), segments.Count, segments.Count(item => item.Segment.Status == "COMPLETED"),
            AsUtc(transport.DepartureAt), segments.LastOrDefault()?.Segment.PlannedArrivalAt, transport.HorseCount,
            new(health.Count, health.Count(item => item == HorseHealthStatus.NORMAL), health.Count(item => item == HorseHealthStatus.OBSERVATION_REQUIRED),
                health.Count(item => item == HorseHealthStatus.AT_RISK), health.Count(item => item == HorseHealthStatus.CRITICAL), health.Count(item => item == null)),
            dossiers, incidents, !actor.IsCrew);
    }

    public async Task<Response.SegmentsResponse> GetSegmentsAsync(Guid accountId, Guid transportId, CancellationToken cancellationToken = default)
    {
        var actor = await GetActorAsync(accountId, cancellationToken);
        var transport = await GetVisibleTransportAsync(actor, transportId, cancellationToken);
        var segments = EffectiveSegments(await ReadSegmentsAsync([transportId], actor, cancellationToken));
        var boundaries = await ReadBoundariesAsync(transport, segments, actor, cancellationToken);
        return new(clock.GetUtcNow(), transportId, transport.Status.ToString(), ToTrackingSegments(segments, transport.Status), boundaries);
    }
}
