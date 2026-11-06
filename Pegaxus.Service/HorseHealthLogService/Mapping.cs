using Microsoft.EntityFrameworkCore;

using static Pegaxus.Service.Common.DbTime;

namespace Pegaxus.Service.HorseHealthLogService;

public sealed partial class Service
{
    private async Task<List<LogData>> ReadLogsAsync(Guid[] ids, CancellationToken cancellationToken) =>
        await db.HorseHealthLogs.AsNoTracking().Where(item => ids.Contains(item.HorseHealthLogId))
            .OrderBy(item => item.Horse.HorseName).ThenBy(item => item.HorseId)
            .Select(item => new LogData(item.PhotoUrls, new Response.HealthLog
            {
                HorseHealthLogId = item.HorseHealthLogId, TransportId = item.TransportId,
                Horse = new(item.HorseId, item.Horse.HorseName, item.Horse.MicrochipNumber, item.Horse.PassportNumber),
                CheckedAt = AsUtc(item.CheckedAt), LogType = item.LogType.ToString(), HealthStatus = item.HealthStatus.ToString(),
                HydrationStatus = item.HydrationStatus.HasValue ? item.HydrationStatus.Value.ToString() : null,
                CheckedBy = new(item.EmployeeId, item.Employee.EmployeeCode, item.Employee.FullName),
                TemperatureCelsius = item.TemperatureCelsius, HeartRateBpm = item.HeartRateBpm, RespiratoryRateBpm = item.RespiratoryRateBpm,
                BehaviorNote = item.BehaviorNote, SymptomNote = item.SymptomNote, InjuryNote = item.InjuryNote, FeedingNote = item.FeedingNote,
                HydrationNote = item.HydrationNote, TreatmentProvided = item.TreatmentProvided, MedicationGiven = item.MedicationGiven,
                VeterinarianRequired = item.VeterinarianRequired, VeterinarianNote = item.VeterinarianNote,
                LocationDescription = item.LocationDescription, GeneralNote = item.GeneralNote, HasPhoto = item.PhotoUrls.Length > 0,
                CreatedAt = AsUtc(item.CreatedAt), UpdatedAt = AsUtc(item.UpdatedAt)
            })).ToListAsync(cancellationToken);

    private sealed record LogData(string[] PhotoUrls, Response.HealthLog Log);
}
