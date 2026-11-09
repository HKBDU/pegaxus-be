using Pegaxus.Repository.Enum;
using Pegaxus.Service.Common;

namespace Pegaxus.Service.BookingService;

public class Response
{
    public sealed class NegotiatedBookingResponse
    {
        public Guid BookingId { get; init; }
        public required string Status { get; init; }
        public Guid? ComplianceDossierId { get; init; }
        public string? DossierStatus { get; init; }
        public required string Reason { get; init; }
    }

    public class BookingHistoryResponse
    {
        public Guid HistoryId { get; init; }
        public required string Action { get; init; }
        public string? PreviousStatus { get; init; }
        public required string NewStatus { get; init; }
        public string? Reason { get; init; }
        public required ChangedByResponse ChangedBy { get; init; }
        public DateTimeOffset ChangedAt { get; init; }
    }

    public class ChangedByResponse
    {
        public Guid AccountId { get; init; }
        public required string FullName { get; init; }
        public required string Role { get; init; }
    }

    public class BookingDetailResponse
    {
        public Guid BookingId { get; init; }
        public required string BookingCode { get; init; }
        public required string Status { get; init; }
        public required string StatusName { get; init; }
        public required CustomerResponse Customer { get; init; }
        public required string OriginAddress { get; init; }
        public required string OriginCountry { get; init; }
        public required string DestinationAddress { get; init; }
        public required string DestinationCountry { get; init; }
        public decimal? OriginLatitude { get; init; }
        public decimal? OriginLongitude { get; init; }
        public decimal? DestinationLatitude { get; init; }
        public decimal? DestinationLongitude { get; init; }
        public DateOnly RequestedDepartureDate { get; init; }
        public DateOnly? RequestedArrivalDate { get; init; }
        public int TotalHorses { get; init; }
        public required IReadOnlyList<BookingHorseResponse> Horses { get; init; }
        public string? SpecialRequirements { get; init; }
        public string? CustomerNote { get; init; }
        public AssignedEmployeeResponse? AssignedEmployee { get; init; }
        public DateTimeOffset SubmittedAt { get; init; }
        public DateTimeOffset? ApprovedAt { get; init; }
        public DateTimeOffset? RejectedAt { get; init; }
        public DateTimeOffset? CancelledAt { get; init; }
        public string? RejectionReason { get; init; }
        public string? CancellationReason { get; init; }
        public TransportDetailResponse? Transport { get; init; }
        /// <summary>Set on a return booking: the original trip that left the horse behind.</summary>
        public ReturnOfResponse? ReturnOf { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset UpdatedAt { get; init; }
    }

    public class CustomerResponse
    {
        public Guid CustomerId { get; init; }
        public required string CustomerCode { get; init; }
        public required string FullName { get; init; }
        public string? Phone { get; init; }
    }

    public sealed class ApprovedBookingDetailResponse
    {
        public Guid BookingId { get; init; }
        public required string BookingCode { get; init; }
        public required string Status { get; init; }
        public required CustomerResponse Customer { get; init; }
        public required string OriginAddress { get; init; }
        public required string OriginCountry { get; init; }
        public required string DestinationAddress { get; init; }
        public required string DestinationCountry { get; init; }
        public decimal? OriginLatitude { get; init; }
        public decimal? OriginLongitude { get; init; }
        public decimal? DestinationLatitude { get; init; }
        public decimal? DestinationLongitude { get; init; }
        public bool IsInternational { get; init; }
        public DateOnly RequestedDepartureDate { get; init; }
        public DateOnly? RequestedArrivalDate { get; init; }
        public string? SpecialRequirements { get; init; }
        public string? CustomerNote { get; init; }
        public int TotalHorses { get; init; }
        public required IReadOnlyList<BookingHorseResponse> Horses { get; init; }
        public TransportDetailResponse? Transport { get; init; }
        /// <summary>Set on a return booking: the original trip that left the horse behind.</summary>
        public ReturnOfResponse? ReturnOf { get; init; }
        public ApprovedTransportPlanResponse? TransportPlan { get; init; }
        public required IReadOnlyList<ComplianceDossierItemResponse> ComplianceDossiers { get; init; }
        public required IReadOnlyList<ApprovedRoutePlanResponse> RoutePlans { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
    }

    public sealed class ApprovedRoutePlanResponse
    {
        public Guid RoutePlanId { get; init; }
        public required string RoutePlanCode { get; init; }
        public required string RouteType { get; init; }
        public required string Status { get; init; }
        public bool LegalReviewRequested { get; init; }
        public DateTimeOffset? LegalReviewRequestedAt { get; init; }
    }

    public class BookingHorseResponse
    {
        public Guid HorseId { get; init; }
        public required string HorseName { get; init; }
        public string? Sex { get; init; }
        public string? Breed { get; init; }
        public string? MicrochipNumber { get; init; }
        public string? PassportNumber { get; init; }
        public string? SpecialHandlingNotes { get; init; }
        /// <summary>Set when the horse was left behind mid-trip; it no longer travels with this booking's transport.</summary>
        public DateTimeOffset? DetachedAt { get; init; }
        public string? DetachedLocationName { get; init; }
    }

    public sealed class ReturnOfResponse
    {
        public Guid TransportId { get; init; }
        public required string TransportCode { get; init; }
        public Guid BookingId { get; init; }
        public required string BookingCode { get; init; }
        public ReturnOfHorseResponse? Horse { get; init; }
    }

    public sealed class ReturnOfHorseResponse
    {
        public Guid HorseId { get; init; }
        public required string HorseName { get; init; }
        public DateTimeOffset? DetachedAt { get; init; }
        public string? DetachedLocationName { get; init; }
    }

    /// <summary>Pre-filled values for the return booking form of a horse left behind.</summary>
    public sealed class ReturnDraftResponse
    {
        public Guid ReturnOfTransportId { get; init; }
        public required string TransportCode { get; init; }
        public required string BookingCode { get; init; }
        public Guid HorseId { get; init; }
        public required string HorseName { get; init; }
        public required string OriginAddress { get; init; }
        public required string OriginCountry { get; init; }
        public decimal? OriginLatitude { get; init; }
        public decimal? OriginLongitude { get; init; }
        public required string DestinationAddress { get; init; }
        public required string DestinationCountry { get; init; }
        public decimal? DestinationLatitude { get; init; }
        public decimal? DestinationLongitude { get; init; }
        /// <summary>A live return booking that already exists for this horse, if any.</summary>
        public Guid? ExistingReturnBookingId { get; init; }
    }

    public class AssignedEmployeeResponse
    {
        public Guid EmployeeId { get; init; }
        public required string FullName { get; init; }
    }

    public class TransportDetailResponse
    {
        public Guid TransportId { get; init; }
        public required string TransportCode { get; init; }
        public required string Status { get; init; }
    }

    public class BookingListResponse
    {
        public required IReadOnlyList<BookingListItemResponse> Items { get; init; }
        public int PageIndex { get; init; }
        public int PageSize { get; init; }
        public int TotalItems { get; init; }
        public int TotalPages { get; init; }
    }

    public class BookingListItemResponse
    {
        public Guid BookingId { get; init; }
        public required string BookingCode { get; init; }
        public required string Status { get; init; }
        public required string StatusName { get; init; }
        public required string CustomerName { get; init; }
        public required string OriginAddress { get; init; }
        public required string OriginCountry { get; init; }
        public required string DestinationAddress { get; init; }
        public required string DestinationCountry { get; init; }
        public DateOnly RequestedDepartureDate { get; init; }
        public int TotalHorses { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
    }

    public class ApprovedBookingListResponse
    {
        public required IReadOnlyList<ApprovedBookingListItemResponse> Items { get; init; }
        public int PageIndex { get; init; }
        public int PageSize { get; init; }
        public int TotalItems { get; init; }
        public int TotalPages { get; init; }
    }

    public class ApprovedBookingListItemResponse
    {
        public Guid BookingId { get; init; }
        public required string BookingCode { get; init; }
        public required string Status { get; init; }
        public required string CustomerName { get; init; }
        public required string CustomerCode { get; init; }
        public required string OriginAddress { get; init; }
        public required string OriginCountry { get; init; }
        public required string DestinationAddress { get; init; }
        public required string DestinationCountry { get; init; }
        public bool IsInternational { get; init; }
        public DateOnly RequestedDepartureDate { get; init; }
        public DateOnly? RequestedArrivalDate { get; init; }
        public string? SpecialRequirements { get; init; }
        public DateTimeOffset? ApprovedAt { get; init; }
        public int TotalHorses { get; init; }
        public Guid? TransportId { get; init; }
        public TransportPlanSummary? TransportPlan { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public ComplianceDossierSummary? ComplianceDossier { get; init; }
        public RoutePlanSummary? RoutePlan { get; init; }
    }

    public class TransportPlanSummary
    {
        public Guid TransportPlanId { get; init; }
        public required string PlanCode { get; init; }
        public required string Status { get; init; }
        public DateTimeOffset? PlannedStartAt { get; init; }
        public DateTimeOffset? PlannedEndAt { get; init; }
        public decimal? EstimatedDistanceKm { get; init; }
        public int? EstimatedDurationMinutes { get; init; }
        public string? PlanningNote { get; init; }
    }

    public sealed class ApprovedTransportPlanResponse : TransportPlanSummary
    {
        public LocationBrief? OriginLocation { get; init; }
        public LocationBrief? DestinationLocation { get; init; }
    }

    public sealed class ComplianceDossierItemResponse
    {
        public Guid ComplianceDossierId { get; init; }
        public required string DossierCode { get; init; }
        public required string DossierType { get; init; }
        public required string Status { get; init; }
    }

    public class ComplianceDossierSummary
    {
        public Guid ComplianceDossierId { get; init; }
        public required string Status { get; init; }
    }

    public class RoutePlanSummary
    {
        public Guid RoutePlanId { get; init; }
        public required string RoutePlanCode { get; init; }
        public required string Status { get; init; }
        public bool LegalReviewRequested { get; init; }
        public DateTimeOffset? LegalReviewRequestedAt { get; init; }
    }

    public class CreateBookingResponse
    {
        public Guid BookingId { get; set; }
        public required string BookingCode { get; set; }
    }

    public class UpdateBookingResponse
    {
        public DateTimeOffset UpdatedAt { get; set; }
    }

    public class ApproveBookingResponse
    {
        public BookingStatus Status { get; set; }
        public required TransportResponse Transport { get; set; }
    }

    public class TransportResponse
    {
        public Guid TransportId { get; set; }
        public required string TransportCode { get; set; }
        public TransportStatus Status { get; set; }
    }

    public sealed class BookingAssignmentResponse
    {
        public Guid BookingId { get; init; }
        public Guid TransportPlanId { get; init; }
        public Guid EmployeeId { get; init; }
        public required string EmployeeCode { get; init; }
        public required string EmployeeName { get; init; }
        public required string Role { get; init; }
        public DateTimeOffset AssignedAt { get; init; }
        public bool IsPrimary { get; init; }
        public string? Responsibility { get; init; }
        public string? Note { get; init; }
    }

}
