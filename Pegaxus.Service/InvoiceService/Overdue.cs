using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Repository.Extensions;
using Pegaxus.Service.Common;
using Pegaxus.Service.NotificationService;
using Pegaxus.Service.OutboxService;

using static Pegaxus.Service.Common.DbTime;

namespace Pegaxus.Service.InvoiceService;

public sealed partial class Service
{
    private const int OverdueBatchSize = 100;

    /// <summary>
    /// Flow6 R13: an ISSUED invoice (never a QUOTATION) whose due moment has passed becomes OVERDUE. Run it periodically;
    /// an invoice that is already OVERDUE is not picked again, so a repeated run adds no history and no notification.
    /// </summary>
    /// <returns>The number of invoices marked overdue in this run (at most one batch; the rest follows on the next run).</returns>
    public async Task<int> MarkOverdueAsync(CancellationToken cancellationToken = default)
    {
        var now = Truncate(clock.GetUtcNow());
        var due = await db.Invoices.AsNoTracking()
            .Where(x => x.Status == InvoiceStatus.ISSUED && x.Type != InvoiceType.QUOTATION && x.DueDate < now)
            .OrderBy(x => x.DueDate).ThenBy(x => x.InvoiceId).Select(x => x.InvoiceId).Take(OverdueBatchSize).ToArrayAsync(cancellationToken);
        var marked = 0;
        foreach (var invoiceId in due)
            if (await MarkOneOverdueAsync(invoiceId, cancellationToken)) marked++;
        return marked;
    }

    private async Task<bool> MarkOneOverdueAsync(Guid invoiceId, CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await db.Database.BeginSerializableAsync(cancellationToken);
            var query = db.Invoices.Where(x => x.InvoiceId == invoiceId);
            await db.LockRowsAsync(query, x => x.InvoiceId, cancellationToken);
            var invoice = await query.SingleAsync(cancellationToken);
            if (db.Database.IsRelational()) await db.Entry(invoice).ReloadAsync(cancellationToken);
            var now = Truncate(clock.GetUtcNow());
            // Checked again under the lock: a payment or a cancellation may have won the race since the list was read.
            if (invoice.Status != InvoiceStatus.ISSUED || invoice.Type == InvoiceType.QUOTATION || invoice.DueDate is not { } dueAt || dueAt >= now)
                return false;

            var customer = await db.Bookings.AsNoTracking().Where(x => x.BookingId == invoice.BookingId)
                .Select(x => new { x.BookingCode, x.Customer.AccountId, x.Customer.Account.Email, x.Customer.FullName,
                    TransportId = x.Transport == null ? (Guid?)null : x.Transport.TransportId }).SingleAsync(cancellationToken);
            var managers = await db.Employees.AsNoTracking().Active().Where(x => x.Role == EmployeeRole.LOGISTICS_MANAGER)
                .Select(x => x.AccountId).ToListAsync(cancellationToken);

            invoice.Status = InvoiceStatus.OVERDUE;
            invoice.UpdatedAt = now > invoice.UpdatedAt ? now : Truncate(invoice.UpdatedAt).AddTicks(10);
            db.InvoiceStatusHistories.Add(new InvoiceStatusHistory
            {
                InvoiceStatusHistoryId = Guid.NewGuid(), InvoiceId = invoiceId, Type = InvoiceHistoryType.MARK_OVERDUE,
                PreviousStatus = InvoiceStatus.ISSUED, NewStatus = InvoiceStatus.OVERDUE, ChangedByAccountId = null,
                ChangedAt = now, CreatedAt = now
            });

            var amount = Vnd.Format(invoice.TotalAmount, invoice.Currency);
            var dueText = dueAt.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture) + " UTC";
            var label = InvoiceTypeLabel(invoice.Type);
            // A credit note is a payout the company owes the customer, so only the managers are told; asking the customer to pay it would be wrong.
            if (invoice.Type != InvoiceType.CREDIT_NOTE)
            {
                notifications.Queue(new NotificationCreateRequest
                {
                    AccountId = customer.AccountId, TransportId = customer.TransportId, Type = NotificationType.PAYMENT,
                    Priority = NotificationPriority.HIGH, Title = "Invoice overdue", ReferenceType = nameof(Invoice), ReferenceId = invoiceId,
                    Message = $"{label} {invoice.InvoiceCode} for booking {customer.BookingCode} is overdue. Amount due: {amount}. It was due on {dueText}. Please pay as soon as possible.",
                    ActionUrl = NotificationLinks.Invoice(null, invoice.BookingId, invoiceId), CreatedAt = now
                });
                OutboxQueue.AddEmail(db, new()
                {
                    To = customer.Email, Subject = $"Invoice overdue — {invoice.InvoiceCode}",
                    Body = MailService.EmailBodies.InvoiceOverdueBody(customer.FullName, invoice.InvoiceCode, customer.BookingCode,
                        invoice.TotalAmount, invoice.Currency, invoice.DueDate, NotificationLinks.Invoice(null, invoice.BookingId, invoiceId))
                }, now);
            }
            foreach (var managerAccountId in managers)
                notifications.Queue(new NotificationCreateRequest
                {
                    AccountId = managerAccountId, TransportId = customer.TransportId, Type = NotificationType.PAYMENT,
                    Priority = NotificationPriority.HIGH, Title = "Invoice overdue", ReferenceType = nameof(Invoice), ReferenceId = invoiceId,
                    Message = $"{label} {invoice.InvoiceCode} for booking {customer.BookingCode} ({customer.FullName}) is overdue. Amount: {amount}. It was due on {dueText}.",
                    ActionUrl = NotificationLinks.Invoice(EmployeeRole.LOGISTICS_MANAGER, invoice.BookingId, invoiceId), CreatedAt = now
                });
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            await notifications.FlushAsync(cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is DbUpdateConcurrencyException ||
            ex.GetBaseException() is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected })
        {
            db.ChangeTracker.Clear();
            return false; // another request changed the invoice first; the next run looks at it again
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // One broken invoice must not stop the others from being marked.
            logger.LogError(ex, "Could not mark invoice {InvoiceId} overdue", invoiceId);
            db.ChangeTracker.Clear();
            return false;
        }
    }
}
