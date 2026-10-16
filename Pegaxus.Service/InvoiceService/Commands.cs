using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pegaxus.Repository.Entities;
using Pegaxus.Service.Common;
using Pegaxus.Repository.Enum;
using Pegaxus.Repository.Extensions;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.NotificationService;
using Pegaxus.Service.OutboxService;

using static Pegaxus.Service.Common.DbTime;

namespace Pegaxus.Service.InvoiceService;

public sealed partial class Service
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
        var validation = cancelValidator.Validate(request);
        if (!validation.IsValid)
            throw new FieldValidationException(validation.Errors.Select(x => new FieldError("cancellationReason", x.ErrorMessage)).ToArray());
        var reason = request.CancellationReason.Trim();

        try
        {
            await using var transaction = await db.Database.BeginSerializableAsync(cancellationToken);
            var query = db.Invoices.Where(x => x.InvoiceId == invoiceId);
            // A LINQ self-update holds the invoice row lock until commit.
            await db.LockRowsAsync(query, x => x.InvoiceId, cancellationToken);
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

            var now = Truncate(clock.GetUtcNow().UtcDateTime);
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
                    .Select(x => new { x.Customer.AccountId, x.Customer.Account.Email, x.Customer.FullName,
                        TransportId = x.Transport == null ? (Guid?)null : x.Transport.TransportId }).SingleAsync(cancellationToken);
                var message = $"Invoice {invoice.InvoiceCode} ({invoice.Type}) has been cancelled. Reason: {reason}. Please contact support if you need more information.";
                notifications.Queue(new NotificationCreateRequest
                {
                    AccountId = customer.AccountId, TransportId = customer.TransportId, Type = NotificationType.PAYMENT,
                    Priority = NotificationPriority.NORMAL, Title = "Invoice cancelled", Message = message,
                    ReferenceType = nameof(Invoice), ReferenceId = invoiceId,
                    ActionUrl = NotificationLinks.Invoice(null, invoice.BookingId, invoiceId), CreatedAt = now
                });
                OutboxQueue.AddEmail(db, new()
                {
                    To = customer.Email, Subject = $"Invoice cancelled — {invoice.InvoiceCode}",
                    Body = MailService.EmailBodies.InvoiceCancelledBody(customer.FullName, invoice.InvoiceCode, invoice.Type.ToString(), reason, null)
                }, now);
            }
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            await notifications.FlushAsync(cancellationToken);
            return new(invoiceId, invoice.InvoiceCode, invoice.Status.ToString(), AsUtc(now), reason, ["CREATE_ANOTHER"], AsUtc(invoice.UpdatedAt));
        }
        catch (Exception ex) when (ex is DbUpdateConcurrencyException ||
            ex.GetBaseException() is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected })
        { throw new ConflictException("The record changed. Reload it and try again.", "CONCURRENT_UPDATE"); }
    }

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
            await using var transaction = await db.Database.BeginSerializableAsync(cancellationToken);
            var query = db.Invoices.Where(x => x.InvoiceId == invoiceId);
            // A LINQ self-update holds the invoice row lock until commit.
            await db.LockRowsAsync(query, x => x.InvoiceId, cancellationToken);
            var invoice = await query.SingleOrDefaultAsync(cancellationToken)
                ?? throw new NotFoundException("Invoice not found", "INVOICE_NOT_FOUND");
            // A reused context can already track a version from before the row lock.
            if (db.Database.IsRelational()) await db.Entry(invoice).ReloadAsync(cancellationToken);
            if (invoice.Status != InvoiceStatus.DRAFT)
                throw new ConflictException("Invoice must be in DRAFT status to be issued", "INVALID_INVOICE_STATUS");
            if (Truncate(invoice.UpdatedAt) != Truncate(request.LastUpdatedAt!.Value.UtcDateTime)) throw Modified();

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
                if (!saved.SequenceEqual(expected))
                    throw new ConflictException("The paid deposits changed after this draft was saved. Please open the draft and save it again", "DEPOSIT_DEDUCTION_CHANGED");
            }

            var customer = await db.Bookings.AsNoTracking().Where(x => x.BookingId == invoice.BookingId)
                .Select(x => new { x.BookingCode, x.Customer.AccountId, x.Customer.Account.Email, x.Customer.FullName,
                    TransportId = x.Transport == null ? (Guid?)null : x.Transport.TransportId }).SingleAsync(cancellationToken);
            var now = Truncate(clock.GetUtcNow().UtcDateTime);
            invoice.Status = InvoiceStatus.ISSUED;
            invoice.IssuedAt = now;
            invoice.UpdatedAt = now > invoice.UpdatedAt ? now : Truncate(invoice.UpdatedAt).AddTicks(10);
            invoice.EmployeeId = account.EmployeeAccount!.EmployeeId;
            db.InvoiceStatusHistories.Add(new InvoiceStatusHistory
            {
                InvoiceStatusHistoryId = Guid.NewGuid(), InvoiceId = invoiceId, Type = InvoiceHistoryType.ISSUE,
                PreviousStatus = InvoiceStatus.DRAFT, NewStatus = InvoiceStatus.ISSUED, ChangedByAccountId = accountId,
                ChangedAt = now, CreatedAt = now
            });
            var amount = Vnd.Format(invoice.TotalAmount, invoice.Currency);
            var (title, message, priority) = invoice.Type switch
            {
                InvoiceType.QUOTATION => ("Quotation issued", $"Quotation {invoice.InvoiceCode} for booking {customer.BookingCode} has been issued. Estimated total: {amount}. No payment is required.", NotificationPriority.NORMAL),
                InvoiceType.CREDIT_NOTE => ("Credit note issued", $"Credit note {invoice.InvoiceCode} for booking {customer.BookingCode} has been issued. Payout amount: {amount}.", NotificationPriority.NORMAL),
                _ => ("Invoice issued", $"{InvoiceTypeLabel(invoice.Type)} {invoice.InvoiceCode} for booking {customer.BookingCode} has been issued. Amount due: {amount}. Please pay by {invoice.DueDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "the due date on the invoice"}.", NotificationPriority.HIGH)
            };
            notifications.Queue(new NotificationCreateRequest
            {
                AccountId = customer.AccountId, TransportId = customer.TransportId, Type = NotificationType.PAYMENT,
                Priority = priority, Title = title, Message = message, ReferenceType = nameof(Invoice), ReferenceId = invoiceId,
                ActionUrl = NotificationLinks.Invoice(null, invoice.BookingId, invoiceId), CreatedAt = now
            });
            var mailKind = invoice.Type switch
            {
                InvoiceType.QUOTATION => MailService.EmailBodies.InvoiceMailKind.Quotation,
                InvoiceType.CREDIT_NOTE => MailService.EmailBodies.InvoiceMailKind.CreditNote,
                _ => MailService.EmailBodies.InvoiceMailKind.Invoice
            };
            OutboxQueue.AddEmail(db, new()
            {
                To = customer.Email, Subject = $"{title} — {invoice.InvoiceCode}",
                Body = MailService.EmailBodies.InvoiceIssuedBody(customer.FullName, invoice.InvoiceCode, invoice.Type.ToString(), customer.BookingCode,
                    mailKind, invoice.TotalAmount, invoice.Currency, invoice.DueDate, null)
            }, now);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            await notifications.FlushAsync(cancellationToken);
            return new(invoiceId, invoice.InvoiceCode, invoice.Type.ToString(), invoice.Status.ToString(),
                DateOnly.FromDateTime(now), AsUtc(now), Vnd.ToLong(invoice.TotalAmount), invoice.Currency, invoice.DueDate,
                invoice.Type == InvoiceType.QUOTATION ? ["CANCEL", "CREATE_ANOTHER"] : ["RECORD_PAYMENT", "CANCEL", "CREATE_ANOTHER"],
                AsUtc(invoice.UpdatedAt));
        }
        catch (Exception ex) when (ex is DbUpdateConcurrencyException ||
            ex.GetBaseException() is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected })
        { throw Modified(); }
    }

    public async Task<Response.DraftResponse> CreateAsync(Guid accountId, Guid bookingId,
        Request.CreateRequest request, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginSerializableAsync(cancellationToken);
        var employee = await db.Employees.AsNoTracking().Include(item => item.Account)
            .Active()
            .SingleOrDefaultAsync(item => item.AccountId == accountId && item.Role == EmployeeRole.LOGISTICS_MANAGER && item.Account.AccountType == AccountType.EMPLOYEE, cancellationToken)
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
            Status = InvoiceStatus.DRAFT, Currency = Vnd.Currency, DueDate = request.DueDate,
            BillingNote = request.Note, CreatedAt = now, UpdatedAt = now
        };
        try
        {
            foreach (var item in requestedItems)
                invoice.InvoiceItems.Add(new InvoiceItem
                {
                    InvoiceItemId = Guid.NewGuid(), ItemType = Enum.Parse<InvoiceItemType>(item.ItemType.Trim(), true),
                    Description = item.Description.Trim(), Quantity = item.Quantity, UnitPrice = item.Price,
                    LineAmount = Money(item.Quantity * item.Price),
                    HorseId = item.HorseId, Note = item.Note, CreatedAt = now, UpdatedAt = now
                });
            invoice.Subtotal = Money(invoice.InvoiceItems.Sum(item => item.LineAmount));
            invoice.TaxAmount = Money(Vnd.Tax(invoice.Subtotal));
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
        await using var transaction = await db.Database.BeginSerializableAsync(cancellationToken);
        try
        {
            var actor = await db.Employees.AsNoTracking().Include(item => item.Account)
                .Active()
                .SingleOrDefaultAsync(item => item.AccountId == accountId && item.Role == EmployeeRole.LOGISTICS_MANAGER && item.Account.AccountType == AccountType.EMPLOYEE, cancellationToken)
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
                        LineAmount = Money(item.Quantity * item.Price),
                        HorseId = item.HorseId, Note = item.Note,
                        CreatedAt = item.InvoiceItemId.HasValue ? existing[item.InvoiceItemId.Value].CreatedAt : now, UpdatedAt = now
                    });
                subtotal = Money(items.Sum(item => item.LineAmount));
                tax = Money(Vnd.Tax(subtotal));
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
            invoice.Currency = Vnd.Currency;
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
                item.Description, null, Vnd.ToInt(item.Quantity), Vnd.ToLong(item.UnitPrice), Vnd.ToLong(item.LineAmount),
                item.HorseId.HasValue ? horses[item.HorseId.Value] : null, null,
                IsDeduction(invoice, item), item.Note)).ToArray(),
            Summary = new(Vnd.ToLong(invoice.Subtotal), Vnd.ToLong(invoice.TaxAmount),
                Vnd.ToLong(items.Where(item => IsDeduction(invoice, item)).Sum(item => item.LineAmount)), Vnd.ToLong(invoice.TotalAmount)),
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
