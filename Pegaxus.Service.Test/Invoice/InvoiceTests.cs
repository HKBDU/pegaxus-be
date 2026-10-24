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
        var cost = f.Cost(EmergencyCostStatus.APPROVED, 3_500_000);
        db.Invoices.AddRange(paid, f.Invoice(InvoiceType.DEPOSIT, InvoiceStatus.CANCELLED, 99_000_000), f.IssuedQuotation(120_000_000));
        db.EmergencyCostRequests.AddRange(cost, f.Cost(EmergencyCostStatus.REJECTED), f.Cost(EmergencyCostStatus.CANCELLED));
        await db.SaveChangesAsync();
        var request = Fixture.Request("FINAL");
        request.Items = null;
        request.Note = "Final settlement";
        request.Payment = JsonSerializer.SerializeToElement(new { method = "BANKING", qrCodeUrl = "/qr.png" });

        var result = await f.Service.CreateAsync(f.Account.AccountId, f.Booking.BookingId, request);

        Assert.Matches("^INV-20261012-[0-9]{4}$", result.InvoiceNumber);
        Assert.Equal("DRAFT", result.Status);
        Assert.Equal(new Response.SummaryResponse(123_500_000, 24_700_000, -30_000_000, 118_200_000), result.Summary);
        Assert.Equal(0, result.Summary.Discount);
        Assert.Equal(20, result.Summary.TaxRatePercent);
        Assert.Equal(["TRANSPORT_FEE", "EMERGENCY_COST", "OTHER"], result.Items.Select(item => item.ItemType).ToArray());
        Assert.Equal(cost.EmergencyCostRequestId, result.Items[1].EmergencyCostRequestId);
        Assert.Equal(3_500_000, result.Items[1].Price);
        var deduction = Assert.Single(result.Items.Where(item => item.IsDepositDeduction));
        Assert.Equal($"Deposit deduction ({paid.InvoiceCode})", deduction.Description);
        Assert.Equal(-30_000_000, deduction.Price);
        Assert.Equal(1, deduction.Quantity);
        Assert.Null(deduction.Horse);
        Assert.Null(result.Payment);
        Assert.All(result.Items, item => Assert.Null(item.ItemDate));
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
        Assert.Equal(3, invoice.InvoiceItems.Count);
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
            db.Invoices.AddRange(f.IssuedQuotation(10), f.Invoice(InvoiceType.DEPOSIT, InvoiceStatus.PAID, 25),
                f.Invoice(InvoiceType.DEPOSIT, InvoiceStatus.PAID, 50));
            await db.SaveChangesAsync();
        }
        var result = await f.Service.CreateAsync(f.Account.AccountId, f.Booking.BookingId,
            Fixture.Request(withDeposits ? "FINAL" : "SUPPLEMENTARY"));
        Assert.Equal(withDeposits ? -63 : 0, result.Summary.GrandTotal);
        Assert.Equal(withDeposits ? 3 : 0, result.Items.Count);
        Assert.True(result.IsPaymentRequired);
    }

    [Fact]
    public async Task Amounts_AreWholeVnd_AndTaxIsRoundedHalfUpToAWholeVnd()
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        var request = Fixture.Request();
        request.Items = [Fixture.Item(3, 5), Fixture.Item(1, 5)];
        var result = await f.Service.CreateAsync(f.Account.AccountId, f.Booking.BookingId, request);
        Assert.Equal([15L, 5L], result.Items.Select(item => item.Total).ToArray());
        Assert.Equal(new Response.SummaryResponse(20, 4, 0, 24), result.Summary);
        Assert.Equal("VND", result.Currency);
    }

    [Fact]
    public async Task CreditNote_CarriesNoTax_UnlikeEveryOtherType()
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        db.Invoices.Add(f.Invoice(InvoiceType.FINAL, InvoiceStatus.PAID));
        await db.SaveChangesAsync();
        var request = Fixture.Request("CREDIT_NOTE");
        request.Items = [Fixture.Item(1, 1_000)];

        var result = await f.Service.CreateAsync(f.Account.AccountId, f.Booking.BookingId, request);

        Assert.Equal(1_000, result.Summary.Subtotal);
        Assert.Equal(0, result.Summary.Tax);
        Assert.Equal(1_000, result.Summary.GrandTotal);
        Assert.Equal(0, result.Summary.TaxRatePercent);
        Assert.Equal(0, (await db.Invoices.SingleAsync(item => item.InvoiceId == result.InvoiceId)).TaxAmount);
    }

    [Fact]
    public async Task Create_KeepsTheDueTimeOfDay_AndStoresItAsUtc()
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        var request = Fixture.Request();
        request.Items = [Fixture.Item(1, 100)];
        request.DueDate = new DateTimeOffset(2026, 10, 20, 17, 30, 0, TimeSpan.FromHours(7)); // 10:30 UTC

        var result = await f.Service.CreateAsync(f.Account.AccountId, f.Booking.BookingId, request);

        Assert.Equal(new DateTimeOffset(2026, 10, 20, 10, 30, 0, TimeSpan.Zero), result.DueDate);
        Assert.Equal(TimeSpan.Zero, result.DueDate.Offset);
        Assert.Equal(new DateTime(2026, 10, 20, 10, 30, 0), (await db.Invoices.SingleAsync(item => item.InvoiceId == result.InvoiceId)).DueDate);
    }

    [Theory]
    [InlineData(2, 0)]
    [InlineData(3, 1)]
    [InlineData(7, 1)]
    [InlineData(8, 2)]
    [InlineData(12, 2)]
    [InlineData(13, 3)]
    public async Task Tax_IsTwentyPercentRoundedToAWholeVnd(int price, int expectedTax)
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        var request = Fixture.Request();
        request.Items = [Fixture.Item(1, price)];
        var result = await f.Service.CreateAsync(f.Account.AccountId, f.Booking.BookingId, request);
        Assert.Equal(new Response.SummaryResponse(price, expectedTax, 0, price + expectedTax), result.Summary);
    }

    [Fact]
    public async Task Create_IgnoresAnyCurrencyTheClientSends_BecauseEverythingIsVnd()
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        var request = Fixture.Request();
        request.Items = [Fixture.Item(1, 100)];
        var result = await f.Service.CreateAsync(f.Account.AccountId, f.Booking.BookingId, request);
        Assert.Equal("VND", result.Currency);
        Assert.Equal("VND", (await db.Invoices.SingleAsync()).Currency);
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
        var result = await f.Service.CreateAsync(f.Account.AccountId, f.Booking.BookingId, Fixture.Request("QUOTATION"));
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
        if (type is InvoiceType.DEPOSIT or InvoiceType.FINAL) db.Invoices.Add(f.IssuedQuotation());
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
        if (type is InvoiceType.DEPOSIT or InvoiceType.FINAL) db.Invoices.Add(f.IssuedQuotation());
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
            case "dueDate": request.DueDate = request.DueDate!.Value.AddDays(-1).AddMinutes(-1); break; // one minute in the past
            case "missingDueDate": request.DueDate = null; break;
            case "missingItems": request.Items = null; break;
            case "nullItem": request.Items[0] = null!; break;
            case "itemType": request.Items[0].ItemType = "1"; break;
            case "description": request.Items[0].Description = "  "; break;
            case "longDescription": request.Items[0].Description = new string('x', 501); break;
            case "quantity": request.Items[0].Quantity = 0; break;
            case "quantityPrecision": request.Items[0].Quantity = 10_000_000_000; break;
            case "quantityScale": request.Items[0].Quantity = 1.001m; break;
            case "price": request.Items[0].Price = -1; break;
            case "pricePrecision": request.Items[0].Price = 10_000_000_000_000_000; break;
            case "priceScale": request.Items[0].Price = 1.001m; break;
            case "horse": request.Items[0].HorseId = Guid.NewGuid(); break;
            case "overflow": request.Items[0].Quantity = 5000; request.Items[0].Price = 9_999_999_999_999_999; break;
            case "taxOverflow": request.Items[0].Price = 9_999_999_999_999_999; break;
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
        internal static Request.CreateRequest Request(string type = "SUPPLEMENTARY") => new()
        {
            Type = type, DueDate = Clock.GetUtcNow().AddDays(1), Items = []
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
        internal Invoice IssuedQuotation(decimal subtotal = 1_000, InvoiceStatus status = InvoiceStatus.ISSUED)
        {
            var quotation = Invoice(InvoiceType.QUOTATION, status, subtotal * 1.2m);
            quotation.Subtotal = subtotal;
            quotation.TaxAmount = subtotal * 0.2m;
            quotation.IssuedAt = Plan.CreatedAt;
            quotation.InvoiceItems.Add(new InvoiceItem { InvoiceItemId = Guid.NewGuid(), ItemType = InvoiceItemType.TRANSPORT_FEE,
                Description = "Quoted transport", Quantity = 1, UnitPrice = subtotal, LineAmount = subtotal,
                CreatedAt = Plan.CreatedAt, UpdatedAt = Plan.CreatedAt });
            return quotation;
        }
        internal EmergencyCostRequest Cost(EmergencyCostStatus status, decimal? approved = null)
        {
            var incident = new IncidentReport { IncidentReportId = Guid.NewGuid(), IncidentCode = $"INC-{Guid.NewGuid():N}",
                TransportId = Transport.TransportId, EmployeeId = Employee.EmployeeId, LocationId = Guid.NewGuid(),
                Category = IncidentCategory.VEHICLE, Severity = IncidentSeverity.HIGH, Status = IncidentStatus.REPORTED,
                Title = "Breakdown", Description = "Breakdown", OccurredAt = Plan.CreatedAt, ReportedAt = Plan.CreatedAt,
                CreatedAt = Plan.CreatedAt, UpdatedAt = Plan.CreatedAt };
            return new EmergencyCostRequest { EmergencyCostRequestId = Guid.NewGuid(), IncidentReport = incident, RequestCode = $"ECR-{Guid.NewGuid():N}",
                Category = EmergencyCostCategory.TOWING, Status = status, RequestedByEmployeeId = Employee.EmployeeId, Description = "Towing",
                Justification = "Towing", Currency = "VND", RequestedAmount = approved ?? 100, ApprovedAmount = approved,
                RequestedAt = Plan.CreatedAt, CreatedAt = Plan.CreatedAt, UpdatedAt = Plan.CreatedAt };
        }
        private sealed class FixedClock : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => new(2026, 10, 12, 8, 0, 0, TimeSpan.Zero);
        }
    }
}
