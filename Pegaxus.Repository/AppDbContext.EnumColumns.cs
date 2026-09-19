using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Entities;

namespace Pegaxus.Repository;

public partial class AppDbContext
{
    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
        Configure<Account>(modelBuilder, "account_type", "account_type", nameof(Account.AccountType), "Account_account_type_idx");
        Configure<Account>(modelBuilder, "status", "account_status", nameof(Account.Status), "Account_status_idx", "ACTIVE");
        Configure<AccountNotification>(modelBuilder, "status", "recipient_status", nameof(AccountNotification.Status), "AccountNotification_status_idx", "PENDING");
        Configure<AuthoritySubmission>(modelBuilder, "status", "authority_submission_status", nameof(AuthoritySubmission.Status), "AuthoritySubmission_status_idx", "DRAFT");
        Configure<Booking>(modelBuilder, "status", "booking_status", nameof(Booking.Status), "Booking_status_idx", "PENDING");
        Configure<BookingStatusHistory>(modelBuilder, "type", "booking_history_type", nameof(BookingStatusHistory.Type), "BookingStatusHistory_type_idx");
        Configure<BookingStatusHistory>(modelBuilder, "previous_status", "booking_status", nameof(BookingStatusHistory.PreviousStatus));
        Configure<BookingStatusHistory>(modelBuilder, "new_status", "booking_status", nameof(BookingStatusHistory.NewStatus));
        Configure<Claim>(modelBuilder, "claim_type", "claim_type", nameof(Claim.ClaimType), "Claim_claim_type_idx");
        Configure<Claim>(modelBuilder, "status", "claim_status", nameof(Claim.Status), "Claim_status_idx", "PENDING");
        Configure<ClaimResolution>(modelBuilder, "resolution_type", "claim_resolution_type", nameof(ClaimResolution.ResolutionType), "ClaimResolution_resolution_type_idx");
        Configure<ComplianceDocument>(modelBuilder, "status", "compliance_document_status", nameof(ComplianceDocument.Status), "ComplianceDocument_status_idx", "PROVIDED");
        Configure<ComplianceDossier>(modelBuilder, "status", "compliance_dossier_status", nameof(ComplianceDossier.Status), "ComplianceDossier_status_idx", "DRAFT");
        Configure<ComplianceRequirement>(modelBuilder, "status", "compliance_requirement_status", nameof(ComplianceRequirement.Status), "ComplianceRequirement_status_idx", "ACTIVE");
        Configure<CostApprovalDecision>(modelBuilder, "decision", "approval_decision", nameof(CostApprovalDecision.Decision), "CostApprovalDecision_decision_idx");
        Configure<DocumentRequest>(modelBuilder, "request_type", "document_request_type", nameof(DocumentRequest.RequestType), "DocumentRequest_request_type_idx");
        Configure<DocumentRequest>(modelBuilder, "status", "document_request_status", nameof(DocumentRequest.Status), "DocumentRequest_status_idx", "OPEN");
        Configure<DocumentReview>(modelBuilder, "result", "document_review_result", nameof(DocumentReview.Result), "DocumentReview_result_idx");
        Configure<DossierRequirement>(modelBuilder, "status", "dossier_requirement_status", nameof(DossierRequirement.Status), "DossierRequirement_status_idx", "PENDING");
        Configure<DossierTemplate>(modelBuilder, "status", "dossier_template_status", nameof(DossierTemplate.Status), "DossierTemplate_status_idx", "DRAFT");
        Configure<EmergencyCostRequest>(modelBuilder, "category", "emergency_cost_category", nameof(EmergencyCostRequest.Category), "EmergencyCostRequest_category_idx");
        Configure<EmergencyCostRequest>(modelBuilder, "status", "emergency_cost_status", nameof(EmergencyCostRequest.Status), "EmergencyCostRequest_status_idx", "PENDING");
        Configure<Employee>(modelBuilder, "role", "employee_role", nameof(Employee.Role), "Employee_role_idx");
        Configure<HandoverRecord>(modelBuilder, "handover_type", "handover_type", nameof(HandoverRecord.HandoverType));
        Configure<HandoverRecord>(modelBuilder, "status", "handover_status", nameof(HandoverRecord.Status), "HandoverRecord_status_idx", "PLANNED");
        Configure<HandoverRecord>(modelBuilder, "from_type", "handover_party_type", nameof(HandoverRecord.FromType), "HandoverRecord_from_type_idx");
        Configure<HandoverRecord>(modelBuilder, "to_type", "handover_party_type", nameof(HandoverRecord.ToType), "HandoverRecord_to_type_idx");
        Configure<Horse>(modelBuilder, "sex", "horse_sex", nameof(Horse.Sex));
        Configure<HorseHealthIncidentDetail>(modelBuilder, "incident_type", "horse_incident_type", nameof(HorseHealthIncidentDetail.IncidentType), "HorseHealthIncidentDetail_incident_type_idx");
        Configure<HorseHealthLog>(modelBuilder, "log_type", "horse_health_log_type", nameof(HorseHealthLog.LogType));
        Configure<HorseHealthLog>(modelBuilder, "health_status", "horse_health_status", nameof(HorseHealthLog.HealthStatus), "HorseHealthLog_health_status_idx");
        Configure<HorseHealthLog>(modelBuilder, "hydration_status", "hydration_status", nameof(HorseHealthLog.HydrationStatus));
        Configure<IncidentReport>(modelBuilder, "category", "incident_category", nameof(IncidentReport.Category), "IncidentReport_category_idx");
        Configure<IncidentReport>(modelBuilder, "severity", "incident_severity", nameof(IncidentReport.Severity), "IncidentReport_severity_idx");
        Configure<IncidentReport>(modelBuilder, "status", "incident_status", nameof(IncidentReport.Status), "IncidentReport_status_idx", "REPORTED");
        Configure<Invoice>(modelBuilder, "status", "invoice_status", nameof(Invoice.Status), "Invoice_status_idx", "DRAFT");
        Configure<InvoiceItem>(modelBuilder, "item_type", "invoice_item_type", nameof(InvoiceItem.ItemType), "InvoiceItem_item_type_idx");
        Configure<InvoiceStatusHistory>(modelBuilder, "type", "invoice_history_type", nameof(InvoiceStatusHistory.Type), "InvoiceStatusHistory_type_idx");
        Configure<InvoiceStatusHistory>(modelBuilder, "previous_status", "invoice_status", nameof(InvoiceStatusHistory.PreviousStatus));
        Configure<InvoiceStatusHistory>(modelBuilder, "new_status", "invoice_status", nameof(InvoiceStatusHistory.NewStatus), "InvoiceStatusHistory_new_status_idx");
        Configure<Location>(modelBuilder, "location_type", "location_type", nameof(Location.LocationType), "Location_location_type_idx");
        Configure<Notification>(modelBuilder, "notification_type", "notification_type", nameof(Notification.NotificationType), "Notification_notification_type_idx");
        Configure<Notification>(modelBuilder, "priority", "notification_priority", nameof(Notification.Priority), "Notification_priority_idx", "NORMAL");
        Configure<Receipt>(modelBuilder, "payment_method", "payment_method", nameof(Receipt.PaymentMethod));
        Configure<Receipt>(modelBuilder, "status", "receipt_status", nameof(Receipt.Status), "Receipt_status_idx", "PENDING");
        Configure<Route>(modelBuilder, "transport_mode", "transport_mode", nameof(Route.TransportMode), "Route_transport_mode_idx");
        Configure<RoutePlan>(modelBuilder, "status", "route_plan_status", nameof(RoutePlan.Status), "RoutePlan_status_idx", "DRAFT");
        Configure<RoutePlanRoute>(modelBuilder, "route_type", "route_type", nameof(RoutePlanRoute.RouteType), "RoutePlanRoute_route_type_idx", "PRIMARY");
        Configure<Transport>(modelBuilder, "status", "transport_status", nameof(Transport.Status), "Transport_status_idx", "CREATED");
        Configure<TransportPlan>(modelBuilder, "status", "transport_plan_status", nameof(TransportPlan.Status), "TransportPlan_status_idx", "DRAFT");
        Configure<TransportProvider>(modelBuilder, "status", "provider_status", nameof(TransportProvider.Status), "TransportProvider_status_idx", "ACTIVE");
        Configure<Vehicle>(modelBuilder, "transport_mode", "transport_mode", nameof(Vehicle.TransportMode), "Vehicle_transport_mode_idx");
        Configure<Vehicle>(modelBuilder, "status", "vehicle_status", nameof(Vehicle.Status), "Vehicle_status_idx", "AVAILABLE");
        Configure<VehicleIncidentDetail>(modelBuilder, "incident_type", "vehicle_incident_type", nameof(VehicleIncidentDetail.IncidentType), "VehicleIncidentDetail_incident_type_idx");
    }

    private static void Configure<TEntity>(
        ModelBuilder modelBuilder,
        string columnName,
        string columnType,
        string propertyName,
        string? indexName = null,
        string? defaultValue = null) where TEntity : class
    {
        var entity = modelBuilder.Entity<TEntity>();
        var property = entity.Property(propertyName)
            .HasColumnName(columnName)
            .HasColumnType(columnType);

        if (defaultValue is not null)
            property
                .HasDefaultValueSql($"'{defaultValue}'::{columnType}")
                .HasSentinel(Activator.CreateInstance(property.Metadata.ClrType));

        if (indexName is not null)
            entity.HasIndex(propertyName).HasDatabaseName(indexName);
    }
}
