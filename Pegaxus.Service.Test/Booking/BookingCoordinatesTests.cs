using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;

using BookingRequest = Pegaxus.Service.BookingService.Request;
using BookingServiceImpl = Pegaxus.Service.BookingService.Service;
using TransportPlanRequest = Pegaxus.Service.TransportPlanService.Request;
using TransportPlanServiceImpl = Pegaxus.Service.TransportPlanService.Service;

namespace Pegaxus.Service.Test;

public class BookingCoordinatesTests
{
    private static BookingServiceImpl Bookings(AppDbContext db) =>
        new(db, TestSupport.CreateMailService(), TestSupport.CreateNotifications(db));

    private static TransportPlanServiceImpl Plans(AppDbContext db) => new(db, TestSupport.CreateNotifications(db));

    private static BookingRequest.CreateBookingRequest CreateRequest(Horse horse) => new()
    {
        OriginAddress = "Haras de Chantilly, Paris",
        OriginCountry = "France",
        OriginLatitude = 49.1943m,
        OriginLongitude = 2.4716m,
        DestinationAddress = "Berlin",
        DestinationCountry = "Germany",
        DestinationLatitude = 52.5200m,
        DestinationLongitude = 13.4050m,
        RequestedDepartureDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(21),
        Horses = [new BookingRequest.BookingHorseRequest { HorseId = horse.HorseId }]
    };

    private static async Task<(Account Account, Horse Horse)> CustomerWithHorseAsync(AppDbContext db)
    {
        var (account, customer) = await TestSupport.AddCustomerAsync(db);
        var horse = new Horse
        {
            HorseId = Guid.NewGuid(), CustomerId = customer.CustomerId, Customer = customer, HorseName = "Silver Arrow",
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        db.Horses.Add(horse);
        await db.SaveChangesAsync();
        return (account, horse);
    }

    [Fact]
    public async Task Create_RequiresAllFourCoordinates()
    {
        await using var db = TestSupport.CreateDb();
        var (account, horse) = await CustomerWithHorseAsync(db);
        var request = CreateRequest(horse);
        request.DestinationLongitude = null;

        var error = await Assert.ThrowsAsync<BadRequestException>(() => Bookings(db).CreateAsync(account.AccountId, request));

        Assert.Equal("VALIDATION_ERROR", error.ErrorCode);
        Assert.Empty(await db.Bookings.ToListAsync());
    }

    [Theory]
    [InlineData(90.1, 0, 0, 0)]
    [InlineData(-90.1, 0, 0, 0)]
    [InlineData(0, 180.1, 0, 0)]
    [InlineData(0, 0, 0, -180.1)]
    [InlineData(0, 0, 91, 0)]
    public async Task Create_RejectsCoordinatesOutsideTheEarth(double originLat, double originLon, double destinationLat, double destinationLon)
    {
        await using var db = TestSupport.CreateDb();
        var (account, horse) = await CustomerWithHorseAsync(db);
        var request = CreateRequest(horse);
        (request.OriginLatitude, request.OriginLongitude) = ((decimal)originLat, (decimal)originLon);
        (request.DestinationLatitude, request.DestinationLongitude) = ((decimal)destinationLat, (decimal)destinationLon);

        var error = await Assert.ThrowsAsync<BadRequestException>(() => Bookings(db).CreateAsync(account.AccountId, request));

        Assert.Equal("INVALID_COORDINATES", error.ErrorCode);
    }

    [Fact]
    public async Task Update_ChangesCoordinatesInPairsAndRecordsThem()
    {
        await using var db = TestSupport.CreateDb();
        var (account, customer) = await TestSupport.AddCustomerAsync(db);
        var (booking, _) = await TestSupport.AddPendingBookingAsync(db, customer);
        var service = Bookings(db);
        await service.ChangeStatusAsync(account.AccountId, booking.BookingId, new BookingRequest.ChangeStatusRequest { Status = "DRAFT" });

        await service.UpdateAsync(account.AccountId, booking.BookingId, new BookingRequest.UpdateBookingRequest
        {
            LastUpdatedAt = booking.UpdatedAt, DestinationAddress = "Hamburg", DestinationLatitude = 53.5511m, DestinationLongitude = 9.9937m
        });

        Assert.Equal((53.5511m, 9.9937m), (booking.DestinationLatitude, booking.DestinationLongitude));
        Assert.Null(booking.OriginLatitude);
        var reason = (await db.BookingStatusHistories.OrderBy(history => history.ChangedAt).LastAsync()).Reason;
        Assert.Contains("destinationLatitude", reason);
        Assert.Contains("destinationLongitude", reason);
    }

    [Fact]
    public async Task Update_RejectsHalfAPairAndInvalidCoordinates()
    {
        await using var db = TestSupport.CreateDb();
        var (account, customer) = await TestSupport.AddCustomerAsync(db);
        var (booking, _) = await TestSupport.AddPendingBookingAsync(db, customer);
        var service = Bookings(db);
        await service.ChangeStatusAsync(account.AccountId, booking.BookingId, new BookingRequest.ChangeStatusRequest { Status = "DRAFT" });

        var half = await Assert.ThrowsAsync<BadRequestException>(() => service.UpdateAsync(account.AccountId, booking.BookingId,
            new() { LastUpdatedAt = booking.UpdatedAt, OriginLatitude = 10m }));
        var invalid = await Assert.ThrowsAsync<BadRequestException>(() => service.UpdateAsync(account.AccountId, booking.BookingId,
            new() { LastUpdatedAt = booking.UpdatedAt, OriginLatitude = 95m, OriginLongitude = 10m }));

        Assert.Equal("VALIDATION_ERROR", half.ErrorCode);
        Assert.Equal("INVALID_COORDINATES", invalid.ErrorCode);
        Assert.Null(booking.OriginLatitude);
    }

    [Fact]
    public async Task BookingDetail_ReturnsTheCoordinatesAndNullForOldBookings()
    {
        await using var db = TestSupport.CreateDb();
        var (account, customer) = await TestSupport.AddCustomerAsync(db);
        var (withCoordinates, _) = await TestSupport.AddPendingBookingAsync(db, customer);
        var (old, _) = await TestSupport.AddPendingBookingAsync(db, customer);
        (withCoordinates.OriginLatitude, withCoordinates.OriginLongitude) = (49.1943m, 2.4716m);
        (withCoordinates.DestinationLatitude, withCoordinates.DestinationLongitude) = (52.52m, 13.405m);
        await db.SaveChangesAsync();

        var detail = await Bookings(db).GetAsync(account.AccountId, withCoordinates.BookingId);
        var oldDetail = await Bookings(db).GetAsync(account.AccountId, old.BookingId);

        Assert.Equal((49.1943m, 2.4716m, 52.52m, 13.405m),
            (detail.OriginLatitude, detail.OriginLongitude, detail.DestinationLatitude, detail.DestinationLongitude));
        Assert.Null(oldDetail.OriginLatitude);
        Assert.Null(oldDetail.DestinationLongitude);
    }

    private sealed record ApprovedBooking(Booking Booking, Transport Transport, Account Manager);

    private static async Task<ApprovedBooking> ApprovedBookingAsync(AppDbContext db, bool withCoordinates = true)
    {
        var (_, customer) = await TestSupport.AddCustomerAsync(db);
        var (manager, _) = await TestSupport.AddLogisticsManagerAsync(db);
        var (booking, _) = await TestSupport.AddPendingBookingAsync(db, customer, withHorse: true);
        booking.Status = BookingStatus.APPROVED;
        booking.OriginAddress = "Haras de Chantilly, 60500 Chantilly";
        booking.DestinationAddress = "Newmarket Stables";
        if (withCoordinates)
        {
            (booking.OriginLatitude, booking.OriginLongitude) = (49.1943m, 2.4716m);
            (booking.DestinationLatitude, booking.DestinationLongitude) = (52.2455m, 0.4062m);
        }
        var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        var transport = new Transport
        {
            TransportId = Guid.NewGuid(), TransportCode = "TR-" + Guid.NewGuid().ToString("N"), BookingId = booking.BookingId,
            Booking = booking, CreatedAt = now, UpdatedAt = now
        };
        booking.Transport = transport;
        db.Transports.Add(transport);
        await db.SaveChangesAsync();
        return new(booking, transport, manager);
    }

    private static TransportPlanRequest.CreateRequest PlanRequest() => new()
    {
        PlannedStartAt = DateTimeOffset.UtcNow.AddDays(21), PlannedEndAt = DateTimeOffset.UtcNow.AddDays(23)
    };

    private static Location NewLocation(string name, decimal? latitude, decimal? longitude, bool deleted = false) => new()
    {
        LocationId = Guid.NewGuid(), LocationName = name, Country = "France", LocationType = LocationType.STABLE,
        Latitude = latitude, Longitude = longitude, IsDeleted = deleted,
        CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified), UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)
    };

    [Fact]
    public async Task CreatePlan_UsesTheNearestLocationWithin100MetersOfTheBookingPoints()
    {
        await using var db = TestSupport.CreateDb();
        var a = await ApprovedBookingAsync(db);
        var farther = NewLocation("Farther", 49.1951m, 2.4716m);   // about 89 m north
        var nearest = NewLocation("Nearest", 49.1945m, 2.4716m);   // about 22 m north
        var tooFar = NewLocation("Too far", 52.2480m, 0.4062m);    // about 278 m north of the destination
        var deleted = NewLocation("Deleted", 52.2455m, 0.4062m, deleted: true);
        db.Locations.AddRange(farther, nearest, tooFar, deleted);
        await db.SaveChangesAsync();

        var plan = await Plans(db).CreateAsync(a.Manager.AccountId, a.Booking.BookingId, PlanRequest());

        Assert.Equal(nearest.LocationId, plan.OriginLocationId);
        Assert.Equal("Nearest", plan.OriginLocation!.LocationName);
        Assert.NotEqual(tooFar.LocationId, plan.DestinationLocationId);
        Assert.NotEqual(deleted.LocationId, plan.DestinationLocationId);
    }

    [Fact]
    public async Task CreatePlan_CreatesAnOtherLocationWhenNothingIsNearby()
    {
        await using var db = TestSupport.CreateDb();
        var a = await ApprovedBookingAsync(db);

        var plan = await Plans(db).CreateAsync(a.Manager.AccountId, a.Booking.BookingId, PlanRequest());

        var origin = await db.Locations.SingleAsync(item => item.LocationId == plan.OriginLocationId);
        Assert.Equal("Haras de Chantilly", origin.LocationName);
        Assert.Equal("Haras de Chantilly, 60500 Chantilly", origin.Address);
        Assert.Equal(LocationType.OTHER, origin.LocationType);
        Assert.Equal((a.Booking.OriginCountry, 49.1943m, 2.4716m), (origin.Country, origin.Latitude, origin.Longitude));
        Assert.Equal(2, await db.Locations.CountAsync());
        Assert.Equal("OTHER", plan.OriginLocation!.LocationType);
        Assert.Equal("Newmarket Stables", plan.DestinationLocation!.LocationName);
    }

    [Fact]
    public async Task CreatePlan_PrefersTheGivenLocationIds_AndLeavesBookingsWithoutCoordinatesEmpty()
    {
        await using var db = TestSupport.CreateDb();
        var a = await ApprovedBookingAsync(db);
        var chosen = NewLocation("Chosen", 10m, 10m);
        db.Locations.Add(chosen);
        await db.SaveChangesAsync();

        var plan = await Plans(db).CreateAsync(a.Manager.AccountId, a.Booking.BookingId,
            new() { PlannedStartAt = PlanRequest().PlannedStartAt, PlannedEndAt = PlanRequest().PlannedEndAt, OriginLocationId = chosen.LocationId });

        Assert.Equal(chosen.LocationId, plan.OriginLocationId);
        Assert.NotNull(plan.DestinationLocationId);

        await using var other = TestSupport.CreateDb();
        var old = await ApprovedBookingAsync(other, withCoordinates: false);
        var oldPlan = await Plans(other).CreateAsync(old.Manager.AccountId, old.Booking.BookingId, PlanRequest());
        Assert.Null(oldPlan.OriginLocation);
        Assert.Null(oldPlan.DestinationLocation);
        Assert.Empty(await other.Locations.ToListAsync());
    }

    [Fact]
    public async Task PlanDetail_ReturnsLocationsBookingCoordinatesDossiersAndRoutePlans()
    {
        await using var db = TestSupport.CreateDb();
        var a = await ApprovedBookingAsync(db);
        var created = await Plans(db).CreateAsync(a.Manager.AccountId, a.Booking.BookingId, PlanRequest());
        var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        db.ComplianceDossiers.AddRange(
            new ComplianceDossier { ComplianceDossierId = Guid.NewGuid(), TransportId = a.Transport.TransportId, DossierCode = "CD-EXTRA", DossierType = DossierType.EXTRA, Status = ComplianceDossierStatus.DRAFT, CreatedAt = now.AddMinutes(1), UpdatedAt = now },
            new ComplianceDossier { ComplianceDossierId = Guid.NewGuid(), TransportId = a.Transport.TransportId, DossierCode = "CD-MAIN", DossierType = DossierType.MAIN, Status = ComplianceDossierStatus.APPROVED, CreatedAt = now.AddMinutes(2), UpdatedAt = now });
        var routePlan = new RoutePlan { RoutePlanId = Guid.NewGuid(), TransportPlanId = created.TransportPlanId, RoutePlanCode = "RP-1", Status = RoutePlanStatus.CONFIRMED, CreatedAt = now, UpdatedAt = now };
        db.RoutePlans.Add(routePlan);
        db.RoutePlanRoutes.AddRange(
            new RoutePlanRoute { RoutePlanRouteId = Guid.NewGuid(), RoutePlanId = routePlan.RoutePlanId, RouteId = Guid.NewGuid(), SequenceNo = 1, RouteType = RouteType.PRIMARY, CreatedAt = now, UpdatedAt = now },
            new RoutePlanRoute { RoutePlanRouteId = Guid.NewGuid(), RoutePlanId = routePlan.RoutePlanId, RouteId = Guid.NewGuid(), SequenceNo = 2, RouteType = RouteType.BACKUP, CreatedAt = now, UpdatedAt = now });
        await db.SaveChangesAsync();

        var detail = await Plans(db).GetByIdAsync(a.Manager.AccountId, created.TransportPlanId);

        Assert.Equal(["CD-MAIN", "CD-EXTRA"], detail.ComplianceDossiers.Select(item => item.DossierCode).ToArray());
        Assert.Equal(["MAIN", "EXTRA"], detail.ComplianceDossiers.Select(item => item.DossierType).ToArray());
        var route = Assert.Single(detail.RoutePlans);
        Assert.Equal(("RP-1", "BACKUP", "CONFIRMED"), (route.RoutePlanCode, route.RouteType, route.Status));
        Assert.Equal((49.1943m, 2.4716m, 52.2455m, 0.4062m),
            (detail.Booking.OriginLatitude, detail.Booking.OriginLongitude, detail.Booking.DestinationLatitude, detail.Booking.DestinationLongitude));
        Assert.Equal(detail.OriginLocationId, detail.OriginLocation!.LocationId);
        Assert.Equal(49.1943m, detail.OriginLocation.Latitude);
    }

    [Fact]
    public async Task UpdatePlan_ReturnsTheNewLocations()
    {
        await using var db = TestSupport.CreateDb();
        var a = await ApprovedBookingAsync(db, withCoordinates: false);
        var created = await Plans(db).CreateAsync(a.Manager.AccountId, a.Booking.BookingId, PlanRequest());
        var (origin, destination) = (NewLocation("Origin", 1m, 1m), NewLocation("Destination", 2m, 2m));
        db.Locations.AddRange(origin, destination);
        await db.SaveChangesAsync();

        var updated = await Plans(db).UpdateAsync(a.Manager.AccountId, created.TransportPlanId,
            new() { LastUpdatedAt = created.UpdatedAt, OriginLocationId = origin.LocationId, DestinationLocationId = destination.LocationId });

        Assert.Equal(("Origin", "Destination"), (updated.OriginLocation!.LocationName, updated.DestinationLocation!.LocationName));
        var missing = await Assert.ThrowsAsync<NotFoundException>(() => Plans(db).UpdateAsync(a.Manager.AccountId, created.TransportPlanId,
            new() { LastUpdatedAt = updated.UpdatedAt, OriginLocationId = Guid.NewGuid() }));
        Assert.Equal("LOCATION_NOT_FOUND", missing.ErrorCode);
    }

    [Fact]
    public async Task ApprovedBookingDetail_ReturnsCoordinatesPlanLocationsAndAllDossiers()
    {
        await using var db = TestSupport.CreateDb();
        var a = await ApprovedBookingAsync(db);
        var created = await Plans(db).CreateAsync(a.Manager.AccountId, a.Booking.BookingId, PlanRequest());
        var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        db.ComplianceDossiers.AddRange(
            new ComplianceDossier { ComplianceDossierId = Guid.NewGuid(), TransportId = a.Transport.TransportId, DossierCode = "CD-EXTRA", DossierType = DossierType.EXTRA, CreatedAt = now.AddMinutes(1), UpdatedAt = now },
            new ComplianceDossier { ComplianceDossierId = Guid.NewGuid(), TransportId = a.Transport.TransportId, DossierCode = "CD-MAIN", DossierType = DossierType.MAIN, CreatedAt = now.AddMinutes(2), UpdatedAt = now });
        await db.SaveChangesAsync();

        var detail = await Bookings(db).GetApprovedAsync(a.Manager.AccountId, a.Booking.BookingId);

        Assert.Equal((49.1943m, 2.4716m, 52.2455m, 0.4062m),
            (detail.OriginLatitude, detail.OriginLongitude, detail.DestinationLatitude, detail.DestinationLongitude));
        Assert.Equal(created.OriginLocationId, detail.TransportPlan!.OriginLocation!.LocationId);
        Assert.Equal(created.DestinationLocationId, detail.TransportPlan.DestinationLocation!.LocationId);
        Assert.Equal(["CD-MAIN", "CD-EXTRA"], detail.ComplianceDossiers.Select(item => item.DossierCode).ToArray());
    }

    [Fact]
    public async Task ApprovedBookingList_KeepsASingleMainDossier()
    {
        await using var db = TestSupport.CreateDb();
        var a = await ApprovedBookingAsync(db);
        var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        db.ComplianceDossiers.AddRange(
            new ComplianceDossier { ComplianceDossierId = Guid.NewGuid(), TransportId = a.Transport.TransportId, DossierCode = "CD-EXTRA", DossierType = DossierType.EXTRA, Status = ComplianceDossierStatus.DRAFT, CreatedAt = now, UpdatedAt = now },
            new ComplianceDossier { ComplianceDossierId = Guid.NewGuid(), TransportId = a.Transport.TransportId, DossierCode = "CD-MAIN", DossierType = DossierType.MAIN, Status = ComplianceDossierStatus.APPROVED, CreatedAt = now, UpdatedAt = now });
        await db.SaveChangesAsync();

        var list = await Bookings(db).GetApprovedListAsync(a.Manager.AccountId, new BookingRequest.ApprovedBookingListRequest());

        var item = Assert.Single(list.Items);
        Assert.Equal("APPROVED", item.ComplianceDossier!.Status);
    }
}
