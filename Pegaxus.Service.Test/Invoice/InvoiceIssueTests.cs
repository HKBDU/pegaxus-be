using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.InvoiceService.Validator;
using Pegaxus.Service.NotificationService;
using Pegaxus.Service.OutboxService;
using IssuanceService = Pegaxus.Service.InvoiceService.Service;
using Request = Pegaxus.Service.InvoiceService.Request;

namespace Pegaxus.Service.Test;

public class InvoiceIssueTests
{
    internal sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 5, 9, 0, 0, TimeSpan.Zero);
    }
    internal static readonly Clock TestClock = new();
    internal static IssuanceService CreateService(AppDbContext db, INotificationRealtimeService? realtime = null) =>
        new(db, TestClock, new CreateRequestValidator(TestClock), new SuggestionRequestValidator(), new UpdateRequestValidator(TestClock),
            new ListRequestValidator(), new BookingListRequestValidator(), new IssueRequestValidator(), new RecordPaymentRequestValidator(TestClock),
            new Pegaxus.Service.NotificationService.Service(db, realtime ?? new NoopNotificationRealtimeService()));
    internal static Request.IssueRequest Version(Invoice invoice) => new()
    { LastUpdatedAt = new DateTimeOffset(DateTime.SpecifyKind(invoice.UpdatedAt, DateTimeKind.Utc)) };

    internal static async Task<(Account Actor, Account Customer, Invoice Invoice, TransportPlan Plan)> SeedAsync(AppDbContext db, InvoiceType type = InvoiceType.DEPOSIT)
    {
        var (actor, _) = await TestSupport.AddLogisticsManagerAsync(db);
        var (_, creator) = await TestSupport.AddLogisticsManagerAsync(db);
        var (customerAccount, customer) = await TestSupport.AddCustomerAsync(db);
        var (booking, _) = await TestSupport.AddPendingBookingAsync(db, customer);
        booking.Status = BookingStatus.APPROVED;
        var time = new DateTime(2026, 11, 5, 8, 0, 0);
        var transport = new Transport { TransportId = Guid.NewGuid(), BookingId = booking.BookingId, Booking = booking,
            TransportCode = "TR-" + Guid.NewGuid().ToString("N"), Status = TransportStatus.COMPLETED, CreatedAt = time, UpdatedAt = time };
        var plan = new TransportPlan { TransportPlanId = Guid.NewGuid(), TransportId = transport.TransportId, Transport = transport,
            PlanCode = "TP-" + Guid.NewGuid().ToString("N"), Status = TransportPlanStatus.PENDING, CreatedAt = time, UpdatedAt = time };
        var invoice = new Invoice { InvoiceId = Guid.NewGuid(), InvoiceCode = "INV-" + Guid.NewGuid().ToString("N"),
            BookingId = booking.BookingId, EmployeeId = creator.EmployeeId, Type = type, Currency = "EUR",
            Subtotal = 100, TaxAmount = 20, TotalAmount = 120, DueDate = type == InvoiceType.CREDIT_NOTE ? null : new DateOnly(2026, 11, 15),
            BillingNote = "Keep this note", CreatedAt = time, UpdatedAt = time };
        db.AddRange(transport, plan, invoice);
        db.InvoiceItems.Add(new InvoiceItem { InvoiceItemId = Guid.NewGuid(), InvoiceId = invoice.InvoiceId,
            ItemType = InvoiceItemType.TRANSPORT_FEE, Description = "Transport fee", Quantity = 1, UnitPrice = 100,
            LineAmount = 100, CreatedAt = time, UpdatedAt = time });
        await db.SaveChangesAsync();
        return (actor, customerAccount, invoice, plan);
    }

    internal static Invoice AddDeposit(AppDbContext db, Invoice final, decimal amount, bool deduction = true)
    {
        var deposit = new Invoice { InvoiceId = Guid.NewGuid(), InvoiceCode = "INV-" + Guid.NewGuid().ToString("N"),
            BookingId = final.BookingId, EmployeeId = final.EmployeeId, Type = InvoiceType.DEPOSIT, Status = InvoiceStatus.PAID,
            Currency = final.Currency, TotalAmount = amount, CreatedAt = final.CreatedAt, UpdatedAt = final.UpdatedAt };
        db.Invoices.Add(deposit);
        if (deduction)
            db.InvoiceItems.Add(new InvoiceItem { InvoiceItemId = Guid.NewGuid(), InvoiceId = final.InvoiceId,
                ItemType = InvoiceItemType.OTHER, Description = $"Deposit deduction ({deposit.InvoiceCode})", Quantity = 1,
                UnitPrice = -amount, LineAmount = -amount, CreatedAt = final.CreatedAt, UpdatedAt = final.UpdatedAt });
        return deposit;
    }

    [Theory]
    [InlineData(InvoiceType.QUOTATION)]
    [InlineData(InvoiceType.DEPOSIT)]
    [InlineData(InvoiceType.FINAL)]
    [InlineData(InvoiceType.SUPPLEMENTARY)]
    [InlineData(InvoiceType.CREDIT_NOTE)]
    public async Task Issue_AllTypes_RecordsIssuerAndHistory_NotifiesOnlyCustomer_PreservesMoneyAndPlan(InvoiceType type)
    {
        await using var db = TestSupport.CreateDb();
        var (actor, customer, invoice, plan) = await SeedAsync(db, type);
        if (type == InvoiceType.FINAL) { AddDeposit(db, invoice, 30); AddDeposit(db, invoice, 10); await db.SaveChangesAsync(); }
        var itemVersions = db.InvoiceItems.Select(x => x.UpdatedAt).ToArray();
        var realtime = new RecordingRealtime();
        var result = await CreateService(db, realtime).IssueAsync(invoice.InvoiceId, Version(invoice), actor.AccountId);
        Assert.Equal(invoice.InvoiceId, result.InvoiceId);
        Assert.Equal(invoice.InvoiceCode, result.InvoiceNumber);
        Assert.Equal(type.ToString(), result.Type);
        Assert.Equal("ISSUED", result.Status);
        Assert.Equal(new DateOnly(2026, 11, 5), result.InvoiceDate);
        Assert.Equal(TestClock.GetUtcNow(), result.IssuedAt);
        Assert.Equal(result.IssuedAt, result.UpdatedAt);
        Assert.Equal(120, result.GrandTotal);
        Assert.Equal("EUR", result.Currency);
        Assert.Equal(invoice.DueDate, result.DueDate);
        Assert.Equal(type == InvoiceType.QUOTATION ? ["CANCEL", "CREATE_ANOTHER"] : new[] { "RECORD_PAYMENT", "CANCEL", "CREATE_ANOTHER" }, result.AvailableActions);
        Assert.Equal((await db.Employees.SingleAsync(x => x.AccountId == actor.AccountId)).EmployeeId, invoice.EmployeeId);
        Assert.Equal((100m, 0m, 20m, 120m, "Keep this note"), (invoice.Subtotal, invoice.DiscountAmount, invoice.TaxAmount, invoice.TotalAmount, invoice.BillingNote));
        Assert.Equal(itemVersions, db.InvoiceItems.Select(x => x.UpdatedAt).ToArray());
        Assert.Equal(TransportPlanStatus.PENDING, plan.Status);
        Assert.Equal(invoice.CreatedAt, plan.UpdatedAt);
        Assert.Empty(db.Receipts);
        var history = Assert.Single(db.InvoiceStatusHistories);
        Assert.Equal(InvoiceHistoryType.ISSUE, history.Type);
        Assert.Equal(InvoiceStatus.DRAFT, history.PreviousStatus);
        Assert.Equal(InvoiceStatus.ISSUED, history.NewStatus);
        Assert.Equal(actor.AccountId, history.ChangedByAccountId);
        Assert.Equal(invoice.IssuedAt, history.ChangedAt);
        Assert.Equal(history.ChangedAt, history.CreatedAt);
        Assert.Null(history.Reason);
        var notification = Assert.Single(db.Notifications);
        Assert.Equal(NotificationType.PAYMENT, notification.NotificationType);
        Assert.Equal(type is InvoiceType.QUOTATION or InvoiceType.CREDIT_NOTE ? NotificationPriority.NORMAL : NotificationPriority.HIGH, notification.Priority);
        Assert.Equal(plan.TransportId, notification.TransportId);
        Assert.Equal("Invoice", notification.ReferenceType);
        Assert.Equal(invoice.InvoiceId, notification.ReferenceId);
        Assert.Equal($"/bookings/{invoice.BookingId}?tab=invoices&invoiceId={invoice.InvoiceId}", notification.ActionUrl);
        Assert.Equal(customer.AccountId, Assert.Single(db.AccountNotifications).AccountId);
        Assert.Equal(customer.AccountId, Assert.Single(realtime.Recipients));
        var mail = OutboxQueue.Deserialize<EmailPayload>(Assert.Single(db.OutboxMessages).Payload);
        Assert.Equal(customer.Email, mail.To);
        Assert.Equal(notification.Title, mail.Subject);
        Assert.Contains("120.00 EUR", mail.Body);
        if (type == InvoiceType.QUOTATION) Assert.Contains("No payment is required", mail.Body);
        else if (type == InvoiceType.CREDIT_NOTE) Assert.Contains("Payout or credit amount", mail.Body);
        else Assert.Contains("2026-11-15", mail.Body);
    }

    [Theory]
    [InlineData("missing", 404, "INVOICE_NOT_FOUND", "Invoice not found")]
    [InlineData("status", 409, "INVALID_INVOICE_STATUS", "Invoice must be in DRAFT status to be issued")]
    [InlineData("version", 409, "INVOICE_MODIFIED", "The invoice was just modified. Please reload the page")]
    [InlineData("empty", 422, "EMPTY_INVOICE_ITEMS", "The invoice must have at least one fee line before it can be issued")]
    [InlineData("deductionOnly", 422, "EMPTY_INVOICE_ITEMS", "The invoice must have at least one fee line before it can be issued")]
    [InlineData("zero", 422, "INVOICE_TOTAL_NOT_POSITIVE", "Invoice total must be greater than 0")]
    [InlineData("negative", 422, "INVOICE_TOTAL_NOT_POSITIVE", "Invoice total must be greater than 0")]
    public async Task Issue_InvalidDraft_RejectsWithoutSideEffects(string scenario, int status, string code, string message)
    {
        await using var db = TestSupport.CreateDb();
        var (actor, _, invoice, _) = await SeedAsync(db, InvoiceType.FINAL);
        var request = Version(invoice);
        if (scenario == "status") invoice.Status = InvoiceStatus.ISSUED;
        if (scenario == "version") request.LastUpdatedAt = request.LastUpdatedAt!.Value.AddTicks(10);
        if (scenario is "empty" or "deductionOnly") db.InvoiceItems.RemoveRange(db.InvoiceItems);
        if (scenario == "deductionOnly") AddDeposit(db, invoice, 30);
        if (scenario is "zero" or "negative") invoice.TotalAmount = scenario == "zero" ? 0 : -1;
        await db.SaveChangesAsync();
        var before = (invoice.Status, invoice.UpdatedAt, invoice.EmployeeId);
        var error = await Assert.ThrowsAnyAsync<Pegaxus.Service.Exceptions.Exceptions>(() =>
            CreateService(db).IssueAsync(scenario == "missing" ? Guid.NewGuid() : invoice.InvoiceId, request, actor.AccountId));
        Assert.Equal(status, error.StatusCode); Assert.Equal(code, error.ErrorCode); Assert.Equal(message, error.Message);
        Assert.Equal(before, (invoice.Status, invoice.UpdatedAt, invoice.EmployeeId));
        AssertNoEffects(db);
    }

    [Theory]
    [InlineData("added")]
    [InlineData("removed")]
    [InlineData("replaced")]
    [InlineData("amount")]
    [InlineData("currency")]
    [InlineData("quantity")]
    [InlineData("unitPrice")]
    [InlineData("description")]
    [InlineData("duplicate")]
    public async Task Final_RejectsEveryStaleDepositSnapshot(string change)
    {
        await using var db = TestSupport.CreateDb();
        var (actor, _, invoice, _) = await SeedAsync(db, InvoiceType.FINAL);
        var deposit = AddDeposit(db, invoice, 30);
        var line = db.InvoiceItems.Local.Single(x => x.LineAmount < 0);
        switch (change)
        {
            case "added": AddDeposit(db, invoice, 10, false); break;
            case "removed": deposit.Status = InvoiceStatus.CANCELLED; break;
            case "replaced": deposit.Status = InvoiceStatus.CANCELLED; AddDeposit(db, invoice, 30, false); break;
            case "amount": deposit.TotalAmount = 31; break;
            case "currency": deposit.Currency = "USD"; break;
            case "quantity": line.Quantity = 2; break;
            case "unitPrice": line.UnitPrice = -29; break;
            case "description": line.Description = "Deposit deduction (INV-wrong)"; break;
            case "duplicate": db.InvoiceItems.Add(new InvoiceItem { InvoiceItemId = Guid.NewGuid(), InvoiceId = invoice.InvoiceId,
                ItemType = line.ItemType, Description = line.Description, Quantity = line.Quantity, UnitPrice = line.UnitPrice, LineAmount = line.LineAmount }); break;
        }
        await db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<ConflictException>(() => CreateService(db).IssueAsync(invoice.InvoiceId, Version(invoice), actor.AccountId));
        Assert.Equal("DEPOSIT_DEDUCTION_CHANGED", error.ErrorCode);
        Assert.Equal("The paid deposits changed after this draft was saved. Please open the draft and save it again", error.Message);
        Assert.Equal(InvoiceStatus.DRAFT, invoice.Status);
        AssertNoEffects(db);
    }

    [Fact]
    public async Task Version_AcceptsEquivalentOffsetAndMicrosecondPrecision_AdvancesFrozenClock_EncodesEmail()
    {
        await using var db = TestSupport.CreateDb();
        var (actor, _, invoice, _) = await SeedAsync(db, InvoiceType.QUOTATION);
        invoice.UpdatedAt = TestClock.GetUtcNow().UtcDateTime.AddTicks(3);
        invoice.InvoiceCode = "INV-<script>";
        await db.SaveChangesAsync();
        var request = Version(invoice); request.LastUpdatedAt = request.LastUpdatedAt!.Value.ToOffset(TimeSpan.FromHours(7)).AddTicks(2);
        var result = await CreateService(db).IssueAsync(invoice.InvoiceId, request, actor.AccountId);
        Assert.Equal(TestClock.GetUtcNow().AddTicks(10), result.UpdatedAt);
        Assert.Equal(TimeSpan.Zero, result.UpdatedAt.Offset);
        Assert.Equal(TestClock.GetUtcNow(), result.IssuedAt);
        var body = OutboxQueue.Deserialize<EmailPayload>(Assert.Single(db.OutboxMessages).Payload).Body;
        Assert.Contains("&lt;script&gt;", body); Assert.DoesNotContain("<script>", body);
        Assert.Equal("INVALID_INVOICE_STATUS", (await Assert.ThrowsAsync<ConflictException>(() =>
            CreateService(db).IssueAsync(invoice.InvoiceId, request, actor.AccountId))).ErrorCode);
    }

    internal static void AssertNoEffects(AppDbContext db)
    {
        Assert.Empty(db.InvoiceStatusHistories); Assert.Empty(db.Notifications);
        Assert.Empty(db.AccountNotifications); Assert.Empty(db.OutboxMessages); Assert.Empty(db.Receipts);
    }

    private sealed class RecordingRealtime : INotificationRealtimeService
    {
        public List<Guid> Recipients { get; } = [];
        public Task SendToAccountAsync(Guid accountId, NotificationRealtimeDto notification, CancellationToken cancellationToken = default)
        { Recipients.Add(accountId); return Task.CompletedTask; }
    }
}
