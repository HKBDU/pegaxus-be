using System.Data;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Extensions;
using Pegaxus.Service.Exceptions;

using static Pegaxus.Service.Common.DbTime;

namespace Pegaxus.Service.RouteChangeService;

public sealed partial class Service
{
    private static Response.EmployeeBrief? Employee(Employee? x) => x is null ? null : new(x.EmployeeId, x.EmployeeCode, x.FullName, x.Role.ToString());

    private static Response.LocationBrief Location(Location x) => new(x.LocationId, x.LocationName, x.LocationType?.ToString(), x.City, x.Country, x.Latitude, x.Longitude);

    private static Response.VehicleBrief Vehicle(Vehicle x) => new(x.VehicleId, x.VehicleCode, x.VehicleName, x.LicensePlate, x.TransportMode.ToString(), x.CapacityHorses);

    private static string[] Modes(string? modes) => (modes ?? "").Split([',', ';', '|', ' ', '[', ']', '"'], StringSplitOptions.RemoveEmptyEntries).Distinct().Order().ToArray();

    private static Response.Segment Segment(RoutePlanRoute x, DateTime? nextDeparture)
    {
        if (x.PlannedArrivalAt is null || x.PlannedDepartureAt is null || x.Route.DistanceKm < 0)
            throw new ConflictException("A valid segment schedule and nonnegative route distance are required.", "ROUTE_DATA_INCOMPLETE");
        return new(x.RoutePlanRouteId, x.SequenceNo, x.RouteType.ToString(),
            new(x.RouteId, x.Route.RouteCode, x.Route.RouteName, x.Route.TransportMode.ToString(), x.Route.DistanceKm, x.Route.EstimatedDurationMinutes),
            Location(x.Route.FromLocation), Location(x.Route.ToLocation), AsUtc(x.PlannedDepartureAt.Value), AsUtc(x.PlannedArrivalAt.Value),
            AsUtc(x.ActualDepartureAt), AsUtc(x.ActualArrivalAt), x.ActualArrivalAt.HasValue ? "COMPLETED" : x.ActualDepartureAt.HasValue ? "IN_PROGRESS" : "PLANNED",
            nextDeparture.HasValue ? (decimal)(nextDeparture.Value - x.PlannedArrivalAt.Value).TotalMinutes : null,
            x.TransportProviderId.HasValue ? "OUTSOURCED" : "INTERNAL", x.RoutePlanRouteVehicles.OrderBy(v => v.VehicleId).Select(v => Vehicle(v.Vehicle)).ToList(),
            x.TransportProvider is { } p ? new(new(p.TransportProviderId, p.ProviderCode, p.ProviderName, p.Status.ToString(), Modes(p.SupportedModes)), x.ServiceType, x.ProviderReference) : null,
            Employee(x.DriverEmployee), Employee(x.EscortEmployee), x.Note);
    }
}
