using System.Text.Json.Serialization;

namespace Pegaxus.Service.ClaimService;

public static class Response
{
    public sealed record BookingBrief(Guid BookingId, string BookingCode);
    public sealed record TransportBrief(Guid TransportId, string TransportCode, string Status);
    public sealed record CustomerBrief(Guid CustomerId, string CustomerCode, string Name, string? Phone, string Email);

    public sealed record EligibilityHandover(Guid HandoverRecordId, string HandoverCode, string HandoverType, DateTimeOffset CompletedAt);

    public sealed record Eligibility(DateTimeOffset ServerTime, BookingBrief Booking, TransportBrief? Transport, EligibilityHandover? Handover,
        DateTimeOffset? ClaimDeadline, int? RemainingSeconds, bool CanSubmit, string? BlockedReason, int ClaimCount);

    /// <summary>The delivery record as the customer sees it; a manager gets <see cref="ManagerHandover"/>, which adds the on-site notes and signatures.</summary>
    [JsonDerivedType(typeof(ManagerHandover))]
    public record HandoverBrief(Guid HandoverRecordId, string HandoverCode, string HandoverType, string Status,
        DateTimeOffset? CompletedAt, DateTimeOffset? ClaimDeadline);

    public sealed record ManagerHandover(Guid HandoverRecordId, string HandoverCode, string HandoverType, string Status,
        DateTimeOffset? CompletedAt, DateTimeOffset? ClaimDeadline, string? FromPartyName, string? ToPartyName,
        string? HorseConditionNote, string? DiscrepancyNote, string? SenderSignatureUrl, string? ReceiverSignatureUrl)
        : HandoverBrief(HandoverRecordId, HandoverCode, HandoverType, Status, CompletedAt, ClaimDeadline);

    public sealed record PickupHandover(Guid HandoverRecordId, string HandoverCode, DateTimeOffset? CompletedAt, string? HorseConditionNote,
        string? DiscrepancyNote, string? SenderSignatureUrl, string? ReceiverSignatureUrl);

    public sealed record HealthLogSummary(int TotalCount, int AbnormalCount, DateTimeOffset? LastCheckedAt);

    public sealed record IncidentBrief(Guid IncidentReportId, string IncidentCode, string Category, string Severity, string Status, DateTimeOffset OccurredAt);

    /// <summary>The outcome as the customer sees it; a manager gets <see cref="ManagerResolution"/>, which adds the internal fields.</summary>
    [JsonDerivedType(typeof(ManagerResolution))]
    public record ResolutionBrief(Guid ClaimResolutionId, string ResolutionType, long? ApprovedCompensationAmount, string? Currency,
        string ResolutionSummary, DateTimeOffset ResolvedAt);

    public sealed record ManagerResolution(Guid ClaimResolutionId, string ResolutionType, long? ApprovedCompensationAmount, string? Currency,
        string ResolutionSummary, DateTimeOffset ResolvedAt, string? CorrectiveAction, string? InternalNote)
        : ResolutionBrief(ClaimResolutionId, ResolutionType, ApprovedCompensationAmount, Currency, ResolutionSummary, ResolvedAt);

    public sealed record FinalInvoiceBrief(Guid InvoiceId, string InvoiceNumber, string Status, long TotalAmount, string Currency, DateTimeOffset? PaidAt);
    public sealed record SuggestedItem(string ItemType, string Description, int Quantity, long Price);
    public sealed record CreditNoteEligibility(bool Eligible, string? BlockedReason, long? ApprovedAmount, SuggestedItem? SuggestedItem);
    public sealed record CreditNoteBrief(Guid InvoiceId, string InvoiceNumber, string Status, long TotalAmount, string Currency);
    public sealed record Billing(FinalInvoiceBrief? FinalInvoice, string? Warning, CreditNoteEligibility CreditNote,
        IReadOnlyList<CreditNoteBrief> BookingCreditNotes);

    /// <summary>A claim as the customer who filed it sees it; a manager gets <see cref="ManagerClaimResponse"/>, which adds the cross-check blocks.</summary>
    [JsonDerivedType(typeof(ManagerClaimResponse))]
    public class ClaimResponse
    {
        public Guid ClaimId { get; init; }
        public required string ClaimCode { get; init; }
        public required string Status { get; init; }
        public required string ClaimType { get; init; }
        public required string Title { get; init; }
        public required string Description { get; init; }
        public long? RequestedCompensationAmount { get; init; }
        public string? Currency { get; init; }
        public string? EvidenceNote { get; init; }
        public string? CustomerNote { get; init; }
        public DateTimeOffset SubmittedAt { get; init; }
        public DateTimeOffset? ReviewedAt { get; init; }
        public DateTimeOffset? ResolvedAt { get; init; }
        public DateTimeOffset? CancelledAt { get; init; }
        public string? CancellationReason { get; init; }
        public required CustomerBrief Customer { get; init; }
        public required BookingBrief Booking { get; init; }
        public required TransportBrief Transport { get; init; }
        public required HandoverBrief Handover { get; init; }
        public ResolutionBrief? Resolution { get; init; }
        public required IReadOnlyList<string> AvailableActions { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset UpdatedAt { get; init; }
    }

    public sealed class ManagerClaimResponse : ClaimResponse
    {
        public PickupHandover? PickupHandover { get; init; }
        public required HealthLogSummary HealthLogSummary { get; init; }
        public required IReadOnlyList<IncidentBrief> Incidents { get; init; }
        public required Billing Billing { get; init; }
    }

    public sealed record ClaimListItem(Guid ClaimId, string ClaimCode, Guid BookingId, string BookingCode, string CustomerName, string ClaimType,
        string Status, string Title, long? RequestedCompensationAmount, long? ApprovedCompensationAmount, string? Currency,
        DateTimeOffset SubmittedAt, DateTimeOffset? ReviewedAt, DateTimeOffset? ResolvedAt, DateTimeOffset UpdatedAt);

    public sealed record ListResponse(IReadOnlyList<ClaimListItem> Items, int PageIndex, int PageSize, int TotalItems, int TotalPages);
}
