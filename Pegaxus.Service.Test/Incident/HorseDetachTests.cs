using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.BookingService.Validations;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;

using static Pegaxus.Service.Test.HorseIncidentTests;

using Bookings = Pegaxus.Service.BookingService;
using HealthLogs = Pegaxus.Service.HorseHealthLogService;
using Incidents = Pegaxus.Service.IncidentService;

namespace Pegaxus.Service.Test;

/// <summary>A horse that cannot continue is left at a stop; the rest travels on and the customer can book its return.</summary>
public class HorseDetachTests
{
    [Fact]
    public async Task Detach_LeavesHorseAtStop_AndNotifiesCrewCustomerAndTeam()
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db);
        var incident = await SeedHorseIncidentAsync(db, f, f.Horse.HorseId);
        var stop = f.Segment.Route.ToLocationId;

        var result = await f.Service.DetachHorseAsync(incident.IncidentReportId, f.Horse.HorseId, Request(incident, stop, " Colic, vet clinic took over "), f.Manager.AccountId);

        var link = await db.BookingHorses.AsNoTracking().SingleAsync(item => item.BookingId == f.Transport.BookingId && item.HorseId == f.Horse.HorseId);
        Assert.NotNull(link.DetachedAt); Assert.Equal(stop, link.DetachedLocationId); Assert.Equal(incident.IncidentReportId, link.DetachedIncidentReportId);
        Assert.Null((await db.BookingHorses.AsNoTracking().SingleAsync(item => item.BookingId == f.Transport.BookingId && item.HorseId == f.SecondHorseId)).DetachedAt);
        var detail = result.HorseDetails.Single(item => item.Horse.HorseId == f.Horse.HorseId);
        Assert.False(detail.HorseTransportable); Assert.Equal(stop, detail.Detachment!.Location!.LocationId);

        var notifications = await db.AccountNotifications.Include(item => item.Notification).ToArrayAsync();
        foreach (var crew in new[] { f.Driver, f.Escort })
            Assert.Contains(notifications, item => item.AccountId == crew.AccountId && item.Notification.Priority == NotificationPriority.URGENT &&
                item.Notification.ActionUrl == $"/crew/incidents/{incident.IncidentReportId}" && item.Notification.Message.Contains("continues with 1 horse"));
        Assert.Contains(notifications, item => item.AccountId == f.Manager.AccountId);
        var customer = Assert.Single(notifications, item => item.AccountId == f.CustomerAccountId);
        Assert.Equal($"/bookings/return?returnOfTransportId={f.Transport.TransportId}&horseId={f.Horse.HorseId}", customer.Notification.ActionUrl);
    }

    [Fact]
    public async Task Detach_RejectsDuplicateLastHorseWrongStopAndNonManager()
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db);
        var incident = await SeedHorseIncidentAsync(db, f, f.Horse.HorseId, f.SecondHorseId);
        var stop = f.Segment.Route.ToLocationId;

        var forbidden = await Assert.ThrowsAsync<ForbiddenException>(() =>
            f.Service.DetachHorseAsync(incident.IncidentReportId, f.Horse.HorseId, Request(incident, stop), f.Coordinator.AccountId));
        Assert.Equal("FORBIDDEN", forbidden.ErrorCode);
        var offRoute = await Assert.ThrowsAsync<UnprocessableEntityException>(() =>
            f.Service.DetachHorseAsync(incident.IncidentReportId, f.Horse.HorseId, Request(incident, Guid.NewGuid()), f.Manager.AccountId));
        Assert.Equal("INVALID_DETACH_LOCATION", offRoute.ErrorCode);

        var first = await f.Service.DetachHorseAsync(incident.IncidentReportId, f.Horse.HorseId, Request(incident, stop), f.Manager.AccountId);
        var again = new Incidents.Request.DetachHorseRequest { LastUpdatedAt = first.UpdatedAt, LocationId = stop };
        var duplicate = await Assert.ThrowsAsync<ConflictException>(() =>
            f.Service.DetachHorseAsync(incident.IncidentReportId, f.Horse.HorseId, again, f.Manager.AccountId));
        Assert.Equal("HORSE_ALREADY_DETACHED", duplicate.ErrorCode);
        var last = await Assert.ThrowsAsync<UnprocessableEntityException>(() =>
            f.Service.DetachHorseAsync(incident.IncidentReportId, f.SecondHorseId, again, f.Manager.AccountId));
        Assert.Equal("LAST_HORSE_CANNOT_DETACH", last.ErrorCode);
        Assert.Contains("DETACH_HORSE", first.AvailableActions);
        Assert.Contains(first.DetachLocations, item => item.LocationId == stop);
    }

    [Fact]
    public async Task HealthLog_SkipsAndRejectsDetachedHorse()
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db);
        var incident = await SeedHorseIncidentAsync(db, f, f.Horse.HorseId);
        await f.Service.DetachHorseAsync(incident.IncidentReportId, f.Horse.HorseId, Request(incident, f.Segment.Route.ToLocationId), f.Manager.AccountId);
        var logs = HorseHealthLogTests.CreateService(db, new HorseHealthLogTests.Storage(), TestSupport.CreateNotifications(db));

        var batch = await logs.MarkAllNormalAsync(f.Transport.TransportId,
            new HealthLogs.Request.RecordRequest { LogType = "ROUTINE_CHECK", CheckedAt = HorseHealthLogTests.Fixture.CheckTime }, f.Escort.AccountId);
        Assert.Equal(f.SecondHorseId, Assert.Single(batch.Value.Logs).Horse.HorseId);
        var single = await Assert.ThrowsAsync<UnprocessableEntityException>(() => logs.CreateAsync(f.Transport.TransportId,
            new HealthLogs.Request.CreateRequest { HorseId = f.Horse.HorseId, LogType = "ROUTINE_CHECK", HealthStatus = "NORMAL", HydrationStatus = "NORMAL",
                CheckedAt = HorseHealthLogTests.Fixture.CheckTime }, f.Escort.AccountId));
        Assert.Equal("HORSE_DETACHED", single.ErrorCode);
    }

    [Fact]
    public async Task ReturnBooking_LinksOriginalTrip_SkipsLeadTime_AndAllowsOnlyOne()
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db);
        var incident = await SeedHorseIncidentAsync(db, f, f.Horse.HorseId);
        await f.Service.DetachHorseAsync(incident.IncidentReportId, f.Horse.HorseId, Request(incident, f.Segment.Route.ToLocationId), f.Manager.AccountId);
        var bookings = new Bookings.Service(db, TestSupport.CreateMailService(), TestSupport.CreateNotifications(db));

        var draft = await bookings.GetReturnDraftAsync(f.CustomerAccountId, f.Transport.TransportId, f.Horse.HorseId);
        Assert.Equal(f.Horse.HorseId, draft.HorseId); Assert.Null(draft.ExistingReturnBookingId);
        Assert.Equal(f.Transport.Booking.OriginAddress, draft.DestinationAddress);

        var request = ReturnRequest(f, draft, f.Horse.HorseId);
        Assert.True((await new CreateBookingRequestValidator().ValidateAsync(request)).IsValid);
        var created = await bookings.CreateAsync(f.CustomerAccountId, request);
        var detail = await bookings.GetAsync(f.CustomerAccountId, created.BookingId);
        Assert.Equal(f.Transport.TransportId, detail.ReturnOf!.TransportId); Assert.Equal(f.Horse.HorseId, detail.ReturnOf.Horse!.HorseId);

        var duplicate = await Assert.ThrowsAsync<ConflictException>(() => bookings.CreateAsync(f.CustomerAccountId, ReturnRequest(f, draft, f.Horse.HorseId)));
        Assert.Equal("RETURN_BOOKING_EXISTS", duplicate.ErrorCode);
        var stillTravelling = await Assert.ThrowsAsync<NotFoundException>(() => bookings.CreateAsync(f.CustomerAccountId, ReturnRequest(f, draft, f.SecondHorseId)));
        Assert.Equal("DETACHED_HORSE_NOT_FOUND", stillTravelling.ErrorCode);
    }

    [Fact]
    public async Task ReturnRouteTemplate_ReversesLegsDrivenUpToTheStop()
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db);
        var next = PersonnelAssignmentTests.Segment(f.Segment.RoutePlan, 2);
        next.Route.FromLocation = f.Segment.Route.ToLocation; next.Route.FromLocationId = f.Segment.Route.ToLocationId;
        db.Add(next); await db.SaveChangesAsync();
        var incident = await SeedHorseIncidentAsync(db, f, f.Horse.HorseId);
        await f.Service.DetachHorseAsync(incident.IncidentReportId, f.Horse.HorseId, Request(incident, f.Segment.Route.ToLocationId), f.Manager.AccountId);
        var bookings = new Bookings.Service(db, TestSupport.CreateMailService(), TestSupport.CreateNotifications(db));
        var draft = await bookings.GetReturnDraftAsync(f.CustomerAccountId, f.Transport.TransportId, f.Horse.HorseId);
        var created = await bookings.CreateAsync(f.CustomerAccountId, ReturnRequest(f, draft, f.Horse.HorseId));
        var returnBooking = await db.Bookings.SingleAsync(item => item.BookingId == created.BookingId);
        var plan = new TransportPlan { TransportPlanId = Guid.NewGuid(), PlanCode = "TP-" + Guid.NewGuid().ToString("N"), Status = TransportPlanStatus.DRAFT,
            Transport = new Transport { TransportId = Guid.NewGuid(), TransportCode = "TR-" + Guid.NewGuid().ToString("N"), Booking = returnBooking,
                Status = TransportStatus.PLANNING, CreatedAt = Scenario.Now, UpdatedAt = Scenario.Now }, CreatedAt = Scenario.Now, UpdatedAt = Scenario.Now };
        db.Add(plan); await db.SaveChangesAsync();
        var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
        var routes = new Pegaxus.Service.RoutePlanService.Service(db, TestSupport.CreateNotifications(db),
            new Pegaxus.Service.RouteService.Service(db, new NoHttp(), configuration), new Pegaxus.Service.RoutePlanService.NoopRoutePlanRealtimeService());

        var template = await routes.GetReturnTemplateAsync(f.Coordinator.AccountId, plan.TransportPlanId);

        Assert.Equal(f.Transport.TransportId, template.SourceTransportId);
        var leg = Assert.Single(template.Segments);
        Assert.Equal(f.Segment.Route.ToLocationId, leg.FromLocation.LocationId); Assert.Equal(f.Segment.Route.FromLocationId, leg.ToLocation.LocationId);
        var notReturn = await Assert.ThrowsAsync<UnprocessableEntityException>(() =>
            routes.GetReturnTemplateAsync(f.Coordinator.AccountId, f.Segment.RoutePlan.TransportPlanId));
        Assert.Equal("NOT_RETURN_BOOKING", notReturn.ErrorCode);
    }

    private sealed class NoHttp : IHttpClientFactory { public HttpClient CreateClient(string name) => new(); }

    private static Bookings.Request.CreateBookingRequest ReturnRequest(Scenario f, Bookings.Response.ReturnDraftResponse draft, Guid horseId) => new()
    {
        OriginAddress = draft.OriginAddress is { Length: > 0 } origin ? origin : "Vet clinic", OriginCountry = "France",
        DestinationAddress = draft.DestinationAddress, DestinationCountry = "France",
        RequestedDepartureDate = DateOnly.FromDateTime(DateTime.UtcNow), Horses = [new() { HorseId = horseId }],
        ReturnOfTransportId = f.Transport.TransportId
    };

    private static Incidents.Request.DetachHorseRequest Request(IncidentReport incident, Guid locationId, string? note = null) =>
        new() { LastUpdatedAt = DbTime.AsUtc(incident.UpdatedAt), LocationId = locationId, Note = note };

    private static async Task<IncidentReport> SeedHorseIncidentAsync(AppDbContext db, Scenario f, params Guid[] horseIds)
    {
        var incident = await f.SeedIncidentAsync();
        foreach (var horseId in horseIds)
            db.Add(new HorseHealthIncidentDetail
            {
                HorseHealthIncidentDetailId = Guid.NewGuid(), IncidentReportId = incident.IncidentReportId, HorseId = horseId, IncidentType = HorseIncidentType.COLIC,
                ConditionDescription = "Cannot stand in the trailer", CreatedAt = Scenario.Now, UpdatedAt = Scenario.Now
            });
        await db.SaveChangesAsync();
        return incident;
    }
}
