using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Enum;
using Pegaxus.Repository.Extensions;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;

using static Pegaxus.Service.Common.DbTime;

namespace Pegaxus.Service.InvoiceService;

public sealed partial class Service
{
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
            AsUtc(x.ChangedAt))).ToArray();
    }

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

        var itemRows = await db.InvoiceItems.AsNoTracking().Where(item => item.InvoiceId == invoiceId)
            .OrderBy(item => item.CreatedAt).ThenBy(item => item.InvoiceItemId)
            .Select(item => new
            {
                item.InvoiceItemId, item.ItemType, item.Description, item.Quantity, item.UnitPrice, item.LineAmount,
                Horse = item.Horse == null ? null : new Response.HorseResponse(item.Horse.HorseId, item.Horse.HorseName),
                IsDepositDeduction = invoice.Type == InvoiceType.FINAL && item.ItemType == InvoiceItemType.OTHER && item.LineAmount < 0,
                item.Note
            }).ToListAsync(cancellationToken);
        var items = itemRows.Select(item => new Response.InvoiceItemResponse(item.InvoiceItemId, item.ItemType.ToString(), item.Description,
            Vnd.ToInt(item.Quantity), Vnd.ToLong(item.UnitPrice), Vnd.ToLong(item.LineAmount), item.Horse, item.IsDepositDeduction, item.Note)).ToList();
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
            new(Vnd.ToLong(invoice.Subtotal), Vnd.ToLong(invoice.TaxAmount), items.Where(item => item.IsDepositDeduction).Sum(item => item.Total), Vnd.ToLong(invoice.TotalAmount)),
            new(contact.FullName, contact.Account.Email, contact.Phone), AsUtc(invoice.IssuedAt), AsUtc(invoice.PaidAt), AsUtc(invoice.CancelledAt),
            invoice.CancellationReason, new(booking.BookingId, booking.BookingCode, booking.Status.ToString(), booking.OriginAddress, booking.OriginCountry,
                booking.DestinationAddress, booking.DestinationCountry, booking.RequestedDepartureDate),
            transport == null ? null : new(transport.TransportId, transport.TransportCode, transport.Status.ToString()), plan,
            receipts.Select(item => new Response.ReceiptResponse(item.ReceiptId, item.ReceiptCode, Vnd.ToLong(item.Amount), item.Currency, item.PaymentMethod.ToString(),
                item.Status.ToString(), item.TransactionReference, AsUtc(item.PaidAt), AsUtc(item.ConfirmedAt), item.Note, item.RecordedBy)).ToArray(),
            actions, AsUtc(invoice.CreatedAt), AsUtc(invoice.UpdatedAt));
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
        var rows = await sorted.ThenBy(item => item.InvoiceId).Skip(Paging.Offset(request.PageIndex, request.PageSize)).Take(request.PageSize)
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
                item.BookingId, item.BookingCode, item.TransportPlanId, item.CustomerName, Vnd.ToLong(item.TotalAmount), item.Currency,
                item.DueDate, AsUtc(item.IssuedAt), AsUtc(item.PaidAt), AsUtc(item.CreatedAt), AsUtc(item.UpdatedAt))).ToArray(),
            request.PageIndex, request.PageSize, total, Paging.TotalPages(total, request.PageSize));
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
        var rows = await sorted.ThenBy(item => item.InvoiceId).Skip(Paging.Offset(request.PageIndex, request.PageSize)).Take(request.PageSize)
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
                item.BookingId, item.BookingCode, item.TransportPlanId, item.CustomerName, Vnd.ToLong(item.TotalAmount), item.Currency,
                item.DueDate, AsUtc(item.IssuedAt), AsUtc(item.PaidAt), AsUtc(item.CreatedAt), AsUtc(item.UpdatedAt))).ToArray(),
            request.PageIndex, request.PageSize, total, Paging.TotalPages(total, request.PageSize));
    }

    public async Task<Response.SuggestionResponse> GetSuggestionAsync(Guid accountId, Guid bookingId,
        Request.SuggestionRequest request, CancellationToken cancellationToken = default)
    {
        var allowed = await db.Employees.AsNoTracking().Active().AnyAsync(employee => employee.AccountId == accountId && employee.Role == EmployeeRole.LOGISTICS_MANAGER && employee.Account.AccountType == AccountType.EMPLOYEE, cancellationToken);
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
        var items = type == InvoiceType.FINAL
            ? deposits.Select(deposit => new Response.ItemResponse(null, nameof(InvoiceItemType.OTHER),
                $"Deposit deduction ({deposit.InvoiceCode})", null, 1, -Vnd.ToLong(deposit.TotalAmount), -Vnd.ToLong(deposit.TotalAmount),
                null, null, true, "Deposit is not refunded in cash; it is deducted from this final invoice.")).ToArray()
            : [];
        return new(booking.BookingId, booking.BookingCode, booking.Status.ToString(), booking.TransportId!.Value,
            booking.TransportStatus!.Value.ToString(), type.ToString(), Vnd.Currency,
            type != InvoiceType.QUOTATION, items);
    }
}
