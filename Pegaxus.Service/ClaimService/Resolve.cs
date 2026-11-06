using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Repository.Extensions;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.OutboxService;

using static Pegaxus.Service.Common.DbTime;

namespace Pegaxus.Service.ClaimService;

public sealed partial class Service
{
    public async Task<Response.ClaimResponse> ResolveAsync(Guid accountId, Guid claimId, Request.ResolveRequest request,
        CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(accountId, cancellationToken);
        if (!actor.IsManager) throw new ForbiddenException("Only Logistics Manager is authorized to resolve claims.");
        Validation.ValidateFields(request, resolveValidator);

        try
        {
            await using var transaction = await db.Database.BeginSerializableAsync(cancellationToken);
            var claim = await LockClaimAsync(claimId, actor, cancellationToken);
            // Checked from the most specific cause to the least: an existing outcome, then the status, then a stale screen.
            if (claim.ClaimResolution is not null) throw new ConflictException("This claim already has a resolution.", "CLAIM_ALREADY_RESOLVED");
            if (claim.Status != ClaimStatus.PENDING) throw InvalidStatus(claim.Status);
            if (Truncate(claim.UpdatedAt) != Truncate(request.LastUpdatedAt!.Value)) throw Modified();

            var now = Truncate(clock.GetUtcNow());
            var type = Enum.Parse<ClaimResolutionType>(request.ResolutionType!.Trim(), true);
            // R21: a refusal and a service recovery carry no money, so they are stored as 0 rather than left empty.
            var amount = request.ApprovedCompensationAmount ?? (type is ClaimResolutionType.NO_COMPENSATION or ClaimResolutionType.SERVICE_RECOVERY ? 0m : (decimal?)null);
            var resolution = new ClaimResolution
            {
                ClaimResolutionId = Guid.NewGuid(), ClaimId = claim.ClaimId, ResolutionType = type, ResolutionSummary = request.ResolutionSummary!.Trim(),
                ApprovedCompensationAmount = amount, Currency = Vnd.Currency, CorrectiveAction = Text.EmptyToNull(request.CorrectiveAction)?.Trim(),
                InternalNote = Text.EmptyToNull(request.InternalNote)?.Trim(), ResolvedAt = now, CreatedAt = now, UpdatedAt = now
            };
            db.ClaimResolutions.Add(resolution);
            var rejected = type == ClaimResolutionType.NO_COMPENSATION;
            claim.Status = rejected ? ClaimStatus.REJECTED : ClaimStatus.APPROVED;
            claim.ReviewedAt ??= now;
            if (rejected) claim.ResolvedAt = now;
            claim.UpdatedAt = NextVersion(claim.UpdatedAt, now);

            var parties = await PartiesAsync(claimId, cancellationToken);
            var outcome = rejected ? "rejected" : "approved";
            // The customer sees the outcome and its reasoning, never the internal note or the corrective action.
            NotifyCustomer(claim, parties, rejected ? "Claim rejected" : "Claim approved",
                $"Claim {claim.ClaimCode} for booking {parties.BookingCode} was {outcome}. {resolution.ResolutionSummary}", now);
            OutboxQueue.AddEmail(db, new()
            {
                To = parties.CustomerEmail, Subject = $"Claim {outcome} — {claim.ClaimCode}",
                Body = MailService.EmailBodies.ClaimResultBody(parties.CustomerName, claim.ClaimCode, parties.BookingCode, !rejected,
                    resolution.ResolutionSummary, amount is > 0 ? Vnd.ToLong(amount.Value) : null, Vnd.Currency,
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
