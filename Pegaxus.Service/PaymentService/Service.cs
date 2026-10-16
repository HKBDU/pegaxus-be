using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Repository.Extensions;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.MailService;
using Pegaxus.Service.NotificationService;
using Pegaxus.Service.OutboxService;

namespace Pegaxus.Service.PaymentService;

public sealed class Service(AppDbContext db, NotificationService.INotificationService notifications,
    IOptions<SePayWebhookOptions> options, TimeProvider clock, ILogger<Service> logger) : IPaymentService
{
    public async Task HandleWebhookAsync(Request.WebhookRequest request, string? secretKey,
        CancellationToken cancellationToken = default)
    {
        var expectedKey = options.Value.SecretKey;
        if (string.IsNullOrWhiteSpace(expectedKey))
            throw new Pegaxus.Service.Exceptions.Exceptions("Payment webhook is not configured", 503, "PAYMENT_GATEWAY_UNAVAILABLE");

        // SePay IPN uses SECRET_KEY authentication, not the checkout HMAC signature.
        if (string.IsNullOrEmpty(secretKey) || !CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(Encoding.UTF8.GetBytes(expectedKey)), SHA256.HashData(Encoding.UTF8.GetBytes(secretKey))))
        {
            logger.LogWarning("Rejected SePay IPN: invalid or missing X-Secret-Key");
            throw new BadRequestException("Invalid webhook authentication", "INVALID_SIGNATURE");
        }

        Validate(request);
        var order = request.Order!;
        var payment = request.Transaction!;
        await using var transaction = await db.Database.BeginSerializableAsync(cancellationToken);
        var invoiceId = await db.Receipts.AsNoTracking().Where(x => x.ReceiptCode == order.ReceiptCode)
            .Select(x => (Guid?)x.InvoiceId).SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Receipt not found", "RECEIPT_NOT_FOUND");

        await db.LockRowsAsync(db.Invoices.Where(x => x.InvoiceId == invoiceId), x => x.InvoiceId, cancellationToken);
        var receipt = await db.Receipts.SingleAsync(x => x.ReceiptCode == order.ReceiptCode, cancellationToken);
        var invoice = await db.Invoices.SingleAsync(x => x.InvoiceId == invoiceId, cancellationToken);
        if (db.Database.IsRelational())
        {
            await db.Entry(receipt).ReloadAsync(cancellationToken);
            await db.Entry(invoice).ReloadAsync(cancellationToken);
        }

        var now = DateTime.SpecifyKind(clock.GetUtcNow().UtcDateTime, DateTimeKind.Unspecified);
        var isVoid = request.NotificationType == "TRANSACTION_VOID";
        if (receipt.Status == ReceiptStatus.REFUNDED)
        {
            LogForAccounting(receipt, payment.Reference);
            return;
        }

        if (receipt.Status == ReceiptStatus.CONFIRMED)
        {
            // An identical re-delivery is silently acknowledged. A void or a second transaction on a paid receipt
            // needs a person to reconcile it, because the gateway state no longer matches the invoice.
            if (isVoid || receipt.TransactionReference != payment.Reference)
            {
                LogForAccounting(receipt, payment.Reference);
                await FlagForReconciliationAsync(receipt, invoice, payment.Reference!, isVoid
                    ? "The gateway voided a payment that was already confirmed"
                    : "The gateway reported another transaction for a receipt that was already paid", now, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                await notifications.FlushAsync(cancellationToken);
            }
            return;
        }

        if (isVoid)
        {
            if (receipt.Status == ReceiptStatus.PENDING)
            {
                receipt.Status = ReceiptStatus.FAILED;
                receipt.TransactionReference = payment.Reference;
                receipt.UpdatedAt = now;
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            return;
        }

        if (order.Amount != receipt.Amount || payment.Amount != receipt.Amount || receipt.Amount != invoice.TotalAmount ||
            order.Currency != receipt.Currency || payment.Currency != receipt.Currency || receipt.Currency != invoice.Currency || receipt.Currency != "VND")
        {
            logger.LogWarning("SePay payment amount/currency mismatch for receipt {ReceiptCode}", receipt.ReceiptCode);
            throw new UnprocessableEntityException("Payment amount or currency does not match the receipt", "AMOUNT_MISMATCH");
        }
        if (receipt.Status == ReceiptStatus.FAILED && receipt.TransactionReference == payment.Reference)
        {
            // This exact transaction was voided earlier, so a later "paid" event for it cannot be trusted.
            LogForAccounting(receipt, payment.Reference);
            return;
        }

        if (invoice.Status is InvoiceStatus.PAID or InvoiceStatus.CANCELLED ||
            await db.Receipts.AnyAsync(x => x.InvoiceId == invoiceId && x.Status == ReceiptStatus.CONFIRMED, cancellationToken))
        {
            // The money arrived but the invoice cannot take it (already paid by another receipt, or cancelled).
            LogForAccounting(receipt, payment.Reference);
            await FlagForReconciliationAsync(receipt, invoice, payment.Reference!,
                $"A payment was received for invoice {invoice.InvoiceCode}, which is already {invoice.Status}", now, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            await notifications.FlushAsync(cancellationToken);
            return;
        }

        // A receipt that is still PENDING, or FAILED only because a newer checkout replaced it, is confirmed here:
        // the gateway has captured the money, so the invoice must reflect it.
        if (invoice.Type is not (InvoiceType.DEPOSIT or InvoiceType.FINAL or InvoiceType.SUPPLEMENTARY))
            throw new UnprocessableEntityException("This invoice type cannot be paid online", "UNPAYABLE_INVOICE_TYPE");
        if (invoice.Status is not (InvoiceStatus.ISSUED or InvoiceStatus.OVERDUE) || invoice.IssuedAt is null)
            throw new ConflictException("Invoice cannot be paid in its current status", "INVALID_INVOICE_STATUS");
        if (await db.Receipts.AnyAsync(x => x.ReceiptId != receipt.ReceiptId && x.Status == ReceiptStatus.CONFIRMED &&
                x.TransactionReference == payment.Reference, cancellationToken))
            throw new ConflictException("Payment transaction has already been recorded", "TRANSACTION_ALREADY_PROCESSED");

        var previousStatus = invoice.Status;
        receipt.Status = ReceiptStatus.CONFIRMED;
        receipt.TransactionReference = payment.Reference;
        receipt.PaymentMethod = payment.PaymentMethod == "BANK_TRANSFER" ? PaymentMethod.BANK_TRANSFER : PaymentMethod.OTHER;
        receipt.PaidAt = receipt.ConfirmedAt = receipt.UpdatedAt = now;
        invoice.Status = InvoiceStatus.PAID;
        invoice.PaidAt = invoice.UpdatedAt = now;
        db.InvoiceStatusHistories.Add(new InvoiceStatusHistory
        {
            InvoiceStatusHistoryId = Guid.NewGuid(), InvoiceId = invoice.InvoiceId,
            Type = InvoiceHistoryType.RECORD_PAYMENT, PreviousStatus = previousStatus, NewStatus = InvoiceStatus.PAID,
            ChangedByAccountId = null, ChangedAt = now, CreatedAt = now
        });

        if (invoice.Type == InvoiceType.DEPOSIT)
        {
            var plan = await db.TransportPlans
                .Where(x => x.Transport.BookingId == invoice.BookingId && x.Status != TransportPlanStatus.CANCELLED)
                .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.TransportPlanId)
                .FirstOrDefaultAsync(cancellationToken);
            if (plan is not null && db.Database.IsRelational())
                await db.Entry(plan).ReloadAsync(cancellationToken);
            if (plan?.Status is TransportPlanStatus.DRAFT or TransportPlanStatus.PENDING)
            {
                plan.Status = TransportPlanStatus.APPROVED;
                plan.ApprovedByEmployeeId = null;
                plan.ApprovedAt = plan.UpdatedAt = now;
            }
        }

        var customer = await db.Bookings.AsNoTracking().Where(x => x.BookingId == invoice.BookingId)
            .Select(x => new { x.Customer.AccountId, x.Customer.Account.Email, x.Customer.FullName, TransportId = (Guid?)x.Transport!.TransportId })
            .SingleAsync(cancellationToken);
        var message = $"Online payment for invoice {invoice.InvoiceCode} has been confirmed. Receipt: {receipt.ReceiptCode}. " +
            $"Amount: {Pegaxus.Service.Common.Vnd.Format(receipt.Amount, receipt.Currency)}. Invoice status: PAID.";
        notifications.Queue(new NotificationCreateRequest
        {
            AccountId = customer.AccountId, TransportId = customer.TransportId, Type = NotificationType.PAYMENT,
            Priority = NotificationPriority.NORMAL, Title = "Payment confirmed", Message = message,
            ReferenceType = "Invoice", ReferenceId = invoice.InvoiceId,
            ActionUrl = $"/bookings/{invoice.BookingId}?invoiceId={invoice.InvoiceId}", CreatedAt = now
        });
        OutboxQueue.AddEmail(db, new MailContent
        {
            To = customer.Email, Subject = $"Payment confirmed — {receipt.ReceiptCode}",
            Body = EmailBodies.PaymentConfirmedBody(customer.FullName, invoice.InvoiceCode, receipt.ReceiptCode,
                receipt.Amount, receipt.Currency, isPayout: false, actionUrl: null)
        }, now);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await notifications.FlushAsync(cancellationToken);
    }

    private async Task FlagForReconciliationAsync(Receipt receipt, Invoice invoice, string reference, string reason,
        DateTime now, CancellationToken cancellationToken)
    {
        const string title = "Payment needs reconciliation";
        // SePay retries a webhook until it is acknowledged, so one gateway transaction must flag only once.
        if (await db.Notifications.AnyAsync(x => x.ReferenceType == nameof(Receipt) && x.ReferenceId == receipt.ReceiptId &&
                x.Title == title && x.Message.Contains(reference), cancellationToken))
            return;

        var managers = await db.Employees.AsNoTracking().Active().Where(x => x.Role == EmployeeRole.LOGISTICS_MANAGER)
            .Select(x => new { x.AccountId, x.Account.Email, x.FullName }).ToListAsync(cancellationToken);
        if (managers.Count == 0)
        {
            logger.LogError("SePay payment {Reference} for receipt {ReceiptCode} needs reconciliation but no active Logistics Manager exists",
                reference, receipt.ReceiptCode);
            return;
        }

        var message = $"{reason}. Receipt: {receipt.ReceiptCode} ({receipt.Status}). Gateway reference: {reference}. " +
            $"Amount: {Pegaxus.Service.Common.Vnd.Format(receipt.Amount, receipt.Currency)}. " +
            $"Invoice: {invoice.InvoiceCode} ({invoice.Status}). Check the bank statement and refund or reconcile manually.";
        foreach (var manager in managers)
        {
            notifications.Queue(new NotificationCreateRequest
            {
                AccountId = manager.AccountId, Type = NotificationType.PAYMENT, Priority = NotificationPriority.URGENT,
                Title = title, Message = message, ReferenceType = nameof(Receipt), ReferenceId = receipt.ReceiptId,
                ActionUrl = $"/invoices/{invoice.InvoiceId}", CreatedAt = now
            });
            OutboxQueue.AddEmail(db, new MailContent
            {
                To = manager.Email, Subject = $"{title} — {receipt.ReceiptCode}",
                Body = EmailBodies.PaymentReconciliationBody(manager.FullName, reason, receipt.ReceiptCode, receipt.Status.ToString(), reference,
                    receipt.Amount, receipt.Currency, invoice.InvoiceCode, invoice.Status.ToString(), actionUrl: null)
            }, now);
        }
    }

    private void LogForAccounting(Receipt receipt, string? reference) =>
        logger.LogWarning("SePay IPN requires accounting review: receipt {ReceiptCode}, transaction {TransactionReference}, receipt status {Status}",
            receipt.ReceiptCode, reference, receipt.Status);

    private static void Validate(Request.WebhookRequest request)
    {
        var order = request.Order;
        var payment = request.Transaction;
        if (request.Timestamp is <= 0 or > 253402300799 || request.NotificationType is not ("ORDER_PAID" or "TRANSACTION_VOID") ||
            order is null || payment is null || string.IsNullOrWhiteSpace(order.ReceiptCode) || order.ReceiptCode.Length > 50 ||
            string.IsNullOrWhiteSpace(order.Status) || order.Status.Length > 50 ||
            string.IsNullOrWhiteSpace(payment.Reference) || payment.Reference.Length > 150 || payment.Type != "PAYMENT" ||
            string.IsNullOrWhiteSpace(payment.Status) || payment.Status.Length > 50 ||
            string.IsNullOrWhiteSpace(payment.PaymentMethod) || payment.PaymentMethod.Length > 50 ||
            order.Amount is null or <= 0 || payment.Amount is null or < 0 ||
            order.Currency?.Length != 3 || payment.Currency?.Length != 3 ||
            (request.NotificationType == "ORDER_PAID" && (order.Status != "CAPTURED" || payment.Status != "APPROVED" || payment.Amount <= 0)))
            throw new BadRequestException("Invalid SePay webhook payload", "VALIDATION_ERROR");
    }
}
