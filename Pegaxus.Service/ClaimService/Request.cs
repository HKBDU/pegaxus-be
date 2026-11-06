namespace Pegaxus.Service.ClaimService;

public static class Request
{
    public sealed class SubmitRequest
    {
        public string? ClaimType { get; set; }
        public string? Title { get; set; }
        public string? Description { get; set; }
        // decimal so that a fractional amount is rejected instead of silently rounded.
        public decimal? RequestedCompensationAmount { get; set; }
        public string? EvidenceNote { get; set; }
        public string? CustomerNote { get; set; }
    }

    public sealed class ListRequest
    {
        public string? Search { get; set; }
        public string? ClaimType { get; set; }
        public string? Status { get; set; }
        public Guid? BookingId { get; set; }
        public DateOnly? SubmittedFrom { get; set; }
        public DateOnly? SubmittedTo { get; set; }
        public int PageIndex { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public string SortBy { get; set; } = "submittedAt:desc";
    }

    public sealed class ResolveRequest
    {
        public DateTimeOffset? LastUpdatedAt { get; set; }
        public string? ResolutionType { get; set; }
        public decimal? ApprovedCompensationAmount { get; set; }
        public string? ResolutionSummary { get; set; }
        public string? CorrectiveAction { get; set; }
        public string? InternalNote { get; set; }
    }

    public sealed class CancelRequest
    {
        public DateTimeOffset? LastUpdatedAt { get; set; }
        public string? CancellationReason { get; set; }
    }

    public sealed class CompleteRequest
    {
        public DateTimeOffset? LastUpdatedAt { get; set; }
    }
}
