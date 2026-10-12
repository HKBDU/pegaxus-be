using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;

namespace Pegaxus.Service.InvoiceService;

public sealed class Service(AppDbContext db, IValidator<Request.SuggestionRequest> validator) : IService
{
    public async Task<Response.SuggestionResponse> GetSuggestionAsync(Guid accountId, Guid bookingId,
        Request.SuggestionRequest request, CancellationToken cancellationToken = default)
    {
        var allowed = await db.Employees.AsNoTracking().AnyAsync(employee => employee.AccountId == accountId &&
            employee.Role == EmployeeRole.LOGISTICS_MANAGER && employee.IsActive && !employee.IsDeleted &&
            !employee.Account.IsDeleted && employee.Account.Status == AccountStatus.ACTIVE &&
            employee.Account.AccountType == AccountType.EMPLOYEE, cancellationToken);
        if (!allowed)
            throw new ForbiddenException("Only Logistics Manager is authorized to check invoice eligibility.");

        var validation = validator.Validate(request);
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
}
