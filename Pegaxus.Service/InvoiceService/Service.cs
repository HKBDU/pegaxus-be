using System.Data;
using System.Globalization;
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

public sealed class Service(AppDbContext db, TimeProvider clock, IValidator<Request.IssueRequest> validator,
    NotificationService.IService notifications) : IService
{
    public async Task<Response.IssueResponse> IssueAsync(Guid invoiceId, Request.IssueRequest request, Guid accountId,
        CancellationToken cancellationToken = default)
    {
        var account = await db.Accounts.AsNoTracking().Include(x => x.EmployeeAccount)
            .SingleOrDefaultAsync(x => x.AccountId == accountId, cancellationToken);
        if (account is null || account.IsDeleted || account.Status != AccountStatus.ACTIVE ||
            account.AccountType == AccountType.EMPLOYEE && account.EmployeeAccount is not { IsDeleted: false, IsActive: true })
            throw new UnauthorizedException("The account is inactive or unavailable");
        if (account.AccountType != AccountType.EMPLOYEE || account.EmployeeAccount?.Role != EmployeeRole.LOGISTICS_MANAGER)
            throw new ForbiddenException("Only Logistics Managers can issue invoices");
        var validation = validator.Validate(request);
        if (!validation.IsValid)
            throw new FieldValidationException(validation.Errors.Select(x => new FieldError("lastUpdatedAt", x.ErrorMessage)).ToArray());

        try
        {
            await using var transaction = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
                : await db.Database.BeginTransactionAsync(cancellationToken);
            var query = db.Invoices.Where(x => x.InvoiceId == invoiceId);
            // A LINQ self-update holds the invoice row lock until commit.
            if (db.Database.IsRelational())
                await query.ExecuteUpdateAsync(setters => setters.SetProperty(x => x.InvoiceId, x => x.InvoiceId), cancellationToken);
            var invoice = await query.SingleOrDefaultAsync(cancellationToken)
                ?? throw new NotFoundException("Invoice not found", "INVOICE_NOT_FOUND");
            // A reused context can already track a version from before the row lock.
            if (db.Database.IsRelational()) await db.Entry(invoice).ReloadAsync(cancellationToken);
            if (invoice.Status != InvoiceStatus.DRAFT)
                throw new ConflictException("Invoice must be in DRAFT status to be issued", "INVALID_INVOICE_STATUS");
            if (Timestamp(invoice.UpdatedAt) != Timestamp(request.LastUpdatedAt!.Value.UtcDateTime)) throw Modified();

            var items = await db.InvoiceItems.AsNoTracking().Where(x => x.InvoiceId == invoiceId)
                .Select(x => new { x.ItemType, x.Description, x.Quantity, x.UnitPrice, x.LineAmount }).ToListAsync(cancellationToken);
            if (!items.Any(x => x.ItemType != InvoiceItemType.OTHER || x.LineAmount >= 0))
                throw new UnprocessableEntityException("The invoice must have at least one fee line before it can be issued", "EMPTY_INVOICE_ITEMS");
            if (invoice.TotalAmount <= 0)
                throw new UnprocessableEntityException("Invoice total must be greater than 0", "INVOICE_TOTAL_NOT_POSITIVE");
            if (invoice.Type == InvoiceType.FINAL)
            {
                var deposits = await db.Invoices.AsNoTracking().Where(x => x.BookingId == invoice.BookingId &&
                        x.Type == InvoiceType.DEPOSIT && x.Status == InvoiceStatus.PAID)
                    .Select(x => new { x.InvoiceCode, x.TotalAmount, x.Currency }).ToListAsync(cancellationToken);
                var expected = deposits.Select(x => (Description: $"Deposit deduction ({x.InvoiceCode})", Quantity: 1m,
                    UnitPrice: -x.TotalAmount, LineAmount: -x.TotalAmount)).OrderBy(x => x.Description, StringComparer.Ordinal);
                var saved = items.Where(x => x.ItemType == InvoiceItemType.OTHER && x.LineAmount < 0)
                    .Select(x => (x.Description, x.Quantity, x.UnitPrice, x.LineAmount)).OrderBy(x => x.Description, StringComparer.Ordinal);
                if (deposits.Any(x => x.Currency != invoice.Currency) || !saved.SequenceEqual(expected))
                    throw new ConflictException("The paid deposits changed after this draft was saved. Please open the draft and save it again", "DEPOSIT_DEDUCTION_CHANGED");
            }

            var customer = await db.Bookings.AsNoTracking().Where(x => x.BookingId == invoice.BookingId)
                .Select(x => new { x.BookingCode, x.Customer.AccountId, x.Customer.Account.Email,
                    TransportId = x.Transport == null ? (Guid?)null : x.Transport.TransportId }).SingleAsync(cancellationToken);
            var now = Timestamp(clock.GetUtcNow().UtcDateTime);
            invoice.Status = InvoiceStatus.ISSUED;
            invoice.IssuedAt = now;
            invoice.UpdatedAt = now > invoice.UpdatedAt ? now : Timestamp(invoice.UpdatedAt).AddTicks(10);
            invoice.EmployeeId = account.EmployeeAccount!.EmployeeId;
            db.InvoiceStatusHistories.Add(new InvoiceStatusHistory
            {
                InvoiceStatusHistoryId = Guid.NewGuid(), InvoiceId = invoiceId, Type = InvoiceHistoryType.ISSUE,
                PreviousStatus = InvoiceStatus.DRAFT, NewStatus = InvoiceStatus.ISSUED, ChangedByAccountId = accountId,
                ChangedAt = now, CreatedAt = now
            });
            var amount = invoice.TotalAmount.ToString("0.00", CultureInfo.InvariantCulture) + " " + invoice.Currency;
            var (title, message, priority) = invoice.Type switch
            {
                InvoiceType.QUOTATION => ("Quotation issued", $"Quotation {invoice.InvoiceCode} for booking {customer.BookingCode} has been issued. Estimated total: {amount}. No payment is required.", NotificationPriority.NORMAL),
                InvoiceType.CREDIT_NOTE => ("Credit note issued", $"Credit note {invoice.InvoiceCode} has been issued. Payout or credit amount: {amount}.", NotificationPriority.NORMAL),
                _ => ("Invoice issued", $"Invoice {invoice.InvoiceCode} ({invoice.Type}) has been issued. Amount due: {amount}. Due date: {invoice.DueDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "Not specified"}.", NotificationPriority.HIGH)
            };
            notifications.Queue(new NotificationCreateRequest
            {
                AccountId = customer.AccountId, TransportId = customer.TransportId, Type = NotificationType.PAYMENT,
                Priority = priority, Title = title, Message = message, ReferenceType = nameof(Invoice), ReferenceId = invoiceId,
                ActionUrl = $"/bookings/{invoice.BookingId}?tab=invoices&invoiceId={invoiceId}", CreatedAt = now
            });
            OutboxQueue.AddEmail(db, new() { To = customer.Email, Subject = title, Body = WebUtility.HtmlEncode(message) }, now);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            await notifications.FlushAsync(cancellationToken);
            return new(invoiceId, invoice.InvoiceCode, invoice.Type.ToString(), invoice.Status.ToString(),
                DateOnly.FromDateTime(now), Offset(now), invoice.TotalAmount, invoice.Currency, invoice.DueDate,
                invoice.Type == InvoiceType.QUOTATION ? ["CANCEL", "CREATE_ANOTHER"] : ["RECORD_PAYMENT", "CANCEL", "CREATE_ANOTHER"],
                Offset(invoice.UpdatedAt));
        }
        catch (Exception ex) when (ex is DbUpdateConcurrencyException ||
            ex.GetBaseException() is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected })
        { throw Modified(); }
    }

    private static DateTime Timestamp(DateTime value) => new(value.Ticks / 10 * 10, DateTimeKind.Unspecified);
    private static DateTimeOffset Offset(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    private static ConflictException Modified() => new("The invoice was just modified. Please reload the page", "INVOICE_MODIFIED");
}
