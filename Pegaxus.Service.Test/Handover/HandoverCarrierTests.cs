using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;

using static Pegaxus.Service.Test.HandoverCompletionTests;
using static Pegaxus.Service.Test.HandoverTests;

using Handovers = Pegaxus.Service.HandoverService;

namespace Pegaxus.Service.Test;

/// <summary>A provider runs the middle segment: our crew hands the horses over to it and takes them back with a carrier handover.</summary>
public class HandoverCarrierTests
{
    private sealed record Trip(Fixture F, TransportProvider Provider, RoutePlanRoute Third, Employee LastDriver);

    private static async Task<Trip> TripAsync(AppDbContext db)
    {
        var f = await Fixture.CreateAsync(db);
        var provider = PersonnelAssignmentTests.Provider();
        provider.Phone = "+33123456789";
        db.Add(provider);
        // Segment 2 goes to the provider; the receiver now drives segment 3.
        f.Second.DriverEmployeeId = null;
        f.Second.TransportProviderId = provider.TransportProviderId;
        var third = PersonnelAssignmentTests.Segment(f.Route, 3);
        third.DriverEmployeeId = f.Receiver.EmployeeId;
        db.Add(third);
        f.Route.TransportPlan.Transport.Status = TransportStatus.IN_TRANSIT;
        f.First.ActualDepartureAt = Fixture.Now.AddHours(-2);
        f.Pickup.Status = HandoverStatus.COMPLETED;
        f.Transfer.Status = f.Delivery.Status = HandoverStatus.CANCELLED;
        await db.SaveChangesAsync();
        return new(f, provider, third, f.Receiver);
    }

    private static Handovers.Request.CreateRequest Carrier(RoutePlanRoute from, RoutePlanRoute to) =>
        new() { HandoverType = "CARRIER_HANDOVER", FromRoutePlanRouteId = from.RoutePlanRouteId, ToRoutePlanRouteId = to.RoutePlanRouteId };

    [Fact]
    public async Task Carrier_CrewHandsOverToTheProviderAndTakesBack_RecordsTheProviderSegmentTimes()
    {
        await using var db = TestSupport.CreateDb();
        var (f, provider, third, lastDriver) = await TripAsync(db);

        var handOver = await f.Service.CreateAsync(f.TransportId, Carrier(f.First, f.Second), f.Driver.AccountId);
        Assert.Equal(("EMPLOYEE", f.Driver.EmployeeId), (handOver.From.Type, handOver.From.EmployeeId));
        Assert.Equal(("PROVIDER", provider.TransportProviderId, (Guid?)null), (handOver.To.Type, handOver.To.TransportProviderId, handOver.To.EmployeeId));
        Assert.Equal((provider.ProviderName, "+33123456789"), (handOver.To.PartyName, handOver.To.ContactPhone));
        var record = await db.HandoverRecords.SingleAsync(item => item.HandoverRecordId == handOver.HandoverRecordId);
        await f.Service.CompleteAsync(record.HandoverRecordId, Request(record), f.Driver.AccountId);
        var handedOverAt = DateTime.SpecifyKind(f.Clock.GetUtcNow().UtcDateTime, DateTimeKind.Unspecified);
        Assert.Equal(handedOverAt, f.First.ActualArrivalAt);
        Assert.Equal(handedOverAt, f.Second.ActualDepartureAt);

        f.Clock.Now = f.Clock.Now.AddHours(5);
        var takeBack = await f.Service.CreateAsync(f.TransportId, Carrier(f.Second, third), lastDriver.AccountId);
        Assert.Equal(("PROVIDER", provider.TransportProviderId), (takeBack.From.Type, takeBack.From.TransportProviderId));
        Assert.Equal(("EMPLOYEE", lastDriver.EmployeeId), (takeBack.To.Type, takeBack.To.EmployeeId));
        record = await db.HandoverRecords.SingleAsync(item => item.HandoverRecordId == takeBack.HandoverRecordId);
        await f.Service.CompleteAsync(record.HandoverRecordId, Request(record), lastDriver.AccountId);
        var takenBackAt = DateTime.SpecifyKind(f.Clock.GetUtcNow().UtcDateTime, DateTimeKind.Unspecified);
        Assert.Equal(takenBackAt, f.Second.ActualArrivalAt);
        Assert.Equal(takenBackAt, third.ActualDepartureAt);
        Assert.Equal(TransportStatus.IN_TRANSIT, f.Route.TransportPlan.Transport.Status);
    }

    [Fact]
    public async Task Carrier_OnlyFitsAnOutsourcedBoundary_AndOnlyItsCrewOrAManagerMayRecordIt()
    {
        await using var db = TestSupport.CreateDb();
        var (f, _, third, _) = await TripAsync(db);

        var transfer = Carrier(f.First, f.Second);
        transfer.HandoverType = "TRANSFER";
        Assert.Equal("OUTSOURCED_SEGMENT", (await Assert.ThrowsAsync<UnprocessableEntityException>(() =>
            f.Service.CreateAsync(f.TransportId, transfer, f.Driver.AccountId))).ErrorCode);
        var stranger = await PersonnelAssignmentTests.EmployeeAsync(db, EmployeeRole.DRIVER);
        await Assert.ThrowsAsync<ForbiddenException>(() => f.Service.CreateAsync(f.TransportId, Carrier(f.First, f.Second), stranger.AccountId));
        // Not adjacent: segment 1 does not end where segment 3 starts.
        Assert.Equal("INVALID_SEGMENT_PAIR", (await Assert.ThrowsAsync<UnprocessableEntityException>(() =>
            f.Service.CreateAsync(f.TransportId, Carrier(f.First, third), f.Manager.AccountId))).ErrorCode);
        Assert.Equal("CARRIER_HANDOVER", (await f.Service.CreateAsync(f.TransportId, Carrier(f.First, f.Second), f.Manager.AccountId)).HandoverType);
    }
}
