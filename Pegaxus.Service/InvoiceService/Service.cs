using System.Data;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
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

public sealed class Service(AppDbContext db, TimeProvider clock, IValidator<Request.CreateRequest> validator,
    IValidator<Request.SuggestionRequest> suggestionValidator, IValidator<Request.UpdateRequest> updateValidator,
    IValidator<Request.ListRequest> listValidator, IValidator<Request.BookingListRequest> bookingListValidator,
    IValidator<Request.IssueRequest> issueValidator, IValidator<Request.RecordPaymentRequest> paymentValidator,
    IValidator<Request.CancelRequest> cancelValidator, NotificationService.IService notifications) : IService
{
    private const decimal MaxAmount = 9_999_999_999_999_999.99m;

    public async Task<IReadOnlyList<Response.StatusHistoryResponse>> GetStatusHistoryAsync(Guid invoiceId, Guid accountId,
        CancellationToken cancellationToken = default)
    {
        var account = await db.Accounts.AsNoTracking().Include(x => x.EmployeeAccount).Include(x => x.CustomerAccount)
            .SingleOrDefaultAsync(x => x.AccountId == accountId, cancellationToken);
        if (account is null || account.IsDeleted || account.Status != AccountStatus.ACTIVE ||
            account.AccountType == AccountType.EMPLOYEE && account.EmployeeAccount is not { IsDeleted: false, IsActive: true } ||
            account.AccountType == AccountType.CUSTOMER && account.CustomerAccount is not { IsDeleted: false })
            throw new UnauthorizedException("The account is inactive or unavailable");
        var isCustomer = account.AccountType == AccountType.CUSTOMER;
        if (!isCustomer && (account.AccountType != AccountType.EMPLOYEE || account.EmployeeAccount?.Role != EmployeeRole.LOGISTICS_MANAGER))
            throw new ForbiddenException("Only Logistics Managers and Customers can view invoice status history");

        var invoices = db.Invoices.AsNoTracking().Where(x => x.InvoiceId == invoiceId);
        if (isCustomer) invoices = invoices.Where(x => x.Booking.Customer.AccountId == accountId);
        var invoice = await invoices.Select(x => new { x.IssuedAt }).SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Invoice not found", "INVOICE_NOT_FOUND");
        if (isCustomer && !invoice.IssuedAt.HasValue)
            throw new ForbiddenException("Invoice status history is only available after the invoice has been issued");

        var query = db.InvoiceStatusHistories.AsNoTracking().Where(x => x.InvoiceId == invoiceId);
        if (isCustomer) query = query.Where(x => x.Type != InvoiceHistoryType.CREATE && x.NewStatus != InvoiceStatus.DRAFT);
        var histories = await query.OrderBy(x => x.ChangedAt).ThenBy(x => x.InvoiceStatusHistoryId)
            .Select(x => new
            {
                x.InvoiceStatusHistoryId, x.Type, x.PreviousStatus, x.NewStatus, x.Reason, x.ChangedAt, x.ChangedByAccountId,
                FullName = x.ChangedByAccount == null || x.ChangedByAccount.EmployeeAccount == null
                    ? null : x.ChangedByAccount.EmployeeAccount.FullName,
                Role = x.ChangedByAccount == null || x.ChangedByAccount.EmployeeAccount == null
                    ? (EmployeeRole?)null : x.ChangedByAccount.EmployeeAccount.Role
            }).ToListAsync(cancellationToken);
        return histories.Select(x => new Response.StatusHistoryResponse(x.InvoiceStatusHistoryId, x.Type.ToString(),
            x.PreviousStatus?.ToString(), x.NewStatus.ToString(), x.Reason,
            new(x.ChangedByAccountId, x.ChangedByAccountId.HasValue ? x.FullName ?? "Unknown" : "System", x.Role?.ToString()),
            Offset(x.ChangedAt)!.Value)).ToArray();
    }

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
        var validation = cancelValidator.Validate(request);
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
            invoice.UpdatedAt = NextVersion(invoice.UpdatedAt, now);
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
            return new(invoiceId, invoice.InvoiceCode, invoice.Status.ToString(), Offset(now)!.Value, reason, ["CREATE_ANOTHER"], Offset(invoice.UpdatedAt)!.Value);
        }
        catch (Exception ex) when (ex is DbUpdateConcurrencyException ||
            ex.GetBaseException() is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected })
        { throw new ConflictException("The record changed. Reload it and try again.", "CONCURRENT_UPDATE"); }
    }

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
        var validation = paymentValidator.Validate(request);
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
                ReceiptId = Guid.NewGuid(), ReceiptCode = await GenerateReceiptCodeAsync(now, cancellationToken), InvoiceId = invoiceId,
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
                        x.Status != TransportPlanStatus.CANCELLED).OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.TransportPlanId)
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
                new(invoiceId, invoice.InvoiceCode, invoice.Type.ToString(), invoice.Status.ToString(), Offset(now)!.Value, ["CREATE_ANOTHER"], Offset(invoice.UpdatedAt)!.Value),
                new(planStatus, invoice.Type == InvoiceType.FINAL));
        }
        catch (Exception ex) when (ex is DbUpdateConcurrencyException || ex.GetBaseException() is PostgresException
            { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected } or PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "Receipt_receipt_code_key" })
        { throw new ConflictException("The record changed. Reload it and try again.", "CONCURRENT_UPDATE"); }
    }

    private async Task<string> GenerateReceiptCodeAsync(DateTime now, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var code = $"RCP-{now.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}-{RandomNumberGenerator.GetInt32(10_000):D4}";
            if (!await db.Receipts.AnyAsync(x => x.ReceiptCode == code, cancellationToken)) return code;
        }
        throw new ConflictException("Unable to generate a unique receipt code. Please try again.", "CONCURRENT_UPDATE");
    }
    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static DateTime NextVersion(DateTime previous, DateTime now) => now > previous ? now : Timestamp(previous).AddTicks(10);

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
        var validation = issueValidator.Validate(request);
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
                DateOnly.FromDateTime(now), Offset(now)!.Value, invoice.TotalAmount, invoice.Currency, invoice.DueDate,
                invoice.Type == InvoiceType.QUOTATION ? ["CANCEL", "CREATE_ANOTHER"] : ["RECORD_PAYMENT", "CANCEL", "CREATE_ANOTHER"],
                Offset(invoice.UpdatedAt)!.Value);
        }
        catch (Exception ex) when (ex is DbUpdateConcurrencyException ||
            ex.GetBaseException() is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected })
        { throw Modified(); }
    }

    private static DateTime Timestamp(DateTime value) => new(value.Ticks / 10 * 10, DateTimeKind.Unspecified);

    public async Task<Response.InvoiceResponse> GetAsync(Guid accountId, Guid invoiceId, CancellationToken cancellationToken = default)
    {
        var account = await db.Accounts.AsNoTracking().Include(item => item.CustomerAccount).Include(item => item.EmployeeAccount)
            .SingleOrDefaultAsync(item => item.AccountId == accountId, cancellationToken);
        var customer = account is { IsDeleted: false, Status: AccountStatus.ACTIVE, AccountType: AccountType.CUSTOMER,
            CustomerAccount: { IsDeleted: false } };
        var manager = account is { IsDeleted: false, Status: AccountStatus.ACTIVE, AccountType: AccountType.EMPLOYEE,
            EmployeeAccount: { IsDeleted: false, IsActive: true, Role: EmployeeRole.LOGISTICS_MANAGER } };
        if (!customer && !manager)
            throw new ForbiddenException("Only Customers and Logistics Managers are authorized to view invoices.");
        if (invoiceId == Guid.Empty)
            throw new FieldValidationException([new FieldError("invoiceId", "Invoice identifier must not be empty.")]);
        var invoice = await db.Invoices.AsNoTracking()
            .Include(item => item.Booking).ThenInclude(item => item.Customer).ThenInclude(item => item.Account)
            .Include(item => item.Booking).ThenInclude(item => item.Transport)
            .Include(item => item.Employee).ThenInclude(item => item.Account)
            .SingleOrDefaultAsync(item => item.InvoiceId == invoiceId && (manager || item.Booking.Customer.AccountId == accountId), cancellationToken);
        if (invoice is null) throw new NotFoundException("Invoice not found", "INVOICE_NOT_FOUND");
        if (customer && (invoice.IssuedAt is null || invoice.Status == InvoiceStatus.DRAFT))
            throw new ForbiddenException("Customer cannot view draft invoices");

        var items = await db.InvoiceItems.AsNoTracking().Where(item => item.InvoiceId == invoiceId)
            .OrderBy(item => item.CreatedAt).ThenBy(item => item.InvoiceItemId)
            .Select(item => new Response.InvoiceItemResponse(item.InvoiceItemId, item.ItemType.ToString(), item.Description,
                item.Quantity, item.UnitPrice, item.LineAmount,
                item.Horse == null ? null : new Response.HorseResponse(item.Horse.HorseId, item.Horse.HorseName),
                invoice.Type == InvoiceType.FINAL && item.ItemType == InvoiceItemType.OTHER && item.LineAmount < 0, item.Note))
            .ToListAsync(cancellationToken);
        var receipts = await db.Receipts.AsNoTracking().Where(item => item.InvoiceId == invoiceId)
            .OrderBy(item => item.CreatedAt).ThenBy(item => item.ReceiptId)
            .Select(item => new
            {
                item.ReceiptId, item.ReceiptCode, item.Amount, item.Currency, item.PaymentMethod, item.Status,
                item.TransactionReference, item.PaidAt, item.ConfirmedAt, item.Note,
                // R10 allows one confirmed receipt; its RECORD_PAYMENT event identifies the recorder.
                RecordedBy = db.InvoiceStatusHistories.Where(history => history.InvoiceId == invoiceId &&
                        history.Type == InvoiceHistoryType.RECORD_PAYMENT && history.NewStatus == InvoiceStatus.PAID && item.Status == ReceiptStatus.CONFIRMED)
                    .OrderByDescending(history => history.ChangedAt).ThenByDescending(history => history.InvoiceStatusHistoryId)
                    .Select(history => history.ChangedByAccount == null || history.ChangedByAccount.EmployeeAccount == null ? null :
                        new Response.RecordedByResponse(history.ChangedByAccount.AccountId, history.ChangedByAccount.EmployeeAccount.FullName))
                    .FirstOrDefault()
            }).ToListAsync(cancellationToken);
        var plan = await db.TransportPlans.AsNoTracking().Where(item => item.Transport.BookingId == invoice.BookingId && item.Status != TransportPlanStatus.CANCELLED)
            .OrderByDescending(item => item.CreatedAt).ThenByDescending(item => item.TransportPlanId)
            .Select(item => new Response.PlanResponse(item.TransportPlanId, item.PlanCode, item.Status.ToString())).FirstOrDefaultAsync(cancellationToken);
        var confirmed = receipts.Any(item => item.Status == ReceiptStatus.CONFIRMED);
        var payable = invoice.Status is InvoiceStatus.ISSUED or InvoiceStatus.OVERDUE;
        var actions = new List<string>();
        if (manager)
        {
            if (invoice.Status == InvoiceStatus.DRAFT) actions.AddRange(["EDIT", "ISSUE"]);
            if (payable && invoice.Type != InvoiceType.QUOTATION && !confirmed) actions.Add("RECORD_PAYMENT");
            if ((invoice.Status == InvoiceStatus.DRAFT || payable) && !confirmed) actions.Add("CANCEL");
            actions.Add("CREATE_ANOTHER");
        }
        else if (payable && invoice.Type is InvoiceType.DEPOSIT or InvoiceType.FINAL or InvoiceType.SUPPLEMENTARY && !confirmed)
            actions.Add("PAY_NOW");

        var client = invoice.Booking.Customer;
        var contact = invoice.Employee;
        var booking = invoice.Booking;
        var transport = booking.Transport;
        return new(invoice.InvoiceId, invoice.InvoiceCode, invoice.Type.ToString(), invoice.Status.ToString(), invoice.Type != InvoiceType.QUOTATION,
            DateOnly.FromDateTime(invoice.IssuedAt ?? invoice.CreatedAt), invoice.DueDate, invoice.Currency, invoice.BillingNote,
            new(client.CustomerId, client.CustomerCode, client.FullName, client.Phone, client.Address, client.Account.Email), items,
            new(invoice.Subtotal, invoice.TaxAmount, items.Where(item => item.IsDepositDeduction).Sum(item => item.Total), invoice.TotalAmount),
            new(contact.FullName, contact.Account.Email, contact.Phone), Offset(invoice.IssuedAt), Offset(invoice.PaidAt), Offset(invoice.CancelledAt),
            invoice.CancellationReason, new(booking.BookingId, booking.BookingCode, booking.Status.ToString(), booking.OriginAddress, booking.OriginCountry,
                booking.DestinationAddress, booking.DestinationCountry, booking.RequestedDepartureDate),
            transport == null ? null : new(transport.TransportId, transport.TransportCode, transport.Status.ToString()), plan,
            receipts.Select(item => new Response.ReceiptResponse(item.ReceiptId, item.ReceiptCode, item.Amount, item.Currency, item.PaymentMethod.ToString(),
                item.Status.ToString(), item.TransactionReference, Offset(item.PaidAt), Offset(item.ConfirmedAt), item.Note, item.RecordedBy)).ToArray(),
            actions, Offset(invoice.CreatedAt)!.Value, Offset(invoice.UpdatedAt)!.Value);
    }

    public async Task<Response.ListResponse> GetByBookingAsync(Guid accountId, Guid bookingId, Request.BookingListRequest request,
        CancellationToken cancellationToken = default)
    {
        var account = await db.Accounts.AsNoTracking().Include(item => item.CustomerAccount).Include(item => item.EmployeeAccount)
            .SingleOrDefaultAsync(item => item.AccountId == accountId, cancellationToken);
        var customer = account is { IsDeleted: false, Status: AccountStatus.ACTIVE, AccountType: AccountType.CUSTOMER,
            CustomerAccount: { IsDeleted: false } };
        var manager = account is { IsDeleted: false, Status: AccountStatus.ACTIVE, AccountType: AccountType.EMPLOYEE,
            EmployeeAccount: { IsDeleted: false, IsActive: true, Role: EmployeeRole.LOGISTICS_MANAGER } };
        if (!customer && !manager)
            throw new ForbiddenException("Only Customers and Logistics Managers are authorized to view booking invoices.");
        if (bookingId == Guid.Empty)
            throw new FieldValidationException([new FieldError("bookingId", "Booking identifier must not be empty.")]);
        var validation = bookingListValidator.Validate(request);
        if (!validation.IsValid)
            throw new FieldValidationException(validation.Errors.Select(error => new FieldError(
                JsonNamingPolicy.CamelCase.ConvertName(error.PropertyName), error.ErrorMessage)).ToArray());
        if (!await db.Bookings.AsNoTracking().AnyAsync(item => item.BookingId == bookingId &&
                (manager || item.Customer.AccountId == accountId), cancellationToken))
            throw new NotFoundException("Booking not found", "BOOKING_NOT_FOUND");

        var query = db.Invoices.AsNoTracking().Where(item => item.BookingId == bookingId);
        if (customer) query = query.Where(item => item.IssuedAt != null && item.Status != InvoiceStatus.DRAFT);
        if (!string.IsNullOrWhiteSpace(request.Type))
        {
            var type = Enum.Parse<InvoiceType>(request.Type.Trim(), true);
            query = query.Where(item => item.Type == type);
        }
        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            var status = Enum.Parse<InvoiceStatus>(request.Status.Trim(), true);
            query = query.Where(item => item.Status == status);
        }
        var total = await query.CountAsync(cancellationToken);
        var sorted = request.SortBy.Trim().ToLowerInvariant() switch
        {
            "createdat:asc" => query.OrderBy(item => item.CreatedAt),
            "issuedat:asc" => query.OrderBy(item => item.IssuedAt == null).ThenBy(item => item.IssuedAt),
            "issuedat:desc" => query.OrderBy(item => item.IssuedAt == null).ThenByDescending(item => item.IssuedAt),
            "duedate:asc" => query.OrderBy(item => item.DueDate == null).ThenBy(item => item.DueDate),
            "duedate:desc" => query.OrderBy(item => item.DueDate == null).ThenByDescending(item => item.DueDate),
            "totalamount:asc" => query.OrderBy(item => item.TotalAmount),
            "totalamount:desc" => query.OrderByDescending(item => item.TotalAmount),
            _ => query.OrderByDescending(item => item.CreatedAt)
        };
        var rows = await sorted.ThenBy(item => item.InvoiceId).Skip((request.PageIndex - 1) * request.PageSize).Take(request.PageSize)
            .Select(item => new
            {
                item.InvoiceId, item.InvoiceCode, item.Type, item.Status, item.BookingId, item.Booking.BookingCode,
                TransportPlanId = db.TransportPlans.Where(plan => plan.Transport.BookingId == item.BookingId && plan.Status != TransportPlanStatus.CANCELLED)
                    .OrderByDescending(plan => plan.CreatedAt).ThenByDescending(plan => plan.TransportPlanId)
                    .Select(plan => (Guid?)plan.TransportPlanId).FirstOrDefault(),
                CustomerName = item.Booking.Customer.FullName, item.TotalAmount, item.Currency, item.DueDate,
                item.IssuedAt, item.PaidAt, item.CreatedAt, item.UpdatedAt
            }).ToListAsync(cancellationToken);
        return new(rows.Select(item => new Response.InvoiceListItem(item.InvoiceId, item.InvoiceCode, item.Type.ToString(), item.Status.ToString(),
                item.BookingId, item.BookingCode, item.TransportPlanId, item.CustomerName, item.TotalAmount, item.Currency,
                item.DueDate, Offset(item.IssuedAt), Offset(item.PaidAt), Offset(item.CreatedAt)!.Value, Offset(item.UpdatedAt)!.Value)).ToArray(),
            request.PageIndex, request.PageSize, total, (int)Math.Ceiling(total / (double)request.PageSize));
    }

    public async Task<Response.ListResponse> GetListAsync(Guid accountId, Request.ListRequest request,
        CancellationToken cancellationToken = default)
    {
        var account = await db.Accounts.AsNoTracking().Include(item => item.EmployeeAccount)
            .SingleOrDefaultAsync(item => item.AccountId == accountId, cancellationToken);
        if (account?.AccountType == AccountType.CUSTOMER)
            throw new ForbiddenException("Customer cannot access the invoice list");
        if (account is not { IsDeleted: false, Status: AccountStatus.ACTIVE, AccountType: AccountType.EMPLOYEE,
                EmployeeAccount: { IsDeleted: false, IsActive: true, Role: EmployeeRole.LOGISTICS_MANAGER } })
            throw new ForbiddenException("Only Logistics Manager is authorized to view the invoice list.");
        var validation = listValidator.Validate(request);
        if (!validation.IsValid)
            throw new FieldValidationException(validation.Errors.Select(error => new FieldError(
                JsonNamingPolicy.CamelCase.ConvertName(error.PropertyName), error.ErrorMessage)).ToArray());

        var query = db.Invoices.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(request.Type))
        {
            var type = Enum.Parse<InvoiceType>(request.Type.Trim(), true);
            query = query.Where(item => item.Type == type);
        }
        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            var status = Enum.Parse<InvoiceStatus>(request.Status.Trim(), true);
            query = query.Where(item => item.Status == status);
        }
        if (request.BookingId.HasValue) query = query.Where(item => item.BookingId == request.BookingId);
        if (request.TransportPlanId.HasValue)
            query = query.Where(item => db.TransportPlans.Any(plan => plan.TransportPlanId == request.TransportPlanId &&
                plan.Transport.BookingId == item.BookingId));
        if (request.IssuedFrom.HasValue)
        {
            var from = request.IssuedFrom.Value.ToDateTime(TimeOnly.MinValue);
            query = query.Where(item => item.IssuedAt >= from);
        }
        if (request.IssuedTo.HasValue)
        {
            var to = request.IssuedTo.Value.ToDateTime(TimeOnly.MaxValue);
            query = query.Where(item => item.IssuedAt <= to);
        }
        if (request.DueFrom.HasValue) query = query.Where(item => item.DueDate >= request.DueFrom);
        if (request.DueTo.HasValue) query = query.Where(item => item.DueDate <= request.DueTo);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToUpperInvariant();
            query = query.Where(item => item.InvoiceCode.ToUpper().Contains(search) || item.Booking.BookingCode.ToUpper().Contains(search) ||
                item.Booking.Customer.FullName.ToUpper().Contains(search));
        }
        var total = await query.CountAsync(cancellationToken);
        var sorted = request.SortBy.Trim().ToLowerInvariant() switch
        {
            "createdat:asc" => query.OrderBy(item => item.CreatedAt),
            "issuedat:asc" => query.OrderBy(item => item.IssuedAt == null).ThenBy(item => item.IssuedAt),
            "issuedat:desc" => query.OrderBy(item => item.IssuedAt == null).ThenByDescending(item => item.IssuedAt),
            "duedate:asc" => query.OrderBy(item => item.DueDate == null).ThenBy(item => item.DueDate),
            "duedate:desc" => query.OrderBy(item => item.DueDate == null).ThenByDescending(item => item.DueDate),
            "totalamount:asc" => query.OrderBy(item => item.TotalAmount),
            "totalamount:desc" => query.OrderByDescending(item => item.TotalAmount),
            _ => query.OrderByDescending(item => item.CreatedAt)
        };
        var rows = await sorted.ThenBy(item => item.InvoiceId).Skip((request.PageIndex - 1) * request.PageSize).Take(request.PageSize)
            .Select(item => new
            {
                item.InvoiceId, item.InvoiceCode, item.Type, item.Status, item.BookingId, item.Booking.BookingCode,
                TransportPlanId = db.TransportPlans.Where(plan => plan.Transport.BookingId == item.BookingId && plan.Status != TransportPlanStatus.CANCELLED)
                    .OrderByDescending(plan => plan.CreatedAt).ThenByDescending(plan => plan.TransportPlanId)
                    .Select(plan => (Guid?)plan.TransportPlanId).FirstOrDefault(),
                CustomerName = item.Booking.Customer.FullName, item.TotalAmount, item.Currency, item.DueDate,
                item.IssuedAt, item.PaidAt, item.CreatedAt, item.UpdatedAt
            }).ToListAsync(cancellationToken);
        return new(rows.Select(item => new Response.InvoiceListItem(item.InvoiceId, item.InvoiceCode, item.Type.ToString(), item.Status.ToString(),
                item.BookingId, item.BookingCode, item.TransportPlanId, item.CustomerName, item.TotalAmount, item.Currency,
                item.DueDate, Offset(item.IssuedAt), Offset(item.PaidAt), Offset(item.CreatedAt)!.Value, Offset(item.UpdatedAt)!.Value)).ToArray(),
            request.PageIndex, request.PageSize, total, (int)Math.Ceiling(total / (double)request.PageSize));
    }

    private static DateTimeOffset? Offset(DateTime? value) => value.HasValue ? new(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)) : null;


    public async Task<Response.SuggestionResponse> GetSuggestionAsync(Guid accountId, Guid bookingId,
        Request.SuggestionRequest request, CancellationToken cancellationToken = default)
    {
        var allowed = await db.Employees.AsNoTracking().AnyAsync(employee => employee.AccountId == accountId &&
            employee.Role == EmployeeRole.LOGISTICS_MANAGER && employee.IsActive && !employee.IsDeleted &&
            !employee.Account.IsDeleted && employee.Account.Status == AccountStatus.ACTIVE &&
            employee.Account.AccountType == AccountType.EMPLOYEE, cancellationToken);
        if (!allowed)
            throw new ForbiddenException("Only Logistics Manager is authorized to check invoice eligibility.");

        var validation = suggestionValidator.Validate(request);
        if (!validation.IsValid)
            throw new FieldValidationException(validation.Errors.Select(error => new FieldError("type", error.ErrorMessage)).ToArray());
        var type = Enum.Parse<InvoiceType>(request.Type.Trim(), true);

        var booking = await db.Bookings.AsNoTracking().Where(item => item.BookingId == bookingId)
            .Select(item => new
            {
                item.BookingId, item.BookingCode, item.Status,
                TransportId = item.Transport == null ? (Guid?)null : item.Transport.TransportId,
                TransportStatus = item.Transport == null ? (TransportStatus?)null : item.Transport.Status
            }).SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Booking not found", "BOOKING_NOT_FOUND");
        if (booking.Status != BookingStatus.APPROVED)
            throw new ConflictException("Booking must be APPROVED", "INVALID_BOOKING_STATUS");

        var validTransport = booking.TransportStatus.HasValue && type switch
        {
            InvoiceType.DEPOSIT => booking.TransportStatus is TransportStatus.CREATED or TransportStatus.PLANNING or TransportStatus.READY,
            InvoiceType.FINAL => booking.TransportStatus == TransportStatus.COMPLETED,
            _ => booking.TransportStatus != TransportStatus.CANCELLED
        };
        if (!validTransport)
            throw new ConflictException("Transport status does not allow this invoice type", "INVALID_TRANSPORT_STATUS");

        var invoices = await db.Invoices.AsNoTracking().Where(invoice => invoice.BookingId == bookingId)
            .OrderBy(invoice => invoice.CreatedAt).ThenBy(invoice => invoice.InvoiceId)
            .Select(invoice => new { invoice.Type, invoice.Status, invoice.InvoiceCode, invoice.Currency, invoice.TotalAmount })
            .ToListAsync(cancellationToken);
        if (type == InvoiceType.FINAL && invoices.Any(invoice => invoice.Type == InvoiceType.FINAL && invoice.Status != InvoiceStatus.CANCELLED))
            throw new ConflictException("This booking already has a FINAL invoice. A second FINAL invoice is only allowed after the previous one is cancelled.",
                "FINAL_INVOICE_ALREADY_EXISTS");
        if (invoices.Any(invoice => invoice.Type == type && invoice.Status is InvoiceStatus.DRAFT or InvoiceStatus.ISSUED or InvoiceStatus.OVERDUE))
            throw new ConflictException("An unresolved invoice of the same type already exists. Please edit or cancel the existing invoice.",
                "INVOICE_ALREADY_EXISTS");
        if (type == InvoiceType.CREDIT_NOTE && !invoices.Any(invoice => invoice.Type == InvoiceType.FINAL && invoice.Status == InvoiceStatus.PAID))
            throw new UnprocessableEntityException("Cannot create a claim/compensation invoice. The FINAL invoice of this booking must be PAID first.",
                "UNPAID_FINAL_INVOICE");

        var deposits = invoices.Where(invoice => invoice.Type == InvoiceType.DEPOSIT && invoice.Status == InvoiceStatus.PAID).ToArray();
        if (type == InvoiceType.FINAL && deposits.Select(deposit => deposit.Currency).Distinct().Skip(1).Any())
            throw new UnprocessableEntityException("Paid deposits must use the same currency for a FINAL invoice.", "CURRENCY_MISMATCH");
        var items = type == InvoiceType.FINAL
            ? deposits.Select(deposit => new Response.ItemResponse(null, nameof(InvoiceItemType.OTHER),
                $"Deposit deduction ({deposit.InvoiceCode})", null, 1, -deposit.TotalAmount, -deposit.TotalAmount,
                null, null, true, "Deposit is not refunded in cash; it is deducted from this final invoice.")).ToArray()
            : [];
        return new(booking.BookingId, booking.BookingCode, booking.Status.ToString(), booking.TransportId!.Value,
            booking.TransportStatus!.Value.ToString(), type.ToString(), deposits.FirstOrDefault()?.Currency,
            type != InvoiceType.QUOTATION, items);
    }

    public async Task<Response.DraftResponse> CreateAsync(Guid accountId, Guid bookingId,
        Request.CreateRequest request, CancellationToken cancellationToken = default)
    {
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : await db.Database.BeginTransactionAsync(cancellationToken);
        var employee = await db.Employees.AsNoTracking().Include(item => item.Account)
            .SingleOrDefaultAsync(item => item.AccountId == accountId && item.Role == EmployeeRole.LOGISTICS_MANAGER &&
                item.IsActive && !item.IsDeleted && !item.Account.IsDeleted && item.Account.Status == AccountStatus.ACTIVE &&
                item.Account.AccountType == AccountType.EMPLOYEE, cancellationToken)
            ?? throw new ForbiddenException("Only Logistics Manager is authorized to create invoices.");
        var validation = validator.Validate(request);
        if (!validation.IsValid)
            throw new FieldValidationException(validation.Errors.Select(error => new FieldError(
                string.Join(".", error.PropertyName.Split('.').Select(JsonNamingPolicy.CamelCase.ConvertName)), error.ErrorMessage)).ToArray());

        var type = Enum.Parse<InvoiceType>(request.Type.Trim(), true);
        var booking = await db.Bookings.AsNoTracking().Include(item => item.Customer).ThenInclude(item => item.Account)
            .Include(item => item.Transport).ThenInclude(item => item!.TransportPlans)
            .SingleOrDefaultAsync(item => item.BookingId == bookingId, cancellationToken)
            ?? throw new NotFoundException("Booking not found", "BOOKING_NOT_FOUND");
        if (booking.Status != BookingStatus.APPROVED)
            throw new ConflictException("Booking must be APPROVED", "INVALID_BOOKING_STATUS");
        var validTransport = booking.Transport is not null && type switch
        {
            InvoiceType.DEPOSIT => booking.Transport.Status is TransportStatus.CREATED or TransportStatus.PLANNING or TransportStatus.READY,
            InvoiceType.FINAL => booking.Transport.Status == TransportStatus.COMPLETED,
            _ => booking.Transport.Status != TransportStatus.CANCELLED
        };
        if (!validTransport)
            throw new ConflictException("Transport status does not allow this invoice type", "INVALID_TRANSPORT_STATUS");

        var invoices = await db.Invoices.AsNoTracking().Where(item => item.BookingId == bookingId)
            .OrderBy(item => item.CreatedAt).ThenBy(item => item.InvoiceId)
            .Select(item => new { item.Type, item.Status, item.InvoiceCode, item.Currency, item.TotalAmount })
            .ToListAsync(cancellationToken);
        if (type == InvoiceType.FINAL && invoices.Any(item => item.Type == InvoiceType.FINAL && item.Status != InvoiceStatus.CANCELLED))
            throw new ConflictException("This booking already has a FINAL invoice. A second FINAL invoice is only allowed after the previous one is cancelled.",
                "FINAL_INVOICE_ALREADY_EXISTS");
        if (invoices.Any(item => item.Type == type && item.Status is InvoiceStatus.DRAFT or InvoiceStatus.ISSUED or InvoiceStatus.OVERDUE))
            throw new ConflictException("An unresolved invoice of the same type already exists. Please edit or cancel the existing invoice.", "INVOICE_ALREADY_EXISTS");
        if (type == InvoiceType.CREDIT_NOTE && !invoices.Any(item => item.Type == InvoiceType.FINAL && item.Status == InvoiceStatus.PAID))
            throw new UnprocessableEntityException("Cannot create a claim/compensation invoice. The FINAL invoice of this booking must be PAID first.", "UNPAID_FINAL_INVOICE");
        var deposits = invoices.Where(item => type == InvoiceType.FINAL && item.Type == InvoiceType.DEPOSIT && item.Status == InvoiceStatus.PAID).ToArray();
        if (deposits.Any(item => item.Currency != request.Currency))
            throw new UnprocessableEntityException("Currency must match the paid deposit currency", "CURRENCY_MISMATCH");

        var requestedItems = request.Items!.Where(item => !item.IsDepositDeduction).ToArray();
        var horseIds = requestedItems.Where(item => item.HorseId.HasValue).Select(item => item.HorseId!.Value).Distinct().ToArray();
        var horses = await db.BookingHorses.AsNoTracking().Where(item => item.BookingId == bookingId && horseIds.Contains(item.HorseId))
            .Select(item => new Response.HorseResponse(item.HorseId, item.Horse.HorseName)).ToDictionaryAsync(item => item.HorseId, cancellationToken);
        var invalidHorses = request.Items!.Select((item, index) => (item, index))
            .Where(entry => !entry.item.IsDepositDeduction && entry.item.HorseId.HasValue && !horses.ContainsKey(entry.item.HorseId.Value))
            .Select(entry => new FieldError($"items[{entry.index}].horseId", "Horse must belong to this booking.")).ToArray();
        if (invalidHorses.Length > 0) throw new FieldValidationException(invalidHorses);

        var now = new DateTime(clock.GetUtcNow().Ticks / 10 * 10, DateTimeKind.Unspecified);
        var invoice = new Invoice
        {
            InvoiceId = Guid.NewGuid(), BookingId = bookingId, EmployeeId = employee.EmployeeId, Type = type,
            Status = InvoiceStatus.DRAFT, Currency = request.Currency, DueDate = request.DueDate,
            BillingNote = request.Note, CreatedAt = now, UpdatedAt = now
        };
        try
        {
            foreach (var item in requestedItems)
                invoice.InvoiceItems.Add(new InvoiceItem
                {
                    InvoiceItemId = Guid.NewGuid(), ItemType = Enum.Parse<InvoiceItemType>(item.ItemType.Trim(), true),
                    Description = item.Description.Trim(), Quantity = item.Quantity, UnitPrice = item.Price,
                    LineAmount = Money(decimal.Round(item.Quantity * item.Price, 2, MidpointRounding.AwayFromZero)),
                    HorseId = item.HorseId, Note = item.Note, CreatedAt = now, UpdatedAt = now
                });
            invoice.Subtotal = Money(invoice.InvoiceItems.Sum(item => item.LineAmount));
            invoice.TaxAmount = Money(decimal.Round(invoice.Subtotal * 0.20m, 2, MidpointRounding.AwayFromZero));
            foreach (var deposit in deposits)
                invoice.InvoiceItems.Add(new InvoiceItem
                {
                    InvoiceItemId = Guid.NewGuid(), ItemType = InvoiceItemType.OTHER,
                    Description = $"Deposit deduction ({deposit.InvoiceCode})", Quantity = 1,
                    UnitPrice = -deposit.TotalAmount, LineAmount = -deposit.TotalAmount,
                    Note = "Deposit is not refunded in cash; it is deducted from this final invoice.", CreatedAt = now, UpdatedAt = now
                });
            invoice.TotalAmount = Money(invoice.Subtotal + invoice.TaxAmount - deposits.Sum(item => item.TotalAmount));
        }
        catch (OverflowException)
        {
            throw new FieldValidationException([new("items", "Invoice amounts must fit decimal(18,2).")]);
        }
        invoice.InvoiceStatusHistories.Add(new InvoiceStatusHistory
        {
            InvoiceStatusHistoryId = Guid.NewGuid(), Type = InvoiceHistoryType.CREATE, NewStatus = InvoiceStatus.DRAFT,
            ChangedByAccountId = accountId, ChangedAt = now, CreatedAt = now
        });
        db.Invoices.Add(invoice);
        await SaveWithUniqueCodeAsync(invoice, cancellationToken);
        var response = Map(invoice, invoice.InvoiceItems, booking, employee, horses);
        await transaction.CommitAsync(cancellationToken);
        return response;
    }

    public async Task<Response.DraftResponse> UpdateAsync(Guid accountId, Guid invoiceId,
        Request.UpdateRequest request, CancellationToken cancellationToken = default)
    {
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var actor = await db.Employees.AsNoTracking().Include(item => item.Account)
                .SingleOrDefaultAsync(item => item.AccountId == accountId && item.Role == EmployeeRole.LOGISTICS_MANAGER &&
                    item.IsActive && !item.IsDeleted && !item.Account.IsDeleted && item.Account.Status == AccountStatus.ACTIVE &&
                    item.Account.AccountType == AccountType.EMPLOYEE, cancellationToken)
                ?? throw new ForbiddenException("Only Logistics Manager is authorized to edit invoices.");
            var validation = updateValidator.Validate(request);
            if (!validation.IsValid)
                throw new FieldValidationException(validation.Errors.Select(error => new FieldError(
                    string.Join(".", error.PropertyName.Split('.').Select(JsonNamingPolicy.CamelCase.ConvertName)), error.ErrorMessage)).ToArray());

            // Refresh this aggregate when a context has already tracked an older invoice or item list.
            foreach (var entry in db.ChangeTracker.Entries().Where(entry =>
                         entry.Entity is Invoice invoice && invoice.InvoiceId == invoiceId ||
                         entry.Entity is InvoiceItem item && item.InvoiceId == invoiceId).ToArray())
                entry.State = EntityState.Detached;
            var invoice = await db.Invoices.Include(item => item.InvoiceItems)
                .SingleOrDefaultAsync(item => item.InvoiceId == invoiceId, cancellationToken)
                ?? throw new NotFoundException("Invoice not found", "INVOICE_NOT_FOUND");
            if (invoice.Status != InvoiceStatus.DRAFT)
                throw new ConflictException("Only DRAFT invoices can be edited", "INVOICE_LOCKED");
            if (invoice.UpdatedAt.Ticks != request.LastUpdatedAt!.Value.UtcDateTime.Ticks) throw Modified();
            if (request.Type is not null && !string.Equals(request.Type.Trim(), invoice.Type.ToString(), StringComparison.OrdinalIgnoreCase))
                throw new FieldValidationException([new("type", "Invoice type cannot be changed.")]);

            var existing = invoice.InvoiceItems.ToDictionary(item => item.InvoiceItemId);
            var supplied = request.Items!.Select((item, index) => (item, index)).Where(entry => !entry.item.IsDepositDeduction).ToArray();
            var invalidIds = supplied.Where(entry => entry.item.InvoiceItemId.HasValue && !existing.ContainsKey(entry.item.InvoiceItemId.Value))
                .Select(entry => new FieldError($"items[{entry.index}].invoiceItemId", "Invoice item must belong to this invoice.")).ToArray();
            if (invalidIds.Length > 0) throw new FieldValidationException(invalidIds);
            var requested = supplied.Where(entry => !entry.item.InvoiceItemId.HasValue ||
                !IsDeduction(invoice, existing[entry.item.InvoiceItemId.Value])).ToArray();
            var horseIds = requested.Where(entry => entry.item.HorseId.HasValue).Select(entry => entry.item.HorseId!.Value).Distinct().ToArray();
            var horses = await db.BookingHorses.AsNoTracking().Where(item => item.BookingId == invoice.BookingId && horseIds.Contains(item.HorseId))
                .Select(item => new Response.HorseResponse(item.HorseId, item.Horse.HorseName)).ToDictionaryAsync(item => item.HorseId, cancellationToken);
            var invalidHorses = requested.Where(entry => entry.item.HorseId.HasValue && !horses.ContainsKey(entry.item.HorseId.Value))
                .Select(entry => new FieldError($"items[{entry.index}].horseId", "Horse must belong to this booking.")).ToArray();
            if (invalidHorses.Length > 0) throw new FieldValidationException(invalidHorses);

            var deposits = await db.Invoices.AsNoTracking()
                .Where(item => invoice.Type == InvoiceType.FINAL && item.BookingId == invoice.BookingId &&
                    item.Type == InvoiceType.DEPOSIT && item.Status == InvoiceStatus.PAID)
                .OrderBy(item => item.CreatedAt).ThenBy(item => item.InvoiceId)
                .Select(item => new { item.InvoiceCode, item.Currency, item.TotalAmount }).ToListAsync(cancellationToken);
            if (deposits.Any(item => item.Currency != request.Currency))
                throw new UnprocessableEntityException("Currency must match the paid deposit currency", "CURRENCY_MISMATCH");
            var now = new DateTime(Math.Max(clock.GetUtcNow().UtcTicks / 10 * 10, invoice.UpdatedAt.Ticks + 10), DateTimeKind.Unspecified);
            var items = new List<InvoiceItem>();
            decimal subtotal, tax, total;
            try
            {
                foreach (var (item, _) in requested)
                    items.Add(new InvoiceItem
                    {
                        InvoiceItemId = item.InvoiceItemId ?? Guid.NewGuid(), InvoiceId = invoiceId,
                        ItemType = Enum.Parse<InvoiceItemType>(item.ItemType.Trim(), true), Description = item.Description.Trim(),
                        Quantity = item.Quantity, UnitPrice = item.Price,
                        LineAmount = Money(decimal.Round(item.Quantity * item.Price, 2, MidpointRounding.AwayFromZero)),
                        HorseId = item.HorseId, Note = item.Note,
                        CreatedAt = item.InvoiceItemId.HasValue ? existing[item.InvoiceItemId.Value].CreatedAt : now, UpdatedAt = now
                    });
                subtotal = Money(items.Sum(item => item.LineAmount));
                tax = Money(decimal.Round(subtotal * 0.20m, 2, MidpointRounding.AwayFromZero));
                foreach (var deposit in deposits)
                    items.Add(new InvoiceItem
                    {
                        InvoiceItemId = Guid.NewGuid(), InvoiceId = invoiceId, ItemType = InvoiceItemType.OTHER,
                        Description = $"Deposit deduction ({deposit.InvoiceCode})", Quantity = 1,
                        UnitPrice = -deposit.TotalAmount, LineAmount = -deposit.TotalAmount,
                        Note = "Deposit is not refunded in cash; it is deducted from this final invoice.", CreatedAt = now, UpdatedAt = now
                    });
                total = Money(subtotal + tax - deposits.Sum(item => item.TotalAmount));
            }
            catch (OverflowException) { throw AmountError(); }

            var booking = await db.Bookings.AsNoTracking().Include(item => item.Customer).ThenInclude(item => item.Account)
                .Include(item => item.Transport).ThenInclude(item => item!.TransportPlans)
                .SingleAsync(item => item.BookingId == invoice.BookingId, cancellationToken);
            var creator = invoice.EmployeeId == actor.EmployeeId ? actor : await db.Employees.AsNoTracking().Include(item => item.Account)
                .SingleAsync(item => item.EmployeeId == invoice.EmployeeId, cancellationToken);
            var keptIds = items.Select(item => item.InvoiceItemId).ToHashSet();
            foreach (var old in existing.Values.Where(item => !keptIds.Contains(item.InvoiceItemId)))
            {
                db.InvoiceItems.Remove(old);
                invoice.InvoiceItems.Remove(old);
            }
            foreach (var item in items)
            {
                if (existing.TryGetValue(item.InvoiceItemId, out var old)) db.Entry(old).CurrentValues.SetValues(item);
                else db.InvoiceItems.Add(item);
            }
            invoice.Currency = request.Currency;
            invoice.DueDate = request.DueDate;
            invoice.BillingNote = request.Note;
            invoice.Subtotal = subtotal;
            invoice.TaxAmount = tax;
            invoice.DiscountAmount = 0;
            invoice.TotalAmount = total;
            invoice.UpdatedAt = now;
            await db.SaveChangesAsync(cancellationToken);
            var response = Map(invoice, items, booking, creator, horses);
            await transaction.CommitAsync(cancellationToken);
            return response;
        }
        catch (DbUpdateConcurrencyException) { throw Modified(); }
        catch (Exception error) when (error.GetBaseException() is PostgresException
            { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected })
        { throw Modified(); }
    }

    private static bool IsDeduction(Invoice invoice, InvoiceItem item) =>
        invoice.Type == InvoiceType.FINAL && item.ItemType == InvoiceItemType.OTHER && item.LineAmount < 0;
    private static decimal Money(decimal amount) => amount is >= -MaxAmount and <= MaxAmount ? amount : throw AmountError();
    private static FieldValidationException AmountError() => new([new("items", "Invoice amounts must fit decimal(18,2).")]);
    private static ConflictException Modified() => new("The invoice was just modified. Please reload the page", "INVOICE_MODIFIED");

    private async Task SaveWithUniqueCodeAsync(Invoice invoice, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            invoice.InvoiceCode = $"INV-{invoice.CreatedAt:yyyyMMdd}-{RandomNumberGenerator.GetInt32(10_000):D4}";
            if (await db.Invoices.AsNoTracking().AnyAsync(item => item.InvoiceCode == invoice.InvoiceCode, cancellationToken)) continue;
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (DbUpdateException error) when (error.InnerException is PostgresException
                { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "Invoice_invoice_code_key" })
            {
                // EF restores its savepoint after a failed insert; retry the code inside the same transaction.
            }
        }
        throw new InvalidOperationException("Unable to generate a unique INV code");
    }

    private static Response.DraftResponse Map(Invoice invoice, IEnumerable<InvoiceItem> items, Booking booking, Employee employee,
        IReadOnlyDictionary<Guid, Response.HorseResponse> horses)
    {
        var transport = booking.Transport;
        var plan = transport?.TransportPlans.Where(item => item.Status != TransportPlanStatus.CANCELLED)
            .OrderByDescending(item => item.CreatedAt).ThenByDescending(item => item.TransportPlanId).FirstOrDefault();
        var customer = booking.Customer;
        return new()
        {
            InvoiceId = invoice.InvoiceId, InvoiceNumber = invoice.InvoiceCode, Type = invoice.Type.ToString(),
            InvoiceDate = DateOnly.FromDateTime(invoice.CreatedAt), DueDate = invoice.DueDate!.Value,
            Currency = invoice.Currency, Note = invoice.BillingNote,
            Customer = new(customer.CustomerId, customer.CustomerCode, customer.FullName, customer.Phone, customer.Address, customer.Account.Email),
            Items = items.OrderBy(item => item.CreatedAt).Select(item => new Response.ItemResponse(item.InvoiceItemId, item.ItemType.ToString(),
                item.Description, null, item.Quantity, item.UnitPrice, item.LineAmount,
                item.HorseId.HasValue ? horses[item.HorseId.Value] : null, null,
                IsDeduction(invoice, item), item.Note)).ToArray(),
            Summary = new(invoice.Subtotal, invoice.TaxAmount, items.Where(item => IsDeduction(invoice, item)).Sum(item => item.LineAmount), invoice.TotalAmount),
            ContactPerson = new(employee.FullName, employee.Account.Email, employee.Phone),
            Booking = new(booking.BookingId, booking.BookingCode, booking.Status.ToString(), booking.OriginAddress,
                booking.OriginCountry, booking.DestinationAddress, booking.DestinationCountry, booking.RequestedDepartureDate),
            Transport = transport is null ? null : new(transport.TransportId, transport.TransportCode, transport.Status.ToString()),
            TransportPlan = plan is null ? null : new(plan.TransportPlanId, plan.PlanCode, plan.Status.ToString()),
            CreatedAt = new(DateTime.SpecifyKind(invoice.CreatedAt, DateTimeKind.Utc)),
            UpdatedAt = new(DateTime.SpecifyKind(invoice.UpdatedAt, DateTimeKind.Utc))
        };
    }
}
