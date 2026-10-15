using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.NotificationService;
using Pegaxus.Service.OutboxService;
using Pegaxus.Service.PaymentService;

using PaymentWebhookService = Pegaxus.Service.PaymentService.Service;
using Request = Pegaxus.Service.PaymentService.Request;

namespace Pegaxus.Service.Test;

public class PaymentWebhookTests
{
    internal const string Secret = "sepay-ipn-test-secret";
    internal static readonly DateTime Now = new(2026, 10, 12, 9, 0, 0, DateTimeKind.Unspecified);
    internal static PaymentWebhookService CreateService(AppDbContext db, INotificationRealtimeService? realtime = null, string secret = Secret) =>
        new(db, new Pegaxus.Service.NotificationService.Service(db, realtime ?? new NoopNotificationRealtimeService()),
            Options.Create(new SePayWebhookOptions { SecretKey = secret }), new TestClock(), NullLogger<PaymentWebhookService>.Instance);

    internal static async Task<Receipt> SeedAsync(AppDbContext db, InvoiceType type = InvoiceType.DEPOSIT,
        InvoiceStatus status = InvoiceStatus.ISSUED, bool withPlan = true)
    {
        var (_, customer) = await TestSupport.AddCustomerAsync(db);
        var (_, employee) = await TestSupport.AddLogisticsManagerAsync(db);
        var (booking, _) = await TestSupport.AddPendingBookingAsync(db, customer);
        var invoice = new Invoice
        {
            InvoiceId = Guid.NewGuid(), InvoiceCode = "INV-" + Guid.NewGuid().ToString("N"), BookingId = booking.BookingId,
            EmployeeId = employee.EmployeeId, Type = type, Status = status, Currency = "VND",
            TotalAmount = 100000, Subtotal = 100000, IssuedAt = status == InvoiceStatus.DRAFT ? null : Now.AddDays(-1),
            CreatedAt = Now.AddDays(-1), UpdatedAt = Now.AddDays(-1)
        };
        var receipt = new Receipt
        {
            ReceiptId = Guid.NewGuid(), ReceiptCode = "RCP-" + Guid.NewGuid().ToString("N"), Invoice = invoice,
            InvoiceId = invoice.InvoiceId, Amount = invoice.TotalAmount, Currency = "VND", PaymentMethod = PaymentMethod.BANK_TRANSFER,
            Status = ReceiptStatus.PENDING, CreatedAt = Now.AddHours(-1), UpdatedAt = Now.AddHours(-1)
        };
        db.Receipts.Add(receipt);
        if (withPlan)
        {
            var transport = new Transport
            {
                TransportId = Guid.NewGuid(), BookingId = booking.BookingId, Booking = booking,
                TransportCode = "TR-" + Guid.NewGuid().ToString("N"), CreatedAt = Now, UpdatedAt = Now
            };
            db.TransportPlans.Add(new TransportPlan
            {
                TransportPlanId = Guid.NewGuid(), Transport = transport, TransportId = transport.TransportId,
                PlanCode = "TP-" + Guid.NewGuid().ToString("N"), Status = TransportPlanStatus.PENDING, CreatedAt = Now, UpdatedAt = Now
            });
        }
        await db.SaveChangesAsync();
        return receipt;
    }

    internal static Request.WebhookRequest Payload(Receipt receipt, string notification = "ORDER_PAID") => new()
    {
        Timestamp = new DateTimeOffset(DateTime.SpecifyKind(Now, DateTimeKind.Utc)).ToUnixTimeSeconds(), NotificationType = notification,
        Order = new() { ReceiptCode = receipt.ReceiptCode, Status = notification == "ORDER_PAID" ? "CAPTURED" : "CANCELLED", Amount = receipt.Amount, Currency = receipt.Currency },
        Transaction = new() { Reference = "SEPAY-" + receipt.ReceiptId, Type = "PAYMENT", Status = "APPROVED",
            PaymentMethod = "BANK_TRANSFER", Amount = receipt.Amount, Currency = receipt.Currency }
    };

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("wrong")]
    [InlineData(Secret + "," + Secret)]
    public async Task InvalidAuthentication_DoesNotChangePayment(string? secret)
    {
        await using var db = TestSupport.CreateDb();
        var receipt = await SeedAsync(db);
        Assert.Equal("INVALID_SIGNATURE", (await Assert.ThrowsAsync<BadRequestException>(() =>
            CreateService(db).HandleWebhookAsync(Payload(receipt), secret))).ErrorCode);
        Assert.Equal(ReceiptStatus.PENDING, receipt.Status);
        Assert.Equal(InvoiceStatus.ISSUED, receipt.Invoice.Status);
        Assert.Empty(db.InvoiceStatusHistories); Assert.Empty(db.Notifications); Assert.Empty(db.OutboxMessages);
    }

    [Fact]
    public async Task MissingConfiguration_FailsClosed()
    {
        await using var db = TestSupport.CreateDb();
        var error = await Assert.ThrowsAsync<Pegaxus.Service.Exceptions.Exceptions>(() =>
            CreateService(db, secret: "").HandleWebhookAsync(new(), ""));
        Assert.Equal(503, error.StatusCode);
    }

    [Theory]
    [InlineData(InvoiceType.DEPOSIT, InvoiceStatus.ISSUED, true)]
    [InlineData(InvoiceType.FINAL, InvoiceStatus.OVERDUE, true)]
    [InlineData(InvoiceType.SUPPLEMENTARY, InvoiceStatus.ISSUED, false)]
    [InlineData(InvoiceType.DEPOSIT, InvoiceStatus.ISSUED, false)]
    public async Task Success_ConfirmsOnce_WithSystemHistoryNotificationAndEmail(InvoiceType type, InvoiceStatus status, bool withPlan)
    {
        await using var db = TestSupport.CreateDb();
        var receipt = await SeedAsync(db, type, status, withPlan);
        var realtime = new RecordingRealtime();
        var service = CreateService(db, realtime);
        var payload = Payload(receipt);
        payload.Transaction!.PaymentMethod = "CARD";
        await service.HandleWebhookAsync(payload, Secret);
        await service.HandleWebhookAsync(payload, Secret);
        Assert.Equal(ReceiptStatus.CONFIRMED, receipt.Status);
        Assert.Equal(PaymentMethod.OTHER, receipt.PaymentMethod);
        Assert.Equal(payload.Transaction.Reference, receipt.TransactionReference);
        Assert.Equal(Now, receipt.PaidAt); Assert.Equal(Now, receipt.ConfirmedAt); Assert.Equal(Now, receipt.UpdatedAt);
        Assert.Equal(InvoiceStatus.PAID, receipt.Invoice.Status); Assert.Equal(Now, receipt.Invoice.PaidAt);
        var history = Assert.Single(db.InvoiceStatusHistories);
        Assert.Equal(InvoiceHistoryType.RECORD_PAYMENT, history.Type); Assert.Equal(status, history.PreviousStatus);
        Assert.Equal(InvoiceStatus.PAID, history.NewStatus); Assert.Null(history.ChangedByAccountId);
        var notification = Assert.Single(db.Notifications);
        Assert.Equal(NotificationType.PAYMENT, notification.NotificationType);
        Assert.Equal("Invoice", notification.ReferenceType); Assert.Equal(receipt.InvoiceId, notification.ReferenceId);
        Assert.Contains(receipt.ReceiptCode, notification.Message); Assert.Single(db.AccountNotifications);
        Assert.Equal(1, realtime.Calls);
        var email = OutboxQueue.Deserialize<EmailPayload>(Assert.Single(db.OutboxMessages).Payload);
        Assert.Equal((await db.Accounts.SingleAsync(x => x.AccountType == AccountType.CUSTOMER)).Email, email.To);
        Assert.Contains(receipt.ReceiptCode, email.Body);
        if (withPlan)
        {
            var plan = Assert.Single(db.TransportPlans);
            Assert.Equal(type == InvoiceType.DEPOSIT ? TransportPlanStatus.APPROVED : TransportPlanStatus.PENDING, plan.Status);
            Assert.Null(plan.ApprovedByEmployeeId);
            Assert.Equal(type == InvoiceType.DEPOSIT ? Now : (DateTime?)null, plan.ApprovedAt);
        }

        // A void after confirmation leaves the invoice PAID and asks a Logistics Manager to reconcile it, once.
        await service.HandleWebhookAsync(Payload(receipt, "TRANSACTION_VOID"), Secret);
        await service.HandleWebhookAsync(Payload(receipt, "TRANSACTION_VOID"), Secret);
        Assert.Equal(ReceiptStatus.CONFIRMED, receipt.Status); Assert.Equal(InvoiceStatus.PAID, receipt.Invoice.Status);
        var flagged = await db.Notifications.SingleAsync(x => x.Title == "Payment needs reconciliation");
        Assert.Equal(NotificationPriority.URGENT, flagged.Priority); Assert.Contains(receipt.ReceiptCode, flagged.Message);
        Assert.Equal(2, await db.Notifications.CountAsync()); Assert.Equal(2, await db.OutboxMessages.CountAsync());
    }

    [Theory]
    [InlineData("orderAmount")]
    [InlineData("transactionAmount")]
    [InlineData("orderCurrency")]
    [InlineData("transactionCurrency")]
    [InlineData("invoiceAmount")]
    [InlineData("invoiceCurrency")]
    public async Task Mismatch_DoesNotConfirm(string field)
    {
        await using var db = TestSupport.CreateDb();
        var receipt = await SeedAsync(db);
        var payload = Payload(receipt);
        switch (field)
        {
            case "orderAmount": payload.Order!.Amount++; break;
            case "transactionAmount": payload.Transaction!.Amount++; break;
            case "orderCurrency": payload.Order!.Currency = "USD"; break;
            case "transactionCurrency": payload.Transaction!.Currency = "USD"; break;
            case "invoiceAmount": receipt.Invoice.TotalAmount++; break;
            case "invoiceCurrency": receipt.Invoice.Currency = "USD"; break;
        }
        Assert.Equal("AMOUNT_MISMATCH", (await Assert.ThrowsAsync<UnprocessableEntityException>(() =>
            CreateService(db).HandleWebhookAsync(payload, Secret))).ErrorCode);
        Assert.Equal(ReceiptStatus.PENDING, receipt.Status); Assert.Empty(db.InvoiceStatusHistories); Assert.Empty(db.OutboxMessages);
    }

    [Theory]
    [InlineData("missingOrder")]
    [InlineData("missingTransaction")]
    [InlineData("missingReference")]
    [InlineData("longReference")]
    [InlineData("unknownEvent")]
    [InlineData("declinedPayment")]
    [InlineData("uncapturedOrder")]
    [InlineData("refund")]
    [InlineData("negativeAmount")]
    [InlineData("missingAmount")]
    [InlineData("missingCurrency")]
    [InlineData("missingTimestamp")]
    public async Task InvalidPayload_DoesNotChangePayment(string field)
    {
        await using var db = TestSupport.CreateDb();
        var receipt = await SeedAsync(db);
        var payload = Payload(receipt);
        switch (field)
        {
            case "missingOrder": payload.Order = null; break;
            case "missingTransaction": payload.Transaction = null; break;
            case "missingReference": payload.Transaction!.Reference = null; break;
            case "longReference": payload.Transaction!.Reference = new string('x', 151); break;
            case "unknownEvent": payload.NotificationType = "UNKNOWN"; break;
            case "declinedPayment": payload.Transaction!.Status = "DECLINED"; break;
            case "uncapturedOrder": payload.Order!.Status = "AUTHENTICATION_NOT_NEEDED"; break;
            case "refund": payload.Transaction!.Type = "REFUND"; break;
            case "negativeAmount": payload.Transaction!.Amount = -1; break;
            case "missingAmount": payload.Order!.Amount = null; break;
            case "missingCurrency": payload.Transaction!.Currency = null; break;
            case "missingTimestamp": payload.Timestamp = 0; break;
        }
        Assert.Equal("VALIDATION_ERROR", (await Assert.ThrowsAsync<BadRequestException>(() =>
            CreateService(db).HandleWebhookAsync(payload, Secret))).ErrorCode);
        Assert.Equal(ReceiptStatus.PENDING, receipt.Status); Assert.Empty(db.Notifications);
    }

    [Fact]
    public async Task UnknownReceipt_ReturnsNotFound()
    {
        await using var db = TestSupport.CreateDb();
        var payload = Payload(await SeedAsync(db)); payload.Order!.ReceiptCode = "RCP-UNKNOWN";
        Assert.Equal("RECEIPT_NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(() =>
            CreateService(db).HandleWebhookAsync(payload, Secret))).ErrorCode);
    }

    [Fact]
    public async Task Void_FailsPendingReceipt_WithoutInvoiceEffects()
    {
        await using var db = TestSupport.CreateDb();
        var receipt = await SeedAsync(db);
        var service = CreateService(db);
        await service.HandleWebhookAsync(Payload(receipt, "TRANSACTION_VOID"), Secret);
        await service.HandleWebhookAsync(Payload(receipt, "TRANSACTION_VOID"), Secret);
        await service.HandleWebhookAsync(Payload(receipt), Secret);
        Assert.Equal(ReceiptStatus.FAILED, receipt.Status); Assert.Null(receipt.PaidAt); Assert.Null(receipt.ConfirmedAt);
        Assert.Equal(InvoiceStatus.ISSUED, receipt.Invoice.Status);
        Assert.Empty(db.InvoiceStatusHistories); Assert.Empty(db.Notifications); Assert.Empty(db.OutboxMessages);
    }

    [Fact]
    public async Task RefundedReceipt_IsAcknowledgedWithoutEffects()
    {
        await using var db = TestSupport.CreateDb();
        var receipt = await SeedAsync(db); receipt.Status = ReceiptStatus.REFUNDED; await db.SaveChangesAsync();
        await CreateService(db).HandleWebhookAsync(Payload(receipt), Secret);
        Assert.Equal(ReceiptStatus.REFUNDED, receipt.Status); Assert.Equal(InvoiceStatus.ISSUED, receipt.Invoice.Status);
        Assert.Empty(db.InvoiceStatusHistories); Assert.Empty(db.Notifications); Assert.Empty(db.OutboxMessages);
    }

    [Theory]
    [InlineData(InvoiceStatus.PAID)]
    [InlineData(InvoiceStatus.CANCELLED)]
    public async Task PaymentForClosedInvoice_IsAcknowledged_AndFlaggedForReconciliationOnce(InvoiceStatus status)
    {
        await using var db = TestSupport.CreateDb();
        var receipt = await SeedAsync(db, status: status);
        var service = CreateService(db);
        await service.HandleWebhookAsync(Payload(receipt), Secret);
        await service.HandleWebhookAsync(Payload(receipt), Secret);
        Assert.Equal(ReceiptStatus.PENDING, receipt.Status); Assert.Equal(status, receipt.Invoice.Status);
        Assert.Empty(db.InvoiceStatusHistories);
        var flagged = Assert.Single(db.Notifications);
        Assert.Equal("Payment needs reconciliation", flagged.Title); Assert.Equal(NotificationPriority.URGENT, flagged.Priority);
        Assert.Equal(receipt.ReceiptId, flagged.ReferenceId); Assert.Contains(receipt.ReceiptCode, flagged.Message);
        var manager = await db.Accounts.SingleAsync(x => x.AccountType == AccountType.EMPLOYEE);
        Assert.Equal(manager.AccountId, Assert.Single(db.AccountNotifications).AccountId);
        Assert.Equal(manager.Email, OutboxQueue.Deserialize<EmailPayload>(Assert.Single(db.OutboxMessages).Payload).To);
    }

    [Fact]
    public async Task ReceiptReplacedByNewerCheckout_IsConfirmed_WhenTheOldCheckoutIsPaid()
    {
        await using var db = TestSupport.CreateDb();
        var receipt = await SeedAsync(db); receipt.Status = ReceiptStatus.FAILED; await db.SaveChangesAsync();
        await CreateService(db).HandleWebhookAsync(Payload(receipt), Secret);
        Assert.Equal(ReceiptStatus.CONFIRMED, receipt.Status); Assert.Equal(InvoiceStatus.PAID, receipt.Invoice.Status);
        Assert.Single(db.InvoiceStatusHistories);
        Assert.Equal("Payment confirmed", Assert.Single(db.Notifications).Title);
    }

    [Theory]
    [InlineData(InvoiceType.QUOTATION, InvoiceStatus.ISSUED, "UNPAYABLE_INVOICE_TYPE")]
    [InlineData(InvoiceType.CREDIT_NOTE, InvoiceStatus.ISSUED, "UNPAYABLE_INVOICE_TYPE")]
    [InlineData(InvoiceType.DEPOSIT, InvoiceStatus.DRAFT, "INVALID_INVOICE_STATUS")]
    public async Task UnpayableInvoice_IsRejected(InvoiceType type, InvoiceStatus status, string errorCode)
    {
        await using var db = TestSupport.CreateDb();
        var receipt = await SeedAsync(db, type, status);
        var error = await Assert.ThrowsAnyAsync<Pegaxus.Service.Exceptions.Exceptions>(() => CreateService(db).HandleWebhookAsync(Payload(receipt), Secret));
        Assert.Equal(errorCode, error.ErrorCode); Assert.Equal(ReceiptStatus.PENDING, receipt.Status);
    }

    [Fact]
    public async Task Deposit_ApprovesLatestNonCancelledPlan_WithoutChangingStartedPlan()
    {
        await using var db = TestSupport.CreateDb();
        var receipt = await SeedAsync(db);
        var older = await db.TransportPlans.SingleAsync();
        var latest = new TransportPlan
        {
            TransportPlanId = Guid.NewGuid(), TransportId = older.TransportId, Transport = older.Transport,
            PlanCode = "TP-LATEST", Status = TransportPlanStatus.IN_PROGRESS, CreatedAt = Now.AddMinutes(1), UpdatedAt = Now
        };
        var cancelled = new TransportPlan
        {
            TransportPlanId = Guid.NewGuid(), TransportId = older.TransportId, Transport = older.Transport,
            PlanCode = "TP-CANCELLED", Status = TransportPlanStatus.CANCELLED, CreatedAt = Now.AddMinutes(2), UpdatedAt = Now
        };
        db.TransportPlans.AddRange(latest, cancelled); await db.SaveChangesAsync();
        await CreateService(db).HandleWebhookAsync(Payload(receipt), Secret);
        Assert.Equal(TransportPlanStatus.PENDING, older.Status);
        Assert.Equal(TransportPlanStatus.IN_PROGRESS, latest.Status);
        Assert.Equal(TransportPlanStatus.CANCELLED, cancelled.Status);
        Assert.Null(latest.ApprovedAt);
    }

    [Fact]
    public async Task ReferenceReusedForAnotherInvoice_IsRejected()
    {
        await using var db = TestSupport.CreateDb();
        var first = await SeedAsync(db); await CreateService(db).HandleWebhookAsync(Payload(first), Secret);
        var second = await SeedAsync(db); var payload = Payload(second); payload.Transaction!.Reference = first.TransactionReference;
        Assert.Equal("TRANSACTION_ALREADY_PROCESSED", (await Assert.ThrowsAsync<ConflictException>(() =>
            CreateService(db).HandleWebhookAsync(payload, Secret))).ErrorCode);
        Assert.Equal(ReceiptStatus.PENDING, second.Status); Assert.Single(db.InvoiceStatusHistories);
    }

    internal sealed class TestClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(Now, DateTimeKind.Utc));
    }

    private sealed class RecordingRealtime : INotificationRealtimeService
    {
        public int Calls { get; private set; }
        public Task SendToAccountAsync(Guid accountId, NotificationRealtimeDto notification, CancellationToken cancellationToken = default)
        { Calls++; return Task.CompletedTask; }
    }
}
