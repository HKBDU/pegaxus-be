using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;

using Plans = Pegaxus.Service.TransportPlanService;

namespace Pegaxus.Service.Test;

public class TransportPlanPlannedStartTests
{
    private static async Task<(Account Manager, Booking Booking)> ApprovedBookingAsync(AppDbContext db, string origin, string destination, int bookedDaysAgo = 0)
    {
        var (_, customer) = await TestSupport.AddCustomerAsync(db);
        var (manager, _) = await TestSupport.AddLogisticsManagerAsync(db);
        var (booking, _) = await TestSupport.AddPendingBookingAsync(db, customer);
        booking.Status = BookingStatus.APPROVED;
        booking.OriginCountry = origin;
        booking.DestinationCountry = destination;
        booking.CreatedAt = booking.CreatedAt.AddDays(-bookedDaysAgo);
        booking.Transport = new Transport { TransportId = Guid.NewGuid(), TransportCode = "TR-START", Booking = booking };
        db.Transports.Add(booking.Transport);
        await db.SaveChangesAsync();
        return (manager, booking);
    }

    private static Plans.Request.CreateRequest Request(DateTimeOffset start) => new() { PlannedStartAt = start, PlannedEndAt = start.AddDays(1) };

    private static async Task<string?> CreateErrorCodeAsync(AppDbContext db, Account manager, Booking booking, DateTimeOffset start)
    {
        try
        {
            await new Plans.Service(db, TestSupport.CreateNotifications(db)).CreateAsync(manager.AccountId, booking.BookingId, Request(start));
            return null;
        }
        catch (BadRequestException error) { return error.ErrorCode; }
    }

    [Fact]
    public async Task Create_RejectsAStartInThePast()
    {
        await using var db = TestSupport.CreateDb();
        var (manager, booking) = await ApprovedBookingAsync(db, "FR", "FR", bookedDaysAgo: 90);

        Assert.Equal("PLANNED_START_IN_PAST", await CreateErrorCodeAsync(db, manager, booking, DateTimeOffset.UtcNow.AddDays(-1)));
        Assert.Empty(await db.TransportPlans.ToListAsync());
    }

    [Fact]
    public async Task Create_DomesticStartNeedsThreeWeeksFromTheDayTheBookingWasMade()
    {
        await using var db = TestSupport.CreateDb();
        var (manager, booking) = await ApprovedBookingAsync(db, "FR", "FR");

        Assert.Equal("PLANNED_START_TOO_EARLY", await CreateErrorCodeAsync(db, manager, booking, DateTimeOffset.UtcNow.AddDays(20)));
        Assert.Null(await CreateErrorCodeAsync(db, manager, booking, DateTimeOffset.UtcNow.AddDays(22)));
    }

    [Fact]
    public async Task Create_InternationalStartNeedsTwoMonths_AndHasNoLatestDate()
    {
        await using var db = TestSupport.CreateDb();
        var (manager, booking) = await ApprovedBookingAsync(db, "FR", "DE");

        Assert.Equal("PLANNED_START_TOO_EARLY", await CreateErrorCodeAsync(db, manager, booking, DateTimeOffset.UtcNow.AddMonths(2).AddDays(-2)));
        Assert.Null(await CreateErrorCodeAsync(db, manager, booking, DateTimeOffset.UtcNow.AddYears(2)));
    }

    [Fact]
    public async Task Create_CountsTheLeadTimeFromTheBookingDay_NotFromToday()
    {
        await using var db = TestSupport.CreateDb();
        var (manager, booking) = await ApprovedBookingAsync(db, "FR", "FR", bookedDaysAgo: 30);

        Assert.Null(await CreateErrorCodeAsync(db, manager, booking, DateTimeOffset.UtcNow.AddDays(1)));
    }

    [Fact]
    public async Task Update_ChecksOnlyAStartThatChanges()
    {
        await using var db = TestSupport.CreateDb();
        var (manager, booking) = await ApprovedBookingAsync(db, "FR", "FR");
        var service = new Plans.Service(db, TestSupport.CreateNotifications(db));
        var created = await service.CreateAsync(manager.AccountId, booking.BookingId, Request(DateTimeOffset.UtcNow.AddDays(30)));
        var plan = await db.TransportPlans.SingleAsync();
        plan.PlannedStartAt = DateTime.UtcNow.AddDays(-2);
        plan.PlannedEndAt = DateTime.UtcNow.AddDays(-1);
        await db.SaveChangesAsync();
        var current = Pegaxus.Service.Common.DbTime.AsUtc(plan.UpdatedAt);

        await service.UpdateAsync(manager.AccountId, created.TransportPlanId, new() { LastUpdatedAt = current, PlanningNote = "Only the note changes" });
        plan = await db.TransportPlans.SingleAsync();
        current = Pegaxus.Service.Common.DbTime.AsUtc(plan.UpdatedAt);
        var past = await Assert.ThrowsAsync<BadRequestException>(() => service.UpdateAsync(manager.AccountId, created.TransportPlanId,
            new() { LastUpdatedAt = current, PlannedStartAt = DateTimeOffset.UtcNow.AddDays(-3), PlannedEndAt = DateTimeOffset.UtcNow.AddDays(-1) }));
        var early = await Assert.ThrowsAsync<BadRequestException>(() => service.UpdateAsync(manager.AccountId, created.TransportPlanId,
            new() { LastUpdatedAt = current, PlannedStartAt = DateTimeOffset.UtcNow.AddDays(5), PlannedEndAt = DateTimeOffset.UtcNow.AddDays(6) }));

        Assert.Equal("PLANNED_START_IN_PAST", past.ErrorCode);
        Assert.Equal("PLANNED_START_TOO_EARLY", early.ErrorCode);
    }
}
