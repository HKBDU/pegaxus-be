using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;

using Reports = Pegaxus.Service.ReportService;

namespace Pegaxus.Service.Test;

public class ReportTests
{
    internal static readonly DateTimeOffset Now = new(2026, 11, 1, 1, 0, 0, TimeSpan.Zero);

    internal static DateTime D(string value) => DateTime.SpecifyKind(DateTime.Parse(value, CultureInfo.InvariantCulture), DateTimeKind.Unspecified);

    internal static Reports.Request.OnTimeRequest OnTime() => new() { DateFrom = new(2026, 10, 1), DateTo = new(2026, 10, 31) };
    internal static Reports.Request.IncidentClaimRequest IncidentClaim() => new() { DateFrom = new(2026, 10, 1), DateTo = new(2026, 10, 31) };
    internal static Reports.Request.TransitTimeRequest Transit() => new() { DateFrom = new(2026, 10, 1), DateTo = new(2026, 10, 31) };

    internal sealed class Fixture
    {
        private static readonly DateTime Created = new(2026, 9, 1);
        private readonly Dictionary<Guid, HandoverRecord> handovers = [];
        private int counter;

        internal required AppDbContext Context { get; init; }
        internal required Account ManagerAccount { get; init; }
        internal required Employee Manager { get; init; }
        internal required Account CustomerAccount { get; init; }
        internal required Customer Customer { get; init; }
        internal required Location Place { get; init; }
        internal required Route Route { get; init; }
        internal required Vehicle Vehicle { get; init; }
        internal required Horse Horse { get; init; }

        internal Reports.Service Service => For(Context);
        internal Reports.Service For(AppDbContext context) => new(context, new FinancialReportTests.FixedClock(Now));

        internal static async Task<Fixture> CreateAsync(AppDbContext db)
        {
            var (customerAccount, customer) = await TestSupport.AddCustomerAsync(db);
            var (managerAccount, manager) = await TestSupport.AddLogisticsManagerAsync(db);
            var place = new Location { LocationId = Guid.NewGuid(), LocationName = "Roadside", Country = "FR", CreatedAt = Created, UpdatedAt = Created };
            var other = new Location { LocationId = Guid.NewGuid(), LocationName = "Port", Country = "GB", CreatedAt = Created, UpdatedAt = Created };
            var route = NewRoute(place, other);
            var vehicle = new Vehicle { VehicleId = Guid.NewGuid(), VehicleCode = "VEH-0003", LicensePlate = "51C-123.45", TransportMode = TransportMode.ROAD, CreatedAt = Created, UpdatedAt = Created };
            var horse = new Horse { HorseId = Guid.NewGuid(), CustomerId = customer.CustomerId, Customer = customer, HorseName = "Pegasus", CreatedAt = Created, UpdatedAt = Created };
            db.AddRange(place, other, route, vehicle, horse);
            await db.SaveChangesAsync();
            return new() { Context = db, ManagerAccount = managerAccount, Manager = manager, CustomerAccount = customerAccount, Customer = customer, Place = place, Route = route, Vehicle = vehicle, Horse = horse };
        }

        internal static Route NewRoute(Location from, Location to) => new()
        {
            RouteId = Guid.NewGuid(), RouteCode = "ROUTE-" + Guid.NewGuid().ToString("N"), TransportMode = TransportMode.ROAD, FromLocation = from, ToLocation = to,
            CreatedAt = Created, UpdatedAt = Created
        };

        internal TransportProvider AddProvider(string name)
        {
            var provider = new TransportProvider { TransportProviderId = Guid.NewGuid(), ProviderCode = "TP-" + Guid.NewGuid().ToString("N")[..8], ProviderName = name, CreatedAt = Created, UpdatedAt = Created };
            Context.Add(provider);
            return provider;
        }

        /// <summary>A trip with its booking and executed plan; the optional pieces create the primary route plan, one segment and a signed DELIVERY handover.</summary>
        internal Transport AddTransport(string? completedAt, string? plannedEnd = null, string? arrival = null, string? departure = null, int? planMinutes = null, int? routeMinutes = null,
            string origin = "France", string destination = "United Kingdom", TransportProvider? provider = null, Employee? driver = null, Route? route = null,
            string? deliveredAt = null, TransportStatus status = TransportStatus.COMPLETED)
        {
            var booking = new Booking { BookingId = Guid.NewGuid(), BookingCode = "BK-" + Guid.NewGuid().ToString("N"), CustomerId = Customer.CustomerId, Customer = Customer,
                Status = BookingStatus.APPROVED, OriginAddress = "A", OriginCountry = origin, DestinationAddress = "B", DestinationCountry = destination,
                RequestedDepartureDate = new DateOnly(2026, 10, 1), CreatedAt = Created, UpdatedAt = Created };
            var transport = new Transport { TransportId = Guid.NewGuid(), TransportCode = $"TR-{++counter:0000}", BookingId = booking.BookingId, Booking = booking, Status = status,
                ActualDepartureAt = departure is null ? null : D(departure), ActualArrivalAt = arrival is null ? null : D(arrival), CompletedAt = completedAt is null ? null : D(completedAt),
                CreatedAt = Created, UpdatedAt = Created };
            var plan = new TransportPlan { TransportPlanId = Guid.NewGuid(), Transport = transport, PlanCode = "TPL-" + Guid.NewGuid().ToString("N"), Status = TransportPlanStatus.COMPLETED,
                PlannedEndAt = plannedEnd is null ? null : D(plannedEnd), EstimatedDurationMinutes = planMinutes, CreatedAt = Created, UpdatedAt = Created };
            Context.AddRange(booking, transport, plan);
            if (routeMinutes is not null || provider is not null || driver is not null || route is not null)
            {
                var routePlan = new RoutePlan { RoutePlanId = Guid.NewGuid(), TransportPlan = plan, RoutePlanCode = "RP-" + Guid.NewGuid().ToString("N"), Status = RoutePlanStatus.COMPLETED,
                    EstimatedDurationMinutes = routeMinutes, CreatedAt = Created, UpdatedAt = Created };
                Context.AddRange(routePlan, new RoutePlanRoute { RoutePlanRouteId = Guid.NewGuid(), RoutePlan = routePlan, Route = route ?? Route, SequenceNo = 1, RouteType = RouteType.PRIMARY,
                    TransportProviderId = provider?.TransportProviderId, DriverEmployeeId = driver?.EmployeeId, CreatedAt = Created, UpdatedAt = Created });
            }
            if (deliveredAt is not null) Delivery(transport, deliveredAt);
            return transport;
        }

        private HandoverRecord Delivery(Transport transport, string completedAt)
        {
            if (handovers.TryGetValue(transport.TransportId, out var existing)) return existing;
            var handover = new HandoverRecord { HandoverRecordId = Guid.NewGuid(), TransportId = transport.TransportId, Transport = transport, HandoverCode = "HO-" + Guid.NewGuid().ToString("N"),
                SequenceNo = 1, HandoverType = HandoverType.DELIVERY, Status = HandoverStatus.COMPLETED, FromType = HandoverPartyType.EMPLOYEE, ToType = HandoverPartyType.CUSTOMER,
                CompletedAt = D(completedAt), CreatedAt = Created, UpdatedAt = Created };
            Context.Add(handover);
            return handovers[transport.TransportId] = handover;
        }

        internal IncidentReport AddIncident(Transport? transport, string occurredAt, IncidentCategory category, IncidentSeverity severity, IncidentStatus status = IncidentStatus.REPORTED,
            VehicleIncidentType? vehicleType = null, bool repair = false, HorseIncidentType? horseType = null)
        {
            var at = D(occurredAt);
            var incident = new IncidentReport { IncidentReportId = Guid.NewGuid(), TransportId = transport?.TransportId, IncidentCode = $"INC-{++counter:00000}", EmployeeId = Manager.EmployeeId,
                LocationId = Place.LocationId, Category = category, Severity = severity, Status = status, Title = "Incident", Description = "Incident", OccurredAt = at, ReportedAt = at,
                CreatedAt = at, UpdatedAt = at };
            Context.Add(incident);
            if (vehicleType is { } vehicleKind)
                Context.Add(new VehicleIncidentDetail { VehicleIncidentDetailId = Guid.NewGuid(), IncidentReportId = incident.IncidentReportId, VehicleId = Vehicle.VehicleId,
                    IncidentType = vehicleKind, RepairRequired = repair, CreatedAt = at, UpdatedAt = at });
            if (horseType is { } horseKind)
                Context.Add(new HorseHealthIncidentDetail { HorseHealthIncidentDetailId = Guid.NewGuid(), IncidentReportId = incident.IncidentReportId, HorseId = Horse.HorseId,
                    IncidentType = horseKind, ConditionDescription = "Condition", CreatedAt = at, UpdatedAt = at });
            return incident;
        }

        internal EmergencyCostRequest AddCost(IncidentReport incident, EmergencyCostCategory category, decimal amount, EmergencyCostStatus status = EmergencyCostStatus.APPROVED)
        {
            var cost = new EmergencyCostRequest { EmergencyCostRequestId = Guid.NewGuid(), IncidentReportId = incident.IncidentReportId, RequestedByEmployeeId = Manager.EmployeeId,
                RequestCode = "ECR-" + Guid.NewGuid().ToString("N")[..8], Category = category, Status = status, Description = "Cost", Justification = "Needed", Currency = "VND",
                RequestedAmount = amount, ApprovedAmount = status == EmergencyCostStatus.APPROVED ? amount : null, CreatedAt = incident.OccurredAt, UpdatedAt = incident.OccurredAt };
            Context.Add(cost);
            return cost;
        }

        internal Claim AddClaim(Transport transport, string submittedAt, ClaimType type, ClaimStatus status, string code)
        {
            var at = D(submittedAt);
            var claim = new Claim { ClaimId = Guid.NewGuid(), ClaimCode = code, CustomerId = Customer.CustomerId, HandoverRecordId = Delivery(transport, submittedAt).HandoverRecordId,
                ClaimType = type, Status = status, Title = "Claim title", Description = "Claim description", SubmittedAt = at, CreatedAt = at, UpdatedAt = at };
            Context.Add(claim);
            return claim;
        }
    }

    private static async Task<T> ThrowsAsync<T>(Task task, string errorCode) where T : Exceptions.Exceptions
    {
        var error = await Assert.ThrowsAsync<T>(() => task);
        Assert.Equal(errorCode, error.ErrorCode);
        return error;
    }

    private static async Task AssertFieldsAsync(Task task, params string[] fields)
    {
        var error = await Assert.ThrowsAsync<FieldValidationException>(() => task);
        foreach (var field in fields) Assert.Contains(error.Errors, item => item.Field == field);
    }

    // ---------------------------------------------------------------- RA1

    /// <summary>October holds eight completed trips and September two; the comments say how each one is judged.</summary>
    internal sealed record OnTimeScenario(Fixture F, TransportProvider Provider, Employee Driver, Route OtherRoute, Transport OnTime30, Transport OnTime60, Transport Minor, Transport Major,
        Transport NoPlan, Transport Fallback, Transport NoArrival, Transport Early);

    internal static async Task<OnTimeScenario> SeedOnTimeAsync(AppDbContext db)
    {
        var f = await Fixture.CreateAsync(db);
        var provider = f.AddProvider("Eurotunnel");
        var driver = await PersonnelAssignmentTests.EmployeeAsync(db, EmployeeRole.DRIVER);
        var otherRoute = Fixture.NewRoute(f.Place, f.Place);
        db.Add(otherRoute);
        var onTime30 = f.AddTransport("2026-10-03 12:30", "2026-10-03 12:00", "2026-10-03 12:30");
        var onTime60 = f.AddTransport("2026-10-04 13:00", "2026-10-04 12:00", "2026-10-04 13:00", route: otherRoute);
        var minor = f.AddTransport("2026-10-12 14:05", "2026-10-12 12:00", "2026-10-12 14:05", driver: driver);
        var major = f.AddTransport("2026-10-20 15:00", "2026-10-20 12:00", "2026-10-20 15:00", destination: "Germany", provider: provider);
        var noPlan = f.AddTransport("2026-10-21 12:00", null, "2026-10-21 12:00");
        var fallback = f.AddTransport("2026-10-22 12:15", "2026-10-22 12:00", null, deliveredAt: "2026-10-22 12:15");
        var noArrival = f.AddTransport("2026-10-23 12:00", "2026-10-23 12:00", null);
        var early = f.AddTransport("2026-10-25 10:00", "2026-10-25 12:00", "2026-10-25 10:00");
        f.AddTransport("2026-09-10 12:10", "2026-09-10 12:00", "2026-09-10 12:10");
        f.AddTransport("2026-09-12 13:40", "2026-09-12 12:00", "2026-09-12 13:40");
        f.AddTransport("2026-11-02 12:00", "2026-11-02 12:00", "2026-11-02 12:00");
        f.AddTransport("2026-10-15 12:00", "2026-10-15 12:00", "2026-10-15 12:00", status: TransportStatus.IN_TRANSIT);
        // The worst incident (highest severity) names the cause of a delay, whatever else happened on the trip.
        f.AddIncident(minor, "2026-10-12 09:00", IncidentCategory.HORSE_HEALTH, IncidentSeverity.LOW);
        f.AddIncident(minor, "2026-10-12 10:00", IncidentCategory.BOTH, IncidentSeverity.HIGH);
        f.AddIncident(major, "2026-10-20 09:00", IncidentCategory.VEHICLE, IncidentSeverity.CRITICAL, IncidentStatus.CANCELLED);
        await db.SaveChangesAsync();
        return new(f, provider, driver, otherRoute, onTime30, onTime60, minor, major, noPlan, fallback, noArrival, early);
    }

    [Fact]
    public async Task OnTime_WorksOutTheKpisTheBucketsTheTrendAndTheLateTable()
    {
        await using var db = TestSupport.CreateDb(); var s = await SeedOnTimeAsync(db);

        var report = await s.F.Service.GetOnTimeAsync(s.F.ManagerAccount.AccountId, OnTime());

        Assert.Equal(new Reports.Response.Period(new(2026, 10, 1), new(2026, 10, 31), new(2026, 8, 31), new(2026, 9, 30)), report.Period);
        Assert.Equal(Now, report.GeneratedAt);
        // 30, 60, 125, 180, 15 (arrival from the handover) and -120 minutes: six trips, four of them within 60.
        Assert.Equal((6L, 2L, 4L), (report.Kpi.TotalTransports.Value, report.Kpi.TotalTransports.Previous, report.Kpi.TotalTransports.Change));
        Assert.Equal((66.7m, 50.0m, 16.7m), (report.Kpi.OnTimeRate.Value, report.Kpi.OnTimeRate.Previous, report.Kpi.OnTimeRate.Change));
        Assert.Equal((48.3m, 55.0m, -6.7m), (report.Kpi.AvgDelayMinutes.Value, report.Kpi.AvgDelayMinutes.Previous, report.Kpi.AvgDelayMinutes.Change));
        Assert.Equal(92, report.Kpi.SlaTargetRate); Assert.False(report.Kpi.MeetsSla);
        Assert.Equal(new[] { 4, 1, 1 }, report.Distribution.Values);
        Assert.Equal(new[] { "ON_TIME", "MINOR_DELAY", "MAJOR_DELAY" }, report.Distribution.Keys);
        Assert.Equal(2, report.Excluded.Count);
        Assert.Equal((1, 1), (report.Excluded.Reasons["MISSING_PLANNED_END"], report.Excluded.Reasons["MISSING_ACTUAL_ARRIVAL"]));

        Assert.Equal(new[] { "2026-W40", "2026-W41", "2026-W42", "2026-W43", "2026-W44" }, report.Trend.Select(item => item.Label));
        Assert.Equal(new DateOnly(2026, 9, 28), report.Trend[0].From); Assert.Equal(new DateOnly(2026, 10, 4), report.Trend[0].To);
        Assert.Equal(new[] { 2, 0, 1, 3, 0 }, report.Trend.Select(item => item.Total));
        Assert.Equal(new decimal?[] { 100.0m, null, 0.0m, 66.7m, null }, report.Trend.Select(item => item.OnTimeRate));

        // A trip with a partner counts under that partner only; the rest are the internal fleet.
        Assert.Equal(("Eurotunnel", 1, 0.0m), (report.ByProvider[0].ProviderName, report.ByProvider[0].Total, report.ByProvider[0].OnTimeRate));
        Assert.Equal((null, "Internal fleet", 5, 80.0m), (report.ByProvider[1].TransportProviderId, report.ByProvider[1].ProviderName, report.ByProvider[1].Total, report.ByProvider[1].OnTimeRate));
        // The cancelled critical incident of the major delay is ignored, so its cause is unknown.
        Assert.Equal(new[] { ("UNKNOWN", 1), ("VEHICLE_INCIDENT", 1) }, report.DelayCauses.Select(item => (item.Cause, item.Count)));

        Assert.Equal(2, report.TotalItems); Assert.Equal(1, report.TotalPages);
        Assert.Equal(new[] { s.Major.TransportCode, s.Minor.TransportCode }, report.Items.Select(item => item.TransportCode));
        var minor = report.Items[1];
        Assert.Equal((125, "MINOR_DELAY", "VEHICLE_INCIDENT"), (minor.DelayMinutes, minor.Bucket, minor.MainCause));
        Assert.Equal(new DateTimeOffset(2026, 10, 12, 12, 0, 0, TimeSpan.Zero), minor.PlannedEndAt);
        Assert.Equal(("France", "United Kingdom"), (minor.OriginCountry, minor.DestinationCountry));
    }

    [Fact]
    public async Task OnTime_BucketFilterOnlyNarrowsTheTable_AndTheTableCanBeSortedAndPaged()
    {
        await using var db = TestSupport.CreateDb(); var s = await SeedOnTimeAsync(db);
        var request = OnTime(); request.Bucket = "on_time"; request.PageSize = 2;

        var first = await s.F.Service.GetOnTimeAsync(s.F.ManagerAccount.AccountId, request);
        request.PageIndex = 2; var second = await s.F.Service.GetOnTimeAsync(s.F.ManagerAccount.AccountId, request);
        request.PageIndex = 1; request.SortBy = "completedAt:asc";
        var oldest = await s.F.Service.GetOnTimeAsync(s.F.ManagerAccount.AccountId, request);

        Assert.Equal((4, 2, 2), (first.TotalItems, first.TotalPages, first.Items.Count));
        Assert.Equal(new[] { 60, 30 }, first.Items.Select(item => item.DelayMinutes)); Assert.Equal(new[] { 15, -120 }, second.Items.Select(item => item.DelayMinutes));
        Assert.All(first.Items.Concat(second.Items), item => { Assert.Equal("ON_TIME", item.Bucket); Assert.Null(item.MainCause); });
        Assert.Equal(s.OnTime30.TransportCode, oldest.Items[0].TransportCode);
        Assert.Equal(6, first.Kpi.TotalTransports.Value); // the KPIs ignore the bucket filter
    }

    [Fact]
    public async Task OnTime_MonthlyTrendHasOneBucketPerMonth()
    {
        await using var db = TestSupport.CreateDb(); var s = await SeedOnTimeAsync(db);
        var request = OnTime(); request.DateFrom = new(2026, 9, 1); request.TrendBy = "month";

        var report = await s.F.Service.GetOnTimeAsync(s.F.ManagerAccount.AccountId, request);

        Assert.Equal(new[] { ("2026-09", 2), ("2026-10", 6) }, report.Trend.Select(item => (item.Label, item.Total)));
        Assert.Equal(new DateOnly(2026, 10, 31), report.Trend[1].To);
    }

    [Fact]
    public async Task OnTime_FiltersNarrowTheTripsThatAreCounted()
    {
        await using var db = TestSupport.CreateDb(); var s = await SeedOnTimeAsync(db);
        async Task<Reports.Response.OnTimeReport> Run(Action<Reports.Request.OnTimeRequest> change)
        {
            var request = OnTime(); change(request);
            return await s.F.Service.GetOnTimeAsync(s.F.ManagerAccount.AccountId, request);
        }

        Assert.Equal(1, (await Run(r => r.DestinationCountry = " germany ")).Kpi.TotalTransports.Value);
        Assert.Equal(6, (await Run(r => r.OriginCountry = "FRANCE")).Kpi.TotalTransports.Value);
        Assert.Equal(0, (await Run(r => r.OriginCountry = "Spain")).Kpi.TotalTransports.Value);
        var byProvider = await Run(r => r.TransportProviderId = s.Provider.TransportProviderId);
        Assert.Equal((1L, 1), (byProvider.Kpi.TotalTransports.Value, byProvider.Items.Count)); Assert.Equal(s.Major.TransportCode, byProvider.Items[0].TransportCode);
        var byDriver = await Run(r => r.DriverEmployeeId = s.Driver.EmployeeId);
        Assert.Equal(s.Minor.TransportCode, Assert.Single(byDriver.Items).TransportCode);
        var byRoute = await Run(r => { r.RouteId = s.OtherRoute.RouteId; r.Bucket = "ON_TIME"; });
        Assert.Equal(s.OnTime60.TransportCode, Assert.Single(byRoute.Items).TransportCode); Assert.Equal(1, byRoute.Kpi.TotalTransports.Value);
    }

    [Fact]
    public async Task OnTime_NothingInThePeriodIsAnEmptyReportNotAnError()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);

        var report = await f.Service.GetOnTimeAsync(f.ManagerAccount.AccountId, OnTime());

        Assert.Equal((0L, 0L, null), (report.Kpi.TotalTransports.Value, report.Kpi.TotalTransports.Previous, report.Kpi.TotalTransports.Change));
        Assert.Null(report.Kpi.OnTimeRate.Value); Assert.Null(report.Kpi.AvgDelayMinutes.Value); Assert.False(report.Kpi.MeetsSla);
        Assert.Empty(report.Items); Assert.Empty(report.DelayCauses); Assert.Empty(report.ByProvider); Assert.Equal(0, report.TotalItems);
        Assert.Equal(0, report.Excluded.Count);
    }

    [Fact]
    public async Task OnTime_MeetsTheSlaWhenTheRateReachesTheTarget()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        for (var index = 0; index < 12; index++) f.AddTransport("2026-10-05 12:00", "2026-10-05 12:00", "2026-10-05 12:00");
        await db.SaveChangesAsync();

        var report = await f.Service.GetOnTimeAsync(f.ManagerAccount.AccountId, OnTime());

        Assert.Equal(100.0m, report.Kpi.OnTimeRate.Value); Assert.True(report.Kpi.MeetsSla);
    }

    // ---------------------------------------------------------------- RA2

    internal sealed record IncidentScenario(Fixture F, Transport A, Transport B, Transport C, IncidentReport Breakdown, IncidentReport Fever, IncidentReport Both);

    internal static async Task<IncidentScenario> SeedIncidentsAsync(AppDbContext db)
    {
        var f = await Fixture.CreateAsync(db);
        var a = f.AddTransport("2026-10-05 12:00", "2026-10-05 12:00", "2026-10-05 12:00");
        var b = f.AddTransport("2026-10-10 12:00", "2026-10-10 12:00", "2026-10-10 12:00", destination: "Germany");
        var c = f.AddTransport("2026-10-16 12:00", "2026-10-16 12:00", "2026-10-16 12:00");
        f.AddTransport("2026-10-20 12:00", "2026-10-20 12:00", "2026-10-20 12:00");
        var e = f.AddTransport("2026-09-15 12:00", "2026-09-15 12:00", "2026-09-15 12:00");
        var breakdown = f.AddIncident(a, "2026-10-05 08:00", IncidentCategory.VEHICLE, IncidentSeverity.HIGH, vehicleType: VehicleIncidentType.BREAKDOWN, repair: true);
        f.AddCost(breakdown, EmergencyCostCategory.VEHICLE_REPAIR, 3_500_000);
        var fever = f.AddIncident(a, "2026-10-06 08:00", IncidentCategory.HORSE_HEALTH, IncidentSeverity.MEDIUM, horseType: HorseIncidentType.FEVER);
        f.AddCost(fever, EmergencyCostCategory.VETERINARY, 5_000_000); f.AddCost(fever, EmergencyCostCategory.MEDICATION, 1_000_000, EmergencyCostStatus.REJECTED);
        var both = f.AddIncident(b, "2026-10-10 08:00", IncidentCategory.BOTH, IncidentSeverity.CRITICAL, vehicleType: VehicleIncidentType.ACCIDENT, horseType: HorseIncidentType.INJURY);
        f.AddIncident(b, "2026-10-11 08:00", IncidentCategory.VEHICLE, IncidentSeverity.CRITICAL, IncidentStatus.CANCELLED, VehicleIncidentType.OTHER);
        f.AddIncident(e, "2026-09-20 08:00", IncidentCategory.VEHICLE, IncidentSeverity.LOW, vehicleType: VehicleIncidentType.OTHER);
        f.AddIncident(null, "2026-10-12 08:00", IncidentCategory.ROUTE_CHANGE, IncidentSeverity.LOW);
        f.AddIncident(a, "2026-11-02 08:00", IncidentCategory.VEHICLE, IncidentSeverity.LOW);
        f.AddClaim(a, "2026-10-15 09:00", ClaimType.DAMAGE, ClaimStatus.PENDING, "CLM-A");
        f.AddClaim(c, "2026-10-16 09:00", ClaimType.DELAY, ClaimStatus.RESOLVED, "CLM-C");
        f.AddClaim(a, "2026-10-17 09:00", ClaimType.LOSS, ClaimStatus.CANCELLED, "CLM-X");
        f.AddClaim(e, "2026-09-22 09:00", ClaimType.DELAY, ClaimStatus.PENDING, "CLM-E");
        await db.SaveChangesAsync();
        return new(f, a, b, c, breakdown, fever, both);
    }

    [Fact]
    public async Task IncidentClaim_CountsTheRatesTheBreakdownsAndTheCosts()
    {
        await using var db = TestSupport.CreateDb(); var s = await SeedIncidentsAsync(db);

        var report = await s.F.Service.GetIncidentClaimAsync(s.F.ManagerAccount.AccountId, IncidentClaim());

        // October: four trips; incidents on A and B (the incident without a trip still counts); claims on A and C; cancelled ones never count.
        Assert.Equal((4L, 1L, 3L), (report.Kpi.CompletedTransports.Value, report.Kpi.CompletedTransports.Previous, report.Kpi.CompletedTransports.Change));
        Assert.Equal((4L, 1L, 3L), (report.Kpi.TotalIncidents.Value, report.Kpi.TotalIncidents.Previous, report.Kpi.TotalIncidents.Change));
        Assert.Equal((50.0m, 100.0m, -50.0m), (report.Kpi.IncidentRate.Value, report.Kpi.IncidentRate.Previous, report.Kpi.IncidentRate.Change));
        Assert.Equal((2L, 1L, 1L), (report.Kpi.TotalClaims.Value, report.Kpi.TotalClaims.Previous, report.Kpi.TotalClaims.Change));
        Assert.Equal((50.0m, 100.0m, -50.0m), (report.Kpi.ClaimRate.Value, report.Kpi.ClaimRate.Previous, report.Kpi.ClaimRate.Change));
        Assert.Equal((8_500_000L, 0L, 8_500_000L), (report.Kpi.TotalEmergencyCost.Value, report.Kpi.TotalEmergencyCost.Previous, report.Kpi.TotalEmergencyCost.Change));
        Assert.Equal("VND", report.Kpi.Currency);

        Assert.Equal(new[] { ("VEHICLE", 1), ("HORSE_HEALTH", 1), ("BOTH", 1), ("ROUTE_CHANGE", 1) }, report.IncidentsByCategory.Select(item => (item.Category, item.Count)));
        Assert.Equal(new[] { 1, 1, 1, 1 }, report.IncidentsBySeverity.Values); Assert.Equal(new[] { "LOW", "MEDIUM", "HIGH", "CRITICAL" }, report.IncidentsBySeverity.Keys);
        Assert.Equal(new[] { ("ACCIDENT", 1), ("BREAKDOWN", 1), ("FEVER", 1), ("INJURY", 1) }, report.IncidentsByType.Select(item => (item.IncidentType, item.Count)));
        Assert.Equal(new[] { ("DAMAGE", 1), ("DELAY", 1) }, report.ClaimsByType.Select(item => (item.ClaimType, item.Count)));
        Assert.Equal(new[] { 1, 0, 0, 1, 0 }, report.ClaimsByStatus.Values); Assert.Equal(new[] { "PENDING", "APPROVED", "REJECTED", "RESOLVED", "CANCELLED" }, report.ClaimsByStatus.Keys);
        Assert.Equal(new[] { ("VETERINARY", 5_000_000L), ("VEHICLE_REPAIR", 3_500_000L) }, report.EmergencyCostByCategory.Select(item => (item.Category, item.Amount)));
        var vehicle = Assert.Single(report.VehicleReliability);
        Assert.Equal((s.F.Vehicle.VehicleCode, "51C-123.45", 2, 1), (vehicle.VehicleCode, vehicle.LicensePlate, vehicle.IncidentCount, vehicle.RepairRequiredCount));

        Assert.Equal(4, report.TotalItems);
        Assert.Equal(new[] { "2026-10-12", "2026-10-10", "2026-10-06", "2026-10-05" }, report.Items.Select(item => item.OccurredAt.ToString("yyyy-MM-dd")));
        var fever = report.Items[2];
        Assert.Equal((s.Fever.IncidentCode, "HORSE_HEALTH", "FEVER", "MEDIUM", "REPORTED", s.A.TransportCode, 5_000_000L), (fever.IncidentCode, fever.Category, fever.IncidentType, fever.Severity,
            fever.Status, fever.TransportCode, fever.EmergencyCost));
        Assert.Equal(new[] { "CLM-A" }, fever.ClaimCodes);
        Assert.Equal(("ACCIDENT", 0L), (report.Items[1].IncidentType, report.Items[1].EmergencyCost)); Assert.Empty(report.Items[1].ClaimCodes);
        Assert.Null(report.Items[0].TransportCode); Assert.Null(report.Items[0].IncidentType);
    }

    [Fact]
    public async Task IncidentClaim_FiltersNarrowTheIncidentsAndTheClaims()
    {
        await using var db = TestSupport.CreateDb(); var s = await SeedIncidentsAsync(db);
        async Task<Reports.Response.IncidentClaimReport> Run(Action<Reports.Request.IncidentClaimRequest> change)
        {
            var request = IncidentClaim(); change(request);
            return await s.F.Service.GetIncidentClaimAsync(s.F.ManagerAccount.AccountId, request);
        }

        Assert.Equal(2L, (await Run(r => r.Severity = "high,critical")).Kpi.TotalIncidents.Value);
        Assert.Equal(1L, (await Run(r => r.IncidentCategory = "HORSE_HEALTH")).Kpi.TotalIncidents.Value);
        Assert.Equal(2L, (await Run(r => r.VehicleId = s.F.Vehicle.VehicleId)).Kpi.TotalIncidents.Value);
        Assert.Equal(2L, (await Run(r => r.HorseId = s.F.Horse.HorseId)).Kpi.TotalIncidents.Value);
        var delay = await Run(r => r.ClaimType = "DELAY");
        Assert.Equal((1L, 25.0m), (delay.Kpi.TotalClaims.Value, delay.Kpi.ClaimRate.Value));
        // A country filter keeps only the trips (and their incidents) of that route; the incident without a trip drops out.
        var germany = await Run(r => r.DestinationCountry = "Germany");
        Assert.Equal((1L, 1L, 0L), (germany.Kpi.CompletedTransports.Value, germany.Kpi.TotalIncidents.Value, germany.Kpi.TotalClaims.Value));
        Assert.Equal(s.Both.IncidentCode, Assert.Single(germany.Items).IncidentCode);
        Assert.Equal(new[] { "2026-10-05", "2026-10-06" }, (await Run(r => { r.SortBy = "occurredAt:asc"; r.Severity = "HIGH,MEDIUM"; })).Items.Select(item => item.OccurredAt.ToString("yyyy-MM-dd")));
    }

    [Fact]
    public async Task IncidentClaim_AnEmptyPeriodIsAnEmptyReport()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);

        var report = await f.Service.GetIncidentClaimAsync(f.ManagerAccount.AccountId, IncidentClaim());

        Assert.Equal((0L, 0L, null), (report.Kpi.CompletedTransports.Value, report.Kpi.TotalIncidents.Value, report.Kpi.IncidentRate.Value));
        Assert.Null(report.Kpi.TotalIncidents.Change); Assert.Empty(report.Items); Assert.Empty(report.ClaimsByType); Assert.Empty(report.VehicleReliability);
        Assert.Equal(4, report.IncidentsByCategory.Count); Assert.Equal(5, report.ClaimsByStatus.Count);
    }

    // ---------------------------------------------------------------- RA3

    internal sealed record TransitScenario(Fixture F, Transport OnEstimate, Transport TwoDaysLate, Transport OneDayEarly, Transport Late);

    internal static async Task<TransitScenario> SeedTransitAsync(AppDbContext db)
    {
        var f = await Fixture.CreateAsync(db);
        // 3.0 days against 3.0 estimated, 4.0 against 2.0 (transport plan), 3.0 against 4.0 (route plan wins over the plan's 2000), 4.25 against 2.0.
        var onEstimate = f.AddTransport("2026-10-04 06:00", "2026-10-04 06:00", "2026-10-04 06:00", "2026-10-01 06:00", routeMinutes: 4320);
        var twoDays = f.AddTransport("2026-10-09 06:00", "2026-10-09 06:00", "2026-10-09 06:00", "2026-10-05 06:00", planMinutes: 2880);
        var early = f.AddTransport("2026-10-13 06:00", "2026-10-13 06:00", "2026-10-13 06:00", "2026-10-10 06:00", planMinutes: 2000, routeMinutes: 5760);
        var late = f.AddTransport("2026-10-19 12:00", "2026-10-19 12:00", "2026-10-19 12:00", "2026-10-15 06:00", planMinutes: 2880, destination: "Germany");
        f.AddTransport("2026-10-20 12:00", "2026-10-20 12:00", "2026-10-20 12:00", null, planMinutes: 2880);
        f.AddTransport("2026-10-21 12:00", "2026-10-21 12:00", "2026-10-21 12:00", "2026-10-18 12:00");
        f.AddTransport("2026-10-22 12:00", "2026-10-22 12:00", null, "2026-10-19 12:00", planMinutes: 2880);
        f.AddTransport("2026-09-07 06:00", "2026-09-07 06:00", "2026-09-07 06:00", "2026-09-05 06:00", planMinutes: 1440);
        await db.SaveChangesAsync();
        return new(f, onEstimate, twoDays, early, late);
    }

    [Fact]
    public async Task Transit_WorksOutTheKpisTheDistributionAndTheRouteBenchmark()
    {
        await using var db = TestSupport.CreateDb(); var s = await SeedTransitAsync(db);

        var report = await s.F.Service.GetTransitTimeAsync(s.F.ManagerAccount.AccountId, Transit());

        Assert.Equal((4L, 1L, 3L), (report.Kpi.TotalTransports.Value, report.Kpi.TotalTransports.Previous, report.Kpi.TotalTransports.Change));
        Assert.Equal((3.6m, 2.0m, 1.6m), (report.Kpi.AvgActualDays.Value, report.Kpi.AvgActualDays.Previous, report.Kpi.AvgActualDays.Change));
        Assert.Equal((0.8m, 1.0m, -0.2m), (report.Kpi.AvgDelayDays.Value, report.Kpi.AvgDelayDays.Previous, report.Kpi.AvgDelayDays.Change));
        Assert.Equal((129.5m, 200.0m, -70.5m), (report.Kpi.AccuracyRatio.Value, report.Kpi.AccuracyRatio.Previous, report.Kpi.AccuracyRatio.Change));
        Assert.Equal(new[] { ("<=-1", 1), ("(-1,0]", 1), ("(0,1]", 0), ("(1,2]", 1), (">2", 1) }, report.Distribution.Select(item => (item.Range, item.Count)));
        Assert.Equal(3, report.Excluded.Count);
        Assert.Equal((1, 1, 1), (report.Excluded.Reasons["MISSING_ACTUAL_DEPARTURE"], report.Excluded.Reasons["MISSING_ACTUAL_ARRIVAL"], report.Excluded.Reasons["MISSING_ESTIMATE"]));
        Assert.Equal(new[] { ("France", "United Kingdom", 3, 3.3m, 3.0m, 0.3m), ("France", "Germany", 1, 4.3m, 2.0m, 2.3m) },
            report.RouteBenchmark.Select(item => (item.OriginCountry, item.DestinationCountry, item.Total, item.AvgActualDays, item.AvgEstimatedDays, item.AvgDeltaDays)));
        Assert.Equal(new[] { s.Late.TransportCode, s.TwoDaysLate.TransportCode, s.OnEstimate.TransportCode, s.OneDayEarly.TransportCode }, report.Items.Select(item => item.TransportCode));
        Assert.Equal((2.0m, 4.3m, 2.3m), (report.Items[0].EstimatedDays, report.Items[0].ActualDays, report.Items[0].DeltaDays));
        Assert.Equal(new DateTimeOffset(2026, 10, 15, 6, 0, 0, TimeSpan.Zero), report.Items[0].ActualDepartureAt);
    }

    [Fact]
    public async Task Transit_DelayFilterAndSortOnlyChangeTheTable()
    {
        await using var db = TestSupport.CreateDb(); var s = await SeedTransitAsync(db);
        var request = Transit(); request.DelayFilter = "late_over_1_day";
        var late = await s.F.Service.GetTransitTimeAsync(s.F.ManagerAccount.AccountId, request);
        request.DelayFilter = "EARLY"; var early = await s.F.Service.GetTransitTimeAsync(s.F.ManagerAccount.AccountId, request);
        request.DelayFilter = null; request.SortBy = "actualDays:asc";
        var shortest = await s.F.Service.GetTransitTimeAsync(s.F.ManagerAccount.AccountId, request);

        Assert.Equal(new[] { s.Late.TransportCode, s.TwoDaysLate.TransportCode }, late.Items.Select(item => item.TransportCode));
        Assert.Equal(s.OneDayEarly.TransportCode, Assert.Single(early.Items).TransportCode);
        Assert.Equal(4, late.Kpi.TotalTransports.Value);
        Assert.Contains(shortest.Items[0].TransportCode, new[] { s.OnEstimate.TransportCode, s.OneDayEarly.TransportCode }); // both took 3.0 days
        Assert.Equal(s.Late.TransportCode, shortest.Items[^1].TransportCode);
    }

    [Fact]
    public async Task Transit_ACountryFilterKeepsOnlyThatRoute()
    {
        await using var db = TestSupport.CreateDb(); var s = await SeedTransitAsync(db);
        var request = Transit(); request.DestinationCountry = "germany";

        var report = await s.F.Service.GetTransitTimeAsync(s.F.ManagerAccount.AccountId, request);

        Assert.Equal(1L, report.Kpi.TotalTransports.Value); Assert.Equal(s.Late.TransportCode, Assert.Single(report.Items).TransportCode);
    }

    // ---------------------------------------------------------------- access and validation

    [Fact]
    public async Task EveryReport_IsForLogisticsManagersOnly()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        var coordinator = await PersonnelAssignmentTests.EmployeeAsync(db, EmployeeRole.FLEET_ROUTE_COORDINATOR);
        var forbidden = "Only Logistics Manager can view management reports.";

        foreach (var accountId in new[] { f.CustomerAccount.AccountId, coordinator.AccountId, Guid.NewGuid() })
        {
            Assert.Equal(forbidden, (await Assert.ThrowsAsync<ForbiddenException>(() => f.Service.GetOnTimeAsync(accountId, OnTime()))).Message);
            await Assert.ThrowsAsync<ForbiddenException>(() => f.Service.GetIncidentClaimAsync(accountId, IncidentClaim()));
            await Assert.ThrowsAsync<ForbiddenException>(() => f.Service.GetTransitTimeAsync(accountId, Transit()));
            await Assert.ThrowsAsync<ForbiddenException>(() => f.Service.ExportAsync(accountId, "transit-time", new() { Format = "pdf", DateFrom = new(2026, 10, 1), DateTo = new(2026, 10, 31) }));
        }
    }

    [Fact]
    public async Task Validation_RejectsABadRangeAndBadFilters()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db); var id = f.ManagerAccount.AccountId;

        await AssertFieldsAsync(f.Service.GetOnTimeAsync(id, new()), "dateFrom", "dateTo");
        await AssertFieldsAsync(f.Service.GetOnTimeAsync(id, new() { DateFrom = new(2026, 10, 31), DateTo = new(2026, 10, 1) }), "dateTo");
        await AssertFieldsAsync(f.Service.GetIncidentClaimAsync(id, new() { DateFrom = new(2026, 1, 1), DateTo = new(2027, 1, 2) }), "dateTo");
        await f.Service.GetTransitTimeAsync(id, new() { DateFrom = new(2026, 1, 1), DateTo = new(2027, 1, 1) }); // exactly 366 days is allowed
        var onTime = OnTime(); onTime.Bucket = "LATE"; onTime.TrendBy = "DAY"; onTime.SortBy = "bad"; onTime.PageSize = 0; onTime.RouteId = Guid.Empty;
        await AssertFieldsAsync(f.Service.GetOnTimeAsync(id, onTime), "bucket", "trendBy", "sortBy", "pageSize", "routeId");
        var incident = IncidentClaim(); incident.IncidentCategory = "VEHICLE,NOPE"; incident.Severity = "9"; incident.ClaimType = "WRONG"; incident.SortBy = "code:asc";
        await AssertFieldsAsync(f.Service.GetIncidentClaimAsync(id, incident), "incidentCategory", "severity", "claimType", "sortBy");
        var transit = Transit(); transit.DelayFilter = "LATE"; transit.SortBy = "x";
        await AssertFieldsAsync(f.Service.GetTransitTimeAsync(id, transit), "delayFilter", "sortBy");
    }

    // ---------------------------------------------------------------- RA4

    private static Reports.Request.ExportRequest Export(string format) => new() { Format = format, DateFrom = new(2026, 10, 1), DateTo = new(2026, 10, 31) };

    private sealed record Cell(string Text, bool IsNumber, double Number);

    private static List<Cell> Cells(byte[] xlsx)
    {
        using var book = new XLWorkbook(new MemoryStream(xlsx));
        return book.Worksheet(1).CellsUsed().Select(cell => new Cell(cell.GetString(), cell.DataType == XLDataType.Number, cell.DataType == XLDataType.Number ? cell.GetDouble() : 0)).ToList();
    }

    [Fact]
    public async Task Export_OnTimeXlsxCarriesTheSameNumbersAsTheScreen()
    {
        await using var db = TestSupport.CreateDb(); var s = await SeedOnTimeAsync(db);

        var file = await s.F.Service.ExportAsync(s.F.ManagerAccount.AccountId, "on-time-delivery", Export("XLSX"));

        Assert.Equal("on-time-delivery-2026-10-01_2026-10-31.xlsx", file.FileName);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", file.ContentType);
        var cells = Cells(file.Content);
        Assert.Contains(cells, cell => cell.Text == "On-time delivery report");
        Assert.Contains(cells, cell => cell.Text == s.Minor.TransportCode);
        Assert.Contains(cells, cell => cell.IsNumber && cell.Number == 125);
        Assert.Contains(cells, cell => cell.IsNumber && cell.Number == 66.7);
    }

    [Fact]
    public async Task Export_EveryReportCanBeWrittenAsPdf()
    {
        await using var db = TestSupport.CreateDb(); var s = await SeedOnTimeAsync(db);
        await SeedIncidentsOnAsync(s.F);

        foreach (var type in new[] { "on-time-delivery", "incident-claim-frequency", "transit-time" })
        {
            var file = await s.F.Service.ExportAsync(s.F.ManagerAccount.AccountId, type, Export("pdf"));
            Assert.Equal("application/pdf", file.ContentType); Assert.Equal($"{type}-2026-10-01_2026-10-31.pdf", file.FileName);
            Assert.Equal("%PDF", Encoding.ASCII.GetString(file.Content, 0, 4));
        }
    }

    internal static async Task<IncidentReport> SeedIncidentsOnAsync(Fixture f)
    {
        var trip = f.AddTransport("2026-10-06 12:00", "2026-10-06 12:00", "2026-10-06 12:00", "2026-10-03 12:00", planMinutes: 2880);
        var incident = f.AddIncident(trip, "2026-10-04 08:00", IncidentCategory.BOTH, IncidentSeverity.HIGH, vehicleType: VehicleIncidentType.BREAKDOWN, horseType: HorseIncidentType.COLIC);
        f.AddCost(incident, EmergencyCostCategory.TOWING, 700_000); f.AddClaim(trip, "2026-10-07 08:00", ClaimType.DAMAGE, ClaimStatus.PENDING, "CLM-PDF");
        await f.Context.SaveChangesAsync();
        return incident;
    }

    [Fact]
    public async Task Export_IncidentAndTransitXlsxListTheirRows()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        await SeedIncidentsOnAsync(f);

        var incident = Cells((await f.Service.ExportAsync(f.ManagerAccount.AccountId, "incident-claim-frequency", Export("xlsx"))).Content);
        var transit = Cells((await f.Service.ExportAsync(f.ManagerAccount.AccountId, "transit-time", Export("xlsx"))).Content);

        Assert.Contains(incident, cell => cell.Text == "CLM-PDF");
        Assert.Contains(incident, cell => cell.IsNumber && cell.Number == 700000);
        Assert.Contains(transit, cell => cell.Text == "TR-0001");
    }

    [Fact]
    public async Task Export_RefusesAnUnknownTypeFormatOrFilterAndAnEmptyOrHugeResult()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db); var id = f.ManagerAccount.AccountId;

        await AssertFieldsAsync(f.Service.ExportAsync(id, "weekly", Export("xlsx")), "reportType");
        await AssertFieldsAsync(f.Service.ExportAsync(id, "transit-time", Export("csv")), "format");
        await AssertFieldsAsync(f.Service.ExportAsync(id, "transit-time", new() { Format = "pdf" }), "dateFrom", "dateTo");
        var bad = Export("pdf"); bad.Bucket = "ON_TIME"; bad.TrendBy = "DAY";
        await AssertFieldsAsync(f.Service.ExportAsync(id, "on-time-delivery", bad), "trendBy");
        foreach (var type in new[] { "on-time-delivery", "incident-claim-frequency", "transit-time" })
            await ThrowsAsync<UnprocessableEntityException>(f.Service.ExportAsync(id, type, Export("xlsx")), "NO_DATA_TO_EXPORT");

        var trip = f.AddTransport("2026-10-05 12:00", "2026-10-05 12:00", "2026-10-05 12:00");
        for (var index = 0; index < 10_001; index++) f.AddIncident(trip, "2026-10-05 08:00", IncidentCategory.ROUTE_CHANGE, IncidentSeverity.LOW);
        await db.SaveChangesAsync();
        var error = await ThrowsAsync<UnprocessableEntityException>(f.Service.ExportAsync(id, "incident-claim-frequency", Export("xlsx")), "EXPORT_TOO_LARGE");
        Assert.Equal("More than 10,000 rows match. Narrow the filters and try again", error.Message);
    }

    [Fact]
    public async Task Export_ATripsOnlyPeriodStillExportsWhenNoTripIsLate()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        f.AddTransport("2026-10-05 12:00", "2026-10-05 12:00", "2026-10-05 12:00");
        await db.SaveChangesAsync();

        var file = await f.Service.ExportAsync(f.ManagerAccount.AccountId, "on-time-delivery", Export("xlsx"));

        Assert.Contains(Cells(file.Content), cell => cell.Text == "Transports");
    }
}
