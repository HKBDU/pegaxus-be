using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Repository.Extensions;
using Pegaxus.Service.Common;
using Pegaxus.Service.NotificationService;

namespace Pegaxus.Service.ClaimService;

public sealed partial class Service
{
    private sealed record Parties(Guid CustomerAccountId, string CustomerEmail, string CustomerName, Guid BookingId, string BookingCode, Guid TransportId);

    private Task<Parties> PartiesAsync(Guid claimId, CancellationToken cancellationToken) =>
        db.Claims.AsNoTracking().Where(item => item.ClaimId == claimId)
            .Select(item => new Parties(item.Customer.AccountId, item.Customer.Account.Email, item.Customer.FullName,
                item.HandoverRecord.Transport.BookingId, item.HandoverRecord.Transport.Booking.BookingCode, item.HandoverRecord.TransportId))
            .SingleAsync(cancellationToken);

    private void NotifyCustomer(Claim claim, Parties parties, string title, string message, DateTime now) =>
        notifications.Queue(new NotificationCreateRequest
        {
            AccountId = parties.CustomerAccountId, TransportId = parties.TransportId, Type = NotificationType.CLAIM, Priority = NotificationPriority.NORMAL,
            Title = title, Message = message, ReferenceType = nameof(Claim), ReferenceId = claim.ClaimId,
            ActionUrl = NotificationLinks.Claim(null, parties.BookingId, claim.ClaimId), CreatedAt = now
        });

    private async Task NotifyManagersAsync(Claim claim, Parties parties, string title, string message, DateTime now, CancellationToken cancellationToken)
    {
        var managers = await db.Employees.AsNoTracking().Active().Where(item => item.Role == EmployeeRole.LOGISTICS_MANAGER)
            .Select(item => item.AccountId).ToListAsync(cancellationToken);
        foreach (var managerAccountId in managers)
            notifications.Queue(new NotificationCreateRequest
            {
                AccountId = managerAccountId, TransportId = parties.TransportId, Type = NotificationType.CLAIM, Priority = NotificationPriority.NORMAL,
                Title = title, Message = message, ReferenceType = nameof(Claim), ReferenceId = claim.ClaimId,
                ActionUrl = NotificationLinks.Claim(EmployeeRole.LOGISTICS_MANAGER, parties.BookingId, claim.ClaimId), CreatedAt = now
            });
    }

    /// <summary>
    /// Locks the claim row (the caller's transaction is already open), reloads it and returns it tracked together with its resolution.
    /// A customer only reaches their own claim; anything else is the same 404 as a missing claim (R18).
    /// </summary>
    private async Task<Claim> LockClaimAsync(Guid claimId, Actor actor, CancellationToken cancellationToken)
    {
        var visible = db.Claims.Where(item => item.ClaimId == claimId && (actor.IsManager || item.Customer.AccountId == actor.AccountId));
        await db.LockRowsAsync(visible, item => item.ClaimId, cancellationToken);
        var claim = await visible.Include(item => item.ClaimResolution).SingleOrDefaultAsync(cancellationToken) ?? throw ClaimMissing();
        if (db.Database.IsRelational()) await db.Entry(claim).ReloadAsync(cancellationToken);
        return claim;
    }
}
