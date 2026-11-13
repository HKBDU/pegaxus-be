using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Common;

using static Pegaxus.Service.Common.DbTime;

namespace Pegaxus.Service.IncidentService;

public sealed partial class Service
{
    private async Task<Response.Incident> ReadIncidentAsync(Guid incidentReportId, Transport transport, Employee? actor, CancellationToken ct, bool includeVitalWarnings = false)
    {
        var item = await db.IncidentReports.AsNoTracking().AsSplitQuery().Include(incident => incident.Employee).Include(incident => incident.Location)
            .Include(incident => incident.VehicleIncidentDetail).ThenInclude(detail => detail!.Vehicle)
            .Include(incident => incident.HorseHealthIncidentDetails).ThenInclude(detail => detail.Horse)
            .Include(incident => incident.IncidentPhotos)
            .Include(incident => incident.EmergencyCostRequests).ThenInclude(cost => cost.RequestedByEmployee)
            .Include(incident => incident.EmergencyCostRequests).ThenInclude(cost => cost.CostApprovalDecision).ThenInclude(decision => decision!.Employee)
            .SingleAsync(incident => incident.IncidentReportId == incidentReportId, ct);
        var photos = new List<Response.Photo>();
        foreach (var photo in item.IncidentPhotos.Where(photo => photo.EmergencyCostRequestId == null).OrderBy(photo => photo.SortOrder).ThenBy(photo => photo.IncidentPhotoId))
        {
            var signed = photo.PhotoUrl.StartsWith("s3://", StringComparison.Ordinal)
                ? await documentStorage.CreateReadUrlAsync(photo.PhotoUrl, ct) : await media.CreateReadUrlAsync(photo.PhotoUrl, ct);
            photos.Add(new(photo.IncidentPhotoId, signed.Url, photo.Caption, photo.HorseHealthIncidentDetailId, photo.SortOrder));
        }
        var horseIds = item.HorseHealthIncidentDetails.Select(detail => detail.HorseId).ToArray();
        var detached = await db.BookingHorses.AsNoTracking().Include(link => link.DetachedLocation)
            .Where(link => link.BookingId == transport.BookingId && horseIds.Contains(link.HorseId) && link.DetachedAt != null)
            .ToDictionaryAsync(link => link.HorseId, ct);
        var horseDetails = item.HorseHealthIncidentDetails.OrderBy(detail => detail.CreatedAt).ThenBy(detail => detail.HorseHealthIncidentDetailId)
            .Select(detail => new Response.HorseDetail(detail.HorseHealthIncidentDetailId,
                // shortcut: BookingHorse has no stall number; populate it when allocation is added to the schema.
                new(detail.HorseId, detail.Horse.HorseName, detail.Horse.MicrochipNumber, detail.Horse.Breed, null),
                detail.IncidentType.ToString(), detail.ConditionDescription, detail.Symptoms, detail.BodyTemperatureCelsius,
                detail.HeartRateBpm, detail.RespiratoryRateBpm, detail.FirstAidProvided, detail.VeterinarianRequired,
                AsUtc(detail.VeterinarianContactedAt), detail.IsolationRequired, detail.VeterinarianInstruction,
                detail.MedicationGiven, detail.OutcomeNote, detail.HorseTransportable, AsUtc(detail.UpdatedAt),
                includeVitalWarnings ? VitalWarnings(detail) : null,
                detached.TryGetValue(detail.HorseId, out var link) ? new Response.HorseDetachment(AsUtc(link.DetachedAt!.Value),
                    LocationBrief.From(link.DetachedLocation), link.DetachedIncidentReportId) : null)).ToArray();
        var costs = item.EmergencyCostRequests.OrderBy(cost => cost.RequestedAt).ThenBy(cost => cost.EmergencyCostRequestId)
            .Select(MapCost).ToArray();
        var currentPlanId = await CurrentPlanIdAsync(transport.TransportId, ct);
        var plans = await db.RoutePlans.AsNoTracking().Include(plan => plan.RoutePlanRoutes)
            .Where(plan => plan.TransportPlanId == currentPlanId && plan.UpdatedAt >= item.ReportedAt &&
                plan.RoutePlanRoutes.Any(route => route.RouteType == RouteType.BACKUP || route.RouteType == RouteType.RETURN))
            .OrderBy(plan => plan.CreatedAt).ToArrayAsync(ct);
        var pending = costs.Count(cost => cost.Status == nameof(EmergencyCostStatus.PENDING));
        var active = item.Status is IncidentStatus.REPORTED or IncidentStatus.PENDING;
        var canResolve = actor?.Role == EmployeeRole.LOGISTICS_MANAGER && active && pending == 0 && transport.Status is TransportStatus.IN_TRANSIT or TransportStatus.COMPLETED;
        var detail = item.VehicleIncidentDetail;
        var actions = actor is null ? [] : await ActionsAsync(item, transport, actor, canResolve, ct);
        return new()
        {
            IncidentReportId = item.IncidentReportId, IncidentCode = item.IncidentCode,
            Transport = new(transport.TransportId, transport.TransportCode, transport.Status.ToString(), transport.Booking.BookingCode),
            Category = item.Category.ToString(), Severity = item.Severity.ToString(), Status = item.Status.ToString(),
            Title = item.Title, Description = item.Description, ImmediateAction = item.ImmediateAction,
            OccurredAt = AsUtc(item.OccurredAt), ReportedAt = AsUtc(item.ReportedAt), ResolvedAt = AsUtc(item.ResolvedAt),
            ResolutionSummary = item.ResolutionSummary, CancelledAt = AsUtc(item.CancelledAt), CancellationReason = item.CancellationReason,
            Location = LocationBrief.From(item.Location), ReportedBy = EmployeeBrief(item.Employee),
            Vehicle = detail is null ? null : VehicleBrief(detail.Vehicle), Horses = horseDetails.Select(horse => horse.Horse).ToArray(),
            VehicleDetail = detail is null ? null : new(detail.VehicleIncidentDetailId, VehicleBrief(detail.Vehicle), detail.IncidentType.ToString(),
                detail.VehicleCondition, detail.DamageDescription, detail.VehicleOperable, detail.RepairRequired, detail.TowingRequired,
                detail.ReplacementVehicleRequired, Whole(detail.EstimatedRepairCost), detail.EstimatedRepairHours, detail.DriverSafetyStatus,
                detail.CargoConditionNote, detail.RepairNote, AsUtc(detail.UpdatedAt)),
            HorseDetails = horseDetails, Photos = photos, EmergencySettlementRequired = item.EmergencySettlementRequired, CostRequests = costs,
            CostSummary = new(costs.Where(cost => cost.Status != nameof(EmergencyCostStatus.CANCELLED)).Sum(cost => cost.RequestedAmount), costs.Where(cost => cost.Status == nameof(EmergencyCostStatus.APPROVED))
                .Sum(cost => cost.ApprovedAmount ?? 0), pending),
            RouteChanges = plans.Select(plan => new Response.RouteChange(plan.RoutePlanId, plan.RoutePlanCode, TransportItinerary.PlanType(plan).ToString(),
                plan.Status.ToString(), AsUtc(plan.UpdatedAt))).ToArray(),
            Hold = await ReadHoldAsync(incidentReportId, ct),
            CanResolve = canResolve, AvailableActions = actions,
            DetachLocations = actions.Contains("DETACH_HORSE")
                ? DetachLocations(await EffectiveSegmentsAsync(transport.TransportId, ct)).Select(location => LocationBrief.From(location)!).ToArray() : [],
            ServerTime = clock.GetUtcNow(), CreatedAt = AsUtc(item.CreatedAt), UpdatedAt = AsUtc(item.UpdatedAt)
        };
    }

    private async Task<IReadOnlyList<string>> ActionsAsync(IncidentReport incident, Transport transport, Employee actor, bool canResolve, CancellationToken ct)
    {
        if (incident.Status is not (IncidentStatus.REPORTED or IncidentStatus.PENDING)) return [];
        var manager = actor.Role == EmployeeRole.LOGISTICS_MANAGER;
        var coordinator = await CoordinatorAssignedAsync(transport.TransportId, actor, ct);
        var assigned = Crew(actor) && await CrewAssignedAsync(transport.TransportId, actor, ct);
        var reporter = assigned && incident.EmployeeId == actor.EmployeeId;
        var onDuty = assigned && await CrewOnDutyAsync(transport, actor, ct);
        // The crew on duty, or the reporter, acts on the incident; a crew that is not on its segment yet (or any more) only reads it.
        var crew = onDuty || reporter;
        var actions = new List<string>();
        if (crew && actor.Role == EmployeeRole.ESCORT && incident.Category is IncidentCategory.BOTH or IncidentCategory.HORSE_HEALTH) actions.Add("ADD_HORSE_DETAIL");
        if (incident.VehicleIncidentDetail is not null && (manager || coordinator || reporter)) actions.Add("SUPPLEMENT_VEHICLE");
        if (incident.HorseHealthIncidentDetails.Count > 0 && (manager || coordinator || reporter || onDuty && actor.Role == EmployeeRole.ESCORT)) actions.Add("FOLLOW_UP_HORSE");
        if (transport.Status == TransportStatus.IN_TRANSIT)
        {
            if (crew || coordinator) actions.Add("REQUEST_COST");
            if (coordinator) actions.AddRange(["PROPOSE_REROUTE", "ESCALATE_REROUTE"]);
            if (manager && await ActiveTransportHoldAsync(transport.TransportId, ct) is null) actions.Add("HOLD_ROUTE");
            if (manager && incident.Category is IncidentCategory.BOTH or IncidentCategory.HORSE_HEALTH && incident.HorseHealthIncidentDetails.Count > 0)
                actions.Add("DETACH_HORSE");
            if (crew && incident.Status == IncidentStatus.REPORTED && !incident.EmergencyCostRequests.Any(cost => cost.Status is EmergencyCostStatus.PENDING or EmergencyCostStatus.APPROVED))
                actions.Add("SELF_RESOLVE");
        }
        if (manager && incident.EmergencyCostRequests.Any(cost => cost.Status == EmergencyCostStatus.PENDING)) actions.Add("DECIDE_COST");
        if (manager && canResolve) actions.Add("RESOLVE");
        if (incident.Severity != IncidentSeverity.CRITICAL && (manager || coordinator || crew)) actions.Add("RAISE_SEVERITY");
        // Only the manager lowers a severity.
        if (manager && incident.Severity != IncidentSeverity.LOW) actions.Add("LOWER_SEVERITY");
        // A crew may cancel its own mistaken report only until the manager takes it up, and only while no cost is open.
        if (manager || crew && incident.Status == IncidentStatus.REPORTED &&
            !incident.EmergencyCostRequests.Any(cost => cost.Status is EmergencyCostStatus.PENDING or EmergencyCostStatus.APPROVED)) actions.Add("CANCEL_INCIDENT");
        return actions;
    }

    private static long? Whole(decimal? value) => value.HasValue ? Vnd.ToLong(value.Value) : null;
    private static Response.EmployeeBrief EmployeeBrief(Employee employee) => new(employee.EmployeeId, employee.EmployeeCode, employee.FullName, employee.Role.ToString(), employee.Phone);
    private static Response.VehicleBrief VehicleBrief(Vehicle vehicle) => new(vehicle.VehicleId, vehicle.VehicleCode, vehicle.LicensePlate, vehicle.VehicleName, vehicle.Status.ToString());
}
