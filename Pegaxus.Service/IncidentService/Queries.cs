using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;

using static Pegaxus.Service.Common.DbTime;

namespace Pegaxus.Service.IncidentService;

public sealed partial class Service
{
    public async Task<Response.ListResult> ListAsync(Request.ListRequest request, Guid accountId, CancellationToken cancellationToken = default)
    {
        Validation.ValidateFields(request, listValidator);
        var actor = await ActorAsync(accountId, cancellationToken);
        var scope = VisibleIncidents(actor);
        var counts = await scope.GroupBy(_ => 1).Select(group => new Response.Counts(
            group.Count(item => item.Status == IncidentStatus.REPORTED), group.Count(item => item.Status == IncidentStatus.PENDING),
            group.Count(item => item.Status == IncidentStatus.RESOLVED),
            group.Count(item => (item.Status == IncidentStatus.REPORTED || item.Status == IncidentStatus.PENDING) &&
                (item.Severity == IncidentSeverity.HIGH || item.Severity == IncidentSeverity.CRITICAL))))
            .SingleOrDefaultAsync(cancellationToken) ?? new(0, 0, 0, 0);
        var query = scope;
        if (request.Status is { } status)
        {
            var statuses = status.Split(',', StringSplitOptions.TrimEntries).Select(Enum.Parse<IncidentStatus>).ToArray();
            query = query.Where(item => statuses.Contains(item.Status));
        }
        if (request.Category is { } category)
        {
            var value = Enum.Parse<IncidentCategory>(category); query = query.Where(item => item.Category == value);
        }
        if (request.Severity is { } severity)
        {
            var value = Enum.Parse<IncidentSeverity>(severity); query = query.Where(item => item.Severity == value);
        }
        if (request.TransportId is { } transportId) query = query.Where(item => item.TransportId == transportId);
        if (request.HasPendingCost is { } pending)
            query = query.Where(item => item.EmergencyCostRequests.Any(cost => cost.Status == EmergencyCostStatus.PENDING) == pending);
        if (request.EmergencySettlementRequired is { } settlement) query = query.Where(item => item.EmergencySettlementRequired == settlement);
        if (request.OccurredFrom is { } from)
        {
            var start = from.ToDateTime(TimeOnly.MinValue); query = query.Where(item => item.OccurredAt >= start);
        }
        if (request.OccurredTo is { } to)
        {
            var end = to.ToDateTime(TimeOnly.MaxValue); query = query.Where(item => item.OccurredAt <= end);
        }
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToLowerInvariant();
            query = query.Where(item => item.IncidentCode.ToLower().Contains(search) || item.Title.ToLower().Contains(search) ||
                item.Employee.FullName.ToLower().Contains(search) || item.Location.LocationName.ToLower().Contains(search) ||
                item.VehicleIncidentDetail != null && item.VehicleIncidentDetail.Vehicle.LicensePlate != null &&
                    item.VehicleIncidentDetail.Vehicle.LicensePlate.ToLower().Contains(search) ||
                db.Transports.Any(transport => transport.TransportId == item.TransportId && transport.TransportCode.ToLower().Contains(search)));
        }
        var total = await query.CountAsync(cancellationToken);
        var page = await Sort(query, request.SortBy).Skip(Paging.Offset(request.PageIndex, request.PageSize)).Take(request.PageSize)
            .AsSplitQuery().Include(item => item.Employee).Include(item => item.Location)
            .Include(item => item.VehicleIncidentDetail).ThenInclude(detail => detail!.Vehicle)
            .Include(item => item.HorseHealthIncidentDetails).ThenInclude(detail => detail.Horse)
            .Include(item => item.EmergencyCostRequests).ToArrayAsync(cancellationToken);
        var transportIds = page.Select(item => item.TransportId).ToArray();
        var transports = await db.Transports.AsNoTracking().Where(item => transportIds.Contains(item.TransportId))
            .Select(item => new Response.TransportBrief(item.TransportId, item.TransportCode, item.Status.ToString(), item.Booking.BookingCode))
            .ToDictionaryAsync(item => item.TransportId, cancellationToken);
        var items = page.Select(item => new Response.ListItem(item.IncidentReportId, item.IncidentCode, transports[item.TransportId!.Value],
            item.Category.ToString(), item.Severity.ToString(), item.Status.ToString(), item.Title, AsUtc(item.OccurredAt), AsUtc(item.ReportedAt),
            LocationBrief.From(item.Location), new(item.EmployeeId, item.Employee.EmployeeCode, item.Employee.FullName, item.Employee.Role.ToString()),
            item.VehicleIncidentDetail is { } detail ? new(detail.VehicleId, detail.Vehicle.VehicleCode, detail.Vehicle.LicensePlate) : null,
            item.HorseHealthIncidentDetails.OrderBy(horse => horse.HorseId).Select(horse => new Response.ListHorse(horse.HorseId, horse.Horse.HorseName)).ToArray(),
            item.EmergencySettlementRequired, item.EmergencyCostRequests.Count,
            item.EmergencyCostRequests.Count(cost => cost.Status == EmergencyCostStatus.PENDING),
            Vnd.ToLong(item.EmergencyCostRequests.Where(cost => cost.Status == EmergencyCostStatus.APPROVED).Sum(cost => cost.ApprovedAmount ?? 0)),
            AsUtc(item.UpdatedAt))).ToArray();
        return new(clock.GetUtcNow(), counts, items, request.PageIndex, request.PageSize, total, Paging.TotalPages(total, request.PageSize));
    }

    public async Task<object> GetAsync(Guid incidentReportId, Guid accountId, CancellationToken cancellationToken = default)
    {
        if (incidentReportId == Guid.Empty) throw new BadRequestException("Incident ID must be a non-empty UUID.", "VALIDATION_ERROR");
        var account = await AccountAsync(accountId, cancellationToken);
        var actor = account.AccountType == AccountType.CUSTOMER ? null : await ActorAsync(accountId, cancellationToken);
        var query = actor is null
            ? db.IncidentReports.AsNoTracking().Where(item => (item.Severity == IncidentSeverity.HIGH || item.Severity == IncidentSeverity.CRITICAL) &&
                db.Transports.Any(transport => transport.TransportId == item.TransportId && !transport.Booking.Customer.IsDeleted &&
                    transport.Booking.Customer.AccountId == accountId))
            : VisibleIncidents(actor);
        var transportId = await query.Where(item => item.IncidentReportId == incidentReportId).Select(item => item.TransportId)
            .SingleOrDefaultAsync(cancellationToken) ?? throw new NotFoundException("Incident not found", "INCIDENT_NOT_FOUND");
        var transport = await TransportAsync(transportId, cancellationToken);
        var incident = await ReadIncidentAsync(incidentReportId, transport, actor, cancellationToken);
        if (actor is not null) return incident;
        var horseIds = await db.BookingHorses.AsNoTracking().Where(item => item.BookingId == transport.BookingId)
            .Select(item => item.HorseId).ToArrayAsync(cancellationToken);
        return CustomerResponse.From(incident, horseIds.ToHashSet());
    }

    private IQueryable<IncidentReport> VisibleIncidents(Employee actor)
    {
        var query = db.IncidentReports.AsNoTracking().Where(item => item.TransportId.HasValue &&
            db.Transports.Any(transport => transport.TransportId == item.TransportId));
        if (!Crew(actor)) return query;
        var assigned = CrewSegments(actor).Select(segment => segment.RoutePlan.TransportPlan.TransportId);
        return query.Where(item => item.EmployeeId == actor.EmployeeId || assigned.Contains(item.TransportId!.Value));
    }

    private static IOrderedQueryable<IncidentReport> Sort(IQueryable<IncidentReport> query, string sortBy)
    {
        IOrderedQueryable<IncidentReport>? ordered = null;
        foreach (var token in sortBy.Split(',', StringSplitOptions.TrimEntries))
        {
            var parts = token.Split(':');
            Expression<Func<IncidentReport, object>> key = parts[0] switch
            {
                "severity" => item => item.Severity, "status" => item => item.Status,
                "updatedAt" => item => item.UpdatedAt, _ => item => item.OccurredAt
            };
            ordered = ordered is null
                ? parts[1] == "desc" ? query.OrderByDescending(key) : query.OrderBy(key)
                : parts[1] == "desc" ? ordered.ThenByDescending(key) : ordered.ThenBy(key);
        }
        return (ordered ?? query.OrderByDescending(item => item.OccurredAt)).ThenBy(item => item.IncidentReportId);
    }
}
