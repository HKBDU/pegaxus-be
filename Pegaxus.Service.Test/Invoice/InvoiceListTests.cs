using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.InvoiceService;
using Pegaxus.Service.InvoiceService.Validator;
using ListInvoiceService = Pegaxus.Service.InvoiceService.Service;

namespace Pegaxus.Service.Test;

public class InvoiceListTests
{
    [Fact]
    public async Task List_IncludesEveryTypeAndStatusProjectsContractFieldsWithoutWritesOrTracking()
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        db.ChangeTracker.Clear();
        var result = await f.Service.GetListAsync(f.Account.AccountId, new());
        Assert.Equal(1, result.PageIndex);
        Assert.Equal(20, result.PageSize);
        Assert.Equal(5, result.TotalItems);
        Assert.Equal(1, result.TotalPages);
        Assert.Equal(f.Invoices.AsEnumerable().Reverse().Select(item => item.InvoiceId), result.Items.Select(item => item.InvoiceId));
        Assert.Equal(5, result.Items.Select(item => item.Type).Distinct().Count());
        Assert.Equal(5, result.Items.Select(item => item.Status).Distinct().Count());
        var item = result.Items.Single(item => item.InvoiceId == f.Invoices[2].InvoiceId);
        Assert.Equal(f.Invoices[2].InvoiceCode, item.InvoiceNumber);
        Assert.Equal(f.Booking.BookingId, item.BookingId);
        Assert.Equal(f.Booking.BookingCode, item.BookingCode);
        Assert.Equal(f.LatestPlan.TransportPlanId, item.TransportPlanId);
        Assert.Equal(f.Booking.Customer.FullName, item.CustomerName);
        Assert.Equal(10, item.TotalAmount);
        Assert.Equal("VND", item.Currency);
        Assert.Equal(f.Invoices[2].DueDate, item.DueDate);
        Assert.Equal(Fixture.Offset(f.Invoices[2].IssuedAt!.Value), item.IssuedAt);
        Assert.Equal(Fixture.Offset(f.Invoices[2].PaidAt!.Value), item.PaidAt);
        Assert.Equal(Fixture.Offset(f.Invoices[2].CreatedAt), item.CreatedAt);
        Assert.Equal(Fixture.Offset(f.Invoices[2].UpdatedAt), item.UpdatedAt);
        Assert.Null(result.Items.Single(item => item.InvoiceId == f.Invoices[4].InvoiceId).TransportPlanId);
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Empty(await db.InvoiceStatusHistories.ToListAsync());
        Assert.Empty(await db.Notifications.ToListAsync());
        Assert.Empty(await db.OutboxMessages.ToListAsync());
    }

    [Fact]
    public async Task Filters_CombineTypeStatusBookingAndPlanForTheSelectedTransport()
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        var result = await f.Service.GetListAsync(f.Account.AccountId, new()
        {
            Type = " final ", Status = " paid ", BookingId = f.Booking.BookingId, TransportPlanId = f.OldPlan.TransportPlanId
        });
        Assert.Equal(f.Invoices[2].InvoiceId, Assert.Single(result.Items).InvoiceId);
        Assert.Equal(f.LatestPlan.TransportPlanId, result.Items[0].TransportPlanId);
        result = await f.Service.GetListAsync(f.Account.AccountId, new() { TransportPlanId = f.CancelledPlan.TransportPlanId });
        Assert.Equal(4, result.TotalItems);
        result = await f.Service.GetListAsync(f.Account.AccountId, new() { BookingId = f.OtherBooking.BookingId });
        Assert.Equal(f.Invoices[4].InvoiceId, Assert.Single(result.Items).InvoiceId);
        result = await f.Service.GetListAsync(f.Account.AccountId, new() { BookingId = f.OtherBooking.BookingId, TransportPlanId = f.OldPlan.TransportPlanId });
        Assert.Empty(result.Items);
        result = await f.Service.GetListAsync(f.Account.AccountId, new() { TransportPlanId = Guid.NewGuid() });
        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalPages);
    }

    [Fact]
    public async Task Dates_AreInclusiveWithIndependentBoundsAndExcludeNulls()
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        var day = DateOnly.FromDateTime(Fixture.Day(5));
        var result = await f.Service.GetListAsync(f.Account.AccountId, new() { IssuedFrom = day, IssuedTo = day });
        Assert.Equal(new[] { f.Invoices[2].InvoiceId, f.Invoices[1].InvoiceId }, result.Items.Select(item => item.InvoiceId));
        result = await f.Service.GetListAsync(f.Account.AccountId, new() { IssuedFrom = day });
        Assert.Equal(3, result.TotalItems);
        result = await f.Service.GetListAsync(f.Account.AccountId, new() { IssuedTo = day });
        Assert.Equal(2, result.TotalItems);
        result = await f.Service.GetListAsync(f.Account.AccountId, new() { DueFrom = new(2026, 10, 20), DueTo = new(2026, 10, 25) });
        Assert.Equal(2, result.TotalItems);
        result = await f.Service.GetListAsync(f.Account.AccountId, new() { DueFrom = new(2026, 10, 20) });
        Assert.Equal(2, result.TotalItems);
        result = await f.Service.GetListAsync(f.Account.AccountId, new() { DueTo = new(2026, 10, 15) });
        Assert.Equal(2, result.TotalItems);
        result = await f.Service.GetListAsync(f.Account.AccountId, new() { IssuedTo = DateOnly.MaxValue });
        Assert.Equal(3, result.TotalItems);
    }

    [Theory]
    [InlineData(" inv-list-0003 ", 1)]
    [InlineData(" booking-alpha ", 4)]
    [InlineData(" club bravo ", 1)]
    [InlineData("   ", 5)]
    [InlineData("absent", 0)]
    public async Task Search_MatchesInvoiceNumberBookingCodeAndCustomerNameIgnoringCase(string search, int expected)
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        Assert.Equal(expected, (await f.Service.GetListAsync(f.Account.AccountId, new() { Search = search })).TotalItems);
    }

    [Theory]
    [InlineData("createdAt:asc", new[] { 0, 1, 2, 3, 4 })]
    [InlineData("createdAt:desc", new[] { 4, 3, 2, 1, 0 })]
    [InlineData("issuedAt:asc", new[] { 1, 2, 3, 0, 4 })]
    [InlineData("issuedAt:desc", new[] { 3, 2, 1, 0, 4 })]
    [InlineData("dueDate:asc", new[] { 4, 0, 1, 2, 3 })]
    [InlineData("dueDate:desc", new[] { 2, 1, 0, 4, 3 })]
    [InlineData("totalAmount:asc", new[] { 2, 1, 0, 3, 4 })]
    [InlineData("totalAmount:desc", new[] { 4, 3, 0, 1, 2 })]
    public async Task Sort_OrdersEveryAllowedFieldWithNullDatesLast(string sort, int[] expected)
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        var result = await f.Service.GetListAsync(f.Account.AccountId, new() { SortBy = " " + sort.ToUpperInvariant() + " " });
        Assert.Equal(expected.Select(index => f.Invoices[index].InvoiceId), result.Items.Select(item => item.InvoiceId));
    }

    [Fact]
    public async Task Pagination_TiesUseStableIdentifiersAndOutOfRangePageKeepsTotals()
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        f.Invoices.ForEach(item => item.CreatedAt = Fixture.Day(1));
        await db.SaveChangesAsync();
        var result = await f.Service.GetListAsync(f.Account.AccountId, new() { PageIndex = 2, PageSize = 2 });
        Assert.Equal(f.Invoices.Skip(2).Take(2).Select(item => item.InvoiceId), result.Items.Select(item => item.InvoiceId));
        Assert.Equal(5, result.TotalItems);
        Assert.Equal(3, result.TotalPages);
        result = await f.Service.GetListAsync(f.Account.AccountId, new() { PageIndex = 4, PageSize = 2 });
        Assert.Empty(result.Items);
        Assert.Equal(5, result.TotalItems);
        Assert.Equal(3, result.TotalPages);
    }

    [Theory]
    [InlineData("customer")]
    [InlineData("role")]
    [InlineData("employeeInactive")]
    [InlineData("employeeDeleted")]
    [InlineData("accountInactive")]
    [InlineData("accountDeleted")]
    [InlineData("unknownAccount")]
    public async Task Authorization_RejectsCustomerAndStaleManagerAccounts(string problem)
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
        }
        await db.SaveChangesAsync();
        var id = problem == "customer" ? f.Booking.Customer.AccountId : problem == "unknownAccount" ? Guid.NewGuid() : f.Account.AccountId;
        var error = await Assert.ThrowsAsync<ForbiddenException>(() => f.Service.GetListAsync(id, new()));
        if (problem == "customer") Assert.Equal("Customer cannot access the invoice list", error.Message);
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
    [InlineData("bookingId", "bookingId")]
    [InlineData("planId", "transportPlanId")]
    [InlineData("issuedRange", "issuedTo")]
    [InlineData("dueRange", "dueTo")]
    [InlineData("search", "search")]
    [InlineData("sort", "sortBy")]
    [InlineData("nullSort", "sortBy")]
    public async Task InvalidQuery_ReturnsValidationErrorsWithCorrectField(string problem, string field)
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        var request = new Request.ListRequest();
        switch (problem)
        {
            case "pageIndex": request.PageIndex = 0; break;
            case "pageSize": request.PageSize = 0; break;
            case "pageSizeMax": request.PageSize = 101; break;
            case "pageOverflow": request.PageIndex = int.MaxValue; break;
            case "type": request.Type = "INVALID"; break;
            case "numericType": request.Type = "1"; break;
            case "status": request.Status = "3"; break;
            case "legacyStatus": request.Status = "PARTIALLY_PAID"; break;
            case "bookingId": request.BookingId = Guid.Empty; break;
            case "planId": request.TransportPlanId = Guid.Empty; break;
            case "issuedRange": request.IssuedFrom = new(2026, 10, 2); request.IssuedTo = new(2026, 10, 1); break;
            case "dueRange": request.DueFrom = new(2026, 10, 2); request.DueTo = new(2026, 10, 1); break;
            case "search": request.Search = new string('x', 201); break;
            case "sort": request.SortBy = "invoiceNumber:asc"; break;
            case "nullSort": request.SortBy = null!; break;
        }
        var error = await Assert.ThrowsAsync<FieldValidationException>(() => f.Service.GetListAsync(f.Account.AccountId, request));
        Assert.Equal("VALIDATION_ERROR", error.ErrorCode);
        Assert.Contains(error.Errors, item => item.Field == field);
    }

    internal sealed record Fixture(Account Account, Employee Employee, Booking Booking, Booking OtherBooking,
        TransportPlan OldPlan, TransportPlan LatestPlan, TransportPlan CancelledPlan, List<Invoice> Invoices, ListInvoiceService Service)
    {
        internal static async Task<Fixture> CreateAsync(AppDbContext db)
        {
            var (account, employee) = await TestSupport.AddLogisticsManagerAsync(db);
            var (_, alpha) = await TestSupport.AddCustomerAsync(db);
            var (_, bravo) = await TestSupport.AddCustomerAsync(db);
            alpha.FullName = "Club Alpha"; bravo.FullName = "Club Bravo";
            var (booking, _) = await TestSupport.AddPendingBookingAsync(db, alpha);
            var (other, _) = await TestSupport.AddPendingBookingAsync(db, bravo);
            booking.BookingCode = "BOOKING-ALPHA"; other.BookingCode = "BOOKING-BRAVO";
            var transport = new Transport { TransportId = Guid.NewGuid(), TransportCode = "TR-LIST", BookingId = booking.BookingId,
                CreatedAt = Day(1), UpdatedAt = Day(1) };
            var oldPlan = Plan(transport.TransportId, 1, TransportPlanStatus.COMPLETED);
            var latest = Plan(transport.TransportId, 2, TransportPlanStatus.DRAFT);
            var cancelled = Plan(transport.TransportId, 3, TransportPlanStatus.CANCELLED);
            var invoices = new List<Invoice>();
            var statuses = new[] { InvoiceStatus.DRAFT, InvoiceStatus.ISSUED, InvoiceStatus.PAID, InvoiceStatus.OVERDUE, InvoiceStatus.CANCELLED };
            var totals = new decimal[] { 30, 20, 10, 40, 50 };
            for (var index = 0; index < 5; index++)
                invoices.Add(new Invoice { InvoiceId = Guid.Parse($"00000000-0000-0000-0000-{index + 1:D12}"),
                    InvoiceCode = $"INV-LIST-{index + 1:D4}", BookingId = index == 4 ? other.BookingId : booking.BookingId,
                    EmployeeId = employee.EmployeeId, Type = (InvoiceType)(index + 1), Status = statuses[index], Currency = "VND",
                    TotalAmount = totals[index], DueDate = index == 3 ? null : new(2026, 10, index == 4 ? 10 : 15 + index * 5),
                    IssuedAt = index == 1 ? Day(5) : index == 2 ? Day(6).AddTicks(-10) : index == 3 ? Day(6) : null,
                    PaidAt = index == 2 ? Day(6) : null, CreatedAt = Day(index + 1), UpdatedAt = Day(index + 1).AddHours(1) });
            db.AddRange(transport, oldPlan, latest, cancelled);
            db.Invoices.AddRange(invoices);
            await db.SaveChangesAsync();
            return new(account, employee, booking, other, oldPlan, latest, cancelled, invoices,
                new ListInvoiceService(db, TimeProvider.System, new CreateRequestValidator(TimeProvider.System),
                    new SuggestionRequestValidator(), new UpdateRequestValidator(TimeProvider.System), new ListRequestValidator()));
        }
        internal static DateTime Day(int day) => new(2026, 10, day, 0, 0, 0, DateTimeKind.Unspecified);
        internal static DateTimeOffset Offset(DateTime date) => new(DateTime.SpecifyKind(date, DateTimeKind.Utc));
        private static TransportPlan Plan(Guid transportId, int day, TransportPlanStatus status) => new()
        {
            TransportPlanId = Guid.NewGuid(), TransportId = transportId, PlanCode = $"TP-LIST-{day}", Status = status, CreatedAt = Day(day), UpdatedAt = Day(day)
        };
    }
}
