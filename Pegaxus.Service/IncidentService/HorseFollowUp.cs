using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Enum;
using Pegaxus.Repository.Extensions;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;

using static Pegaxus.Service.Common.DbTime;
using static Pegaxus.Service.Common.Text;

namespace Pegaxus.Service.IncidentService;

public sealed partial class Service
{
    private static readonly string[] ImmutableMedicalFields = ["bodyTemperatureCelsius", "heartRateBpm", "respiratoryRateBpm", "symptoms",
        "conditionDescription", "incidentType", "firstAidProvided", "veterinarianRequired", "horseId", "isolationRequired"];

    public async Task<Response.Incident> FollowUpHorseAsync(Guid incidentReportId, Guid horseHealthIncidentDetailId,
        Request.FollowUpHorseRequest request, Guid accountId, CancellationToken cancellationToken = default)
    {
        if (request.ExtraFields?.Keys.Any(field => ImmutableMedicalFields.Contains(field, StringComparer.OrdinalIgnoreCase)) == true)
            throw ImmutableMedicalRecord();
        Validation.ValidateFields(request, followUpValidator);
        if (horseHealthIncidentDetailId == Guid.Empty) throw new BadRequestException("Horse detail ID must be a non-empty UUID.", "VALIDATION_ERROR");
        for (var attempt = 0; ; attempt++)
        {
            var committed = false;
            try
            {
                await using var transaction = await db.Database.BeginSerializableAsync(cancellationToken);
                var actor = await ActorAsync(accountId, cancellationToken);
                var (incident, transport) = await LockIncidentAsync(incidentReportId, cancellationToken);
                if (actor.Role != EmployeeRole.LOGISTICS_MANAGER && !await CoordinatorAssignedAsync(transport.TransportId, actor, cancellationToken) &&
                    !(Crew(actor) && (actor.Role == EmployeeRole.ESCORT || incident.EmployeeId == actor.EmployeeId) &&
                        await CrewAssignedAsync(transport.TransportId, actor, cancellationToken)))
                    throw new ForbiddenException("Only the assigned escort, reporting driver, assigned coordinator or logistics manager can add follow-up", "FORBIDDEN");
                EnsureOpenHorseIncident(incident);
                if (Truncate(request.LastUpdatedAt!.Value) != Truncate(incident.UpdatedAt))
                    throw new ConflictException("The incident was just modified. Please reload the page", "INCIDENT_MODIFIED");
                var details = db.HorseHealthIncidentDetails.Where(item => item.IncidentReportId == incidentReportId &&
                    item.HorseHealthIncidentDetailId == horseHealthIncidentDetailId);
                await db.LockRowsAsync(details, item => item.HorseHealthIncidentDetailId, cancellationToken);
                var detail = await details.SingleOrDefaultAsync(cancellationToken)
                    ?? throw new NotFoundException("Horse incident detail not found", "HORSE_DETAIL_NOT_FOUND");
                await db.Entry(detail).ReloadAsync(cancellationToken);
                if (request.VeterinarianContactedAt.HasValue && detail.VeterinarianContactedAt.HasValue) throw ImmutableMedicalRecord();
                var now = NextUpdate(incident);
                var prefix = $"\n[{AsUtc(now).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)} · {actor.FullName}] ";
                if (EmptyToNull(request.OutcomeNote) is { } outcome) detail.OutcomeNote += prefix + outcome;
                if (EmptyToNull(request.MedicationGiven) is { } medication) detail.MedicationGiven += prefix + medication;
                if (EmptyToNull(request.VeterinarianInstruction) is { } instruction) detail.VeterinarianInstruction += prefix + instruction;
                if (request.VeterinarianContactedAt is { } contactedAt) detail.VeterinarianContactedAt = Truncate(contactedAt);
                if (request.HasHorseTransportable) detail.HorseTransportable = request.HorseTransportable;
                detail.UpdatedAt = incident.UpdatedAt = now;
                await QueueHorseFollowUpAsync(incident, now, cancellationToken);
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

    private static UnprocessableEntityException ImmutableMedicalRecord() => new(
        "The original medical record cannot be changed. Only follow-up information can be added", "IMMUTABLE_MEDICAL_RECORD");
}
