using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;

namespace Pegaxus.Service.InvoiceService;

public sealed class Service(AppDbContext db) : IService
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
            new DateTimeOffset(DateTime.SpecifyKind(x.ChangedAt, DateTimeKind.Utc)))).ToArray();
    }
}
