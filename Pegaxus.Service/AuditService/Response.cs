using System.Text.Json.Serialization;

namespace Pegaxus.Service.AuditService;

public static class Response
{
    // ------------------------------------------------------------------ AU1

    public sealed record ArchiveHorse(Guid HorseId, string Name, string? MicrochipNumber, string? PassportNumber);

    public sealed record ArchiveItem(Guid TransportId, string TransportCode, string Status, Guid BookingId, string BookingCode, string CustomerName,
        string OriginCountry, string DestinationCountry, IReadOnlyList<ArchiveHorse> Horses, DateTimeOffset? CompletedAt, int ClaimCount, int IncidentCount);

    public sealed record ArchiveResponse(IReadOnlyList<ArchiveItem> Items, int PageIndex, int PageSize, int TotalItems, int TotalPages);

    // ------------------------------------------------------------------ AU2

    public sealed record TransportBlock(Guid TransportId, string TransportCode, string Status, DateTimeOffset? CompletedAt, DateTimeOffset? ActualDepartureAt, DateTimeOffset? ActualArrivalAt);

    public sealed record CustomerRef(Guid CustomerId, string Name);
    public sealed record InvoiceRef(Guid InvoiceId, string InvoiceNumber, string Type, string Status, long TotalAmount, string Currency);
    public sealed record ClaimRef(Guid ClaimId, string ClaimCode, string Status);
    public sealed record BookingBlock(Guid BookingId, string BookingCode, string Status, CustomerRef Customer, string OriginCountry, string DestinationCountry,
        IReadOnlyList<InvoiceRef> Invoices, IReadOnlyList<ClaimRef> Claims);

    public sealed record HealthLogItem(Guid HealthLogId, DateTimeOffset CheckedAt, string LogType, string HealthStatus, decimal? TemperatureCelsius, int? HeartRateBpm,
        int? RespiratoryRateBpm, string? SymptomNote, string? InjuryNote, string? TreatmentProvided, string? MedicationGiven, bool VeterinarianRequired,
        string? VeterinarianNote, string? GeneralNote, int PhotoCount);
    public sealed record HorseItem(Guid HorseId, string Name, string? MicrochipNumber, string? PassportNumber, decimal? WeightKg, decimal? HeightCm, IReadOnlyList<HealthLogItem> HealthLogs);

    /// <summary>A leg of a route plan. For a customer the planned times, partner, cost, vehicle and staff are null: they only see what really happened.</summary>
    public sealed record SegmentItem(Guid RoutePlanRouteId, int SequenceNo, string RouteType, string Status, string From, string To, string TransportMode, string ExecutedBy,
        string? ProviderName, decimal? OutsourcingCost, string? OutsourcingCurrency, string? VehicleCode, string? LicensePlate, string? DriverName, string? EscortName,
        DateTimeOffset? PlannedDepartureAt, DateTimeOffset? PlannedArrivalAt, DateTimeOffset? ActualDepartureAt, DateTimeOffset? ActualArrivalAt, DateTimeOffset? SupersededAt);
    public sealed record RoutePlanItem(Guid RoutePlanId, string RoutePlanCode, string Status, decimal? TotalDistanceKm, int? EstimatedDurationMinutes, IReadOnlyList<SegmentItem> Segments);
    public sealed record RoutingBlock(RoutePlanItem? Primary, IReadOnlyList<RoutePlanItem> Alternatives);

    public sealed record ReviewItem(string Result, string? ReviewComment, DateTimeOffset ReviewedAt);
    public sealed record VersionItem(Guid DocumentVersionId, int VersionNo, string FileName, DateTimeOffset UploadedAt, IReadOnlyList<ReviewItem>? Reviews);
    public sealed record DocumentItem(Guid ComplianceDocumentId, string DocumentCode, string DocumentName, string Status, string? DocumentNumber, string? IssuingAuthority,
        DateOnly? IssuedDate, DateOnly? ExpiryDate, IReadOnlyList<VersionItem> Versions);
    public sealed record RequirementItem(Guid DossierRequirementId, string RequirementName, Guid? HorseId, string Status, bool IsMandatory, DocumentItem? Document);
    public sealed record QuarantineItem(Guid QuarantineProofId, string Status, string BcpName, DateTimeOffset? InspectionAt, DateTimeOffset? VerifiedAt,
        string? OfficialVeterinarianName, string? Conditions, string? VerificationNote);
    public sealed record CustomsItem(Guid CustomsClearanceProofId, string Status, string CustomsPortName, string? MrnNumber, DateTimeOffset? ClearanceAt, DateTimeOffset? VerifiedAt,
        string? VerificationNote);
    public sealed record DossierItem(Guid ComplianceDossierId, string DossierCode, string DossierType, string Status, DateTimeOffset OpenedAt, DateTimeOffset? ApprovedAt,
        IReadOnlyList<RequirementItem> Requirements, QuarantineItem? QuarantineProof, CustomsItem? CustomsClearanceProof);
    public sealed record ComplianceBlock(IReadOnlyList<DossierItem> Dossiers);

    public sealed record HandoverItem(Guid HandoverRecordId, string HandoverCode, string HandoverType, string Status, DateTimeOffset? CompletedAt);
    public sealed record IncidentItem(Guid IncidentReportId, string IncidentCode, string Category, string Severity, string Status, DateTimeOffset OccurredAt);
    public sealed record AuditDossierItem(Guid AuditDossierId, string ActionType, string TargetEntity, string? Summary, string Visibility, DateTimeOffset CreatedAt);
    public sealed record OperationsBlock(IReadOnlyList<HandoverItem> Handovers, IReadOnlyList<IncidentItem> Incidents, IReadOnlyList<AuditDossierItem> AuditDossiers);

    public sealed record ReceiptItem(Guid ReceiptId, string ReceiptCode, string Status, string PaymentMethod, long Amount, DateTimeOffset? PaidAt, DateTimeOffset? ConfirmedAt);
    public sealed record FinanceInvoice(Guid InvoiceId, string InvoiceNumber, string Type, string Status, long TotalAmount, string Currency, DateTimeOffset? IssuedAt,
        DateTimeOffset? DueDate, DateTimeOffset? PaidAt, IReadOnlyList<ReceiptItem> Receipts);
    public sealed record FinanceReport(string ReportCode, long TotalInvoicedAmount, long TotalPaidAmount, long TotalEmergencyCost, long OutstandingAmount, string? SummaryNote, DateTimeOffset GeneratedAt);
    public sealed record EmergencyCostItem(string RequestCode, string? IncidentCode, string Category, long ApprovedAmount);
    public sealed record FinanceBlock(IReadOnlyList<FinanceInvoice> Invoices, FinanceReport? FinancialReport, IReadOnlyList<EmergencyCostItem> EmergencyCosts);

    public sealed record TimelineItem(DateTimeOffset At, string Source, string Title, Guid? RefId, string Visibility);

    /// <summary>Only the blocks that were asked for (and that the role may see) are written; the others are left out of the JSON.</summary>
    public sealed record AuditResponse(string ViewMode, DateTimeOffset GeneratedAt, TransportBlock Transport,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] BookingBlock? Booking,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<HorseItem>? Horses,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] RoutingBlock? Routing,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ComplianceBlock? Compliance,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] OperationsBlock? Operations,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] FinanceBlock? Finance,
        IReadOnlyList<TimelineItem> Timeline);

    // ------------------------------------------------------------------ AU3, AU4

    public sealed record ExportQueued(Guid ExportId, Guid TransportId, string Format, string Mode, string Status, DateTimeOffset RequestedAt);

    public sealed record ExportStatus(Guid ExportId, Guid TransportId, string TransportCode, string Format, string Mode, string Status, string? Delivery, string? DownloadUrl,
        DateTimeOffset? DownloadUrlExpiresAt, string? FileName, long? SizeBytes, string? Sha256, string? ErrorMessage, DateTimeOffset RequestedAt, DateTimeOffset? CompletedAt);
}
