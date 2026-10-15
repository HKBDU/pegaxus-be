using System.Data;
using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

using static Pegaxus.Service.Common.DbTime;

namespace Pegaxus.Service.RoutePlanService;

public sealed partial class Service
{
    private static RouteType GetRouteType(RoutePlan plan)
    {
        var types = plan.RoutePlanRoutes.Select(item => item.RouteType).Distinct().ToList();
        return types.Count == 1 ? types[0] : RouteType.PRIMARY;
    }

    private static Response.SummaryResponse ToSummary(RoutePlan item)
    {
        var segments = item.RoutePlanRoutes.OrderBy(route => route.SequenceNo).ToList();
        var routeType = GetRouteType(item);
        return new Response.SummaryResponse
        {
            RoutePlanId = item.RoutePlanId,
            RoutePlanCode = item.RoutePlanCode,
            TransportPlanId = item.TransportPlanId,
            RouteType = routeType.ToString(),
            Status = item.Status.ToString(),
            TotalDistanceKm = item.TotalDistanceKm ?? 0,
            EstimatedDurationMinutes = item.EstimatedDurationMinutes ?? 0,
            SegmentCount = segments.Count,
            IsPersonnelComplete = segments.Where(segment => !segment.TransportProviderId.HasValue).All(segment => segment.DriverEmployeeId.HasValue && segment.EscortEmployeeId.HasValue),
            ReplacesSequenceNo = routeType == RouteType.BACKUP ? ReplacementSequenceRange(item, segments) : null,
            RouteNote = item.RouteNote,
            UpdatedAt = AsUtc(item.UpdatedAt)
        };
    }

    private static Response.SequenceRangeResponse? ReplacementSequenceRange(RoutePlan item, List<RoutePlanRoute> segments)
    {
        if (segments.Count == 0) return null;
        var primary = item.TransportPlan.RoutePlans.SingleOrDefault(plan => plan.RoutePlanId == item.PrimaryRoutePlanId);
        var primarySegments = primary?.RoutePlanRoutes.OrderBy(segment => segment.SequenceNo).ToList();
        if (primarySegments is null) return null;
        var start = primarySegments.FindIndex(segment => segment.Route.FromLocationId == segments[0].Route.FromLocationId);
        var end = primarySegments.FindLastIndex(segment => segment.Route.ToLocationId == segments[^1].Route.ToLocationId);
        return start >= 0 && end >= start
            ? new Response.SequenceRangeResponse { From = primarySegments[start].SequenceNo, To = primarySegments[end].SequenceNo }
            : null;
    }

    private static Response.DetailResponse ToDetail(RoutePlan item, Response.LegalReviewResponse? legalReview = null)
    {
        var segments = item.RoutePlanRoutes.OrderBy(route => route.SequenceNo).ToList();
        var effective = GetRouteType(item) == RouteType.PRIMARY && item.Status == RoutePlanStatus.ACTIVE;
        if (effective)
            segments = item.TransportPlan.RoutePlans.Where(plan => plan.Status == RoutePlanStatus.ACTIVE)
                .SelectMany(plan => plan.RoutePlanRoutes).Where(segment => segment.SupersededAt == null)
                .OrderBy(segment => segment.PlannedDepartureAt).ThenBy(segment => segment.SequenceNo).ToList();
        return new Response.DetailResponse
        {
            RoutePlanId = item.RoutePlanId,
            RoutePlanCode = item.RoutePlanCode,
            TransportPlanId = item.TransportPlanId,
            BookingId = item.TransportPlan.Transport.BookingId,
            BookingCode = item.TransportPlan.Transport.Booking.BookingCode,
            RouteType = GetRouteType(item).ToString(),
            Status = item.Status.ToString(),
            IsCrossBorder = item.TransportPlan.Transport.Booking.OriginCountry != item.TransportPlan.Transport.Booking.DestinationCountry,
            TotalDistanceKm = effective ? segments.Sum(x => x.Route.DistanceKm ?? 0) : item.TotalDistanceKm ?? 0,
            EstimatedDurationMinutes = effective && segments.Count > 0 &&
                segments[0].PlannedDepartureAt.HasValue && segments[^1].PlannedArrivalAt.HasValue
                    ? (int)(segments[^1].PlannedArrivalAt!.Value - segments[0].PlannedDepartureAt!.Value).TotalMinutes
                    : item.EstimatedDurationMinutes ?? 0,
            PlannedDepartureAt = segments.FirstOrDefault()?.PlannedDepartureAt is { } start ? AsUtc(start) : null,
            PlannedArrivalAt = segments.LastOrDefault()?.PlannedArrivalAt is { } end ? AsUtc(end) : null,
            Segments = segments.Select((segment, index) => new Response.SegmentResponse
            {
                RoutePlanRouteId = segment.RoutePlanRouteId,
                SequenceNo = segment.SequenceNo,
                RouteType = segment.RouteType.ToString(),
                Route = new Response.RouteResponse { RouteId = segment.Route.RouteId, RouteCode = segment.Route.RouteCode, RouteName = segment.Route.RouteName, TransportMode = segment.Route.TransportMode.ToString(), DistanceKm = segment.Route.DistanceKm, EstimatedDurationMinutes = segment.Route.EstimatedDurationMinutes },
                FromLocation = ToLocation(segment.Route.FromLocation),
                ToLocation = ToLocation(segment.Route.ToLocation),
                PlannedDepartureAt = segment.PlannedDepartureAt.HasValue ? AsUtc(segment.PlannedDepartureAt.Value) : null,
                PlannedArrivalAt = segment.PlannedArrivalAt.HasValue ? AsUtc(segment.PlannedArrivalAt.Value) : null,
                ActualDepartureAt = segment.ActualDepartureAt.HasValue ? AsUtc(segment.ActualDepartureAt.Value) : null,
                ActualArrivalAt = segment.ActualArrivalAt.HasValue ? AsUtc(segment.ActualArrivalAt.Value) : null,
                Status = segment.ActualArrivalAt.HasValue
                    ? "COMPLETED"
                    : segment.ActualDepartureAt.HasValue
                        ? "IN_PROGRESS"
                        : "PENDING",
                DwellAfterMinutes = index < segments.Count - 1 && segment.PlannedArrivalAt.HasValue && segments[index + 1].PlannedDepartureAt.HasValue ? (int)(segments[index + 1].PlannedDepartureAt.GetValueOrDefault() - segment.PlannedArrivalAt.GetValueOrDefault()).TotalMinutes : null,
                IsOutsourced = segment.TransportProviderId.HasValue,
                Vehicles = segment.RoutePlanRouteVehicles.Select(item => new Response.VehicleResponse { VehicleId = item.Vehicle.VehicleId, VehicleCode = item.Vehicle.VehicleCode, VehicleName = item.Vehicle.VehicleName, LicensePlate = item.Vehicle.LicensePlate, TransportMode = item.Vehicle.TransportMode.ToString(), CapacityHorses = item.Vehicle.CapacityHorses }).ToList(),
                Outsourcing = segment.TransportProvider is null ? null : new Response.ProviderResponse { TransportProviderId = segment.TransportProvider.TransportProviderId, ProviderCode = segment.TransportProvider.ProviderCode, ProviderName = segment.TransportProvider.ProviderName, Status = segment.TransportProvider.Status.ToString(), SupportedModes = ParseSupportedModes(segment.TransportProvider.SupportedModes) },
                Driver = segment.DriverEmployee is null ? null : new Response.EmployeeResponse { EmployeeId = segment.DriverEmployee.EmployeeId, EmployeeCode = segment.DriverEmployee.EmployeeCode, FullName = segment.DriverEmployee.FullName, Role = segment.DriverEmployee.Role.ToString() },
                Escort = segment.EscortEmployee is null ? null : new Response.EmployeeResponse { EmployeeId = segment.EscortEmployee.EmployeeId, EmployeeCode = segment.EscortEmployee.EmployeeCode, FullName = segment.EscortEmployee.FullName, Role = segment.EscortEmployee.Role.ToString() },
                Note = segment.Note
            }).ToList(),
            IsPersonnelComplete = segments.Where(item => !item.TransportProviderId.HasValue).All(item => item.DriverEmployeeId.HasValue && item.EscortEmployeeId.HasValue),
            LegalReview = legalReview,
            RouteNote = item.RouteNote,
            CreatedAt = AsUtc(item.CreatedAt),
            UpdatedAt = AsUtc(item.UpdatedAt)
        };
    }

    private static Response.LocationResponse ToLocation(Location item) => new() { LocationId = item.LocationId, LocationName = item.LocationName, LocationType = item.LocationType?.ToString(), City = item.City, Country = item.Country, Latitude = item.Latitude, Longitude = item.Longitude };

    private static IReadOnlyList<string> ParseSupportedModes(string? supportedModes) =>
        string.IsNullOrWhiteSpace(supportedModes)
            ? []
            : supportedModes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static int CalculateDurationMinutes(IReadOnlyList<MaterializedSegment> segments)
    {
        if (segments.Count == 0)
            return 0;
        var duration = segments[^1].Request.PlannedArrivalAt - segments[0].Request.PlannedDepartureAt;
        return Math.Max(0, (int)duration.TotalMinutes);
    }
}
