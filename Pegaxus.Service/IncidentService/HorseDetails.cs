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
    public async Task<Response.Incident> AddHorseDetailsAsync(Guid incidentReportId, Request.AddHorseDetailsRequest request, Guid accountId, CancellationToken cancellationToken = default)
    {
        CheckPhotoCount(request);
        Validation.ValidateFields(request, detailsValidator);
        CheckVitals(request.Horses);
        for (var attempt = 0; ; attempt++)
        {
            var committed = false;
            try
            {
                await using var transaction = await db.Database.BeginSerializableAsync(cancellationToken);
                var actor = await ActorAsync(accountId, cancellationToken);
                if (actor.Role != EmployeeRole.ESCORT) throw new ForbiddenException("Only the assigned escort can add horse details", "FORBIDDEN");
                var (incident, transport) = await LockIncidentAsync(incidentReportId, cancellationToken);
                if (!await CrewAssignedAsync(transport.TransportId, actor, cancellationToken))
                    throw new ForbiddenException("Only the assigned escort can add horse details", "FORBIDDEN");
                EnsureOpenHorseIncident(incident);
                await BookingHorsesAsync(transport, request.Horses, cancellationToken);
                var ids = request.Horses.Select(horse => horse.HorseId).ToArray();
                var duplicate = await db.HorseHealthIncidentDetails.AsNoTracking().Where(detail => detail.IncidentReportId == incidentReportId && ids.Contains(detail.HorseId))
                    .OrderBy(detail => detail.HorseId).Select(detail => (Guid?)detail.HorseId).FirstOrDefaultAsync(cancellationToken);
                if (duplicate.HasValue)
                    throw new ConflictException("The horse already has a detail on this incident. Use follow-up to update it",
                        "HORSE_DETAIL_ALREADY_EXISTS", new { horseId = duplicate.Value });
                var now = NextUpdate(incident);
                var photos = await AddPhotosAsync(incident, request.PhotoFiles, accountId, now, cancellationToken);
                AddMedicalRecords(incident, transport, request.Horses, actor, photos, now, now);
                incident.UpdatedAt = now;
                var emailQueued = await QueueHorseAlertAsync(incident, transport, false, now, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                var response = await ReadIncidentAsync(incidentReportId, transport, actor, cancellationToken, true);
                await transaction.CommitAsync(cancellationToken);
                committed = true;
                await notifications.FlushAsync(cancellationToken);
                return response with { Effects = new(request.Horses.Count, "URGENT", emailQueued) };
            }
            catch (Exception error) when (!committed && PostgresErrors.IsWriteConflict(error))
            {
                if (attempt < 2) { db.ChangeTracker.Clear(); continue; }
                throw new ConflictException("The incident was just modified. Please reload the page", "INCIDENT_MODIFIED");
            }
        }
    }

    private async Task<Dictionary<Guid, Horse>> BookingHorsesAsync(Transport transport, IReadOnlyList<Request.HorseDetail> horses, CancellationToken ct)
    {
        var ids = horses.Select(horse => horse.HorseId).ToArray();
        var found = await db.BookingHorses.AsNoTracking().Where(item => item.BookingId == transport.BookingId && ids.Contains(item.HorseId))
            .Select(item => item.Horse).ToDictionaryAsync(horse => horse.HorseId, ct);
        if (found.Count != ids.Length)
            throw new UnprocessableEntityException("The horse does not belong to this transport", "HORSE_NOT_IN_TRANSPORT");
        return found;
    }

    private void AddMedicalRecords(IncidentReport incident, Transport transport, IReadOnlyList<Request.HorseDetail> horses,
        Employee actor, string[] photos, DateTime checkedAt, DateTime now)
    {
        foreach (var horse in horses)
        {
            db.HorseHealthIncidentDetails.Add(new()
            {
                HorseHealthIncidentDetailId = Guid.NewGuid(), IncidentReport = incident, HorseId = horse.HorseId,
                IncidentType = Enum.Parse<HorseIncidentType>(horse.IncidentType), ConditionDescription = horse.ConditionDescription.Trim(),
                Symptoms = EmptyToNull(horse.Symptoms), BodyTemperatureCelsius = horse.BodyTemperatureCelsius,
                HeartRateBpm = horse.HeartRateBpm, RespiratoryRateBpm = horse.RespiratoryRateBpm, FirstAidProvided = EmptyToNull(horse.FirstAidProvided),
                VeterinarianRequired = horse.VeterinarianRequired, IsolationRequired = horse.IsolationRequired,
                VeterinarianContactedAt = horse.VeterinarianContactedAt is { } contactedAt ? Truncate(contactedAt) : null,
                VeterinarianInstruction = EmptyToNull(horse.VeterinarianInstruction), MedicationGiven = EmptyToNull(horse.MedicationGiven),
                HorseTransportable = horse.HorseTransportable, CreatedAt = now, UpdatedAt = now
            });
            db.HorseHealthLogs.Add(new()
            {
                HorseHealthLogId = Guid.NewGuid(), TransportId = transport.TransportId, HorseId = horse.HorseId, EmployeeId = actor.EmployeeId,
                LogType = HorseHealthLogType.INCIDENT_FOLLOWUP, CheckedAt = checkedAt,
                HealthStatus = incident.Severity == IncidentSeverity.CRITICAL ? HorseHealthStatus.CRITICAL : HorseHealthStatus.AT_RISK,
                TemperatureCelsius = horse.BodyTemperatureCelsius, HeartRateBpm = horse.HeartRateBpm, RespiratoryRateBpm = horse.RespiratoryRateBpm,
                SymptomNote = EmptyToNull(horse.Symptoms), TreatmentProvided = EmptyToNull(horse.FirstAidProvided),
                MedicationGiven = EmptyToNull(horse.MedicationGiven), VeterinarianRequired = horse.VeterinarianRequired,
                VeterinarianNote = EmptyToNull(horse.VeterinarianInstruction), LocationDescription = incident.Location?.Address,
                GeneralNote = $"Incident {incident.IncidentCode}\n{horse.ConditionDescription.Trim()}", PhotoUrls = photos,
                CreatedAt = now, UpdatedAt = now
            });
        }
    }

    private static void CheckPhotoCount(Request.AddHorseDetailsRequest request)
    {
        if (request.PhotoFiles?.Count > 5) throw new BadRequestException("At most 5 photos are accepted", "TOO_MANY_IMAGES");
    }

    private static void CheckVitals(IReadOnlyList<Request.HorseDetail> horses)
    {
        if (horses.Any(horse => horse.BodyTemperatureCelsius is < 35 or > 43))
            throw new UnprocessableEntityException("Body temperature is outside the biological range of a racehorse (35°C - 43°C). Please check the thermometer and enter the value again.", "VITAL_SIGNS_OUT_OF_RANGE");
        if (horses.Any(horse => horse.HeartRateBpm is < 10 or > 200 || horse.RespiratoryRateBpm is < 2 or > 100))
            throw new UnprocessableEntityException("Vital signs are outside the biological range. Please check the entered values again.", "VITAL_SIGNS_OUT_OF_RANGE");
    }

    private static IReadOnlyList<string> VitalWarnings(HorseHealthIncidentDetail horse)
    {
        var warnings = new List<string>();
        if (horse.BodyTemperatureCelsius is < 37.5m or > 38.5m) warnings.Add("BODY_TEMPERATURE_OUTSIDE_NORMAL");
        if (horse.HeartRateBpm is < 28 or > 44) warnings.Add("HEART_RATE_OUTSIDE_NORMAL");
        if (horse.RespiratoryRateBpm is < 8 or > 16) warnings.Add("RESPIRATORY_RATE_OUTSIDE_NORMAL");
        return warnings;
    }
}
