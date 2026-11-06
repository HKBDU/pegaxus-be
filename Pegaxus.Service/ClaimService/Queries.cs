using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Enum;
using Pegaxus.Repository.Extensions;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;

using static Pegaxus.Service.Common.DbTime;

namespace Pegaxus.Service.ClaimService;

public sealed partial class Service
{
    public async Task<Response.ListResponse> GetListAsync(Guid accountId, Request.ListRequest request, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(accountId, cancellationToken);
        Validation.ValidateFields(request, listValidator);

        var query = db.Claims.AsNoTracking().AsQueryable();
        // R18: a customer only ever sees their own claims.
        if (!actor.IsManager) query = query.Where(item => item.Customer.AccountId == accountId);
        if (!string.IsNullOrWhiteSpace(request.ClaimType))
        {
            var types = request.ClaimType.Split(',').Select(part => Enum.Parse<ClaimType>(part.Trim(), true)).ToArray();
            query = query.Where(item => types.Contains(item.ClaimType));
        }
        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            var statuses = request.Status.Split(',').Select(part => Enum.Parse<ClaimStatus>(part.Trim(), true)).ToArray();
            query = query.Where(item => statuses.Contains(item.Status));
        }
        if (request.BookingId.HasValue) query = query.Where(item => item.HandoverRecord.Transport.BookingId == request.BookingId);
        if (request.SubmittedFrom.HasValue)
        {
            var from = request.SubmittedFrom.Value.ToDateTime(TimeOnly.MinValue);
            query = query.Where(item => item.SubmittedAt >= from);
        }
        if (request.SubmittedTo.HasValue)
        {
            var until = request.SubmittedTo.Value.AddDays(1).ToDateTime(TimeOnly.MinValue);
            query = query.Where(item => item.SubmittedAt < until);
        }
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToUpperInvariant();
            query = query.Where(item => item.ClaimCode.ToUpper().Contains(search) || item.HandoverRecord.Transport.Booking.BookingCode.ToUpper().Contains(search) ||
                item.Customer.FullName.ToUpper().Contains(search));
        }

        var total = await query.CountAsync(cancellationToken);
        var sorted = request.SortBy.Trim().ToLowerInvariant() switch
        {
            "submittedat:asc" => query.OrderBy(item => item.SubmittedAt),
            "status:asc" => query.OrderBy(item => item.Status).ThenByDescending(item => item.SubmittedAt),
            "status:desc" => query.OrderByDescending(item => item.Status).ThenByDescending(item => item.SubmittedAt),
            "requestedcompensationamount:asc" => query.OrderBy(item => item.RequestedCompensationAmount == null).ThenBy(item => item.RequestedCompensationAmount),
            "requestedcompensationamount:desc" => query.OrderBy(item => item.RequestedCompensationAmount == null).ThenByDescending(item => item.RequestedCompensationAmount),
            _ => query.OrderByDescending(item => item.SubmittedAt)
        };
        var rows = await sorted.ThenBy(item => item.ClaimId).Skip(Paging.Offset(request.PageIndex, request.PageSize)).Take(request.PageSize)
            .Select(item => new
            {
                item.ClaimId, item.ClaimCode, BookingId = item.HandoverRecord.Transport.BookingId, BookingCode = item.HandoverRecord.Transport.Booking.BookingCode,
                CustomerName = item.Customer.FullName, item.ClaimType, item.Status, item.Title, item.RequestedCompensationAmount,
                Approved = item.ClaimResolution == null ? null : item.ClaimResolution.ApprovedCompensationAmount, item.Currency,
                item.SubmittedAt, item.ReviewedAt, item.ResolvedAt, item.UpdatedAt
            }).ToListAsync(cancellationToken);
        return new(rows.Select(item => new Response.ClaimListItem(item.ClaimId, item.ClaimCode, item.BookingId, item.BookingCode, item.CustomerName,
                item.ClaimType.ToString(), item.Status.ToString(), item.Title, Amount(item.RequestedCompensationAmount), Amount(item.Approved), item.Currency,
                AsUtc(item.SubmittedAt), AsUtc(item.ReviewedAt), AsUtc(item.ResolvedAt), AsUtc(item.UpdatedAt))).ToArray(),
            request.PageIndex, request.PageSize, total, Paging.TotalPages(total, request.PageSize));
    }

    public async Task<Response.ClaimResponse> GetAsync(Guid accountId, Guid claimId, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(accountId, cancellationToken);
        var visible = db.Claims.AsNoTracking().Where(item => item.ClaimId == claimId && (actor.IsManager || item.Customer.AccountId == accountId));
        var claim = await visible.Select(item => new { item.Status, item.ReviewedAt }).SingleOrDefaultAsync(cancellationToken) ?? throw ClaimMissing();

        // O-F6-09: the first time a manager opens a pending claim, that moment is recorded as the review start.
        if (actor.IsManager && claim.Status == ClaimStatus.PENDING && claim.ReviewedAt is null)
            await MarkReviewedAsync(claimId, cancellationToken);
        return await BuildAsync(claimId, actor, cancellationToken);
    }

    private async Task MarkReviewedAsync(Guid claimId, CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await db.Database.BeginSerializableAsync(cancellationToken);
            var query = db.Claims.Where(item => item.ClaimId == claimId);
            await db.LockRowsAsync(query, item => item.ClaimId, cancellationToken);
            var claim = await query.SingleAsync(cancellationToken);
            if (db.Database.IsRelational()) await db.Entry(claim).ReloadAsync(cancellationToken);
            // Another request may have reviewed or resolved it while this one waited for the lock.
            if (claim.Status != ClaimStatus.PENDING || claim.ReviewedAt is not null) return;
            var now = Truncate(clock.GetUtcNow());
            claim.ReviewedAt = now;
            claim.UpdatedAt = NextVersion(claim.UpdatedAt, now);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception ex) when (IsWriteConflict(ex))
        {
            // Two managers opened it at once; the other one recorded the review, so reading it again is enough.
            db.ChangeTracker.Clear();
        }
    }
}
