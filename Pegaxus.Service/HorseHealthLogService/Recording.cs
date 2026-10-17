using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Repository.Extensions;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;

using static Pegaxus.Service.Common.DbTime;
using static Pegaxus.Service.Common.Text;

namespace Pegaxus.Service.HorseHealthLogService;

public sealed partial class Service
{
    private async Task<WriteResult> RecordAsync(Guid transportId, Request.RecordRequest request, Request.CreateRequest? single,
        Guid accountId, CancellationToken cancellationToken)
    {
        if (single is null) Validation.Validate(request, recordValidator);
        else Validation.Validate(single, createValidator);
        if (single is null && request.Files.Count > 1)
            throw new FieldValidationException([new("files", "Only one panorama image is accepted.")]);
        if (transportId == Guid.Empty) throw new BadRequestException("Transport ID must be a non-empty UUID.", "VALIDATION_ERROR");
        if (single is not null && (single.TemperatureCelsius is < 30 or > 43 || single.HeartRateBpm is < 10 or > 200 || single.RespiratoryRateBpm is < 2 or > 100))
            throw new UnprocessableEntityException("Vital signs are outside the biological range. Please check the entered values again.", "VITAL_SIGNS_OUT_OF_RANGE");
        var checkedAt = Truncate(request.CheckedAt ?? clock.GetUtcNow());
        var logType = Enum.Parse<HorseHealthLogType>(request.LogType);
        var selectedHorseId = single?.HorseId;
        for (var attempt = 0; ; attempt++)
        {
            var committed = false;
            try
            {
                await using var transaction = await db.Database.BeginSerializableAsync(cancellationToken);
                // The transport lock serializes replays and batch writes with route/lifecycle changes.
                await db.LockRowsAsync(db.Transports.Where(item => item.TransportId == transportId), item => item.TransportId, cancellationToken);
                var escortId = await GetEscortAsync(accountId, cancellationToken);
                var bookingId = await GetTransportBookingAsync(transportId, escortId, cancellationToken);
                var horseIds = await db.BookingHorses.AsNoTracking().Where(item => item.BookingId == bookingId && (!selectedHorseId.HasValue || item.HorseId == selectedHorseId))
                    .OrderBy(item => item.HorseId).Select(item => item.HorseId).ToArrayAsync(cancellationToken);
                if (horseIds.Length == 0)
                    throw new UnprocessableEntityException(single is null ? "There are no horses in this transport" : "The horse does not belong to this transport",
                        single is null ? "NO_HORSES_IN_TRANSPORT" : "HORSE_NOT_IN_TRANSPORT");
                var existing = await db.HorseHealthLogs.AsNoTracking().Where(item => item.TransportId == transportId && horseIds.Contains(item.HorseId) &&
                        item.EmployeeId == escortId && item.LogType == logType && item.CheckedAt == checkedAt)
                    .OrderBy(item => item.CreatedAt).ThenBy(item => item.HorseHealthLogId)
                    .Select(item => new { item.HorseHealthLogId, item.HorseId }).ToListAsync(cancellationToken);
                var idsByHorse = existing.GroupBy(item => item.HorseId).ToDictionary(group => group.Key, group => group.First().HorseHealthLogId);
                var missing = horseIds.Where(id => !idsByHorse.ContainsKey(id)).ToArray();
                var photoUrls = new List<string>();
                if (missing.Length > 0)
                {
                    foreach (var file in request.Files)
                        photoUrls.Add(await ConfirmImageAsync(accountId, file.PublicId, cancellationToken));
                }
                var now = Truncate(clock.GetUtcNow());
                var created = missing.Select(horseId => new HorseHealthLog
                {
                    HorseHealthLogId = Guid.NewGuid(), TransportId = transportId, HorseId = horseId, EmployeeId = escortId,
                    LogType = logType, CheckedAt = checkedAt,
                    HealthStatus = single is null ? HorseHealthStatus.NORMAL : Enum.Parse<HorseHealthStatus>(single.HealthStatus),
                    HydrationStatus = single is null ? HydrationStatus.NORMAL : Enum.Parse<HydrationStatus>(single.HydrationStatus),
                    TemperatureCelsius = single?.TemperatureCelsius, HeartRateBpm = single?.HeartRateBpm, RespiratoryRateBpm = single?.RespiratoryRateBpm,
                    BehaviorNote = EmptyToNull(single?.BehaviorNote), FeedingNote = EmptyToNull(single?.FeedingNote), HydrationNote = EmptyToNull(single?.HydrationNote),
                    SymptomNote = EmptyToNull(single?.SymptomNote), InjuryNote = EmptyToNull(single?.InjuryNote),
                    TreatmentProvided = EmptyToNull(single?.TreatmentProvided), MedicationGiven = EmptyToNull(single?.MedicationGiven),
                    VeterinarianRequired = single?.VeterinarianRequired ?? false, VeterinarianNote = EmptyToNull(single?.VeterinarianNote),
                    LocationDescription = EmptyToNull(request.LocationDescription), GeneralNote = EmptyToNull(request.GeneralNote),
                    PhotoUrls = photoUrls.ToArray(), CreatedAt = now, UpdatedAt = now
                }).ToArray();
                db.HorseHealthLogs.AddRange(created);
                var emergencyNotified = false;
                if (single is not null && created.Length > 0 && (created[0].HealthStatus == HorseHealthStatus.CRITICAL || created[0].VeterinarianRequired))
                    emergencyNotified = await QueueEmergencyAsync(created[0], now, cancellationToken);
                if (created.Length > 0) await db.SaveChangesAsync(cancellationToken);
                var ids = idsByHorse.Values.Concat(created.Select(item => item.HorseHealthLogId)).ToArray();
                var rows = await ReadLogsAsync(ids, cancellationToken);
                if (single is not null)
                    rows[0] = rows[0] with { Log = rows[0].Log with { Photos = await SignPhotosAsync(rows[0].PhotoUrls, cancellationToken) } };
                await transaction.CommitAsync(cancellationToken);
                committed = true;
                await notifications.FlushAsync(cancellationToken);
                return new(rows.Select(item => item.Log).ToArray(), created.Length, emergencyNotified);
            }
            catch (Exception error) when (!committed && PostgresErrors.IsWriteConflict(error))
            {
                if (attempt < 2)
                {
                    db.ChangeTracker.Clear();
                    continue;
                }
                throw new ConflictException("The record changed. Please retry the request", "CONCURRENT_UPDATE");
            }
        }
    }
}
