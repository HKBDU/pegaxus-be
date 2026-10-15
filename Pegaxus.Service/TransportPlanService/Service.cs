using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Enum;
using Pegaxus.Repository.Extensions;
using Pegaxus.Service.Exceptions;

using static Pegaxus.Service.Common.DbTime;

namespace Pegaxus.Service.TransportPlanService;

public sealed partial class Service : ITransportPlanService
{
    private readonly AppDbContext db;
    private readonly NotificationService.INotificationService notificationService;

    public Service(AppDbContext db, NotificationService.INotificationService notificationService)
    {
        this.db = db;
        this.notificationService = notificationService;
    }

    private async Task GetLogisticsManagerAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var exists = await db.Employees.Active().AnyAsync(item => item.AccountId == accountId && item.Role == EmployeeRole.LOGISTICS_MANAGER,
            cancellationToken);
        if (!exists)
            throw new ForbiddenException("You do not have permission to perform this action");
    }

    private static void EnsureConcurrency(DateTime actualUpdatedAt, DateTimeOffset expectedUpdatedAt)
    {
        if (actualUpdatedAt != Truncate(expectedUpdatedAt.UtcDateTime))
            throw new ConflictException(
                "The Transport Plan has been modified by another request. Please reload and try again.",
                "TRANSPORT_PLAN_CONCURRENCY_CONFLICT");
    }
}
