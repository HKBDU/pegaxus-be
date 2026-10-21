using Pegaxus.Service.Common;

namespace Pegaxus.Service.IncidentService;

public static class CustomerResponse
{
    public sealed record Identity(string FullName, string Role);
    public sealed record VehicleDetail(Guid VehicleIncidentDetailId, Response.VehicleBrief Vehicle, string IncidentType,
        string? VehicleCondition, string? DamageDescription, bool VehicleOperable, bool RepairRequired, bool TowingRequired,
        bool ReplacementVehicleRequired, decimal? EstimatedRepairHours, string? DriverSafetyStatus, string? CargoConditionNote,
        string? RepairNote, DateTimeOffset UpdatedAt);

    // Keep a separate allowlist so new staff fields cannot silently become customer-visible.
    public sealed record Incident
    {
        public Guid IncidentReportId { get; init; }
        public required string IncidentCode { get; init; }
        public Response.TransportBrief? Transport { get; init; }
        public required string Category { get; init; }
        public required string Severity { get; init; }
        public required string Status { get; init; }
        public required string Title { get; init; }
        public required string Description { get; init; }
        public string? ImmediateAction { get; init; }
        public DateTimeOffset OccurredAt { get; init; }
        public DateTimeOffset ReportedAt { get; init; }
        public DateTimeOffset? ResolvedAt { get; init; }
        public string? ResolutionSummary { get; init; }
        public DateTimeOffset? CancelledAt { get; init; }
        public string? CancellationReason { get; init; }
        public LocationBrief? Location { get; init; }
        public required Identity ReportedBy { get; init; }
        public Response.VehicleBrief? Vehicle { get; init; }
        public IReadOnlyList<Response.HorseBrief> Horses { get; init; } = [];
        public VehicleDetail? VehicleDetail { get; init; }
        public IReadOnlyList<Response.HorseDetail> HorseDetails { get; init; } = [];
        public IReadOnlyList<Response.Photo> Photos { get; init; } = [];
        public int PhotoCount => Photos.Count;
        public IReadOnlyList<Response.RouteChange> RouteChanges { get; init; } = [];
        // shortcut: route holds have no writer yet; add a phone-free hold DTO with the route-hold feature.
        public object? Hold => null;
        public DateTimeOffset ServerTime { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset UpdatedAt { get; init; }
    }

    internal static Incident From(Response.Incident item, IReadOnlySet<Guid> horseIds)
    {
        var detail = item.VehicleDetail;
        var horses = item.HorseDetails.Where(horse => horseIds.Contains(horse.Horse.HorseId)).ToArray();
        var detailIds = horses.Select(horse => horse.HorseHealthIncidentDetailId).ToHashSet();
        return new()
        {
            IncidentReportId = item.IncidentReportId, IncidentCode = item.IncidentCode, Transport = item.Transport,
            Category = item.Category, Severity = item.Severity, Status = item.Status, Title = item.Title, Description = item.Description,
            ImmediateAction = item.ImmediateAction, OccurredAt = item.OccurredAt, ReportedAt = item.ReportedAt,
            ResolvedAt = item.ResolvedAt, ResolutionSummary = item.ResolutionSummary, CancelledAt = item.CancelledAt,
            CancellationReason = item.CancellationReason, Location = item.Location,
            ReportedBy = new(item.ReportedBy.FullName, item.ReportedBy.Role), Vehicle = item.Vehicle,
            VehicleDetail = detail is null ? null : new(detail.VehicleIncidentDetailId, detail.Vehicle, detail.IncidentType,
                detail.VehicleCondition, detail.DamageDescription, detail.VehicleOperable, detail.RepairRequired, detail.TowingRequired,
                detail.ReplacementVehicleRequired, detail.EstimatedRepairHours, detail.DriverSafetyStatus, detail.CargoConditionNote,
                detail.RepairNote, detail.UpdatedAt),
            HorseDetails = horses, Horses = horses.Select(horse => horse.Horse).ToArray(),
            Photos = item.Photos.Where(photo => photo.HorseHealthIncidentDetailId is not { } id || detailIds.Contains(id)).ToArray(),
            RouteChanges = item.RouteChanges, ServerTime = item.ServerTime, CreatedAt = item.CreatedAt, UpdatedAt = item.UpdatedAt
        };
    }
}
