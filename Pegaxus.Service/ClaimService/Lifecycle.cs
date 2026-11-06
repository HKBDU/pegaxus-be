using Pegaxus.Repository.Enum;
using Pegaxus.Repository.Extensions;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.OutboxService;

using static Pegaxus.Service.Common.DbTime;

namespace Pegaxus.Service.ClaimService;

public sealed partial class Service
{
    public async Task<Response.ClaimResponse> CancelAsync(Guid accountId, Guid claimId, Request.CancelRequest request,
        CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(accountId, cancellationToken);
        Validation.ValidateFields(request, cancelValidator);

        try
        {
            await using var transaction = await db.Database.BeginSerializableAsync(cancellationToken);
            var claim = await LockClaimAsync(claimId, actor, cancellationToken);
            if (claim.Status != ClaimStatus.PENDING) throw InvalidStatus(claim.Status);
            if (Truncate(claim.UpdatedAt) != Truncate(request.LastUpdatedAt!.Value)) throw Modified();

            var now = Truncate(clock.GetUtcNow());
            claim.Status = ClaimStatus.CANCELLED;
            claim.CancelledAt = now;
            claim.CancellationReason = request.CancellationReason!.Trim();
            claim.UpdatedAt = NextVersion(claim.UpdatedAt, now);

            var parties = await PartiesAsync(claimId, cancellationToken);
            // The other side is told: managers when the customer withdraws, the customer when a manager cancels (O-F6-08).
            if (actor.IsCustomer)
                await NotifyManagersAsync(claim, parties, "Claim cancelled by customer",
                    $"Claim {claim.ClaimCode} for booking {parties.BookingCode} was cancelled by {parties.CustomerName}. Reason: {claim.CancellationReason}", now, cancellationToken);
            else
                NotifyCustomer(claim, parties, "Claim cancelled",
                    $"Claim {claim.ClaimCode} for booking {parties.BookingCode} was cancelled. Reason: {claim.CancellationReason}", now);

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            await notifications.FlushAsync(cancellationToken);
        }
        catch (Exception ex) when (IsWriteConflict(ex))
        {
            throw Modified();
        }
        return await BuildAsync(claimId, actor, cancellationToken);
    }

    public async Task<Response.ClaimResponse> CompleteAsync(Guid accountId, Guid claimId, Request.CompleteRequest request,
        CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(accountId, cancellationToken);
        if (!actor.IsManager) throw new ForbiddenException("Only Logistics Manager is authorized to complete claims.");
        Validation.ValidateFields(request, completeValidator);

        try
        {
            await using var transaction = await db.Database.BeginSerializableAsync(cancellationToken);
            var claim = await LockClaimAsync(claimId, actor, cancellationToken);
            if (claim.Status != ClaimStatus.APPROVED) throw InvalidStatus(claim.Status);
            if (Truncate(claim.UpdatedAt) != Truncate(request.LastUpdatedAt!.Value)) throw Modified();

            var now = Truncate(clock.GetUtcNow());
            claim.Status = ClaimStatus.RESOLVED;
            claim.ResolvedAt = now;
            claim.UpdatedAt = NextVersion(claim.UpdatedAt, now);

            var parties = await PartiesAsync(claimId, cancellationToken);
            NotifyCustomer(claim, parties, "Claim compensation completed",
                $"The compensation for claim {claim.ClaimCode} (booking {parties.BookingCode}) has been completed and the claim is closed.", now);
            OutboxQueue.AddEmail(db, new()
            {
                To = parties.CustomerEmail, Subject = $"Claim closed — {claim.ClaimCode}",
                Body = MailService.EmailBodies.ClaimClosedBody(parties.CustomerName, claim.ClaimCode, parties.BookingCode,
                    NotificationLinks.Claim(null, parties.BookingId, claim.ClaimId))
            }, now);

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            await notifications.FlushAsync(cancellationToken);
        }
        catch (Exception ex) when (IsWriteConflict(ex))
        {
            throw Modified();
        }
        return await BuildAsync(claimId, actor, cancellationToken);
    }
}
