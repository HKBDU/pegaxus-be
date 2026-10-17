using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Repository.Extensions;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;

using static Pegaxus.Service.Common.DbTime;

namespace Pegaxus.Service.HandoverService;

public sealed partial class Service
{
    public async Task<Response.DetailResponse> StartAsync(Guid handoverRecordId, Guid accountId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginSerializableAsync(cancellationToken);
        var actor = await ActorAsync(accountId, cancellationToken);
        var item = await LockAsync(handoverRecordId, cancellationToken);
        if (!await CanOperateAsync(item, actor, cancellationToken)) throw Forbidden();
        if (item.Status != HandoverStatus.PLANNED) throw InvalidStatus(item);
        item.Status = HandoverStatus.IN_PROGRESS;
        item.UpdatedAt = Truncate(clock.GetUtcNow());
        var result = await DetailAsync(item, await HeaderAsync(item.TransportId, cancellationToken), actor, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<Response.DetailResponse> CancelAsync(Guid handoverRecordId, Request.CancelRequest request, Guid accountId, CancellationToken cancellationToken = default)
    {
        Validation.Validate(request, cancelValidator);
        await using var transaction = await db.Database.BeginSerializableAsync(cancellationToken);
        var actor = await ActorAsync(accountId, cancellationToken);
        if (!actor.IsManager) throw Forbidden();
        var item = await LockAsync(handoverRecordId, cancellationToken);
        if (item.Status is not (HandoverStatus.PLANNED or HandoverStatus.IN_PROGRESS)) throw InvalidStatus(item);
        if (Truncate(request.LastUpdatedAt!.Value) != Truncate(item.UpdatedAt))
            throw new ConflictException("The handover record was just modified. Please reload the page", "HANDOVER_MODIFIED");
        var now = Truncate(clock.GetUtcNow());
        item.Status = HandoverStatus.CANCELLED;
        item.CancellationReason = Text.EmptyToNull(request.CancellationReason);
        item.CancelledAt = item.UpdatedAt = now;
        var recipients = await db.Employees.AsNoTracking().Active().Where(employee => employee.Account.AccountType == AccountType.EMPLOYEE &&
                (employee.EmployeeId == item.FromEmployeeId || employee.EmployeeId == item.ToEmployeeId ||
                 employee.Role == EmployeeRole.FLEET_ROUTE_COORDINATOR && employee.EmployeeTransportPlans.Any(link => link.TransportPlan.TransportId == item.TransportId)))
            .Select(employee => employee.AccountId).Distinct().ToArrayAsync(cancellationToken);
        foreach (var recipient in recipients)
            notifications.Queue(new()
            {
                AccountId = recipient, TransportId = item.TransportId, Type = NotificationType.HANDOVER, Priority = NotificationPriority.HIGH,
                Title = "Handover cancelled", Message = $"Handover {item.HandoverCode} was cancelled: {item.CancellationReason}",
                ReferenceType = "HandoverRecord", ReferenceId = item.HandoverRecordId,
                ActionUrl = $"/admin/ongoing-transports/{item.TransportId}", CreatedAt = now
            });
        var result = await DetailAsync(item, await HeaderAsync(item.TransportId, cancellationToken), actor, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await notifications.FlushAsync(cancellationToken);
        return result;
    }

    private async Task<HandoverRecord> LockAsync(Guid handoverRecordId, CancellationToken cancellationToken)
    {
        var transportId = await db.HandoverRecords.AsNoTracking().Where(item => item.HandoverRecordId == handoverRecordId)
            .Select(item => (Guid?)item.TransportId).SingleOrDefaultAsync(cancellationToken) ?? throw Missing();
        // Use the transport first, matching journey and route mutation lock order.
        await db.LockRowsAsync(db.Transports.Where(item => item.TransportId == transportId), item => item.TransportId, cancellationToken);
        await db.LockRowsAsync(db.HandoverRecords.Where(item => item.HandoverRecordId == handoverRecordId), item => item.HandoverRecordId, cancellationToken);
        var record = await db.HandoverRecords.SingleOrDefaultAsync(item => item.HandoverRecordId == handoverRecordId, cancellationToken) ?? throw Missing();
        await db.Entry(record).ReloadAsync(cancellationToken);
        return record;
    }

    private static ConflictException InvalidStatus(HandoverRecord item) =>
        new($"This action cannot be performed while the handover record is in {item.Status} status", "INVALID_HANDOVER_STATUS");
}
