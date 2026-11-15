using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;

using static Pegaxus.Service.Common.DbTime;

namespace Pegaxus.Service.ComplianceDossierService;

public sealed partial class Service
{
    private static readonly RoutePlanStatus[] ScheduledPlanStatuses = [RoutePlanStatus.CONFIRMED, RoutePlanStatus.ACTIVE, RoutePlanStatus.COMPLETED];

    /// <summary>A required authority that sent the dossier back, while the specialist has not changed anything since.</summary>
    private sealed record PendingRevision(string AuthorityName, string Decision, DateTime SentBackAt);

    /// <summary>
    /// First and last day of the trip: the scheduled itinerary once a route is confirmed, otherwise the booking's requested dates.
    /// A permit must stay valid until the last day.
    /// </summary>
    private async Task<(DateOnly Start, DateOnly End)> TripWindowAsync(Guid transportId, CancellationToken cancellationToken)
    {
        var booking = await _db.Transports.AsNoTracking().Where(item => item.TransportId == transportId)
            .Select(item => new { item.Booking.RequestedDepartureDate, item.Booking.RequestedArrivalDate })
            .SingleAsync(cancellationToken);
        var plans = await _db.RoutePlans.AsNoTracking().Include(plan => plan.RoutePlanRoutes)
            .Where(plan => plan.TransportPlan.TransportId == transportId && ScheduledPlanStatuses.Contains(plan.Status))
            .ToArrayAsync(cancellationToken);
        var itinerary = TransportItinerary.Effective(plans);
        var departure = itinerary.Select(segment => segment.Route.PlannedDepartureAt).FirstOrDefault(value => value.HasValue);
        var arrival = itinerary.Select(segment => segment.Route.PlannedArrivalAt).LastOrDefault(value => value.HasValue);
        var start = departure is { } first ? DateOnly.FromDateTime(first) : booking.RequestedDepartureDate;
        var end = arrival is { } last ? DateOnly.FromDateTime(last) : booking.RequestedArrivalDate ?? booking.RequestedDepartureDate;
        return (start, end < start ? start : end);
    }

    /// <summary>
    /// The latest send-back (REJECTED, or RETURNED for more information) by a required authority, when no requirement was added,
    /// changed, waived or removed and no document was requested from the customer since. Null when nothing is waiting.
    /// </summary>
    private async Task<PendingRevision?> PendingRevisionAsync(Guid dossierId, CancellationToken cancellationToken)
    {
        var sentBack = await _db.AuthoritySubmissions.AsNoTracking()
            .Where(item => item.ComplianceDossierId == dossierId && item.IsRequired && item.ResponseReceivedAt != null &&
                (item.Status == AuthoritySubmissionStatus.REJECTED || item.Status == AuthoritySubmissionStatus.RETURNED))
            .OrderByDescending(item => item.ResponseReceivedAt).ThenByDescending(item => item.SubmissionNo)
            .Select(item => new { item.AuthorityName, item.Status, At = item.ResponseReceivedAt!.Value })
            .FirstOrDefaultAsync(cancellationToken);
        if (sentBack is null) return null;
        // A later submission to any authority means the revision was already sent off.
        var resubmitted = await _db.AuthoritySubmissions.AsNoTracking()
            .AnyAsync(item => item.ComplianceDossierId == dossierId && item.CreatedAt > sentBack.At, cancellationToken);
        var revised = resubmitted ||
            await _db.DossierRequirements.AsNoTracking().AnyAsync(item => item.ComplianceDossierId == dossierId &&
                (item.CreatedAt > sentBack.At || item.UpdatedAt > sentBack.At), cancellationToken) ||
            await _db.DocumentRequests.AsNoTracking().AnyAsync(item => item.DossierRequirement.ComplianceDossierId == dossierId &&
                item.CreatedAt > sentBack.At, cancellationToken);
        return revised ? null : new PendingRevision(sentBack.AuthorityName, sentBack.Status.ToString(), sentBack.At);
    }

    /// <summary>Date rules that depend on the submission and the trip, on top of the shape checks in ValidateDecisionRequest.</summary>
    private async Task ValidateDecisionDatesAsync(AuthoritySubmissionStatus decision, Request.RecordAuthorityDecisionRequest request,
        AuthoritySubmission submission, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(UtcNow());
        if (decision == AuthoritySubmissionStatus.RETURNED && request.RevisionDueDate is { } due && due < today)
            throw new BadRequestException("Revision due date must not be in the past", "REVISION_DUE_DATE_IN_PAST");
        if (decision != AuthoritySubmissionStatus.APPROVED) return;

        if (request.PermitIssuedDate is { } issued)
        {
            var submitted = DateOnly.FromDateTime(submission.SubmittedAt ?? submission.CreatedAt);
            if (issued > today)
                throw new BadRequestException("Permit issued date must not be in the future", "PERMIT_ISSUED_DATE_IN_FUTURE");
            if (issued < submitted)
                throw new BadRequestException($"Permit issued date must not be before the submission date ({submitted:yyyy-MM-dd})",
                    "PERMIT_ISSUED_BEFORE_SUBMISSION");
        }

        if (request.PermitExpiryDate is { } expiry)
        {
            var (_, tripEnd) = await TripWindowAsync(submission.ComplianceDossier.TransportId, cancellationToken);
            if (expiry < tripEnd)
                throw new BadRequestException($"The permit must stay valid for the whole trip, until {tripEnd:yyyy-MM-dd}",
                    "PERMIT_EXPIRES_BEFORE_TRIP_END");
        }
    }
}
