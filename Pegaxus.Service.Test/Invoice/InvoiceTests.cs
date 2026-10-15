using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.InvoiceService;
using Pegaxus.Service.InvoiceService.Validator;

using DraftInvoiceService = Pegaxus.Service.InvoiceService.Service;

namespace Pegaxus.Service.Test;

public class InvoiceTests
{
    [Fact]
    public async Task Final_CalculatesContractExampleAndPersistsDraftWithoutSideEffects()
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        var paid = f.Invoice(InvoiceType.DEPOSIT, InvoiceStatus.PAID, 30_000_000);
        db.Invoices.AddRange(paid, f.Invoice(InvoiceType.DEPOSIT, InvoiceStatus.CANCELLED, 99_000_000));
        await db.SaveChangesAsync();
        var request = Fixture.Request("FINAL");
        request.Items = [Fixture.Item(1, 120_000_000), Fixture.Item(4, 5_000_000),
            Fixture.Item(1, 8_000_000), Fixture.Item(1, 3_500_000)];
        request.Items[1].HorseId = f.Horse.HorseId;
        request.Items[3].ItemType = "EMERGENCY_COST";
        request.Note = "Final settlement";
        request.Payment = JsonSerializer.SerializeToElement(new { method = "BANKING", qrCodeUrl = "/qr.png" });
        request.Items[0].ItemDate = JsonSerializer.SerializeToElement("not-a-date");
        request.Items.Add(new() { IsDepositDeduction = true, Price = -99_000_000, HorseId = Guid.NewGuid() });

        var result = await f.Service.CreateAsync(f.Account.AccountId, f.Booking.BookingId, request);

        Assert.Matches("^INV-20261012-[0-9]{4}$", result.InvoiceNumber);
        Assert.Equal("DRAFT", result.Status);
        Assert.Equal(new Response.SummaryResponse(151_500_000, 30_300_000, -30_000_000, 151_800_000), result.Summary);
        Assert.Equal(0, result.Summary.Discount);
        Assert.Equal(20, result.Summary.TaxRatePercent);
        Assert.Equal(f.Horse.HorseName, result.Items[1].Horse!.HorseName);
        Assert.Equal("EMERGENCY_COST", result.Items[3].ItemType);
        var deduction = Assert.Single(result.Items.Where(item => item.IsDepositDeduction));
        Assert.Equal($"Deposit deduction ({paid.InvoiceCode})", deduction.Description);
        Assert.Equal(-30_000_000, deduction.Price);
        Assert.Equal(1, deduction.Quantity);
        Assert.Null(deduction.Horse);
        Assert.Null(result.Payment);
        Assert.All(result.Items, item => { Assert.Null(item.ItemDate); Assert.Null(item.EmergencyCostRequestId); });
        Assert.Equal(f.Account.Email, result.ContactPerson.Email);
        Assert.Equal(f.Booking.Customer.Account.Email, result.Customer.Email);
        Assert.Equal(f.Plan.TransportPlanId, result.TransportPlan!.TransportPlanId);
        Assert.Equal(["EDIT", "ISSUE", "CANCEL", "CREATE_ANOTHER"], result.AvailableActions);
        Assert.Equal(Fixture.Clock.GetUtcNow(), result.CreatedAt);
        Assert.Equal(result.CreatedAt, result.UpdatedAt);
        db.ChangeTracker.Clear();
        var invoice = await db.Invoices.Include(item => item.InvoiceItems).SingleAsync(item => item.InvoiceId == result.InvoiceId);
        Assert.Equal(f.Employee.EmployeeId, invoice.EmployeeId);
        Assert.Equal(InvoiceStatus.DRAFT, invoice.Status);
        Assert.Equal(5, invoice.InvoiceItems.Count);
        Assert.Equal(result.Summary.GrandTotal, invoice.TotalAmount);
        Assert.Equal(request.Note, invoice.BillingNote);
        var history = await db.InvoiceStatusHistories.SingleAsync();
        Assert.Equal(invoice.InvoiceId, history.InvoiceId);
        Assert.Equal(InvoiceHistoryType.CREATE, history.Type);
        Assert.Null(history.PreviousStatus);
        Assert.Equal(InvoiceStatus.DRAFT, history.NewStatus);
        Assert.Equal(f.Account.AccountId, history.ChangedByAccountId);
        Assert.Empty(await db.Receipts.ToListAsync());
        Assert.Empty(await db.Notifications.ToListAsync());
        Assert.Empty(await db.OutboxMessages.ToListAsync());
        Assert.Equal(TransportPlanStatus.DRAFT, (await db.TransportPlans.SingleAsync()).Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Draft_AllowsEmptyItemsAndNonPositiveTotal(bool withDeposits)
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        if (withDeposits)
        {
            db.Invoices.AddRange(f.Invoice(InvoiceType.DEPOSIT, InvoiceStatus.PAID, 25),
                f.Invoice(InvoiceType.DEPOSIT, InvoiceStatus.PAID, 50));
            await db.SaveChangesAsync();
        }
        var result = await f.Service.CreateAsync(f.Account.AccountId, f.Booking.BookingId,
            Fixture.Request(withDeposits ? "FINAL" : "QUOTATION"));
        Assert.Equal(withDeposits ? -75 : 0, result.Summary.GrandTotal);
        Assert.Equal(withDeposits ? 2 : 0, result.Items.Count);
        Assert.Equal(withDeposits, result.IsPaymentRequired);
    }

    [Fact]
    public async Task Amounts_RoundEachLineThenTaxToTwoDecimals()
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        var request = Fixture.Request();
        request.Items = [Fixture.Item(1.5m, 0.03m), Fixture.Item(1.5m, 0.03m)];
        var result = await f.Service.CreateAsync(f.Account.AccountId, f.Booking.BookingId, request);
        Assert.All(result.Items, item => Assert.Equal(0.05m, item.Total));
        Assert.Equal(new Response.SummaryResponse(0.10m, 0.02m, 0, 0.12m), result.Summary);
    }

    [Fact]
    public async Task Response_UsesNewestNonCancelledPlanOrNullWhenNoneExists()
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        var latest = new TransportPlan { TransportPlanId = Guid.NewGuid(), TransportId = f.Transport.TransportId,
            PlanCode = "TP-LATEST", Status = TransportPlanStatus.PENDING, CreatedAt = f.Plan.CreatedAt.AddDays(1) };
        var cancelled = new TransportPlan { TransportPlanId = Guid.NewGuid(), TransportId = f.Transport.TransportId,
            PlanCode = "TP-CANCELLED", Status = TransportPlanStatus.CANCELLED, CreatedAt = f.Plan.CreatedAt.AddDays(2) };
        db.TransportPlans.AddRange(latest, cancelled);
        await db.SaveChangesAsync();
        var result = await f.Service.CreateAsync(f.Account.AccountId, f.Booking.BookingId, Fixture.Request());
        Assert.Equal(latest.TransportPlanId, result.TransportPlan!.TransportPlanId);
        latest.Status = f.Plan.Status = TransportPlanStatus.CANCELLED;
        await db.SaveChangesAsync();
        result = await f.Service.CreateAsync(f.Account.AccountId, f.Booking.BookingId, Fixture.Request("SUPPLEMENTARY"));
        Assert.Null(result.TransportPlan);
    }

    public static IEnumerable<object[]> TransportCases() =>
        from type in Enum.GetValues<InvoiceType>()
        from status in Enum.GetValues<TransportStatus>()
        select new object[] { type, status, type switch
        {
            InvoiceType.DEPOSIT => status is TransportStatus.CREATED or TransportStatus.PLANNING or TransportStatus.READY,
            InvoiceType.FINAL => status == TransportStatus.COMPLETED,
            _ => status != TransportStatus.CANCELLED
        } };

    [Theory]
    [MemberData(nameof(TransportCases))]
    public async Task TransportStatus_EnforcesAllFiveTypeGates(InvoiceType type, TransportStatus status, bool allowed)
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        f.Transport.Status = status;
        if (type == InvoiceType.CREDIT_NOTE) db.Invoices.Add(f.Invoice(InvoiceType.FINAL, InvoiceStatus.PAID));
        await db.SaveChangesAsync();
        var action = () => f.Service.CreateAsync(f.Account.AccountId, f.Booking.BookingId, Fixture.Request(type.ToString()));
        if (allowed) Assert.Equal(type.ToString(), (await action()).Type);
        else Assert.Equal("INVALID_TRANSPORT_STATUS", (await Assert.ThrowsAsync<ConflictException>(action)).ErrorCode);
    }

    public static IEnumerable<object[]> DuplicateCases() =>
        from type in Enum.GetValues<InvoiceType>()
        from status in Enum.GetValues<InvoiceStatus>()
        select new object[] { type, status, type == InvoiceType.FINAL
            ? status != InvoiceStatus.CANCELLED
            : status is InvoiceStatus.DRAFT or InvoiceStatus.ISSUED or InvoiceStatus.OVERDUE };

    [Theory]
    [MemberData(nameof(DuplicateCases))]
    public async Task ExistingInvoice_BlocksOnlyContractStatuses(InvoiceType type, InvoiceStatus status, bool blocked)
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        if (type == InvoiceType.DEPOSIT) f.Transport.Status = TransportStatus.READY;
        if (type == InvoiceType.CREDIT_NOTE) db.Invoices.Add(f.Invoice(InvoiceType.FINAL, InvoiceStatus.PAID));
        db.Invoices.Add(f.Invoice(type, status));
        await db.SaveChangesAsync();
        var action = () => f.Service.CreateAsync(f.Account.AccountId, f.Booking.BookingId, Fixture.Request(type.ToString()));
        if (!blocked) Assert.Equal(type.ToString(), (await action()).Type);
        else Assert.Equal(type == InvoiceType.FINAL ? "FINAL_INVOICE_ALREADY_EXISTS" : "INVOICE_ALREADY_EXISTS",
            (await Assert.ThrowsAsync<ConflictException>(action)).ErrorCode);
    }

    [Theory]
    [InlineData(InvoiceStatus.DRAFT)]
    [InlineData(InvoiceStatus.ISSUED)]
    [InlineData(InvoiceStatus.OVERDUE)]
    [InlineData(InvoiceStatus.CANCELLED)]
    [InlineData(InvoiceStatus.PARTIALLY_PAID)]
    public async Task CreditNote_RequiresPaidFinal(InvoiceStatus status)
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        db.Invoices.Add(f.Invoice(InvoiceType.FINAL, status));
        await db.SaveChangesAsync();
        Assert.Equal("UNPAID_FINAL_INVOICE", (await Assert.ThrowsAsync<UnprocessableEntityException>(() =>
            f.Service.CreateAsync(f.Account.AccountId, f.Booking.BookingId, Fixture.Request("CREDIT_NOTE")))).ErrorCode);
    }

    [Fact]
    public async Task Final_RejectsAnyPaidDepositWithDifferentCurrency()
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        var other = f.Invoice(InvoiceType.DEPOSIT, InvoiceStatus.PAID);
        other.Currency = "EUR";
        db.Invoices.AddRange(f.Invoice(InvoiceType.DEPOSIT, InvoiceStatus.PAID), other);
        await db.SaveChangesAsync();
        Assert.Equal("CURRENCY_MISMATCH", (await Assert.ThrowsAsync<UnprocessableEntityException>(() =>
            f.Service.CreateAsync(f.Account.AccountId, f.Booking.BookingId, Fixture.Request("FINAL")))).ErrorCode);
        Assert.Empty(await db.InvoiceItems.ToListAsync());
    }

    [Theory]
    [InlineData("role")]
    [InlineData("employeeInactive")]
    [InlineData("employeeDeleted")]
    [InlineData("accountInactive")]
    [InlineData("accountDeleted")]
    [InlineData("accountType")]
    [InlineData("unknownAccount")]
    public async Task Authorization_ChecksLiveAccountAndEmployee(string problem)
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
            case "accountType": f.Account.AccountType = AccountType.CUSTOMER; break;
        }
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ForbiddenException>(() => f.Service.CreateAsync(
            problem == "unknownAccount" ? Guid.NewGuid() : f.Account.AccountId, f.Booking.BookingId, Fixture.Request()));
        Assert.Empty(await db.Invoices.ToListAsync());
    }

    [Fact]
    public async Task Booking_RequiresExistenceApprovalAndTransport()
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        Assert.Equal("BOOKING_NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(() =>
            f.Service.CreateAsync(f.Account.AccountId, Guid.NewGuid(), Fixture.Request()))).ErrorCode);
        f.Booking.Status = BookingStatus.PENDING;
        await db.SaveChangesAsync();
        Assert.Equal("INVALID_BOOKING_STATUS", (await Assert.ThrowsAsync<ConflictException>(() =>
            f.Service.CreateAsync(f.Account.AccountId, f.Booking.BookingId, Fixture.Request()))).ErrorCode);
        f.Booking.Status = BookingStatus.APPROVED;
        db.TransportPlans.Remove(f.Plan);
        db.Transports.Remove(f.Transport);
        await db.SaveChangesAsync();
        Assert.Equal("INVALID_TRANSPORT_STATUS", (await Assert.ThrowsAsync<ConflictException>(() =>
            f.Service.CreateAsync(f.Account.AccountId, f.Booking.BookingId, Fixture.Request()))).ErrorCode);
    }

    [Theory]
    [InlineData("type", "type")]
    [InlineData("currency", "currency")]
    [InlineData("dueDate", "dueDate")]
    [InlineData("missingDueDate", "dueDate")]
    [InlineData("missingItems", "items")]
    [InlineData("nullItem", "items[0]")]
    [InlineData("itemType", "items[0].itemType")]
    [InlineData("description", "items[0].description")]
    [InlineData("longDescription", "items[0].description")]
    [InlineData("quantity", "items[0].quantity")]
    [InlineData("quantityPrecision", "items[0].quantity")]
    [InlineData("quantityScale", "items[0].quantity")]
    [InlineData("price", "items[0].price")]
    [InlineData("pricePrecision", "items[0].price")]
    [InlineData("priceScale", "items[0].price")]
    [InlineData("horse", "items[0].horseId")]
    [InlineData("overflow", "items")]
    [InlineData("taxOverflow", "items")]
    public async Task InvalidInput_ReturnsCamelCaseFieldErrorsWithoutSaving(string problem, string field)
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        var request = Fixture.Request();
        request.Items = [Fixture.Item(1, 100)];
        switch (problem)
        {
            case "type": request.Type = "1"; break;
            case "currency": request.Currency = "USD"; break;
            case "dueDate": request.DueDate = request.DueDate!.Value.AddDays(-1); break;
            case "missingDueDate": request.DueDate = null; break;
            case "missingItems": request.Items = null; break;
            case "nullItem": request.Items[0] = null!; break;
            case "itemType": request.Items[0].ItemType = "1"; break;
            case "description": request.Items[0].Description = "  "; break;
            case "longDescription": request.Items[0].Description = new string('x', 501); break;
            case "quantity": request.Items[0].Quantity = 0; break;
            case "quantityPrecision": request.Items[0].Quantity = 100_000_000; break;
            case "quantityScale": request.Items[0].Quantity = 1.001m; break;
            case "price": request.Items[0].Price = -1; break;
            case "pricePrecision": request.Items[0].Price = 10_000_000_000_000_000; break;
            case "priceScale": request.Items[0].Price = 1.001m; break;
            case "horse": request.Items[0].HorseId = Guid.NewGuid(); break;
            case "overflow": request.Items[0].Quantity = 99_999_999.99m; request.Items[0].Price = 9_999_999_999_999_999.99m; break;
            case "taxOverflow": request.Items[0].Price = 9_999_999_999_999_999.99m; break;
        }
        var error = await Assert.ThrowsAsync<FieldValidationException>(() =>
            f.Service.CreateAsync(f.Account.AccountId, f.Booking.BookingId, request));
        Assert.Contains(error.Errors, item => item.Field == field);
        Assert.Empty(await db.Invoices.ToListAsync());
        Assert.Empty(await db.InvoiceStatusHistories.ToListAsync());
    }

    internal sealed record Fixture(Account Account, Employee Employee, Booking Booking, Horse Horse,
        Transport Transport, TransportPlan Plan, DraftInvoiceService Service)
    {
        internal static TimeProvider Clock { get; } = new FixedClock();
        internal static async Task<Fixture> CreateAsync(AppDbContext db)
        {
            var (account, employee) = await TestSupport.AddLogisticsManagerAsync(db);
            var (_, customer) = await TestSupport.AddCustomerAsync(db);
            var (booking, horse) = await TestSupport.AddPendingBookingAsync(db, customer, withHorse: true);
            booking.Status = BookingStatus.APPROVED;
            var now = DateTime.SpecifyKind(Clock.GetUtcNow().UtcDateTime, DateTimeKind.Unspecified);
            var transport = new Transport { TransportId = Guid.NewGuid(), TransportCode = $"TR-{Guid.NewGuid():N}",
                BookingId = booking.BookingId, Status = TransportStatus.COMPLETED, CreatedAt = now, UpdatedAt = now };
            var plan = new TransportPlan { TransportPlanId = Guid.NewGuid(), PlanCode = $"TP-{Guid.NewGuid():N}",
                TransportId = transport.TransportId, Status = TransportPlanStatus.DRAFT, CreatedAt = now, UpdatedAt = now };
            db.AddRange(transport, plan);
            await db.SaveChangesAsync();
            return new(account, employee, booking, horse!, transport, plan,
                new DraftInvoiceService(db, new InvoiceOnlinePaymentTests.Gateway(), InvoiceOnlinePaymentTests.Logger,
                    Clock, new CreateRequestValidator(Clock), new SuggestionRequestValidator(), new UpdateRequestValidator(Clock),
                    new ListRequestValidator(), new BookingListRequestValidator(), new IssueRequestValidator(), new RecordPaymentRequestValidator(Clock), new CancelRequestValidator(),
                    new NotificationService.Service(db, new NotificationService.NoopNotificationRealtimeService())));
        }
        internal static Request.CreateRequest Request(string type = "QUOTATION") => new()
        {
            Type = type, Currency = "VND", DueDate = DateOnly.FromDateTime(Clock.GetUtcNow().UtcDateTime), Items = []
        };
        internal static Request.ItemRequest Item(decimal quantity, decimal price) => new()
        {
            ItemType = "TRANSPORT_FEE", Description = "Transport fee", Quantity = quantity, Price = price
        };
        internal Invoice Invoice(InvoiceType type, InvoiceStatus status, decimal total = 100) => new()
        {
            InvoiceId = Guid.NewGuid(), InvoiceCode = $"INV-{Guid.NewGuid():N}", BookingId = Booking.BookingId,
            EmployeeId = Employee.EmployeeId, Type = type, Status = status, Currency = "VND", TotalAmount = total,
            CreatedAt = Plan.CreatedAt, UpdatedAt = Plan.CreatedAt
        };
        private sealed class FixedClock : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => new(2026, 10, 12, 8, 0, 0, TimeSpan.Zero);
        }
    }
}
