using System.Data;
using System.Net;
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

public sealed class Service(AppDbContext db, TimeProvider clock, IValidator<Request.CancelRequest> validator,
    NotificationService.IService notifications) : IService
{
    public async Task<Response.CancelResponse> CancelAsync(Guid invoiceId, Request.CancelRequest request, Guid accountId,
        CancellationToken cancellationToken = default)
    {
        var account = await db.Accounts.AsNoTracking().Include(x => x.EmployeeAccount)
            .SingleOrDefaultAsync(x => x.AccountId == accountId, cancellationToken);
        if (account is null || account.IsDeleted || account.Status != AccountStatus.ACTIVE ||
            account.AccountType == AccountType.EMPLOYEE && account.EmployeeAccount is not { IsDeleted: false, IsActive: true })
            throw new UnauthorizedException("The account is inactive or unavailable");
        if (account.AccountType != AccountType.EMPLOYEE || account.EmployeeAccount?.Role != EmployeeRole.LOGISTICS_MANAGER)
            throw new ForbiddenException("Only Logistics Managers can cancel invoices");
        var validation = validator.Validate(request);
        if (!validation.IsValid)
            throw new FieldValidationException(validation.Errors.Select(x => new FieldError("cancellationReason", x.ErrorMessage)).ToArray());
        var reason = request.CancellationReason.Trim();

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
            if (invoice.Status == InvoiceStatus.CANCELLED)
                throw new ConflictException("Invoice is already cancelled", "INVALID_INVOICE_STATUS");
            if (invoice.Status == InvoiceStatus.PAID ||
                await db.Receipts.AnyAsync(x => x.InvoiceId == invoiceId && x.Status == ReceiptStatus.CONFIRMED, cancellationToken))
                throw new UnprocessableEntityException("Cannot cancel an invoice that has a confirmed payment. Please contact accounting for the refund process", "INVOICE_HAS_PAYMENT");
            if (invoice.Status is not (InvoiceStatus.DRAFT or InvoiceStatus.ISSUED or InvoiceStatus.OVERDUE))
                throw new ConflictException($"Cannot cancel an invoice in status {invoice.Status}", "INVALID_INVOICE_STATUS");
            if (await db.Receipts.AnyAsync(x => x.InvoiceId == invoiceId && x.Status == ReceiptStatus.PENDING, cancellationToken))
                throw new ConflictException("A payment for this invoice is being processed. Please try again after it completes", "PAYMENT_IN_PROGRESS");

            var now = Timestamp(clock.GetUtcNow().UtcDateTime);
            var previousStatus = invoice.Status;
            invoice.Status = InvoiceStatus.CANCELLED;
            invoice.CancelledAt = now;
            invoice.CancellationReason = reason;
            invoice.UpdatedAt = now > invoice.UpdatedAt ? now : Timestamp(invoice.UpdatedAt).AddTicks(10);
            db.InvoiceStatusHistories.Add(new InvoiceStatusHistory
            {
                InvoiceStatusHistoryId = Guid.NewGuid(), InvoiceId = invoiceId, Type = InvoiceHistoryType.CANCEL,
                PreviousStatus = previousStatus, NewStatus = InvoiceStatus.CANCELLED, ChangedByAccountId = accountId,
                Reason = reason, ChangedAt = now, CreatedAt = now
            });
            if (invoice.IssuedAt.HasValue)
            {
                var customer = await db.Bookings.AsNoTracking().Where(x => x.BookingId == invoice.BookingId)
                    .Select(x => new { x.Customer.AccountId, x.Customer.Account.Email,
                        TransportId = x.Transport == null ? (Guid?)null : x.Transport.TransportId }).SingleAsync(cancellationToken);
                var message = $"Invoice {invoice.InvoiceCode} ({invoice.Type}) has been cancelled. Reason: {reason}. Please contact support if you need more information.";
                notifications.Queue(new NotificationCreateRequest
                {
                    AccountId = customer.AccountId, TransportId = customer.TransportId, Type = NotificationType.PAYMENT,
                    Priority = NotificationPriority.NORMAL, Title = "Invoice cancelled", Message = message,
                    ReferenceType = nameof(Invoice), ReferenceId = invoiceId,
                    ActionUrl = $"/bookings/{invoice.BookingId}?tab=invoices&invoiceId={invoiceId}", CreatedAt = now
                });
                OutboxQueue.AddEmail(db, new() { To = customer.Email, Subject = "Invoice cancelled", Body = WebUtility.HtmlEncode(message) }, now);
            }
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            await notifications.FlushAsync(cancellationToken);
            return new(invoiceId, invoice.InvoiceCode, invoice.Status.ToString(), Offset(now), reason, ["CREATE_ANOTHER"], Offset(invoice.UpdatedAt));
        }
        catch (Exception ex) when (ex is DbUpdateConcurrencyException ||
            ex.GetBaseException() is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected })
        { throw new ConflictException("The record changed. Reload it and try again.", "CONCURRENT_UPDATE"); }
    }

    private static DateTime Timestamp(DateTime value) => new(value.Ticks / 10 * 10, DateTimeKind.Unspecified);
    private static DateTimeOffset Offset(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
