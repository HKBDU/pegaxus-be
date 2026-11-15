using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;

using BookingRequest = Pegaxus.Service.BookingService.Request;
using BookingServiceImpl = Pegaxus.Service.BookingService.Service;
using TransportPlanRequest = Pegaxus.Service.TransportPlanService.Request;
using TransportPlanServiceImpl = Pegaxus.Service.TransportPlanService.Service;

namespace Pegaxus.Service.Test;

/// <summary>
/// QA-01: a booking sent back for changes after its approval can be approved (or rejected) again.
/// QA-02: a deposit paid before the transport plan exists still opens phase 1 assignment.
/// QA-03: a second request cannot create a second transport plan for the same booking.
/// QA-08: seed and booking countries use names.
/// </summary>
public class BookingRenegotiationAndPlanTests
{
    [Fact]
    public async Task ApprovingAgainAfterNegotiation_ReusesTheTransport()
    {
        await using var db = TestSupport.CreateDb();
        var (manager, booking) = await ApprovedBookingAsync(db);
        var first = await db.Transports.SingleAsync();
        await SendBackForChangesAsync(db, manager, booking);

        var again = await Bookings(db).ApproveAsync(manager.AccountId, booking.BookingId);

        Assert.Equal(BookingStatus.APPROVED, booking.Status);
        Assert.Equal(first.TransportId, again.Transport.TransportId);
        Assert.Single(await db.Transports.ToListAsync());
    }

    [Fact]
    public async Task RejectingAfterNegotiation_CancelsTheTransport()
    {
        await using var db = TestSupport.CreateDb();
        var (manager, booking) = await ApprovedBookingAsync(db);
        await SendBackForChangesAsync(db, manager, booking);

        await Bookings(db).RejectAsync(manager.AccountId, booking.BookingId, new BookingRequest.RejectBookingRequest { Reason = "The new dates cannot be served" });

        Assert.Equal(BookingStatus.REJECTED, booking.Status);
        var transport = await db.Transports.SingleAsync();
        Assert.Equal(TransportStatus.CANCELLED, transport.Status);
        Assert.Equal("The new dates cannot be served", transport.CancellationReason);
    }

    [Fact]
    public async Task PlanCreatedAfterTheDepositWasPaid_IsApprovedForPhaseOne()
    {
        await using var db = TestSupport.CreateDb();
        var (manager, booking) = await ApprovedBookingAsync(db);
        var employee = await db.Employees.SingleAsync(item => item.AccountId == manager.AccountId);
        db.Invoices.Add(new Invoice
        {
            InvoiceId = Guid.NewGuid(), InvoiceCode = "INV-DEP", BookingId = booking.BookingId, EmployeeId = employee.EmployeeId,
            Type = InvoiceType.DEPOSIT, Status = InvoiceStatus.PAID, Currency = "VND", Subtotal = 1_000_000, TotalAmount = 1_000_000,
            CreatedAt = DbTime.UtcNow(), UpdatedAt = DbTime.UtcNow()
        });
        await db.SaveChangesAsync();

        await Plans(db).CreateAsync(manager.AccountId, booking.BookingId, PlanRequest());

        var plan = await db.TransportPlans.SingleAsync();
        Assert.Equal(TransportPlanStatus.APPROVED, plan.Status);
        Assert.NotNull(plan.ApprovedAt);
        Assert.Equal(employee.EmployeeId, plan.ApprovedByEmployeeId);
    }

    [Fact]
    public async Task PlanCreatedBeforeAnyDeposit_StaysDraft()
    {
        await using var db = TestSupport.CreateDb();
        var (manager, booking) = await ApprovedBookingAsync(db);

        await Plans(db).CreateAsync(manager.AccountId, booking.BookingId, PlanRequest());

        Assert.Equal(TransportPlanStatus.DRAFT, (await db.TransportPlans.SingleAsync()).Status);
    }

    [Fact]
    public async Task SecondPlanFromAnotherRequest_IsRefused()
    {
        await using var db = TestSupport.CreateDb();
        var (manager, booking) = await ApprovedBookingAsync(db);
        await Plans(db).CreateAsync(manager.AccountId, booking.BookingId, PlanRequest());
        // A new request starts with nothing tracked: the earlier plan is only in the database.
        db.ChangeTracker.Clear();

        var duplicate = await Assert.ThrowsAsync<ConflictException>(() =>
            Plans(db).CreateAsync(manager.AccountId, booking.BookingId, PlanRequest()));

        Assert.Equal("TRANSPORT_PLAN_EXISTS", duplicate.ErrorCode);
        Assert.Single(await db.TransportPlans.ToListAsync());
    }

    [Theory]
    [InlineData("FR", "France")]
    [InlineData("GB", "United Kingdom")]
    [InlineData("France", "France")]
    [InlineData("US", null)]
    public void CountryName_MapsAcceptedCodesAndNamesToTheBookingName(string input, string? expected) =>
        Assert.Equal(expected, CountryUtils.CountryName(input));

    private static async Task<(Account Manager, Booking Booking)> ApprovedBookingAsync(AppDbContext db)
    {
        var (_, customer) = await TestSupport.AddCustomerAsync(db);
        var (manager, _) = await TestSupport.AddLogisticsManagerAsync(db);
        var (booking, _) = await TestSupport.AddPendingBookingAsync(db, customer, withHorse: true);
        await Bookings(db).ApproveAsync(manager.AccountId, booking.BookingId);
        return (manager, booking);
    }

    /// <summary>The manager returns the approved booking to draft; the customer changes it and submits it again.</summary>
    private static async Task SendBackForChangesAsync(AppDbContext db, Account manager, Booking booking)
    {
        await Bookings(db).NegotiateAsync(manager.AccountId, booking.BookingId,
            new BookingRequest.NegotiateBookingRequest { Status = "DRAFT", Reason = "Customer asked to move the departure date" });
        Assert.Equal(BookingStatus.DRAFT, booking.Status);
        booking.Status = BookingStatus.PENDING;
        await db.SaveChangesAsync();
    }

    private static TransportPlanRequest.CreateRequest PlanRequest()
    {
        var start = DateTimeOffset.UtcNow.AddMonths(3);
        return new TransportPlanRequest.CreateRequest { PlannedStartAt = start, PlannedEndAt = start.AddDays(1) };
    }

    private static BookingServiceImpl Bookings(AppDbContext db) =>
        new(db, TestSupport.CreateMailService(), TestSupport.CreateNotifications(db));

    private static TransportPlanServiceImpl Plans(AppDbContext db) => new(db, TestSupport.CreateNotifications(db));
}
