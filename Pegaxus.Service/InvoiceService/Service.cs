using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;

namespace Pegaxus.Service.InvoiceService;

public sealed class Service(AppDbContext db, IValidator<Request.ListRequest> validator) : IService
{
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
        var validation = validator.Validate(request);
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
}
