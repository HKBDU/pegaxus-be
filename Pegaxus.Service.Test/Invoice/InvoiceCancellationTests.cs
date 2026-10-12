using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.InvoiceService.Validator;
using Pegaxus.Service.NotificationService;
using Pegaxus.Service.OutboxService;
using CancellationService = Pegaxus.Service.InvoiceService.Service;
using Request = Pegaxus.Service.InvoiceService.Request;

namespace Pegaxus.Service.Test;

public class InvoiceCancellationTests
{
    internal sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 6, 2, 0, 0, TimeSpan.Zero);
    }
    internal static readonly Clock TestClock = new();
    internal static CancellationService CreateService(AppDbContext db, INotificationRealtimeService? realtime = null) => new(db,
        new InvoiceOnlinePaymentTests.Gateway(), InvoiceOnlinePaymentTests.Logger,
        TestClock, new CreateRequestValidator(TestClock), new SuggestionRequestValidator(), new UpdateRequestValidator(TestClock),
        new ListRequestValidator(), new BookingListRequestValidator(), new IssueRequestValidator(), new RecordPaymentRequestValidator(TestClock),
        new CancelRequestValidator(), new Pegaxus.Service.NotificationService.Service(db, realtime ?? new NoopNotificationRealtimeService()));
    internal static Request.CancelRequest ValidRequest() => new() { CancellationReason = "  Wrong unit price on the transport fee line  " };
    internal static async Task<(Account Actor, Account Customer, Invoice Invoice, TransportPlan Plan)> SeedAsync(AppDbContext db,
        InvoiceType type = InvoiceType.DEPOSIT, InvoiceStatus status = InvoiceStatus.ISSUED)
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
            BookingId = booking.BookingId, EmployeeId = issuer.EmployeeId, Type = type, Status = status, Currency = "EUR",
            Subtotal = 100, TaxAmount = 20, TotalAmount = 120, IssuedAt = status == InvoiceStatus.DRAFT ? null : time,
            DueDate = new(2026, 11, 15), BillingNote = "Keep billing note", CreatedAt = time, UpdatedAt = time };
        db.AddRange(transport, plan, invoice);
        db.InvoiceItems.Add(new InvoiceItem { InvoiceItemId = Guid.NewGuid(), InvoiceId = invoice.InvoiceId,
            ItemType = InvoiceItemType.TRANSPORT_FEE, Description = "Transport fee", Quantity = 1, UnitPrice = 100,
            LineAmount = 100, CreatedAt = time, UpdatedAt = time });
        await db.SaveChangesAsync(); return (actor, customerAccount, invoice, plan);
    }
    internal static Receipt AddReceipt(AppDbContext db, Invoice invoice, ReceiptStatus status)
    {
        var receipt = new Receipt { ReceiptId = Guid.NewGuid(), ReceiptCode = "RCP-" + Guid.NewGuid().ToString("N"),
            InvoiceId = invoice.InvoiceId, Amount = invoice.TotalAmount, Currency = invoice.Currency, PaymentMethod = PaymentMethod.BANK_TRANSFER,
            Status = status, TransactionReference = "Keep reference", CreatedAt = invoice.UpdatedAt, UpdatedAt = invoice.UpdatedAt };
        db.Receipts.Add(receipt); return receipt;
    }

    [Theory]
    [InlineData(InvoiceType.QUOTATION, InvoiceStatus.DRAFT)]
    [InlineData(InvoiceType.QUOTATION, InvoiceStatus.ISSUED)]
    [InlineData(InvoiceType.DEPOSIT, InvoiceStatus.DRAFT)]
    [InlineData(InvoiceType.DEPOSIT, InvoiceStatus.ISSUED)]
    [InlineData(InvoiceType.DEPOSIT, InvoiceStatus.OVERDUE)]
    [InlineData(InvoiceType.FINAL, InvoiceStatus.DRAFT)]
    [InlineData(InvoiceType.FINAL, InvoiceStatus.ISSUED)]
    [InlineData(InvoiceType.FINAL, InvoiceStatus.OVERDUE)]
    [InlineData(InvoiceType.SUPPLEMENTARY, InvoiceStatus.DRAFT)]
    [InlineData(InvoiceType.SUPPLEMENTARY, InvoiceStatus.ISSUED)]
    [InlineData(InvoiceType.SUPPLEMENTARY, InvoiceStatus.OVERDUE)]
    [InlineData(InvoiceType.CREDIT_NOTE, InvoiceStatus.DRAFT)]
    [InlineData(InvoiceType.CREDIT_NOTE, InvoiceStatus.ISSUED)]
    [InlineData(InvoiceType.CREDIT_NOTE, InvoiceStatus.OVERDUE)]
    public async Task Cancel_AllEligibleTypesAndStatuses_AppendsHistory_PreservesFinanceAndNotifiesOnlyIssued(InvoiceType type, InvoiceStatus status)
    {
        await using var db = TestSupport.CreateDb();
        var (actor, customer, invoice, plan) = await SeedAsync(db, type, status);
        var issuer = invoice.EmployeeId; var issuedAt = invoice.IssuedAt;
        var failed = AddReceipt(db, invoice, ReceiptStatus.FAILED); var refunded = AddReceipt(db, invoice, ReceiptStatus.REFUNDED);
        var initial = new InvoiceStatusHistory { InvoiceStatusHistoryId = Guid.NewGuid(), InvoiceId = invoice.InvoiceId,
            Type = InvoiceHistoryType.CREATE, NewStatus = InvoiceStatus.DRAFT, ChangedByAccountId = actor.AccountId,
            ChangedAt = invoice.CreatedAt, CreatedAt = invoice.CreatedAt };
        db.InvoiceStatusHistories.Add(initial); await db.SaveChangesAsync();
        var realtime = new RecordingRealtime(); var request = ValidRequest();
        var result = await CreateService(db, realtime).CancelAsync(invoice.InvoiceId, request, actor.AccountId);
        Assert.Equal(invoice.InvoiceId, result.InvoiceId); Assert.Equal(invoice.InvoiceCode, result.InvoiceNumber);
        Assert.Equal("CANCELLED", result.Status); Assert.Equal(TestClock.GetUtcNow(), result.CancelledAt);
        Assert.Equal(result.CancelledAt, result.UpdatedAt); Assert.Equal(request.CancellationReason.Trim(), result.CancellationReason);
        Assert.Equal(new[] { "CREATE_ANOTHER" }, result.AvailableActions);
        Assert.Equal(issuer, invoice.EmployeeId); Assert.Equal(issuedAt, invoice.IssuedAt); Assert.Null(invoice.PaidAt);
        Assert.Equal((100m, 0m, 20m, 120m, "Keep billing note"), (invoice.Subtotal, invoice.DiscountAmount, invoice.TaxAmount, invoice.TotalAmount, invoice.BillingNote));
        Assert.Equal(new DateOnly(2026, 11, 15), invoice.DueDate);
        var item = Assert.Single(db.InvoiceItems); Assert.Equal(100, item.LineAmount); Assert.Equal(invoice.CreatedAt, item.UpdatedAt);
        Assert.Equal(TransportPlanStatus.PENDING, plan.Status); Assert.Equal(invoice.CreatedAt, plan.UpdatedAt);
        Assert.Equal(TransportStatus.CREATED, plan.Transport.Status); Assert.Equal(invoice.CreatedAt, plan.Transport.UpdatedAt);
        Assert.Equal(ReceiptStatus.FAILED, failed.Status); Assert.Equal(ReceiptStatus.REFUNDED, refunded.Status);
        Assert.Equal(invoice.CreatedAt, failed.UpdatedAt); Assert.Equal(invoice.CreatedAt, refunded.UpdatedAt);
        var history = Assert.Single(db.InvoiceStatusHistories.Where(x => x.Type == InvoiceHistoryType.CANCEL));
        Assert.Equal(status, history.PreviousStatus); Assert.Equal(InvoiceStatus.CANCELLED, history.NewStatus);
        Assert.Equal(actor.AccountId, history.ChangedByAccountId); Assert.Equal(result.CancellationReason, history.Reason);
        Assert.Equal(invoice.CancelledAt, history.ChangedAt); Assert.Equal(history.ChangedAt, history.CreatedAt);
        Assert.Equal(2, db.InvoiceStatusHistories.Count()); Assert.Equal(InvoiceStatus.DRAFT, initial.NewStatus); Assert.Null(initial.Reason);
        if (status == InvoiceStatus.DRAFT)
        {
            Assert.Null(invoice.IssuedAt); Assert.Empty(db.Notifications); Assert.Empty(db.AccountNotifications);
            Assert.Empty(db.OutboxMessages); Assert.Empty(realtime.Recipients);
        }
        else
        {
            var notification = Assert.Single(db.Notifications);
            Assert.Equal(NotificationType.PAYMENT, notification.NotificationType); Assert.Equal(NotificationPriority.NORMAL, notification.Priority);
            Assert.Equal("Invoice cancelled", notification.Title); Assert.Contains(result.CancellationReason, notification.Message);
            Assert.Equal(plan.TransportId, notification.TransportId); Assert.Equal("Invoice", notification.ReferenceType); Assert.Equal(invoice.InvoiceId, notification.ReferenceId);
            Assert.Equal($"/bookings/{invoice.BookingId}?tab=invoices&invoiceId={invoice.InvoiceId}", notification.ActionUrl);
            Assert.Equal(customer.AccountId, Assert.Single(db.AccountNotifications).AccountId); Assert.Equal(customer.AccountId, Assert.Single(realtime.Recipients));
            var email = OutboxQueue.Deserialize<EmailPayload>(Assert.Single(db.OutboxMessages).Payload);
            Assert.Equal(customer.Email, email.To); Assert.Equal(notification.Title, email.Subject); Assert.Contains(result.CancellationReason, email.Body);
        }
    }

    [Theory]
    [InlineData("missing", 404, "INVOICE_NOT_FOUND", "Invoice not found")]
    [InlineData("cancelled", 409, "INVALID_INVOICE_STATUS", "Invoice is already cancelled")]
    [InlineData("paid", 422, "INVOICE_HAS_PAYMENT", "Cannot cancel an invoice that has a confirmed payment. Please contact accounting for the refund process")]
    [InlineData("confirmed", 422, "INVOICE_HAS_PAYMENT", "Cannot cancel an invoice that has a confirmed payment. Please contact accounting for the refund process")]
    [InlineData("confirmedAndPending", 422, "INVOICE_HAS_PAYMENT", "Cannot cancel an invoice that has a confirmed payment. Please contact accounting for the refund process")]
    [InlineData("pending", 409, "PAYMENT_IN_PROGRESS", "A payment for this invoice is being processed. Please try again after it completes")]
    [InlineData("legacyPartial", 409, "INVALID_INVOICE_STATUS", "Cannot cancel an invoice in status PARTIALLY_PAID")]
    public async Task Cancel_BlockedInvoices_HaveExactErrorsAndNoSideEffects(string scenario, int status, string code, string message)
    {
        await using var db = TestSupport.CreateDb();
        var (actor, _, invoice, _) = await SeedAsync(db, InvoiceType.FINAL);
        if (scenario == "cancelled") invoice.Status = InvoiceStatus.CANCELLED;
        if (scenario == "paid") invoice.Status = InvoiceStatus.PAID;
        if (scenario == "legacyPartial") invoice.Status = InvoiceStatus.PARTIALLY_PAID;
        if (scenario is "confirmed" or "confirmedAndPending") AddReceipt(db, invoice, ReceiptStatus.CONFIRMED);
        if (scenario is "pending" or "confirmedAndPending") AddReceipt(db, invoice, ReceiptStatus.PENDING);
        await db.SaveChangesAsync();
        var before = (invoice.Status, invoice.UpdatedAt, invoice.CancelledAt, invoice.CancellationReason);
        var receiptCount = db.Receipts.Count();
        var error = await Assert.ThrowsAnyAsync<Pegaxus.Service.Exceptions.Exceptions>(() => CreateService(db)
            .CancelAsync(scenario == "missing" ? Guid.NewGuid() : invoice.InvoiceId, ValidRequest(), actor.AccountId));
        Assert.Equal(status, error.StatusCode); Assert.Equal(code, error.ErrorCode); Assert.Equal(message, error.Message);
        Assert.Equal(before, (invoice.Status, invoice.UpdatedAt, invoice.CancelledAt, invoice.CancellationReason));
        Assert.Equal(receiptCount, db.Receipts.Count()); AssertNoEffects(db);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("          ")]
    [InlineData("123456789")]
    [InlineData(" 123456789 ")]
    public async Task Reason_RequiresTenCharactersAfterTrim(string? reason)
    {
        await using var db = TestSupport.CreateDb(); var (actor, _, invoice, _) = await SeedAsync(db);
        var request = new Request.CancelRequest { CancellationReason = reason! };
        var error = await Assert.ThrowsAsync<FieldValidationException>(() => CreateService(db).CancelAsync(invoice.InvoiceId, request, actor.AccountId));
        Assert.Equal("cancellationReason", Assert.Single(error.Errors).Field); Assert.Equal("VALIDATION_ERROR", error.ErrorCode);
        Assert.Equal(InvoiceStatus.ISSUED, invoice.Status); AssertNoEffects(db);
    }

    [Fact]
    public async Task Cancel_AdvancesFrozenVersion_EncodesReasonInEmail_RejectsRepeat()
    {
        await using var db = TestSupport.CreateDb(); var (actor, _, invoice, _) = await SeedAsync(db);
        invoice.UpdatedAt = TestClock.GetUtcNow().UtcDateTime.AddTicks(3); await db.SaveChangesAsync();
        var request = new Request.CancelRequest { CancellationReason = "  <script>alert(1)</script>  " };
        var result = await CreateService(db).CancelAsync(invoice.InvoiceId, request, actor.AccountId);
        Assert.Equal(TestClock.GetUtcNow().AddTicks(10), result.UpdatedAt); Assert.Equal(TimeSpan.Zero, result.UpdatedAt.Offset);
        var body = OutboxQueue.Deserialize<EmailPayload>(Assert.Single(db.OutboxMessages).Payload).Body;
        Assert.Contains("&lt;script&gt;", body); Assert.DoesNotContain("<script>", body);
        Assert.Equal("INVALID_INVOICE_STATUS", (await Assert.ThrowsAsync<ConflictException>(() =>
            CreateService(db).CancelAsync(invoice.InvoiceId, request, actor.AccountId))).ErrorCode);
        Assert.Single(db.InvoiceStatusHistories); Assert.Single(db.Notifications); Assert.Single(db.OutboxMessages);
    }

    [Fact]
    public async Task NotificationEligibility_UsesIssuedAt_AndAcceptsTenCharacterReason()
    {
        await using var db = TestSupport.CreateDb(); var (actor, _, invoice, _) = await SeedAsync(db);
        invoice.IssuedAt = null; await db.SaveChangesAsync();
        var result = await CreateService(db).CancelAsync(invoice.InvoiceId, new() { CancellationReason = " 0123456789 " }, actor.AccountId);
        Assert.Equal("0123456789", result.CancellationReason); Assert.Single(db.InvoiceStatusHistories);
        Assert.Empty(db.Notifications); Assert.Empty(db.OutboxMessages);
    }
    internal static void AssertNoEffects(AppDbContext db)
    { Assert.Empty(db.InvoiceStatusHistories); Assert.Empty(db.Notifications); Assert.Empty(db.AccountNotifications); Assert.Empty(db.OutboxMessages); }
    private sealed class RecordingRealtime : INotificationRealtimeService
    {
        internal List<Guid> Recipients { get; } = [];
        public Task SendToAccountAsync(Guid accountId, NotificationRealtimeDto notification, CancellationToken cancellationToken = default)
        { Recipients.Add(accountId); return Task.CompletedTask; }
    }
}
