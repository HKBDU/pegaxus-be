using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Common;

using static Pegaxus.Service.Common.DbTime;

namespace Pegaxus.Service.ClaimService;

public sealed partial class Service
{
    private async Task<string?> ReadUrlAsync(string? fileUrl, CancellationToken cancellationToken) =>
        string.IsNullOrWhiteSpace(fileUrl) ? null : (await media.CreateReadUrlAsync(fileUrl, cancellationToken)).Url;

    /// <summary>Loads the claim and shapes it for the viewer: a customer sees the basic fields, a manager also the cross-check blocks (R18).</summary>
    private async Task<Response.ClaimResponse> BuildAsync(Guid claimId, Actor viewer, CancellationToken cancellationToken)
    {
        var claim = await db.Claims.AsNoTracking().Include(item => item.Customer).ThenInclude(item => item.Account).Include(item => item.ClaimResolution)
            .Include(item => item.HandoverRecord).ThenInclude(item => item.Transport).ThenInclude(item => item.Booking)
            .SingleAsync(item => item.ClaimId == claimId, cancellationToken);
        var handover = claim.HandoverRecord;
        var transport = handover.Transport;
        var booking = transport.Booking;
        var completedAt = AsUtc(handover.CompletedAt);
        DateTimeOffset? deadline = completedAt + ClaimWindow;

        Response.HandoverBrief handoverBrief = viewer.IsManager
            ? new Response.ManagerHandover(handover.HandoverRecordId, handover.HandoverCode, handover.HandoverType.ToString(), handover.Status.ToString(),
                completedAt, deadline, handover.FromPartyName, handover.ToPartyName, handover.HorseConditionNote, handover.DiscrepancyNote,
                await ReadUrlAsync(handover.SenderSignatureUrl, cancellationToken), await ReadUrlAsync(handover.ReceiverSignatureUrl, cancellationToken))
            : new Response.HandoverBrief(handover.HandoverRecordId, handover.HandoverCode, handover.HandoverType.ToString(), handover.Status.ToString(),
                completedAt, deadline);

        var resolution = claim.ClaimResolution;
        Response.ResolutionBrief? resolutionBrief = resolution is null ? null : viewer.IsManager
            ? new Response.ManagerResolution(resolution.ClaimResolutionId, resolution.ResolutionType.ToString(), Amount(resolution.ApprovedCompensationAmount),
                resolution.Currency, resolution.ResolutionSummary, AsUtc(resolution.ResolvedAt), resolution.CorrectiveAction, resolution.InternalNote)
            : new Response.ResolutionBrief(resolution.ClaimResolutionId, resolution.ResolutionType.ToString(), Amount(resolution.ApprovedCompensationAmount),
                resolution.Currency, resolution.ResolutionSummary, AsUtc(resolution.ResolvedAt));

        var customer = new Response.CustomerBrief(claim.Customer.CustomerId, claim.Customer.CustomerCode, claim.Customer.FullName,
            claim.Customer.Phone, claim.Customer.Account.Email);
        var bookingBrief = new Response.BookingBrief(booking.BookingId, booking.BookingCode);
        var transportBrief = new Response.TransportBrief(transport.TransportId, transport.TransportCode, transport.Status.ToString());

        if (!viewer.IsManager)
            return new Response.ClaimResponse
            {
                ClaimId = claim.ClaimId, ClaimCode = claim.ClaimCode, Status = claim.Status.ToString(), ClaimType = claim.ClaimType.ToString(),
                Title = claim.Title, Description = claim.Description, RequestedCompensationAmount = Amount(claim.RequestedCompensationAmount),
                Currency = claim.Currency, EvidenceNote = claim.EvidenceNote, CustomerNote = claim.CustomerNote,
                SubmittedAt = AsUtc(claim.SubmittedAt), ReviewedAt = AsUtc(claim.ReviewedAt), ResolvedAt = AsUtc(claim.ResolvedAt),
                CancelledAt = AsUtc(claim.CancelledAt), CancellationReason = claim.CancellationReason,
                Customer = customer, Booking = bookingBrief, Transport = transportBrief, Handover = handoverBrief, Resolution = resolutionBrief,
                AvailableActions = claim.Status == ClaimStatus.PENDING ? ["CANCEL"] : [],
                CreatedAt = AsUtc(claim.CreatedAt), UpdatedAt = AsUtc(claim.UpdatedAt)
            };

        var pickup = await db.HandoverRecords.AsNoTracking().Where(item => item.TransportId == transport.TransportId &&
                item.HandoverType == HandoverType.PICKUP && item.Status == HandoverStatus.COMPLETED && item.CompletedAt != null)
            .OrderByDescending(item => item.CompletedAt).ThenByDescending(item => item.HandoverRecordId).FirstOrDefaultAsync(cancellationToken);
        var logs = db.HorseHealthLogs.AsNoTracking().Where(item => item.TransportId == transport.TransportId);
        var lastChecked = await logs.MaxAsync(item => (DateTime?)item.CheckedAt, cancellationToken);
        var health = new Response.HealthLogSummary(await logs.CountAsync(cancellationToken),
            await logs.CountAsync(item => item.HealthStatus != HorseHealthStatus.NORMAL, cancellationToken), AsUtc(lastChecked));
        var incidents = (await db.IncidentReports.AsNoTracking().Where(item => item.TransportId == transport.TransportId && item.Status != IncidentStatus.CANCELLED)
            .OrderBy(item => item.OccurredAt).ThenBy(item => item.IncidentReportId)
            .Select(item => new { item.IncidentReportId, item.IncidentCode, item.Category, item.Severity, item.Status, item.OccurredAt })
            .ToListAsync(cancellationToken))
            .Select(item => new Response.IncidentBrief(item.IncidentReportId, item.IncidentCode, item.Category.ToString(), item.Severity.ToString(),
                item.Status.ToString(), AsUtc(item.OccurredAt))).ToList();
        var billing = await BillingAsync(claim, booking.BookingId, cancellationToken);

        var actions = new List<string>();
        if (claim.Status == ClaimStatus.PENDING) actions.AddRange(["RESOLVE", "CANCEL"]);
        else if (claim.Status == ClaimStatus.APPROVED)
        {
            actions.Add("COMPLETE");
            if (billing.CreditNote.Eligible) actions.Add("CREATE_CREDIT_NOTE");
        }
        return new Response.ManagerClaimResponse
        {
            ClaimId = claim.ClaimId, ClaimCode = claim.ClaimCode, Status = claim.Status.ToString(), ClaimType = claim.ClaimType.ToString(),
            Title = claim.Title, Description = claim.Description, RequestedCompensationAmount = Amount(claim.RequestedCompensationAmount),
            Currency = claim.Currency, EvidenceNote = claim.EvidenceNote, CustomerNote = claim.CustomerNote,
            SubmittedAt = AsUtc(claim.SubmittedAt), ReviewedAt = AsUtc(claim.ReviewedAt), ResolvedAt = AsUtc(claim.ResolvedAt),
            CancelledAt = AsUtc(claim.CancelledAt), CancellationReason = claim.CancellationReason,
            Customer = customer, Booking = bookingBrief, Transport = transportBrief, Handover = handoverBrief, Resolution = resolutionBrief,
            AvailableActions = actions, CreatedAt = AsUtc(claim.CreatedAt), UpdatedAt = AsUtc(claim.UpdatedAt),
            PickupHandover = pickup is null ? null : new Response.PickupHandover(pickup.HandoverRecordId, pickup.HandoverCode, AsUtc(pickup.CompletedAt),
                pickup.HorseConditionNote, pickup.DiscrepancyNote, await ReadUrlAsync(pickup.SenderSignatureUrl, cancellationToken),
                await ReadUrlAsync(pickup.ReceiverSignatureUrl, cancellationToken)),
            HealthLogSummary = health, Incidents = incidents, Billing = billing
        };
    }

    private static long? Amount(decimal? value) => value.HasValue ? Vnd.ToLong(value.Value) : null;

    /// <summary>R22: how the booking's invoices stand against the claim, and whether a credit note may be created for it.</summary>
    private async Task<Response.Billing> BillingAsync(Repository.Entities.Claim claim, Guid bookingId, CancellationToken cancellationToken)
    {
        var final = await db.Invoices.AsNoTracking().Where(item => item.BookingId == bookingId && item.Type == InvoiceType.FINAL && item.Status != InvoiceStatus.CANCELLED)
            .OrderByDescending(item => item.CreatedAt).ThenByDescending(item => item.InvoiceId).FirstOrDefaultAsync(cancellationToken);
        var creditNotes = (await db.Invoices.AsNoTracking().Where(item => item.BookingId == bookingId && item.Type == InvoiceType.CREDIT_NOTE && item.Status != InvoiceStatus.CANCELLED)
            .OrderBy(item => item.CreatedAt).ThenBy(item => item.InvoiceId).ToListAsync(cancellationToken))
            .Select(item => new Response.CreditNoteBrief(item.InvoiceId, item.InvoiceCode, item.Status.ToString(), Vnd.ToLong(item.TotalAmount), item.Currency)).ToList();

        var approved = claim.ClaimResolution?.ApprovedCompensationAmount;
        var finalPaid = final is { Status: InvoiceStatus.PAID };
        string? blocked = claim.Status != ClaimStatus.APPROVED ? "CLAIM_NOT_APPROVED"
            : approved is null or <= 0 ? "NO_COMPENSATION_APPROVED"
            : !finalPaid ? "FINAL_NOT_PAID" : null;
        var eligible = blocked is null;
        var amount = approved is > 0 ? Vnd.ToLong(approved.Value) : (long?)null;
        var creditNote = new Response.CreditNoteEligibility(eligible, blocked, amount,
            eligible ? new Response.SuggestedItem(InvoiceItemType.OTHER.ToString(), $"Compensation for claim {claim.ClaimCode}", 1, amount!.Value) : null);
        return new(final is null ? null : new Response.FinalInvoiceBrief(final.InvoiceId, final.InvoiceCode, final.Status.ToString(),
                Vnd.ToLong(final.TotalAmount), final.Currency, AsUtc(final.PaidAt)),
            final is null ? "NO_FINAL_INVOICE" : finalPaid ? null : "FINAL_NOT_PAID", creditNote, creditNotes);
    }
}
