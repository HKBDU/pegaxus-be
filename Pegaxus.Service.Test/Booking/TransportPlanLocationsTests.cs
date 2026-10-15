using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;

using Plans = Pegaxus.Service.TransportPlanService;

namespace Pegaxus.Service.Test;

public class TransportPlanLocationsTests
{
    [Fact]
    public async Task Locations_RoundTripCreateReadUpdate_AndListStatusMatchesDetail()
    {
        await using var db = TestSupport.CreateDb();
        var (_, customer) = await TestSupport.AddCustomerAsync(db);
        var (manager, _) = await TestSupport.AddLogisticsManagerAsync(db);
        var (booking, _) = await TestSupport.AddPendingBookingAsync(db, customer);
        booking.Status = BookingStatus.APPROVED;
        booking.Transport = new Transport { TransportId = Guid.NewGuid(), TransportCode = "TR-LOC", Booking = booking };
        db.Transports.Add(booking.Transport);
        var origin = new Location { LocationId = Guid.NewGuid(), LocationName = "Origin", Country = "FR" };
        var destination = new Location { LocationId = Guid.NewGuid(), LocationName = "Destination", Country = "DE" };
        db.AddRange(origin, destination);
        await db.SaveChangesAsync();
        var service = new Plans.Service(db, TestSupport.CreateNotifications(db));
        var start = DateTimeOffset.UtcNow.AddDays(1);
        var created = await service.CreateAsync(manager.AccountId, booking.BookingId, new()
        {
            OriginLocationId = origin.LocationId, DestinationLocationId = destination.LocationId,
            PlannedStartAt = start, PlannedEndAt = start.AddDays(1)
        });
        db.ChangeTracker.Clear();
        var detail = await service.GetByIdAsync(manager.AccountId, created.TransportPlanId);
        Assert.Equal(origin.LocationId, detail.OriginLocationId);
        Assert.Equal(destination.LocationId, detail.DestinationLocationId);

        await service.UpdateAsync(manager.AccountId, detail.TransportPlanId, new()
        {
            LastUpdatedAt = detail.UpdatedAt, OriginLocationId = destination.LocationId
        });
        var plan = await db.TransportPlans.SingleAsync();
        plan.Status = TransportPlanStatus.APPROVED;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        detail = await service.GetByIdAsync(manager.AccountId, created.TransportPlanId);
        Assert.Equal(destination.LocationId, detail.OriginLocationId);
        Assert.Equal(destination.LocationId, detail.DestinationLocationId);
        Assert.Equal("APPROVED", detail.Status);
        Assert.Equal(detail.Status, Assert.Single((await service.GetListAsync(manager.AccountId, new())).Items).Status);

        destination = await db.Locations.SingleAsync(x => x.LocationId == destination.LocationId);
        destination.IsDeleted = true;
        await db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<NotFoundException>(() => service.UpdateAsync(manager.AccountId, detail.TransportPlanId,
            new() { LastUpdatedAt = detail.UpdatedAt, DestinationLocationId = destination.LocationId }));
        Assert.Equal("LOCATION_NOT_FOUND", error.ErrorCode);
    }

    [Fact]
    public async Task Create_RejectsUnknownLocation_WithoutCreatingPlan()
    {
        await using var db = TestSupport.CreateDb();
        var (_, customer) = await TestSupport.AddCustomerAsync(db);
        var (manager, _) = await TestSupport.AddLogisticsManagerAsync(db);
        var (booking, _) = await TestSupport.AddPendingBookingAsync(db, customer);
        booking.Status = BookingStatus.APPROVED;
        booking.Transport = new Transport { TransportId = Guid.NewGuid(), TransportCode = "TR-UNKNOWN", Booking = booking };
        db.Transports.Add(booking.Transport);
        await db.SaveChangesAsync();
        var start = DateTimeOffset.UtcNow.AddDays(1);
        var error = await Assert.ThrowsAsync<NotFoundException>(() => new Plans.Service(db, TestSupport.CreateNotifications(db))
            .CreateAsync(manager.AccountId, booking.BookingId, new()
            {
                OriginLocationId = Guid.NewGuid(), PlannedStartAt = start, PlannedEndAt = start.AddDays(1)
            }));
        Assert.Equal("LOCATION_NOT_FOUND", error.ErrorCode);
        Assert.Empty(await db.TransportPlans.ToListAsync());
    }
}
