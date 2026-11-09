using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Repository.Extensions;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;

using static Pegaxus.Service.Common.DbTime;
using static Pegaxus.Service.Common.Text;

namespace Pegaxus.Service.IncidentService;

/// <summary>
/// A horse that cannot continue is left at a stop of the route, in the care of a local veterinary facility (their staff, not ours).
/// The rest of the booking travels on; the customer may then book the horse's return trip (Booking.ReturnOfTransportId).
/// </summary>
public sealed partial class Service
{
    private const int DetachNoteMaxLength = 1000;

    public async Task<Response.Incident> DetachHorseAsync(Guid incidentReportId, Guid horseId, Request.DetachHorseRequest request, Guid accountId,
        CancellationToken cancellationToken = default)
    {
        if (request.LastUpdatedAt is null) throw new BadRequestException("LastUpdatedAt is required.", "VALIDATION_ERROR");
        if (horseId == Guid.Empty) throw new BadRequestException("Horse ID must be a non-empty UUID.", "VALIDATION_ERROR");
        if (request.LocationId == Guid.Empty) throw new BadRequestException("LocationId must be a non-empty UUID.", "VALIDATION_ERROR");
        if (request.Note?.Length > DetachNoteMaxLength)
            throw new BadRequestException($"Note must be at most {DetachNoteMaxLength} characters.", "VALIDATION_ERROR");
        for (var attempt = 0; ; attempt++)
        {
            var committed = false;
            try
            {
                await using var transaction = await db.Database.BeginSerializableAsync(cancellationToken);
                var actor = await ActorAsync(accountId, cancellationToken);
                if (actor.Role != EmployeeRole.LOGISTICS_MANAGER)
                    throw new ForbiddenException("Only a logistics manager can leave a horse behind", "FORBIDDEN");
                var (incident, transport) = await LockIncidentAsync(incidentReportId, cancellationToken);
                EnsureOpenHorseIncident(incident);
                if (Truncate(request.LastUpdatedAt.Value) != Truncate(incident.UpdatedAt))
                    throw new ConflictException("The incident was just modified. Please reload the page", "INCIDENT_MODIFIED");
                if (transport.Status != TransportStatus.IN_TRANSIT)
                    throw new ConflictException("The transport is not in transit", "TRANSPORT_NOT_IN_TRANSIT");

                var details = db.HorseHealthIncidentDetails.Where(item => item.IncidentReportId == incidentReportId && item.HorseId == horseId);
                await db.LockRowsAsync(details, item => item.HorseHealthIncidentDetailId, cancellationToken);
                var detail = await details.SingleOrDefaultAsync(cancellationToken)
                    ?? throw new UnprocessableEntityException("The horse has no detail on this incident", "HORSE_NOT_IN_INCIDENT");
                await db.Entry(detail).ReloadAsync(cancellationToken);

                // Lock order: Transport, Incident, details (above), then the booking's horses.
                var links = db.BookingHorses.Where(item => item.BookingId == transport.BookingId);
                await db.LockRowsAsync(links, item => item.HorseId, cancellationToken);
                var horses = await links.Include(item => item.Horse).OrderBy(item => item.HorseId).ToListAsync(cancellationToken);
                foreach (var link in horses) await db.Entry(link).ReloadAsync(cancellationToken);
                var target = horses.SingleOrDefault(item => item.HorseId == horseId)
                    ?? throw new UnprocessableEntityException("The horse does not belong to this transport", "HORSE_NOT_IN_TRANSPORT");
                if (target.DetachedAt.HasValue)
                    throw new ConflictException("The horse has already left this transport", "HORSE_ALREADY_DETACHED");
                if (!horses.Any(item => item.HorseId != horseId && item.DetachedAt is null))
                    throw new UnprocessableEntityException("The last horse cannot be left behind. Cancel or reroute the transport instead",
                        "LAST_HORSE_CANNOT_DETACH");

                var segments = await EffectiveSegmentsAsync(transport.TransportId, cancellationToken);
                var location = DetachLocations(segments).SingleOrDefault(item => item.LocationId == request.LocationId)
                    ?? throw new UnprocessableEntityException("The horse can only be left at a stop of the current or last completed leg",
                        "INVALID_DETACH_LOCATION");

                var now = NextUpdate(incident);
                target.DetachedAt = now;
                target.DetachedIncidentReportId = incident.IncidentReportId;
                target.DetachedLocationId = location.LocationId;
                detail.HorseTransportable ??= false;
                detail.UpdatedAt = incident.UpdatedAt = now;

                var note = EmptyToNull(request.Note);
                var summary = $"Horse {target.Horse.HorseName} left in care at {location.LocationName} (incident {incident.IncidentCode})";
                var dossierId = await db.ComplianceDossiers.AsNoTracking()
                    .Where(item => item.TransportId == transport.TransportId && item.DossierType == DossierType.MAIN)
                    .Select(item => (Guid?)item.ComplianceDossierId).SingleOrDefaultAsync(cancellationToken);
                if (dossierId.HasValue)
                    db.AuditDossiers.Add(new AuditDossier
                    {
                        AuditDossierId = Guid.NewGuid(), ComplianceDossierId = dossierId.Value, ActorAccountId = accountId, ActionType = "HORSE_DETACHED",
                        TargetEntity = nameof(BookingHorse), TargetId = horseId, Summary = summary, Visibility = AuditVisibility.Public, CreatedAt = now,
                        Details = JsonSerializer.SerializeToDocument(new { horseId, location.LocationId, incident.IncidentReportId, note })
                    });

                await QueueHorseDetachedAsync(incident, transport, target.Horse, location, segments, horses.Count(item => item.DetachedAt is null), now,
                    cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                var response = await ReadIncidentAsync(incidentReportId, transport, actor, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                committed = true;
                await notifications.FlushAsync(cancellationToken);
                return response;
            }
            catch (Exception error) when (!committed && PostgresErrors.IsWriteConflict(error))
            {
                if (attempt < 2) { db.ChangeTracker.Clear(); continue; }
                throw new ConflictException("The incident was just modified. Please reload the page", "INCIDENT_MODIFIED");
            }
        }
    }

    private async Task<IReadOnlyList<TransportItinerary.Segment>> EffectiveSegmentsAsync(Guid transportId, CancellationToken ct)
    {
        var plans = await db.RoutePlans.AsNoTracking().AsSplitQuery()
            .Include(plan => plan.RoutePlanRoutes).ThenInclude(route => route.Route).ThenInclude(route => route.FromLocation)
            .Include(plan => plan.RoutePlanRoutes).ThenInclude(route => route.Route).ThenInclude(route => route.ToLocation)
            .Where(plan => plan.TransportPlan.TransportId == transportId && AssignmentStatuses.Contains(plan.Status)).ToArrayAsync(ct);
        return TransportItinerary.Effective(plans);
    }

    /// <summary>Both ends of the leg in transit; between legs, the end of the last completed leg.</summary>
    private static IReadOnlyList<Location> DetachLocations(IReadOnlyList<TransportItinerary.Segment> segments)
    {
        var moving = segments.FirstOrDefault(segment => segment.Status == RouteSegmentStatus.IN_TRANSIT);
        if (moving is not null) return new[] { moving.Route.Route.FromLocation, moving.Route.Route.ToLocation }.DistinctBy(item => item.LocationId).ToArray();
        var last = segments.LastOrDefault(segment => segment.Status == RouteSegmentStatus.COMPLETED);
        return last is null ? [] : [last.Route.Route.ToLocation];
    }

    /// <summary>The customer always hears about it (it is their horse), whatever the severity; so do the crew of every leg still to drive.</summary>
    private async Task QueueHorseDetachedAsync(IncidentReport incident, Transport transport, Horse horse, Location location,
        IReadOnlyList<TransportItinerary.Segment> segments, int remaining, DateTime now, CancellationToken ct)
    {
        var crewIds = segments.Where(segment => segment.Status != RouteSegmentStatus.COMPLETED)
            .SelectMany(segment => new[] { segment.Route.DriverEmployeeId, segment.Route.EscortEmployeeId })
            .OfType<Guid>().Distinct().ToArray();
        var crew = await db.Employees.AsNoTracking().Active().Where(employee => crewIds.Contains(employee.EmployeeId)).ToArrayAsync(ct);
        var message = $"{horse.HorseName} stays at {location.LocationName} in the care of a local veterinary facility. " +
                      $"Transport {transport.TransportCode} continues with {remaining} horse(s).";
        foreach (var employee in crew)
            notifications.Queue(new()
            {
                AccountId = employee.AccountId, TransportId = transport.TransportId, Type = NotificationType.HORSE_HEALTH, Priority = NotificationPriority.URGENT,
                Title = $"{horse.HorseName} leaves the transport", Message = message, ReferenceType = nameof(IncidentReport),
                ReferenceId = incident.IncidentReportId, ActionUrl = $"/crew/incidents/{incident.IncidentReportId}", CreatedAt = now
            });
        foreach (var employee in await InternalRecipientsAsync(transport.TransportId, ct))
            QueueHorseNotification(incident, employee.AccountId, NotificationPriority.HIGH, $"{horse.HorseName} leaves the transport", message, now);

        var customerAccountId = await db.Customers.AsNoTracking().Where(customer => customer.CustomerId == transport.Booking.CustomerId &&
                !customer.IsDeleted && !customer.Account.IsDeleted && customer.Account.Status == AccountStatus.ACTIVE)
            .Select(customer => (Guid?)customer.AccountId).SingleOrDefaultAsync(ct);
        if (customerAccountId.HasValue)
            notifications.Queue(new()
            {
                AccountId = customerAccountId.Value, TransportId = transport.TransportId, Type = NotificationType.HORSE_HEALTH, Priority = NotificationPriority.URGENT,
                Title = $"{horse.HorseName} could not continue the journey",
                Message = $"{horse.HorseName} stays at {location.LocationName} in the care of a local veterinary facility. " +
                          "Create a return booking to bring the horse back.",
                ReferenceType = nameof(BookingHorse), ReferenceId = horse.HorseId,
                ActionUrl = $"/bookings/return?returnOfTransportId={transport.TransportId}&horseId={horse.HorseId}", CreatedAt = now
            });
    }
}
