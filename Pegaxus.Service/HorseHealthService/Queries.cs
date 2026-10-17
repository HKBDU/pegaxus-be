using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;

using static Pegaxus.Service.Common.DbTime;

namespace Pegaxus.Service.HorseHealthService;

public sealed partial class Service
{
    public async Task<Response.OverviewResponse> GetOverviewAsync(Guid transportId, Guid accountId, CancellationToken cancellationToken = default)
    {
        var actor = await GetActorAsync(accountId, cancellationToken);
        var transport = await GetVisibleTransportAsync(transportId, actor, cancellationToken);
        var horses = await db.BookingHorses.AsNoTracking().Where(item => item.BookingId == transport.BookingId)
            .OrderBy(item => item.Horse.HorseName).ThenBy(item => item.HorseId)
            .Select(item => new
            {
                Horse = new Response.HorseBrief(item.HorseId, item.Horse.HorseName, item.Horse.MicrochipNumber, item.Horse.PassportNumber),
                LogCount = db.HorseHealthLogs.Count(log => log.TransportId == transportId && log.HorseId == item.HorseId),
                LatestLog = db.HorseHealthLogs.Where(log => log.TransportId == transportId && log.HorseId == item.HorseId)
                    .OrderByDescending(log => log.CheckedAt).ThenByDescending(log => log.CreatedAt).ThenByDescending(log => log.HorseHealthLogId)
                    .Select(log => new Response.LatestLog(log.HorseHealthLogId, log.LogType.ToString(), log.HealthStatus.ToString(),
                        log.HydrationStatus.HasValue ? log.HydrationStatus.Value.ToString() : null, AsUtc(log.CheckedAt),
                        new Response.CheckedBy(log.EmployeeId, log.Employee.EmployeeCode, log.Employee.FullName),
                        log.LocationDescription, Text.EmptyToNull(log.GeneralNote) ?? Text.EmptyToNull(log.BehaviorNote), log.VeterinarianRequired))
                    .FirstOrDefault()
            }).ToListAsync(cancellationToken);
        var escort = await GetCurrentEscortAsync(transportId, transport.Status, actor, cancellationToken);
        return new(clock.GetUtcNow(), transportId, transport.Status.ToString(), escort,
            horses.Select(item => new Response.HorseOverview(item.Horse, item.LatestLog?.HealthStatus, item.LatestLog, item.LogCount)).ToArray());
    }

    public async Task<Response.ListResponse> GetLogsAsync(Guid transportId, Guid horseId, Request.ListRequest request, Guid accountId,
        CancellationToken cancellationToken = default)
    {
        var actor = await GetActorAsync(accountId, cancellationToken);
        Validation.ValidateFields(request, listValidator);
        var transport = await GetVisibleTransportAsync(transportId, actor, cancellationToken);
        if (horseId == Guid.Empty)
            throw new FieldValidationException([new("horseId", "Horse ID must be a non-empty UUID.")]);
        var horse = await db.BookingHorses.AsNoTracking().Where(item => item.BookingId == transport.BookingId && item.HorseId == horseId)
            .Select(item => new Response.HorseBrief(item.HorseId, item.Horse.HorseName, item.Horse.MicrochipNumber, item.Horse.PassportNumber))
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Horse not found", "HORSE_NOT_FOUND");
        var query = db.HorseHealthLogs.AsNoTracking().Where(item => item.TransportId == transportId && item.HorseId == horseId);
        if (request.LogType is not null)
        {
            var types = request.LogType.Split(',', StringSplitOptions.TrimEntries).Select(Enum.Parse<HorseHealthLogType>).Distinct().ToArray();
            query = query.Where(item => types.Contains(item.LogType));
        }
        if (request.HealthStatus is not null)
        {
            var statuses = request.HealthStatus.Split(',', StringSplitOptions.TrimEntries).Select(Enum.Parse<HorseHealthStatus>).Distinct().ToArray();
            query = query.Where(item => statuses.Contains(item.HealthStatus));
        }
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(item => item.CheckedAt).ThenByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.HorseHealthLogId).Skip(request.PageIndex * request.PageSize).Take(request.PageSize)
            .Select(item => new Response.ListItem(item.HorseHealthLogId,
                new Response.HorseSummary(item.HorseId, item.Horse.HorseName, item.Horse.MicrochipNumber), AsUtc(item.CheckedAt),
                item.LogType.ToString(), item.HealthStatus.ToString(), item.HydrationStatus.HasValue ? item.HydrationStatus.Value.ToString() : null,
                new Response.CheckedBy(item.EmployeeId, item.Employee.EmployeeCode, item.Employee.FullName), item.VeterinarianRequired, item.PhotoUrls.Length > 0))
            .ToListAsync(cancellationToken);
        return new(clock.GetUtcNow(), horse, items, request.PageIndex, request.PageSize, total, Paging.TotalPages(total, request.PageSize));
    }

    public async Task<Response.DetailResponse> GetAsync(Guid horseHealthLogId, Guid accountId, CancellationToken cancellationToken = default)
    {
        var actor = await GetActorAsync(accountId, cancellationToken);
        if (horseHealthLogId == Guid.Empty)
            throw new FieldValidationException([new("horseHealthLogId", "Horse health log ID must be a non-empty UUID.")]);
        var transportId = await db.HorseHealthLogs.AsNoTracking().Where(item => item.HorseHealthLogId == horseHealthLogId &&
                (!actor.IsCustomer || item.Transport.Booking.CustomerId == actor.CustomerId))
            .Select(item => (Guid?)item.TransportId).SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Horse health log not found", "HEALTH_LOG_NOT_FOUND");
        await GetVisibleTransportAsync(transportId, actor, cancellationToken);
        var result = await db.HorseHealthLogs.AsNoTracking().Where(item => item.HorseHealthLogId == horseHealthLogId)
            .Select(item => new
            {
                item.PhotoUrls,
                Detail = new Response.DetailResponse
                {
                    HorseHealthLogId = item.HorseHealthLogId, TransportId = item.TransportId,
                    Horse = new Response.HorseBrief(item.HorseId, item.Horse.HorseName, item.Horse.MicrochipNumber, item.Horse.PassportNumber),
                    CheckedAt = AsUtc(item.CheckedAt), LogType = item.LogType.ToString(), HealthStatus = item.HealthStatus.ToString(),
                    HydrationStatus = item.HydrationStatus.HasValue ? item.HydrationStatus.Value.ToString() : null,
                    CheckedBy = new Response.CheckedBy(item.EmployeeId, item.Employee.EmployeeCode, item.Employee.FullName),
                    TemperatureCelsius = item.TemperatureCelsius, HeartRateBpm = item.HeartRateBpm, RespiratoryRateBpm = item.RespiratoryRateBpm,
                    BehaviorNote = item.BehaviorNote, SymptomNote = item.SymptomNote, InjuryNote = item.InjuryNote, FeedingNote = item.FeedingNote,
                    HydrationNote = item.HydrationNote, TreatmentProvided = item.TreatmentProvided, MedicationGiven = item.MedicationGiven,
                    VeterinarianRequired = item.VeterinarianRequired, VeterinarianNote = actor.IsCustomer ? null : item.VeterinarianNote,
                    LocationDescription = item.LocationDescription, GeneralNote = item.GeneralNote, HasPhoto = item.PhotoUrls.Length > 0,
                    CreatedAt = AsUtc(item.CreatedAt), UpdatedAt = AsUtc(item.UpdatedAt)
                }
            }).SingleAsync(cancellationToken);
        var photos = new List<Response.Photo>();
        foreach (var url in result.PhotoUrls)
        {
            var signed = url.StartsWith("s3://", StringComparison.Ordinal)
                ? await documentStorage.CreateReadUrlAsync(url, cancellationToken)
                : await media.CreateReadUrlAsync(url, cancellationToken);
            photos.Add(new(signed.Url));
        }
        return result.Detail with { ServerTime = clock.GetUtcNow(), Photos = photos };
    }

    private async Task<Response.Escort?> GetCurrentEscortAsync(Guid transportId, TransportStatus status, Actor actor, CancellationToken cancellationToken)
    {
        var plans = await db.RoutePlans.AsNoTracking().Where(item => item.TransportPlan.TransportId == transportId &&
                VisiblePlanStatuses.Contains(item.Status)).Include(item => item.RoutePlanRoutes).ToListAsync(cancellationToken);
        var current = TransportItinerary.Current(TransportItinerary.Effective(plans), status);
        if (current is null) return null;
        return await db.RoutePlanRoutes.AsNoTracking().Where(item => item.RoutePlanRouteId == current.Route.RoutePlanRouteId &&
                !item.TransportProviderId.HasValue && item.EscortEmployee != null)
            .Select(item => new Response.Escort(item.EscortEmployee!.EmployeeId, item.EscortEmployee.EmployeeCode,
                item.EscortEmployee.FullName, item.EscortEmployee.Role.ToString(), actor.IsCustomer ? null : item.EscortEmployee.Phone))
            .SingleOrDefaultAsync(cancellationToken);
    }
}
