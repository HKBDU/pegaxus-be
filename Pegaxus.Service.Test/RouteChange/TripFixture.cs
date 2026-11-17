using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;

namespace Pegaxus.Service.Test;

/// <summary>
/// A trip as it really runs: PRIMARY O -> S1 -> S2 -> D, the first segment arrived, the second running (reaching a stop records
/// the departure of the next segment at once, so one segment always runs), the third untravelled. An open incident sits at X,
/// between S1 and S2. Built on <see cref="RouteChangeTests.Fixture"/>: O = Origin, S1 = Start, S2 = End.
/// </summary>
internal sealed class TripFixture
{
    internal static readonly DateTimeOffset Start = RouteChangeTests.Fixture.StartAt;
    internal RouteChangeTests.Fixture F = null!;
    internal Location D = null!, X = null!;
    internal RoutePlanRoute Suffix = null!;
    internal IncidentReport Incident = null!;

    internal static async Task<TripFixture> CreateAsync(AppDbContext db)
    {
        var trip = new TripFixture { F = await RouteChangeTests.Fixture.CreateAsync(db) };
        var f = trip.F;
        f.Current.ActualDepartureAt = Start.UtcDateTime;
        trip.D = new() { LocationId = Guid.NewGuid(), LocationName = "D", Country = "DE" };
        trip.X = new() { LocationId = Guid.NewGuid(), LocationName = "Incident spot", Country = "FR", LocationType = LocationType.OTHER };
        db.AddRange(trip.D, trip.X);
        trip.Suffix = new() { RoutePlanRouteId = Guid.NewGuid(), RoutePlan = f.Plan, Route = f.NewRoute(f.End, trip.D, 20), SequenceNo = 3,
            DriverEmployeeId = f.Driver.EmployeeId, EscortEmployeeId = f.Escort.EmployeeId,
            PlannedDepartureAt = Start.AddHours(3).UtcDateTime, PlannedArrivalAt = Start.AddHours(4).UtcDateTime };
        db.Add(trip.Suffix);
        db.RoutePlanRouteVehicles.Add(new() { RoutePlanRouteId = trip.Suffix.RoutePlanRouteId, VehicleId = db.Vehicles.Single().VehicleId, AssignedAt = Start.UtcDateTime });
        trip.Incident = new() { IncidentReportId = Guid.NewGuid(), TransportId = f.Transport.TransportId, IncidentCode = "INC-X",
            EmployeeId = f.Driver.EmployeeId, LocationId = trip.X.LocationId, Category = IncidentCategory.VEHICLE, Severity = IncidentSeverity.HIGH,
            Status = IncidentStatus.PENDING, Title = "Road blocked", Description = "A landslide blocks the road between S1 and S2",
            OccurredAt = Start.AddMinutes(30).UtcDateTime, ReportedAt = Start.AddMinutes(31).UtcDateTime, CreatedAt = Start.AddMinutes(31).UtcDateTime };
        db.Add(trip.Incident);
        await db.SaveChangesAsync();
        return trip;
    }

    /// <summary>A confirmed RETURN through the given stops (each leg two hours from <paramref name="departure"/>), with an approved extra dossier.</summary>
    internal async Task<RoutePlan> ReturnAsync(AppDbContext db, DateTimeOffset departure, params Location[] stops)
    {
        var plan = new RoutePlan { RoutePlanId = Guid.NewGuid(), TransportPlanId = F.Plan.TransportPlanId, PrimaryRoutePlanId = F.Plan.RoutePlanId,
            RoutePlanCode = "RETURN-" + Guid.NewGuid().ToString("N")[..6], Status = RoutePlanStatus.CONFIRMED, SubmittedForReviewAt = Start.UtcDateTime,
            CreatedAt = Start.UtcDateTime, UpdatedAt = Start.UtcDateTime, RouteNote = "Road closed, return to origin" };
        db.Add(plan);
        for (var i = 0; i + 1 < stops.Length; i++)
            db.Add(new RoutePlanRoute { RoutePlanRouteId = Guid.NewGuid(), RoutePlan = plan, Route = F.NewRoute(stops[i], stops[i + 1], 40),
                SequenceNo = i + 1, RouteType = RouteType.RETURN,
                PlannedDepartureAt = departure.AddHours(2 * i).UtcDateTime, PlannedArrivalAt = departure.AddHours(2 * i + 1).UtcDateTime });
        if (F.Transport.ExtraDossier is null) await F.AddExtraAsync();
        await db.SaveChangesAsync();
        return plan;
    }
}
