using System.Data;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.NotificationService;
using Pegaxus.Service.OutboxService;

namespace Pegaxus.Service.InvoiceService;

public sealed class Service(AppDbContext db, TimeProvider clock, IValidator<Request.RecordPaymentRequest> validator,
    NotificationService.IService notifications) : IService
{
    public async Task<Response.RecordPaymentResponse> RecordPaymentAsync(Guid invoiceId, Request.RecordPaymentRequest request,
        Guid accountId, CancellationToken cancellationToken = default)
    {
        var account = await db.Accounts.AsNoTracking().Include(x => x.EmployeeAccount)
            .SingleOrDefaultAsync(x => x.AccountId == accountId, cancellationToken);
        if (account is null || account.IsDeleted || account.Status != AccountStatus.ACTIVE ||
            account.AccountType == AccountType.EMPLOYEE && account.EmployeeAccount is not { IsDeleted: false, IsActive: true })
            throw new UnauthorizedException("The account is inactive or unavailable");
        if (account.AccountType != AccountType.EMPLOYEE || account.EmployeeAccount?.Role != EmployeeRole.LOGISTICS_MANAGER)
            throw new ForbiddenException("Only Logistics Managers can record manual payments");
        var validation = validator.Validate(request);
        if (!validation.IsValid)
            throw new FieldValidationException(validation.Errors.Select(x => new FieldError(
                char.ToLowerInvariant(x.PropertyName[0]) + x.PropertyName[1..], x.ErrorMessage)).ToArray());

        try
        {
            await using var transaction = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
                : await db.Database.BeginTransactionAsync(cancellationToken);
            var query = db.Invoices.Where(x => x.InvoiceId == invoiceId);
            // A LINQ self-update holds the invoice row lock until commit.
            if (db.Database.IsRelational())
                await query.ExecuteUpdateAsync(s => s.SetProperty(x => x.InvoiceId, x => x.InvoiceId), cancellationToken);
            var invoice = await query.SingleOrDefaultAsync(cancellationToken)
                ?? throw new NotFoundException("Invoice not found", "INVOICE_NOT_FOUND");
            // A reused context may track an invoice from before the lock.
            if (db.Database.IsRelational()) await db.Entry(invoice).ReloadAsync(cancellationToken);
            if (invoice.Type == InvoiceType.QUOTATION)
                throw new UnprocessableEntityException("This invoice type cannot be paid", "UNPAYABLE_INVOICE_TYPE");
            if (invoice.Status is not (InvoiceStatus.ISSUED or InvoiceStatus.OVERDUE) ||
                await db.Receipts.AnyAsync(x => x.InvoiceId == invoiceId && x.Status == ReceiptStatus.CONFIRMED, cancellationToken))
                throw new ConflictException($"Cannot pay an invoice in status {invoice.Status}", "INVALID_INVOICE_STATUS");
            if (request.Amount != invoice.TotalAmount)
                throw new UnprocessableEntityException("Only full payment of the invoice total is accepted", "PARTIAL_PAYMENT_NOT_SUPPORTED");
            if (!string.Equals(request.Currency.Trim(), invoice.Currency, StringComparison.OrdinalIgnoreCase))
                throw new UnprocessableEntityException("Payment currency must match the invoice currency", "CURRENCY_MISMATCH");

            var now = Timestamp(clock.GetUtcNow().UtcDateTime);
            var receipt = new Receipt
            {
                ReceiptId = Guid.NewGuid(), ReceiptCode = await GenerateCodeAsync(now, cancellationToken), InvoiceId = invoiceId,
                PaymentMethod = Enum.Parse<PaymentMethod>(request.PaymentMethod.Trim(), true), Status = ReceiptStatus.CONFIRMED,
                Amount = invoice.TotalAmount, Currency = invoice.Currency, TransactionReference = EmptyToNull(request.TransactionReference),
                PaidAt = request.PaidAt.HasValue ? Timestamp(request.PaidAt.Value.UtcDateTime) : now,
                ConfirmedAt = now, Note = EmptyToNull(request.Note), CreatedAt = now, UpdatedAt = now
            };
            db.Receipts.Add(receipt);
            var pending = db.Receipts.Where(x => x.InvoiceId == invoiceId && x.Status == ReceiptStatus.PENDING);
            if (db.Database.IsRelational())
                await pending.ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, ReceiptStatus.FAILED).SetProperty(x => x.UpdatedAt, now), cancellationToken);
            else
                foreach (var old in await pending.ToListAsync(cancellationToken)) { old.Status = ReceiptStatus.FAILED; old.UpdatedAt = now; }
            var previousStatus = invoice.Status;
            invoice.Status = InvoiceStatus.PAID;
            invoice.PaidAt = now;
            invoice.UpdatedAt = NextVersion(invoice.UpdatedAt, now);
            db.InvoiceStatusHistories.Add(new InvoiceStatusHistory
            {
                InvoiceStatusHistoryId = Guid.NewGuid(), InvoiceId = invoiceId, Type = InvoiceHistoryType.RECORD_PAYMENT,
                PreviousStatus = previousStatus, NewStatus = InvoiceStatus.PAID, ChangedByAccountId = accountId,
                ChangedAt = now, CreatedAt = now
            });
            string? planStatus = null;
            if (invoice.Type == InvoiceType.DEPOSIT)
            {
                var planId = await db.TransportPlans.AsNoTracking().Where(x => x.Transport.BookingId == invoice.BookingId &&
                        x.Status != TransportPlanStatus.CANCELLED).OrderByDescending(x => x.CreatedAt).ThenBy(x => x.TransportPlanId)
                    .Select(x => (Guid?)x.TransportPlanId).FirstOrDefaultAsync(cancellationToken);
                if (planId.HasValue)
                {
                    var plans = db.TransportPlans.Where(x => x.TransportPlanId == planId);
                    if (db.Database.IsRelational())
                        await plans.ExecuteUpdateAsync(s => s.SetProperty(x => x.TransportPlanId, x => x.TransportPlanId), cancellationToken);
                    var plan = await plans.SingleAsync(cancellationToken);
                    if (db.Database.IsRelational()) await db.Entry(plan).ReloadAsync(cancellationToken);
                    if (plan.Status is TransportPlanStatus.DRAFT or TransportPlanStatus.PENDING)
                    {
                        plan.Status = TransportPlanStatus.APPROVED; plan.ApprovedAt = now;
                        plan.ApprovedByEmployeeId = account.EmployeeAccount!.EmployeeId; plan.UpdatedAt = NextVersion(plan.UpdatedAt, now);
                        planStatus = plan.Status.ToString();
                    }
                }
            }
            var customer = await db.Bookings.AsNoTracking().Where(x => x.BookingId == invoice.BookingId)
                .Select(x => new { x.Customer.AccountId, x.Customer.Account.Email,
                    TransportId = x.Transport == null ? (Guid?)null : x.Transport.TransportId }).SingleAsync(cancellationToken);
            var amount = receipt.Amount.ToString("0.00", CultureInfo.InvariantCulture) + " " + receipt.Currency;
            var message = $"{(invoice.Type == InvoiceType.CREDIT_NOTE ? "Payout" : "Payment")} for invoice {invoice.InvoiceCode} has been confirmed. Receipt: {receipt.ReceiptCode}. Amount: {amount}. Invoice status: PAID.";
            notifications.Queue(new NotificationCreateRequest
            {
                AccountId = customer.AccountId, TransportId = customer.TransportId, Type = NotificationType.PAYMENT,
                Priority = NotificationPriority.NORMAL, Title = "Payment confirmed", Message = message,
                ReferenceType = nameof(Invoice), ReferenceId = invoiceId,
                ActionUrl = $"/bookings/{invoice.BookingId}?tab=invoices&invoiceId={invoiceId}", CreatedAt = now
            });
            OutboxQueue.AddEmail(db, new() { To = customer.Email, Subject = "Payment confirmed", Body = WebUtility.HtmlEncode(message) }, now);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            await notifications.FlushAsync(cancellationToken);
            return new(new(receipt.ReceiptId, receipt.ReceiptCode, receipt.Amount, receipt.Currency, receipt.PaymentMethod.ToString(),
                    receipt.Status.ToString(), receipt.TransactionReference, Offset(receipt.PaidAt!.Value), Offset(now), receipt.Note,
                    new(accountId, account.EmployeeAccount!.FullName)),
                new(invoiceId, invoice.InvoiceCode, invoice.Type.ToString(), invoice.Status.ToString(), Offset(now), ["CREATE_ANOTHER"], Offset(invoice.UpdatedAt)),
                new(planStatus, invoice.Type == InvoiceType.FINAL));
        }
        catch (Exception ex) when (ex is DbUpdateConcurrencyException || ex.GetBaseException() is PostgresException
            { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected } or PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "Receipt_receipt_code_key" })
        { throw new ConflictException("The record changed. Reload it and try again.", "CONCURRENT_UPDATE"); }
    }

    private async Task<string> GenerateCodeAsync(DateTime now, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var code = $"RCP-{now.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}-{RandomNumberGenerator.GetInt32(10_000):D4}";
            if (!await db.Receipts.AnyAsync(x => x.ReceiptCode == code, cancellationToken)) return code;
        }
        throw new ConflictException("Unable to generate a unique receipt code. Please try again.", "CONCURRENT_UPDATE");
    }
    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static DateTime Timestamp(DateTime value) => new(value.Ticks / 10 * 10, DateTimeKind.Unspecified);
    private static DateTime NextVersion(DateTime previous, DateTime now) => now > previous ? now : Timestamp(previous).AddTicks(10);
    private static DateTimeOffset Offset(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
