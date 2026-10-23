using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Repository.Extensions;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;

using static Pegaxus.Service.Common.DbTime;
using static Pegaxus.Service.Common.Text;

namespace Pegaxus.Service.IncidentService;

public sealed partial class Service
{
    public async Task<Response.CreateResult> ReportVehicleAsync(Request.ReportVehicleRequest request, Guid accountId, CancellationToken cancellationToken = default)
    {
        if (request.PhotoFiles?.Count > 5) throw new BadRequestException("At most 5 photos are accepted per request.", "TOO_MANY_IMAGES");
        Validation.ValidateFields(request, reportValidator);
        var occurredAt = Truncate(request.OccurredAt ?? clock.GetUtcNow());
        for (var attempt = 0; ; attempt++)
        {
            var committed = false;
            try
            {
                await using var transaction = await db.Database.BeginSerializableAsync(cancellationToken);
                await db.LockRowsAsync(db.Transports.Where(item => item.TransportId == request.TransportId), item => item.TransportId, cancellationToken);
                var actor = await ActorAsync(accountId, cancellationToken);
                if (!Crew(actor)) throw new ForbiddenException(ForbiddenMessage, "FORBIDDEN");
                var transport = await TransportAsync(request.TransportId, cancellationToken);
                if (!await CrewAssignedAsync(request.TransportId, actor, cancellationToken))
                    throw new ForbiddenException(ForbiddenMessage, "FORBIDDEN");
                var type = Enum.Parse<VehicleIncidentType>(request.IncidentType);
                // Offline retries keep the original occurredAt; mutable report text is not part of their identity.
                var existingId = await db.IncidentReports.AsNoTracking().Where(item => item.TransportId == request.TransportId &&
                        item.EmployeeId == actor.EmployeeId && item.OccurredAt == occurredAt && item.VehicleIncidentDetail != null &&
                        item.VehicleIncidentDetail.VehicleId == request.VehicleId && item.VehicleIncidentDetail.IncidentType == type)
                    .OrderBy(item => item.CreatedAt).ThenBy(item => item.IncidentReportId).Select(item => (Guid?)item.IncidentReportId).FirstOrDefaultAsync(cancellationToken);
                if (existingId.HasValue)
                {
                    var replay = await ReadIncidentAsync(existingId.Value, transport, actor, cancellationToken);
                    var safety = await SafetyAsync(transport, cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    return new(replay with { Effects = new(false, false, VehicleStatusChanged: false, CanAddHorseDetails: false), Safety = safety }, false);
                }
                if (transport.Status != TransportStatus.IN_TRANSIT)
                    throw new ConflictException("Incidents can only be reported while the transport is IN_TRANSIT", "INVALID_TRANSPORT_STATUS");
                if (!await db.RoutePlanRouteVehicles.AsNoTracking().AnyAsync(item => item.VehicleId == request.VehicleId &&
                        item.RoutePlanRoute.RoutePlan.TransportPlan.TransportId == transport.TransportId &&
                        AssignmentStatuses.Contains(item.RoutePlanRoute.RoutePlan.Status) &&
                        (item.RoutePlanRoute.SupersededAt == null || item.RoutePlanRoute.ActualDepartureAt != null), cancellationToken))
                    throw new UnprocessableEntityException("The vehicle is not assigned to this transport", "VEHICLE_NOT_IN_TRANSPORT");
                await db.LockRowsAsync(db.Vehicles.Where(item => item.VehicleId == request.VehicleId), item => item.VehicleId, cancellationToken);
                var vehicle = await db.Vehicles.SingleAsync(item => item.VehicleId == request.VehicleId, cancellationToken);
                await db.Entry(vehicle).ReloadAsync(cancellationToken);
                if (vehicle.IsDeleted)
                    throw new UnprocessableEntityException("The vehicle is not assigned to this transport", "VEHICLE_NOT_IN_TRANSPORT");
                var now = Truncate(clock.GetUtcNow());
                var location = await ResolveLocationAsync(request, now, cancellationToken);
                var incident = new IncidentReport
                {
                    IncidentReportId = Guid.NewGuid(), TransportId = transport.TransportId, EmployeeId = actor.EmployeeId,
                    IncidentCode = await CodeGeneration.NextAsync("INC", now, code => db.IncidentReports.AnyAsync(item => item.IncidentCode == code, cancellationToken)),
                    Location = location, Category = Enum.Parse<IncidentCategory>(request.Category),
                    Severity = request.Category == "BOTH" ? IncidentSeverity.CRITICAL : Enum.Parse<IncidentSeverity>(request.Severity),
                    Status = IncidentStatus.REPORTED, Title = request.Title.Trim(), Description = request.Description.Trim(),
                    ImmediateAction = EmptyToNull(request.ImmediateAction), EmergencySettlementRequired = request.EmergencySettlementRequired,
                    OccurredAt = occurredAt, ReportedAt = now, CreatedAt = now, UpdatedAt = now
                };
                var detail = new VehicleIncidentDetail
                {
                    VehicleIncidentDetailId = Guid.NewGuid(), IncidentReport = incident, Vehicle = vehicle, IncidentType = type,
                    VehicleOperable = request.VehicleOperable!.Value, CreatedAt = now, UpdatedAt = now
                };
                ApplyVehicleFields(detail, request);
                incident.VehicleIncidentDetail = detail;
                await AddPhotosAsync(incident, detail, request.PhotoFiles!, accountId, now, cancellationToken);
                var statusChanged = !detail.VehicleOperable && detail.ReplacementVehicleRequired && vehicle.Status != VehicleStatus.OUT_OF_SERVICE;
                if (statusChanged) { vehicle.Status = VehicleStatus.OUT_OF_SERVICE; vehicle.UpdatedAt = now; }
                db.IncidentReports.Add(incident);
                var effects = await QueueReportedAsync(incident, transport, statusChanged, now, cancellationToken);
                var safetyResponse = await SafetyAsync(transport, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                var response = await ReadIncidentAsync(incident.IncidentReportId, transport, actor, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                committed = true;
                await notifications.FlushAsync(cancellationToken);
                return new(response with { Effects = effects, Safety = safetyResponse }, true);
            }
            catch (Exception error) when (!committed && PostgresErrors.IsWriteConflict(error))
            {
                if (attempt < 19) { db.ChangeTracker.Clear(); continue; }
                throw new ConflictException("The incident could not be saved after concurrent changes. Please retry", "INCIDENT_MODIFIED");
            }
        }
    }

    private async Task<Location> ResolveLocationAsync(Request.ReportVehicleRequest request, DateTime now, CancellationToken ct)
    {
        if (request.LocationId.HasValue)
            return await db.Locations.SingleOrDefaultAsync(item => item.LocationId == request.LocationId && !item.IsDeleted, ct)
                ?? throw new NotFoundException("Location not found", "LOCATION_NOT_FOUND");
        var latitude = request.Latitude!.Value;
        var longitude = request.Longitude!.Value;
        var candidates = await db.Locations.AsNoTracking().Where(item => !item.IsDeleted && item.Latitude != null && item.Longitude != null &&
            item.Latitude >= latitude - 0.001m && item.Latitude <= latitude + 0.001m).ToArrayAsync(ct);
        var closest = candidates.Select(item => new { Location = item, Distance = Coordinates.DistanceMeters(latitude, longitude, item.Latitude!.Value, item.Longitude!.Value) })
            .Where(item => item.Distance <= 100).OrderBy(item => item.Distance).ThenBy(item => item.Location.LocationId).FirstOrDefault();
        if (closest is not null)
            return await db.Locations.SingleAsync(item => item.LocationId == closest.Location.LocationId, ct);
        var location = new Location
        {
            LocationId = Guid.NewGuid(), LocationName = $"Incident location {latitude:F4}, {longitude:F4}", LocationType = LocationType.OTHER,
            Latitude = latitude, Longitude = longitude, Address = EmptyToNull(request.LocationDescription),
            Country = EmptyToNull(request.Country) ?? "Unknown", CreatedAt = now, UpdatedAt = now
        };
        db.Locations.Add(location);
        return location;
    }

    private async Task<Response.Safety> SafetyAsync(Transport transport, CancellationToken ct) => new(
        "Báo cáo sự cố đã được gửi khẩn cấp về Trung tâm Điều hành. Quản lý Logistics và Điều phối viên đang xử lý.",
        await ContactsAsync(transport, ct));
}
