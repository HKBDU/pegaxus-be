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
    public async Task<Response.Incident> UpdateVehicleAsync(Guid incidentReportId, Request.UpdateVehicleRequest request, Guid accountId, CancellationToken cancellationToken = default)
    {
        if (request.PhotoFiles?.Count > 5) throw new BadRequestException("At most 5 photos are accepted per request.", "TOO_MANY_IMAGES");
        Validation.ValidateFields(request, updateValidator);
        if (incidentReportId == Guid.Empty) throw new BadRequestException("Incident ID must be a non-empty UUID.", "VALIDATION_ERROR");
        for (var attempt = 0; ; attempt++)
        {
            var committed = false;
            try
            {
                await using var transaction = await db.Database.BeginSerializableAsync(cancellationToken);
                var actor = await ActorAsync(accountId, cancellationToken);
                var transportId = await db.IncidentReports.AsNoTracking().Where(item => item.IncidentReportId == incidentReportId)
                    .Select(item => item.TransportId).SingleOrDefaultAsync(cancellationToken)
                    ?? throw new NotFoundException("Incident not found", "INCIDENT_NOT_FOUND");
                // Keep the same Transport -> Incident -> Detail order as other incident/lifecycle writes.
                await db.LockRowsAsync(db.Transports.Where(item => item.TransportId == transportId), item => item.TransportId, cancellationToken);
                await db.LockRowsAsync(db.IncidentReports.Where(item => item.IncidentReportId == incidentReportId), item => item.IncidentReportId, cancellationToken);
                var incident = await db.IncidentReports.SingleAsync(item => item.IncidentReportId == incidentReportId, cancellationToken);
                await db.Entry(incident).ReloadAsync(cancellationToken);
                if (incident.TransportId != transportId) throw new ConflictException("The incident was just modified. Please reload the page", "INCIDENT_MODIFIED");
                var transport = await TransportAsync(transportId, cancellationToken);
                if (actor.Role != EmployeeRole.LOGISTICS_MANAGER &&
                    !(Crew(actor) && incident.EmployeeId == actor.EmployeeId && await CrewAssignedAsync(transportId, actor, cancellationToken)) &&
                    !await CoordinatorAssignedAsync(transportId, actor, cancellationToken))
                    throw new ForbiddenException("Only the reporter, assigned coordinator or logistics manager can update this incident", "FORBIDDEN");
                if (incident.Status is not (IncidentStatus.REPORTED or IncidentStatus.PENDING))
                    throw new UnprocessableEntityException("The incident is already resolved or cancelled", "INVALID_INCIDENT_STATUS");
                if (incident.Category is not (IncidentCategory.VEHICLE or IncidentCategory.BOTH))
                    throw new UnprocessableEntityException("This incident does not accept this kind of detail", "INCIDENT_CATEGORY_MISMATCH");
                if (Truncate(request.LastUpdatedAt!.Value) != Truncate(incident.UpdatedAt))
                    throw new ConflictException("The incident was just modified. Please reload the page", "INCIDENT_MODIFIED");
                await db.LockRowsAsync(db.VehicleIncidentDetails.Where(item => item.IncidentReportId == incidentReportId), item => item.VehicleIncidentDetailId, cancellationToken);
                var detail = await db.VehicleIncidentDetails.SingleOrDefaultAsync(item => item.IncidentReportId == incidentReportId, cancellationToken)
                    ?? throw new UnprocessableEntityException("This incident does not accept this kind of detail", "INCIDENT_CATEGORY_MISMATCH");
                await db.Entry(detail).ReloadAsync(cancellationToken);
                var now = Truncate(clock.GetUtcNow());
                if (now <= incident.UpdatedAt) now = incident.UpdatedAt.AddTicks(10);
                await AddPhotosAsync(incident, detail, request.PhotoFiles!, accountId, now, cancellationToken);
                ApplyVehicleFields(detail, request);
                if (request.RepairNote is not null) detail.RepairNote = EmptyToNull(request.RepairNote);
                incident.UpdatedAt = detail.UpdatedAt = now;
                await QueueUpdatedAsync(incident, now, cancellationToken);
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

    private static void ApplyVehicleFields(VehicleIncidentDetail detail, Request.VehicleFields request)
    {
        if (request.VehicleOperable.HasValue) detail.VehicleOperable = request.VehicleOperable.Value;
        if (request.RepairRequired.HasValue) detail.RepairRequired = request.RepairRequired.Value;
        if (request.TowingRequired.HasValue) detail.TowingRequired = request.TowingRequired.Value;
        if (request.ReplacementVehicleRequired.HasValue) detail.ReplacementVehicleRequired = request.ReplacementVehicleRequired.Value;
        if (request.VehicleCondition is not null) detail.VehicleCondition = EmptyToNull(request.VehicleCondition);
        if (request.DamageDescription is not null) detail.DamageDescription = EmptyToNull(request.DamageDescription);
        if (request.EstimatedRepairCost.HasValue) detail.EstimatedRepairCost = request.EstimatedRepairCost.Value;
        if (request.EstimatedRepairHours.HasValue) detail.EstimatedRepairHours = request.EstimatedRepairHours.Value;
        if (request.DriverSafetyStatus is not null) detail.DriverSafetyStatus = EmptyToNull(request.DriverSafetyStatus);
        if (request.CargoConditionNote is not null) detail.CargoConditionNote = EmptyToNull(request.CargoConditionNote);
    }
}
