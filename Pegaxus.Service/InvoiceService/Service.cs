using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;

namespace Pegaxus.Service.InvoiceService;

public sealed class Service(AppDbContext db) : IService
{
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
            .Select(item => new Response.TransportPlanResponse(item.TransportPlanId, item.PlanCode, item.Status.ToString())).FirstOrDefaultAsync(cancellationToken);
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
            new(invoice.Subtotal, 0, invoice.TaxAmount, 20, items.Where(item => item.IsDepositDeduction).Sum(item => item.Total), invoice.TotalAmount),
            new(contact.FullName, contact.Account.Email, contact.Phone), Offset(invoice.IssuedAt), Offset(invoice.PaidAt), Offset(invoice.CancelledAt),
            invoice.CancellationReason, new(booking.BookingId, booking.BookingCode, booking.Status.ToString(), booking.OriginAddress, booking.OriginCountry,
                booking.DestinationAddress, booking.DestinationCountry, booking.RequestedDepartureDate),
            transport == null ? null : new(transport.TransportId, transport.TransportCode, transport.Status.ToString()), plan,
            receipts.Select(item => new Response.ReceiptResponse(item.ReceiptId, item.ReceiptCode, item.Amount, item.Currency, item.PaymentMethod.ToString(),
                item.Status.ToString(), item.TransactionReference, Offset(item.PaidAt), Offset(item.ConfirmedAt), item.Note, item.RecordedBy)).ToArray(),
            actions, Offset(invoice.CreatedAt)!.Value, Offset(invoice.UpdatedAt)!.Value);
    }

    private static DateTimeOffset? Offset(DateTime? value) => value.HasValue ? new(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)) : null;
}

