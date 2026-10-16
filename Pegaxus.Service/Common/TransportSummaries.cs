using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;

namespace Pegaxus.Service.Common;

public static class TransportSummaries
{
    public sealed record HealthSummary(int Normal, int ObservationRequired, int AtRisk, int Critical, int NoData, int Total);

    private static readonly IncidentStatus[] ActiveIncidentStatuses = [IncidentStatus.REPORTED, IncidentStatus.PENDING];

    /// <summary>The newest health log of each horse in the query (by checked time, then created time).</summary>
    public static IQueryable<HorseHealthLog> LatestPerHorse(this IQueryable<HorseHealthLog> logs) =>
        logs.Where(log => !logs.Any(newer => newer.TransportId == log.TransportId && newer.HorseId == log.HorseId &&
            (newer.CheckedAt > log.CheckedAt || newer.CheckedAt == log.CheckedAt && newer.CreatedAt > log.CreatedAt)));

    /// <summary>Incidents still being handled for a transport (REPORTED or PENDING).</summary>
    public static IQueryable<IncidentReport> Active(this IQueryable<IncidentReport> incidents, Guid transportId) =>
        incidents.Where(incident => incident.TransportId == transportId && ActiveIncidentStatuses.Contains(incident.Status));

    /// <param name="latest">One entry per horse that has at least one log: its newest health status.</param>
    public static HealthSummary Summarize(IEnumerable<HorseHealthStatus> latest, int horseCount)
    {
        var counts = latest.GroupBy(status => status).ToDictionary(group => group.Key, group => group.Count());
        int Count(HorseHealthStatus status) => counts.GetValueOrDefault(status);
        var withData = counts.Values.Sum();
        return new(Count(HorseHealthStatus.NORMAL), Count(HorseHealthStatus.OBSERVATION_REQUIRED), Count(HorseHealthStatus.AT_RISK),
            Count(HorseHealthStatus.CRITICAL), Math.Max(horseCount - withData, 0), horseCount);
    }

    public static string? LatestNote(HorseHealthLog? log) =>
        log is null ? null : !string.IsNullOrWhiteSpace(log.GeneralNote) ? log.GeneralNote : log.BehaviorNote;
}
