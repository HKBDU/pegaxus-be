using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.InvoiceService;
using Pegaxus.Service.InvoiceService.Validator;

using BookingInvoiceService = Pegaxus.Service.InvoiceService.Service;

namespace Pegaxus.Service.Test;

public class BookingInvoiceListTests
{
    [Fact]
    public async Task List_CustomerSeesOnlyIssuedOwnInvoicesManagerSeesEveryStatusWithoutWrites()
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        db.ChangeTracker.Clear();
        var result = await f.Service.GetByBookingAsync(f.Booking.Customer.AccountId, f.Booking.BookingId, new());
        Assert.Equal(4, result.TotalItems);
        Assert.Equal(1, result.PageIndex);
        Assert.Equal(20, result.PageSize);
        Assert.Equal(1, result.TotalPages);
        Assert.Equal(new[] { 4, 3, 2, 1 }.Select(index => f.Invoices[index].InvoiceId), result.Items.Select(item => item.InvoiceId));
        Assert.DoesNotContain(result.Items, item => item.Status == "DRAFT" || item.IssuedAt is null);
        var item = result.Items.Single(item => item.InvoiceId == f.Invoices[2].InvoiceId);
        Assert.Equal("INV-BOOKING-0003", item.InvoiceNumber);
        Assert.Equal("FINAL", item.Type);
        Assert.Equal("PAID", item.Status);
        Assert.Equal(f.Booking.BookingId, item.BookingId);
        Assert.Equal(f.Booking.BookingCode, item.BookingCode);
        Assert.Equal(f.LatestPlan.TransportPlanId, item.TransportPlanId);
        Assert.Equal("Club Alpha", item.CustomerName);
        Assert.Equal(10, item.TotalAmount);
        Assert.Equal("VND", item.Currency);
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 0, 0, 0, TimeSpan.Zero), item.DueDate);
        Assert.Equal(Fixture.Offset(f.Invoices[2].IssuedAt!.Value), item.IssuedAt);
        Assert.Equal(Fixture.Offset(f.Invoices[2].PaidAt!.Value), item.PaidAt);
        Assert.Equal(Fixture.Offset(f.Invoices[2].CreatedAt), item.CreatedAt);
        Assert.Equal(Fixture.Offset(f.Invoices[2].UpdatedAt), item.UpdatedAt);
        result = await f.Service.GetByBookingAsync(f.Account.AccountId, f.Booking.BookingId, new());
        Assert.Equal(7, result.TotalItems);
        Assert.Equal(5, result.Items.Select(item => item.Type).Distinct().Count());
        Assert.Equal(5, result.Items.Select(item => item.Status).Distinct().Count());
        result = await f.Service.GetByBookingAsync(f.Account.AccountId, f.OtherBooking.BookingId, new());
        Assert.Null(Assert.Single(result.Items).TransportPlanId);
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Equal(8, await db.Invoices.CountAsync());
        Assert.Empty(await db.InvoiceStatusHistories.ToListAsync());
        Assert.Empty(await db.Notifications.ToListAsync());
        Assert.Empty(await db.OutboxMessages.ToListAsync());
    }

    [Fact]
    public async Task Filters_DoNotExposeDraftsOrUnissuedCancelledInvoicesAndEmptyBookingsReturn200()
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        var owner = f.Booking.Customer.AccountId;
        var result = await f.Service.GetByBookingAsync(owner, f.Booking.BookingId, new() { Type = " final ", Status = " paid " });
        Assert.Equal(f.Invoices[2].InvoiceId, Assert.Single(result.Items).InvoiceId);
        result = await f.Service.GetByBookingAsync(owner, f.Booking.BookingId, new() { Status = "DRAFT" });
        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalItems);
        Assert.Equal(0, result.TotalPages);
        result = await f.Service.GetByBookingAsync(owner, f.Booking.BookingId, new() { Status = "CANCELLED" });
        Assert.Equal(f.Invoices[4].InvoiceId, Assert.Single(result.Items).InvoiceId);
        result = await f.Service.GetByBookingAsync(f.Account.AccountId, f.Booking.BookingId, new() { Status = "CANCELLED" });
        Assert.Equal(2, result.TotalItems);
        result = await f.Service.GetByBookingAsync(owner, f.EmptyBooking.BookingId, new());
        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalPages);
        result = await f.Service.GetByBookingAsync(owner, f.Booking.BookingId, new() { Type = " ", Status = " " });
        Assert.Equal(4, result.TotalItems);
    }

    [Fact]
    public async Task MissingAndOtherCustomersBooking_ReturnIdentical404()
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        foreach (var id in new[] { Guid.NewGuid(), f.OtherBooking.BookingId })
        {
            var error = await Assert.ThrowsAsync<NotFoundException>(() => f.Service.GetByBookingAsync(f.Booking.Customer.AccountId, id, new()));
            Assert.Equal("BOOKING_NOT_FOUND", error.ErrorCode);
            Assert.Equal("Booking not found", error.Message);
        }
        await Assert.ThrowsAsync<NotFoundException>(() => f.Service.GetByBookingAsync(f.Account.AccountId, Guid.NewGuid(), new()));
    }

    [Theory]
    [InlineData("createdAt:asc", new[] { 0, 1, 2, 3, 4, 5, 6 })]
    [InlineData("createdAt:desc", new[] { 6, 5, 4, 3, 2, 1, 0 })]
    [InlineData("issuedAt:asc", new[] { 1, 2, 3, 4, 6, 0, 5 })]
    [InlineData("issuedAt:desc", new[] { 6, 4, 3, 2, 1, 0, 5 })]
    [InlineData("dueDate:asc", new[] { 4, 0, 1, 2, 5, 3, 6 })]
    [InlineData("dueDate:desc", new[] { 5, 2, 1, 0, 4, 3, 6 })]
    [InlineData("totalAmount:asc", new[] { 2, 1, 0, 3, 4, 5, 6 })]
    [InlineData("totalAmount:desc", new[] { 6, 5, 4, 3, 0, 1, 2 })]
    public async Task Sort_EveryAllowedFieldKeepsNullsLast(string sort, int[] indexes)
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        var result = await f.Service.GetByBookingAsync(f.Account.AccountId, f.Booking.BookingId, new() { SortBy = " " + sort.ToUpperInvariant() + " " });
        Assert.Equal(indexes.Select(index => f.Invoices[index].InvoiceId), result.Items.Select(item => item.InvoiceId));
    }

    [Fact]
    public async Task Pagination_CountsOnlyVisibleInvoicesAndOrdersTiesStably()
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        f.Invoices.ForEach(item => item.CreatedAt = Fixture.Day(1));
        await db.SaveChangesAsync();
        var result = await f.Service.GetByBookingAsync(f.Booking.Customer.AccountId, f.Booking.BookingId, new() { PageIndex = 2, PageSize = 2 });
        Assert.Equal(new[] { f.Invoices[3].InvoiceId, f.Invoices[4].InvoiceId }, result.Items.Select(item => item.InvoiceId));
        Assert.Equal(4, result.TotalItems);
        Assert.Equal(2, result.TotalPages);
        result = await f.Service.GetByBookingAsync(f.Booking.Customer.AccountId, f.Booking.BookingId, new() { PageIndex = 3, PageSize = 2 });
        Assert.Empty(result.Items);
        Assert.Equal(4, result.TotalItems);
        Assert.Equal(2, result.TotalPages);
    }

    [Theory]
    [InlineData("role")]
    [InlineData("employeeInactive")]
    [InlineData("employeeDeleted")]
    [InlineData("accountInactive")]
    [InlineData("accountDeleted")]
    [InlineData("customerDeleted")]
    [InlineData("unknownAccount")]
    public async Task Authorization_UsesLiveAccountAndProfileState(string problem)
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        switch (problem)
        {
            case "role": f.Employee.Role = EmployeeRole.DRIVER; break;
            case "employeeInactive": f.Employee.IsActive = false; break;
            case "employeeDeleted": f.Employee.IsDeleted = true; break;
            case "accountInactive": f.Account.Status = AccountStatus.INACTIVE; break;
            case "accountDeleted": f.Account.IsDeleted = true; break;
            case "customerDeleted": f.Booking.Customer.IsDeleted = true; break;
        }
        await db.SaveChangesAsync();
        var id = problem == "customerDeleted" ? f.Booking.Customer.AccountId : problem == "unknownAccount" ? Guid.NewGuid() : f.Account.AccountId;
        await Assert.ThrowsAsync<ForbiddenException>(() => f.Service.GetByBookingAsync(id, f.Booking.BookingId, new()));
    }

    [Theory]
    [InlineData("pageIndex", "pageIndex")]
    [InlineData("pageSize", "pageSize")]
    [InlineData("pageSizeMax", "pageSize")]
    [InlineData("pageOverflow", "pageIndex")]
    [InlineData("type", "type")]
    [InlineData("numericType", "type")]
    [InlineData("status", "status")]
    [InlineData("legacyStatus", "status")]
    [InlineData("sort", "sortBy")]
    [InlineData("nullSort", "sortBy")]
    [InlineData("bookingId", "bookingId")]
    public async Task InvalidInput_ReturnsValidationErrorWithCamelCaseField(string problem, string field)
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        var request = new Request.BookingListRequest();
        switch (problem)
        {
            case "pageIndex": request.PageIndex = 0; break;
            case "pageSize": request.PageSize = 0; break;
            case "pageSizeMax": request.PageSize = 101; break;
            case "pageOverflow": request.PageIndex = int.MaxValue; break;
            case "type": request.Type = "INVALID"; break;
            case "numericType": request.Type = "1"; break;
            case "status": request.Status = "2"; break;
            case "legacyStatus": request.Status = "PARTIALLY_PAID"; break;
            case "sort": request.SortBy = "invoiceNumber:asc"; break;
            case "nullSort": request.SortBy = null!; break;
        }
        var error = await Assert.ThrowsAsync<FieldValidationException>(() => f.Service.GetByBookingAsync(f.Account.AccountId,
            problem == "bookingId" ? Guid.Empty : f.Booking.BookingId, request));
        Assert.Equal("VALIDATION_ERROR", error.ErrorCode);
        Assert.Contains(error.Errors, item => item.Field == field);
    }

    internal sealed record Fixture(Account Account, Employee Employee, Booking Booking, Booking OtherBooking, Booking EmptyBooking,
        TransportPlan LatestPlan, List<Invoice> Invoices, BookingInvoiceService Service)
    {
        internal static async Task<Fixture> CreateAsync(AppDbContext db)
        {
            var (account, employee) = await TestSupport.AddLogisticsManagerAsync(db);
            var (_, alpha) = await TestSupport.AddCustomerAsync(db);
            var (_, bravo) = await TestSupport.AddCustomerAsync(db);
            alpha.FullName = "Club Alpha"; bravo.FullName = "Club Bravo";
            var (booking, _) = await TestSupport.AddPendingBookingAsync(db, alpha);
            var (other, _) = await TestSupport.AddPendingBookingAsync(db, bravo);
            var (empty, _) = await TestSupport.AddPendingBookingAsync(db, alpha);
            booking.BookingCode = "BOOKING-ALPHA"; other.BookingCode = "BOOKING-BRAVO";
            var transport = new Transport { TransportId = Guid.NewGuid(), TransportCode = "TR-BOOKING-LIST", BookingId = booking.BookingId,
                CreatedAt = Day(1), UpdatedAt = Day(1) };
            var latest = Plan(transport.TransportId, 2, TransportPlanStatus.DRAFT);
            db.AddRange(transport, Plan(transport.TransportId, 1, TransportPlanStatus.COMPLETED), latest,
                Plan(transport.TransportId, 3, TransportPlanStatus.CANCELLED));
            var statuses = new[] { InvoiceStatus.DRAFT, InvoiceStatus.ISSUED, InvoiceStatus.PAID, InvoiceStatus.OVERDUE,
                InvoiceStatus.CANCELLED, InvoiceStatus.CANCELLED, InvoiceStatus.DRAFT, InvoiceStatus.ISSUED };
            var totals = new decimal[] { 30, 20, 10, 40, 50, 60, 70, 80 };
            var invoices = new List<Invoice>();
            for (var index = 0; index < 8; index++)
                invoices.Add(new Invoice { InvoiceId = Guid.Parse($"00000000-0000-0000-0000-{index + 1:D12}"),
                    InvoiceCode = $"INV-BOOKING-{index + 1:D4}", BookingId = index == 7 ? other.BookingId : booking.BookingId,
                    EmployeeId = employee.EmployeeId, Type = (InvoiceType)(index % 5 + 1), Status = statuses[index], Currency = "VND",
                    TotalAmount = totals[index], DueDate = index is 3 or 6 or 7 ? null : new(2026, 10, index == 4 ? 10 : index == 5 ? 30 : 15 + index * 5),
                    IssuedAt = index is 0 or 5 ? null : index == 1 ? Day(5) : index == 2 ? Day(6).AddTicks(-10) : Day(index + 3),
                    PaidAt = index == 2 ? Day(6) : null, CreatedAt = Day(index + 1), UpdatedAt = Day(index + 1).AddHours(1) });
            db.Invoices.AddRange(invoices);
            await db.SaveChangesAsync();
            return new(account, employee, booking, other, empty, latest, invoices,
                new BookingInvoiceService(db, new InvoiceOnlinePaymentTests.Gateway(), InvoiceOnlinePaymentTests.Logger,
                    TimeProvider.System, new CreateRequestValidator(TimeProvider.System),
                    new SuggestionRequestValidator(), new UpdateRequestValidator(TimeProvider.System), new ListRequestValidator(), new BookingListRequestValidator(),
                    new IssueRequestValidator(), new RecordPaymentRequestValidator(TimeProvider.System), new CancelRequestValidator(), new NotificationService.Service(db, new NotificationService.NoopNotificationRealtimeService())));
        }
        internal static DateTime Day(int day) => new(2026, 10, day, 0, 0, 0, DateTimeKind.Unspecified);
        internal static DateTimeOffset Offset(DateTime date) => new(DateTime.SpecifyKind(date, DateTimeKind.Utc));
        private static TransportPlan Plan(Guid transportId, int day, TransportPlanStatus status) => new()
        {
            TransportPlanId = Guid.NewGuid(), TransportId = transportId, PlanCode = $"TP-BOOKING-LIST-{day}", Status = status,
            CreatedAt = Day(day), UpdatedAt = Day(day)
        };
    }
}
