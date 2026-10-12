using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.InvoiceService.Validator;
using Pegaxus.Service.NotificationService;
using Pegaxus.Service.OutboxService;
using PaymentService = Pegaxus.Service.InvoiceService.Service;
using Request = Pegaxus.Service.InvoiceService.Request;

namespace Pegaxus.Service.Test;

public class InvoicePaymentTests
{
    internal sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 10, 4, 0, 0, TimeSpan.Zero);
    }
    internal static readonly Clock TestClock = new();
    internal static PaymentService CreateService(AppDbContext db, INotificationRealtimeService? realtime = null) => new(db,
        TestClock, new RecordPaymentRequestValidator(TestClock), new Pegaxus.Service.NotificationService.Service(db, realtime ?? new NoopNotificationRealtimeService()));
    internal static Request.RecordPaymentRequest ValidRequest() => new()
    { Amount = 120, Currency = "EUR", PaymentMethod = "BANK_TRANSFER", TransactionReference = " FT26314123456 ", Note = " Matched statement " };

    internal static async Task<(Account Actor, Account Customer, Invoice Invoice, TransportPlan Plan)> SeedAsync(AppDbContext db, InvoiceType type = InvoiceType.DEPOSIT)
    {
        var (actor, _) = await TestSupport.AddLogisticsManagerAsync(db);
        var (_, issuer) = await TestSupport.AddLogisticsManagerAsync(db);
        var (customerAccount, customer) = await TestSupport.AddCustomerAsync(db);
        var (booking, _) = await TestSupport.AddPendingBookingAsync(db, customer); booking.Status = BookingStatus.APPROVED;
        var time = new DateTime(2026, 11, 5, 8, 0, 0);
        var transport = new Transport { TransportId = Guid.NewGuid(), BookingId = booking.BookingId, Booking = booking,
            TransportCode = "TR-" + Guid.NewGuid().ToString("N"), CreatedAt = time, UpdatedAt = time };
        var plan = new TransportPlan { TransportPlanId = Guid.NewGuid(), TransportId = transport.TransportId, Transport = transport,
            PlanCode = "TP-" + Guid.NewGuid().ToString("N"), Status = TransportPlanStatus.PENDING, CreatedAt = time, UpdatedAt = time };
        var invoice = new Invoice { InvoiceId = Guid.NewGuid(), InvoiceCode = "INV-" + Guid.NewGuid().ToString("N"),
            BookingId = booking.BookingId, EmployeeId = issuer.EmployeeId, Type = type, Status = InvoiceStatus.ISSUED, Currency = "EUR",
            Subtotal = 100, TaxAmount = 20, TotalAmount = 120, IssuedAt = time, DueDate = new(2026, 11, 15),
            BillingNote = "Preserve note", CreatedAt = time, UpdatedAt = time };
        db.AddRange(transport, plan, invoice);
        db.InvoiceItems.Add(new InvoiceItem { InvoiceItemId = Guid.NewGuid(), InvoiceId = invoice.InvoiceId,
            ItemType = InvoiceItemType.TRANSPORT_FEE, Description = "Transport fee", Quantity = 1, UnitPrice = 100,
            LineAmount = 100, CreatedAt = time, UpdatedAt = time });
        await db.SaveChangesAsync(); return (actor, customerAccount, invoice, plan);
    }
    internal static Receipt AddReceipt(AppDbContext db, Invoice invoice, ReceiptStatus status = ReceiptStatus.PENDING)
    {
        var receipt = new Receipt { ReceiptId = Guid.NewGuid(), ReceiptCode = "RCP-" + Guid.NewGuid().ToString("N"),
            InvoiceId = invoice.InvoiceId, Amount = invoice.TotalAmount, Currency = invoice.Currency, PaymentMethod = PaymentMethod.BANK_TRANSFER,
            Status = status, TransactionReference = "Keep reference", CreatedAt = invoice.UpdatedAt, UpdatedAt = invoice.UpdatedAt };
        db.Receipts.Add(receipt); return receipt;
    }

    [Theory]
    [InlineData(InvoiceType.DEPOSIT, InvoiceStatus.ISSUED, "CASH")]
    [InlineData(InvoiceType.DEPOSIT, InvoiceStatus.OVERDUE, "BANK_TRANSFER")]
    [InlineData(InvoiceType.FINAL, InvoiceStatus.ISSUED, "OTHER")]
    [InlineData(InvoiceType.FINAL, InvoiceStatus.OVERDUE, "BANK_TRANSFER")]
    [InlineData(InvoiceType.SUPPLEMENTARY, InvoiceStatus.ISSUED, "BANK_TRANSFER")]
    [InlineData(InvoiceType.CREDIT_NOTE, InvoiceStatus.OVERDUE, "BANK_TRANSFER")]
    public async Task RecordPayment_ConfirmsFullPaymentAndEffects_NotifiesOnlyCustomer(InvoiceType type, InvoiceStatus status, string method)
    {
        await using var db = TestSupport.CreateDb();
        var (actor, customer, invoice, plan) = await SeedAsync(db, type); invoice.Status = status;
        var issuer = invoice.EmployeeId;
        var pending = new[] { AddReceipt(db, invoice), AddReceipt(db, invoice) };
        var failed = AddReceipt(db, invoice, ReceiptStatus.FAILED);
        await db.SaveChangesAsync();
        var request = ValidRequest(); request.PaymentMethod = method;
        request.PaidAt = TestClock.GetUtcNow().AddMinutes(-40).ToOffset(TimeSpan.FromHours(7));
        var realtime = new RecordingRealtime();
        var result = await CreateService(db, realtime).RecordPaymentAsync(invoice.InvoiceId, request, actor.AccountId);
        var receipt = Assert.Single(db.Receipts.Where(x => x.Status == ReceiptStatus.CONFIRMED));
        Assert.Matches("^RCP-20261110-[0-9]{4}$", receipt.ReceiptCode);
        Assert.Equal(receipt.ReceiptId, result.Receipt.ReceiptId); Assert.Equal(receipt.ReceiptCode, result.Receipt.ReceiptCode);
        Assert.Equal(120, result.Receipt.Amount); Assert.Equal("EUR", result.Receipt.Currency);
        Assert.Equal(method, result.Receipt.PaymentMethod); Assert.Equal("CONFIRMED", result.Receipt.Status);
        Assert.Equal("FT26314123456", result.Receipt.TransactionReference); Assert.Equal("Matched statement", result.Receipt.Note);
        Assert.Equal(request.PaidAt.Value.ToUniversalTime(), result.Receipt.PaidAt);
        Assert.Equal(TestClock.GetUtcNow(), result.Receipt.ConfirmedAt);
        Assert.Equal(actor.AccountId, result.Receipt.RecordedBy.AccountId); Assert.Equal("Test Manager", result.Receipt.RecordedBy.FullName);
        Assert.Equal("PAID", result.Invoice.Status); Assert.Equal(invoice.InvoiceCode, result.Invoice.InvoiceNumber);
        Assert.Equal(TestClock.GetUtcNow(), result.Invoice.PaidAt); Assert.Equal(result.Invoice.PaidAt, result.Invoice.UpdatedAt);
        Assert.Equal(new[] { "CREATE_ANOTHER" }, result.Invoice.AvailableActions);
        Assert.Equal(issuer, invoice.EmployeeId);
        Assert.Equal((100m, 0m, 20m, 120m, "Preserve note"), (invoice.Subtotal, invoice.DiscountAmount, invoice.TaxAmount, invoice.TotalAmount, invoice.BillingNote));
        Assert.Equal(100, Assert.Single(db.InvoiceItems).LineAmount);
        Assert.All(pending, x => { Assert.Equal(ReceiptStatus.FAILED, x.Status); Assert.Equal(receipt.ConfirmedAt, x.UpdatedAt); Assert.Equal("Keep reference", x.TransactionReference); });
        Assert.Equal(invoice.CreatedAt, failed.UpdatedAt);
        var history = Assert.Single(db.InvoiceStatusHistories);
        Assert.Equal(InvoiceHistoryType.RECORD_PAYMENT, history.Type); Assert.Equal(status, history.PreviousStatus);
        Assert.Equal(InvoiceStatus.PAID, history.NewStatus); Assert.Equal(actor.AccountId, history.ChangedByAccountId);
        Assert.Equal(receipt.ConfirmedAt, history.ChangedAt); Assert.Equal(history.ChangedAt, history.CreatedAt); Assert.Null(history.Reason);
        Assert.Equal(type == InvoiceType.DEPOSIT ? "APPROVED" : null, result.Effects.TransportPlanStatus);
        Assert.Equal(type == InvoiceType.FINAL, result.Effects.CreditNoteUnlocked);
        Assert.Equal(type == InvoiceType.DEPOSIT ? TransportPlanStatus.APPROVED : TransportPlanStatus.PENDING, plan.Status);
        if (type == InvoiceType.DEPOSIT)
        { Assert.Equal(receipt.ConfirmedAt, plan.ApprovedAt); Assert.Equal(actor.EmployeeAccount!.EmployeeId, plan.ApprovedByEmployeeId); }
        var notification = Assert.Single(db.Notifications);
        Assert.Equal(NotificationType.PAYMENT, notification.NotificationType); Assert.Equal(NotificationPriority.NORMAL, notification.Priority);
        Assert.Equal(plan.TransportId, notification.TransportId); Assert.Equal("Invoice", notification.ReferenceType);
        Assert.Equal(invoice.InvoiceId, notification.ReferenceId);
        Assert.Equal($"/bookings/{invoice.BookingId}?tab=invoices&invoiceId={invoice.InvoiceId}", notification.ActionUrl);
        Assert.Equal(customer.AccountId, Assert.Single(db.AccountNotifications).AccountId); Assert.Equal(customer.AccountId, Assert.Single(realtime.Recipients));
        var email = OutboxQueue.Deserialize<EmailPayload>(Assert.Single(db.OutboxMessages).Payload);
        Assert.Equal(customer.Email, email.To); Assert.Contains(receipt.ReceiptCode, email.Body); Assert.Contains("120.00 EUR", email.Body);
        if (type == InvoiceType.CREDIT_NOTE) Assert.Contains("Payout", email.Body);
        Assert.Equal("INVALID_INVOICE_STATUS", (await Assert.ThrowsAsync<ConflictException>(() =>
            CreateService(db).RecordPaymentAsync(invoice.InvoiceId, ValidRequest(), actor.AccountId))).ErrorCode);
        Assert.Single(db.InvoiceStatusHistories);
    }

    [Theory]
    [InlineData(InvoiceStatus.DRAFT)]
    [InlineData(InvoiceStatus.PAID)]
    [InlineData(InvoiceStatus.CANCELLED)]
    [InlineData(InvoiceStatus.PARTIALLY_PAID)]
    public async Task InvalidStatus_AndQuotationPrecedence_HaveNoSideEffects(InvoiceStatus status)
    {
        await using var db = TestSupport.CreateDb();
        var (actor, _, invoice, _) = await SeedAsync(db); invoice.Status = status; await db.SaveChangesAsync();
        var service = CreateService(db);
        var invalid = await Assert.ThrowsAsync<ConflictException>(() => service.RecordPaymentAsync(invoice.InvoiceId, ValidRequest(), actor.AccountId));
        Assert.Equal("INVALID_INVOICE_STATUS", invalid.ErrorCode); Assert.Equal($"Cannot pay an invoice in status {status}", invalid.Message);
        invoice.Type = InvoiceType.QUOTATION; await db.SaveChangesAsync();
        var quotation = await Assert.ThrowsAsync<UnprocessableEntityException>(() => service.RecordPaymentAsync(invoice.InvoiceId, ValidRequest(), actor.AccountId));
        Assert.Equal("UNPAYABLE_INVOICE_TYPE", quotation.ErrorCode); Assert.Equal("This invoice type cannot be paid", quotation.Message);
        AssertNoEffects(db);
    }

    [Theory]
    [InlineData("missing", 404, "INVOICE_NOT_FOUND", "Invoice not found")]
    [InlineData("partial", 422, "PARTIAL_PAYMENT_NOT_SUPPORTED", "Only full payment of the invoice total is accepted")]
    [InlineData("excess", 422, "PARTIAL_PAYMENT_NOT_SUPPORTED", "Only full payment of the invoice total is accepted")]
    [InlineData("currency", 422, "CURRENCY_MISMATCH", "Payment currency must match the invoice currency")]
    [InlineData("confirmed", 409, "INVALID_INVOICE_STATUS", "Cannot pay an invoice in status ISSUED")]
    public async Task InvalidPayment_IsRejectedWithoutChangingPendingReceipts(string scenario, int status, string code, string message)
    {
        await using var db = TestSupport.CreateDb();
        var (actor, _, invoice, plan) = await SeedAsync(db);
        var pending = AddReceipt(db, invoice);
        if (scenario == "confirmed") AddReceipt(db, invoice, ReceiptStatus.CONFIRMED);
        await db.SaveChangesAsync();
        var request = ValidRequest();
        if (scenario == "partial") request.Amount = 119.99m;
        if (scenario == "excess") request.Amount = 120.01m;
        if (scenario == "currency") request.Currency = "VND";
        var error = await Assert.ThrowsAnyAsync<Pegaxus.Service.Exceptions.Exceptions>(() => CreateService(db)
            .RecordPaymentAsync(scenario == "missing" ? Guid.NewGuid() : invoice.InvoiceId, request, actor.AccountId));
        Assert.Equal(status, error.StatusCode); Assert.Equal(code, error.ErrorCode); Assert.Equal(message, error.Message);
        Assert.Equal(InvoiceStatus.ISSUED, invoice.Status); Assert.Null(invoice.PaidAt);
        Assert.Equal(ReceiptStatus.PENDING, pending.Status); Assert.Equal(TransportPlanStatus.PENDING, plan.Status);
        Assert.Empty(db.InvoiceStatusHistories); Assert.Empty(db.Notifications); Assert.Empty(db.OutboxMessages);
    }

    [Theory]
    [InlineData(TransportPlanStatus.DRAFT, true)]
    [InlineData(TransportPlanStatus.PENDING, true)]
    [InlineData(TransportPlanStatus.APPROVED, false)]
    [InlineData(TransportPlanStatus.IN_PROGRESS, false)]
    [InlineData(TransportPlanStatus.COMPLETED, false)]
    public async Task Deposit_OnlyLatestNonCancelledPlanCanBeApproved(TransportPlanStatus latestStatus, bool changed)
    {
        await using var db = TestSupport.CreateDb();
        var (actor, _, invoice, older) = await SeedAsync(db);
        var latest = new TransportPlan { TransportPlanId = Guid.NewGuid(), TransportId = older.TransportId,
            PlanCode = "TP-" + Guid.NewGuid().ToString("N"), Status = latestStatus,
            CreatedAt = older.CreatedAt.AddDays(1), UpdatedAt = older.UpdatedAt };
        var cancelled = new TransportPlan { TransportPlanId = Guid.NewGuid(), TransportId = older.TransportId,
            PlanCode = "TP-" + Guid.NewGuid().ToString("N"), Status = TransportPlanStatus.CANCELLED,
            CreatedAt = older.CreatedAt.AddDays(2), UpdatedAt = older.UpdatedAt };
        db.AddRange(latest, cancelled); await db.SaveChangesAsync();
        var result = await CreateService(db).RecordPaymentAsync(invoice.InvoiceId, ValidRequest(), actor.AccountId);
        Assert.Equal(changed ? "APPROVED" : null, result.Effects.TransportPlanStatus);
        Assert.Equal(changed ? TransportPlanStatus.APPROVED : latestStatus, latest.Status);
        Assert.Equal(TransportPlanStatus.PENDING, older.Status); Assert.Equal(TransportPlanStatus.CANCELLED, cancelled.Status);
        if (!changed) { Assert.Null(latest.ApprovedAt); Assert.Equal(older.UpdatedAt, latest.UpdatedAt); }
    }

    [Fact]
    public async Task DepositWithoutPlan_DefaultPaymentTimeAndNullableFields_AdvancesVersion_EncodesEmail()
    {
        await using var db = TestSupport.CreateDb();
        var (actor, _, invoice, plan) = await SeedAsync(db); db.TransportPlans.Remove(plan);
        invoice.InvoiceCode = "INV-<script>"; invoice.UpdatedAt = TestClock.GetUtcNow().UtcDateTime.AddTicks(3);
        await db.SaveChangesAsync();
        var request = ValidRequest(); request.Currency = " eur "; request.PaymentMethod = " cash "; request.Note = " "; request.TransactionReference = null;
        var result = await CreateService(db).RecordPaymentAsync(invoice.InvoiceId, request, actor.AccountId);
        Assert.Null(result.Effects.TransportPlanStatus); Assert.False(result.Effects.CreditNoteUnlocked);
        Assert.Equal(TestClock.GetUtcNow(), result.Receipt.PaidAt); Assert.Null(result.Receipt.Note); Assert.Null(result.Receipt.TransactionReference);
        Assert.Equal(TestClock.GetUtcNow().AddTicks(10), result.Invoice.UpdatedAt);
        Assert.Equal(TimeSpan.Zero, result.Invoice.UpdatedAt.Offset);
        var body = OutboxQueue.Deserialize<EmailPayload>(Assert.Single(db.OutboxMessages).Payload).Body;
        Assert.Contains("&lt;script&gt;", body); Assert.DoesNotContain("<script>", body);
    }

    internal static void AssertNoEffects(AppDbContext db)
    { Assert.Empty(db.Receipts); Assert.Empty(db.InvoiceStatusHistories); Assert.Empty(db.Notifications); Assert.Empty(db.AccountNotifications); Assert.Empty(db.OutboxMessages); }
    private sealed class RecordingRealtime : INotificationRealtimeService
    {
        internal List<Guid> Recipients { get; } = [];
        public Task SendToAccountAsync(Guid accountId, NotificationRealtimeDto notification, CancellationToken cancellationToken = default)
        { Recipients.Add(accountId); return Task.CompletedTask; }
    }
}
