namespace Pegaxus.Repository.Entities;

public partial class Account
{
    public AccountType AccountType { get; set; }
    public AccountStatus Status { get; set; } = AccountStatus.ACTIVE;
}

public partial class AccountNotification { public RecipientStatus Status { get; set; } = RecipientStatus.PENDING; }
public partial class AuthoritySubmission { public AuthoritySubmissionStatus Status { get; set; } = AuthoritySubmissionStatus.DRAFT; }
public partial class Booking { public BookingStatus Status { get; set; } = BookingStatus.PENDING; }

public partial class BookingStatusHistory
{
    public BookingHistoryType Type { get; set; }
    public BookingStatus? PreviousStatus { get; set; }
    public BookingStatus NewStatus { get; set; }
}

public partial class Claim
{
    public ClaimType ClaimType { get; set; }
    public ClaimStatus Status { get; set; } = ClaimStatus.PENDING;
}

public partial class ClaimResolution { public ClaimResolutionType ResolutionType { get; set; } }
public partial class ComplianceDocument { public ComplianceDocumentStatus Status { get; set; } = ComplianceDocumentStatus.PROVIDED; }
public partial class ComplianceDossier { public ComplianceDossierStatus Status { get; set; } = ComplianceDossierStatus.DRAFT; }
public partial class ComplianceRequirement { public ComplianceRequirementStatus Status { get; set; } = ComplianceRequirementStatus.ACTIVE; }
public partial class CostApprovalDecision { public ApprovalDecision Decision { get; set; } }

public partial class DocumentRequest
{
    public DocumentRequestType RequestType { get; set; }
    public DocumentRequestStatus Status { get; set; } = DocumentRequestStatus.OPEN;
}

public partial class DocumentReview { public DocumentReviewResult Result { get; set; } }
public partial class DossierRequirement { public DossierRequirementStatus Status { get; set; } = DossierRequirementStatus.PENDING; }
public partial class DossierTemplate { public DossierTemplateStatus Status { get; set; } = DossierTemplateStatus.DRAFT; }

public partial class EmergencyCostRequest
{
    public EmergencyCostCategory Category { get; set; }
    public EmergencyCostStatus Status { get; set; } = EmergencyCostStatus.PENDING;
}

public partial class Employee { public EmployeeRole Role { get; set; } }

public partial class HandoverRecord
{
    public HandoverType HandoverType { get; set; }
    public HandoverStatus Status { get; set; } = HandoverStatus.PLANNED;
    public HandoverPartyType FromType { get; set; }
    public HandoverPartyType ToType { get; set; }
}

public partial class Horse { public HorseSex? Sex { get; set; } }
public partial class HorseHealthIncidentDetail { public HorseIncidentType IncidentType { get; set; } }

public partial class HorseHealthLog
{
    public HorseHealthLogType LogType { get; set; }
    public HorseHealthStatus HealthStatus { get; set; }
    public HydrationStatus? HydrationStatus { get; set; }
}

public partial class IncidentReport
{
    public IncidentCategory Category { get; set; }
    public IncidentSeverity Severity { get; set; }
    public IncidentStatus Status { get; set; } = IncidentStatus.REPORTED;
}

public partial class Invoice { public InvoiceStatus Status { get; set; } = InvoiceStatus.DRAFT; }
public partial class InvoiceItem { public InvoiceItemType ItemType { get; set; } }

public partial class InvoiceStatusHistory
{
    public InvoiceHistoryType Type { get; set; }
    public InvoiceStatus? PreviousStatus { get; set; }
    public InvoiceStatus NewStatus { get; set; }
}

public partial class Location { public LocationType? LocationType { get; set; } }

public partial class Notification
{
    public NotificationType NotificationType { get; set; }
    public NotificationPriority Priority { get; set; } = NotificationPriority.NORMAL;
}

public partial class Receipt
{
    public PaymentMethod PaymentMethod { get; set; }
    public ReceiptStatus Status { get; set; } = ReceiptStatus.PENDING;
}

public partial class Route { public TransportMode TransportMode { get; set; } }
public partial class RoutePlan { public RoutePlanStatus Status { get; set; } = RoutePlanStatus.DRAFT; }
public partial class RoutePlanRoute { public RouteType RouteType { get; set; } = RouteType.PRIMARY; }
public partial class Transport { public TransportStatus Status { get; set; } = TransportStatus.CREATED; }
public partial class TransportPlan { public TransportPlanStatus Status { get; set; } = TransportPlanStatus.DRAFT; }
public partial class TransportProvider { public ProviderStatus Status { get; set; } = ProviderStatus.ACTIVE; }

public partial class Vehicle
{
    public TransportMode TransportMode { get; set; }
    public VehicleStatus Status { get; set; } = VehicleStatus.AVAILABLE;
}

public partial class VehicleIncidentDetail { public VehicleIncidentType IncidentType { get; set; } }
