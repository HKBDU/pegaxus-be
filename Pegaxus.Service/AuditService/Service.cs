using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Pegaxus.Repository;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.AuditService.Validator;
using Pegaxus.Service.DocumentStorageService;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.HandoverService;
using Pegaxus.Service.MediaService;
using Pegaxus.Service.NotificationService;

namespace Pegaxus.Service.AuditService;

/// <summary>Archive search, the audit view of a finished transport and the audit package export (UC-77, R26). Nothing here changes business data.</summary>
public sealed partial class Service(AppDbContext db, TimeProvider clock, IDistributedCache cache, IDocumentStorageService storage, IMediaService media,
    IHandoverService handovers, INotificationService notifications, AuditExportQueue queue, ILogger<Service> logger) : IAuditService
{
    private static readonly ArchiveRequestValidator ArchiveValidator = new();
    private static readonly AuditRequestValidator AuditValidator = new();
    private static readonly ExportRequestValidator ExportValidator = new();

    private sealed record Actor(Guid AccountId, bool IsManager, bool IsCustomer);

    /// <summary>Only an active Logistics Manager or an active Customer may open the audit; anyone else is a 403.</summary>
    private async Task<Actor> ActorAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var account = await db.Accounts.AsNoTracking().Include(item => item.CustomerAccount).Include(item => item.EmployeeAccount)
            .SingleOrDefaultAsync(item => item.AccountId == accountId, cancellationToken);
        var customer = account is { IsDeleted: false, Status: AccountStatus.ACTIVE, AccountType: AccountType.CUSTOMER, CustomerAccount: { IsDeleted: false } };
        var manager = account is { IsDeleted: false, Status: AccountStatus.ACTIVE, AccountType: AccountType.EMPLOYEE,
            EmployeeAccount: { IsDeleted: false, IsActive: true, Role: EmployeeRole.LOGISTICS_MANAGER } };
        if (!customer && !manager)
            throw new ForbiddenException("Only Logistics Managers and Customers can view the transport audit.");
        return new(accountId, manager, customer);
    }

    private async Task EnsureManagerAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var allowed = await db.Accounts.AsNoTracking().AnyAsync(account => account.AccountId == accountId && !account.IsDeleted && account.Status == AccountStatus.ACTIVE &&
            account.AccountType == AccountType.EMPLOYEE && account.EmployeeAccount != null && !account.EmployeeAccount.IsDeleted && account.EmployeeAccount.IsActive &&
            account.EmployeeAccount.Role == EmployeeRole.LOGISTICS_MANAGER, cancellationToken);
        if (!allowed) throw new ForbiddenException("Only Logistics Manager can access the audit archive.");
    }

    private static NotFoundException TransportMissing() => new("Transport not found", "TRANSPORT_NOT_FOUND");
}
