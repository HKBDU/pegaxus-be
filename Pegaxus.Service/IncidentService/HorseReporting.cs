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
    public async Task<Response.CreateResult> ReportHorseHealthAsync(Request.ReportHorseHealthRequest request, Guid accountId, CancellationToken cancellationToken = default)
    {
        CheckPhotoCount(request);
        Validation.ValidateFields(request, horseReportValidator);
        CheckVitals(request.Horses);
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
                var candidates = await db.IncidentReports.AsNoTracking().Include(item => item.HorseHealthIncidentDetails)
                    .Where(item => item.TransportId == request.TransportId && item.EmployeeId == actor.EmployeeId &&
                        item.Category == IncidentCategory.HORSE_HEALTH && item.OccurredAt == occurredAt)
                    .OrderBy(item => item.CreatedAt).ThenBy(item => item.IncidentReportId).ToArrayAsync(cancellationToken);
                var key = request.Horses.Select(horse => (horse.HorseId, Enum.Parse<HorseIncidentType>(horse.IncidentType))).ToHashSet();
                // Later IN3 details must not change the identity of the original offline submission.
                var existing = candidates.FirstOrDefault(item => key.SetEquals(item.HorseHealthIncidentDetails
                    .Where(detail => detail.CreatedAt == item.CreatedAt).Select(detail => (detail.HorseId, detail.IncidentType))));
                if (existing is not null)
                {
                    var replay = await ReadIncidentAsync(existing.IncidentReportId, transport, actor, cancellationToken, true);
                    var safety = await HorseSafetyAsync(transport, cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    return new(replay with { Effects = new(false, false, HealthLogsCreated: 0, FirstAidGuide: []), Safety = safety }, false);
                }
                if (transport.Status != TransportStatus.IN_TRANSIT)
                    throw new ConflictException("Incidents can only be reported while the transport is IN_TRANSIT", "INVALID_TRANSPORT_STATUS");
                var horses = await BookingHorsesAsync(transport, request.Horses, cancellationToken);
                var now = Truncate(clock.GetUtcNow());
                var first = request.Horses[0];
                var incident = new IncidentReport
                {
                    IncidentReportId = Guid.NewGuid(), TransportId = transport.TransportId, EmployeeId = actor.EmployeeId,
                    IncidentCode = await CodeGeneration.NextAsync("INC", now, code => db.IncidentReports.AnyAsync(item => item.IncidentCode == code, cancellationToken)),
                    Category = IncidentCategory.HORSE_HEALTH, Severity = Enum.Parse<IncidentSeverity>(request.Severity), Status = IncidentStatus.REPORTED,
                    Title = EmptyToNull(request.Title) ?? $"[SỰ CỐ Y TẾ] Ngựa {horses[first.HorseId].HorseName} - {first.IncidentType}",
                    Description = string.Join("\n", request.Horses.Select(horse => horse.ConditionDescription.Trim())),
                    ImmediateAction = EmptyToNull(request.ImmediateAction) ?? EmptyToNull(first.FirstAidProvided),
                    EmergencySettlementRequired = request.EmergencySettlementRequired, Location = await ResolveLocationAsync(request, now, cancellationToken),
                    OccurredAt = occurredAt, ReportedAt = now, CreatedAt = now, UpdatedAt = now
                };
                var photos = await AddPhotosAsync(incident, request.PhotoFiles, accountId, now, cancellationToken);
                AddMedicalRecords(incident, transport, request.Horses, actor, photos, occurredAt, now);
                db.IncidentReports.Add(incident);
                var emailQueued = await QueueHorseAlertAsync(incident, transport, true, now, cancellationToken);
                var safetyResponse = await HorseSafetyAsync(transport, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                var response = await ReadIncidentAsync(incident.IncidentReportId, transport, actor, cancellationToken, true);
                await transaction.CommitAsync(cancellationToken);
                committed = true;
                await notifications.FlushAsync(cancellationToken);
                return new(response with { Effects = new("URGENT", emailQueued, HealthLogsCreated: request.Horses.Count,
                    FirstAidGuide: request.Horses.Select(horse => horse.IncidentType).Distinct().ToArray()), Safety = safetyResponse }, true);
            }
            catch (Exception error) when (!committed && PostgresErrors.IsWriteConflict(error))
            {
                if (attempt < 19) { db.ChangeTracker.Clear(); continue; }
                throw new ConflictException("The incident could not be saved after concurrent changes. Please retry", "INCIDENT_MODIFIED");
            }
        }
    }

    private async Task<Response.Safety> HorseSafetyAsync(Transport transport, CancellationToken ct) => new(
        "Báo cáo y tế khẩn cấp đã được gửi. Hãy ưu tiên sơ cứu theo hướng dẫn bên dưới.", await ContactsAsync(transport, ct));
}
