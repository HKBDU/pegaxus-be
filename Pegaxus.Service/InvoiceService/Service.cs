using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;

namespace Pegaxus.Service.InvoiceService;

public sealed class Service(AppDbContext db, IValidator<Request.BookingListRequest> validator) : IService
{
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
        var validation = validator.Validate(request);
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

    private static DateTimeOffset? Offset(DateTime? value) => value.HasValue ? new(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)) : null;
}
