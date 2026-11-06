using Microsoft.EntityFrameworkCore;

namespace Pegaxus.Repository.Configurations;

/// <summary>Registers the native PostgreSQL enum types mapped to the CLR enums in <c>Pegaxus.Repository.Enum</c>.</summary>
public static class PostgresEnumConfiguration
{
    public static ModelBuilder HasPostgresEnums(this ModelBuilder modelBuilder)
    {
        modelBuilder
            .HasPostgresEnum("account_status", new[] { "ACTIVE", "INACTIVE", "SUSPENDED" })
            .HasPostgresEnum("account_type", new[] { "CUSTOMER", "EMPLOYEE" })
            .HasPostgresEnum("approval_decision", new[] { "APPROVED", "REJECTED" })
            .HasPostgresEnum("authority_submission_status", new[] { "DRAFT", "SUBMITTED", "PENDING", "APPROVED", "REJECTED", "RETURNED" })
            .HasPostgresEnum("authority_evidence_type", new[] { "Acceptance", "Rejection", "AdditionalInformation", "Other" })
            .HasPostgresEnum("audit_visibility", new[] { "Public", "Internal" })
            .HasPostgresEnum("booking_history_type", new[] { "SUBMIT", "APPROVE", "REJECT", "CANCEL" })
            .HasPostgresEnum("booking_status", new[] { "DRAFT", "PENDING", "APPROVED", "REJECTED", "CANCELLED" })
            .HasPostgresEnum("claim_resolution_type", new[] { "COMPENSATION", "PARTIAL_COMPENSATION", "REFUND", "SERVICE_RECOVERY", "NO_COMPENSATION", "OTHER" })
            .HasPostgresEnum("claim_status", new[] { "PENDING", "APPROVED", "REJECTED", "RESOLVED", "CANCELLED" })
            .HasPostgresEnum("claim_type", new[] { "HORSE_INJURY", "HORSE_HEALTH", "LOSS", "DAMAGE", "DELAY", "DOCUMENT_ISSUE", "SERVICE_ISSUE", "OTHER" })
            .HasPostgresEnum("compliance_document_status", new[] { "PROVIDED", "PENDING", "APPROVED", "REJECTED", "EXPIRED" })
            .HasPostgresEnum("compliance_dossier_status", new[] { "DRAFT", "IN_PREPARATION", "READY_FOR_SUBMISSION", "SUBMITTED", "PENDING", "APPROVED", "REJECTED", "CLOSED" })
            .HasPostgresEnum("compliance_requirement_status", new[] { "ACTIVE", "INACTIVE" })
            .HasPostgresEnum("compliance_requirement_category", new[] { "Quarantine", "Customs", "Transport", "HorseHealth", "HorseIdentity", "Other" })
            .HasPostgresEnum("customs_clearance_proof_status", new[] { "PendingVerification", "Cleared", "Held", "Rejected" })
            .HasPostgresEnum("customs_lane", new[] { "GreenLane", "OrangeLane", "RedLane" })
            .HasPostgresEnum("document_request_status", new[] { "OPEN", "FULFILLED", "CANCELLED" })
            .HasPostgresEnum("document_request_type", new[] { "MISSING_DOCUMENT", "REVISION", "REUPLOAD", "ADDITIONAL_INFORMATION" })
            .HasPostgresEnum("document_review_result", new[] { "APPROVED", "REJECTED", "REVISION_REQUIRED", "REVOKED" })
            .HasPostgresEnum("dossier_type", new[] { "MAIN", "EXTRA" })
            .HasPostgresEnum("dossier_requirement_status", new[] { "PENDING", "REQUESTED", "PROVIDED", "SATISFIED", "REJECTED", "WAIVED" })
            .HasPostgresEnum("dossier_template_status", new[] { "DRAFT", "ACTIVE", "INACTIVE" })
            .HasPostgresEnum("emergency_cost_category", new[] { "VETERINARY", "MEDICATION", "VEHICLE_REPAIR", "TOWING", "REPLACEMENT_VEHICLE", "EMERGENCY_STABLE", "ROUTE_CHANGE", "ACCOMMODATION", "OTHER" })
            .HasPostgresEnum("emergency_cost_status", new[] { "PENDING", "APPROVED", "REJECTED", "CANCELLED" })
            .HasPostgresEnum("emergency_receipt_status", new[] { "NOT_REQUIRED", "PENDING", "SUBMITTED", "APPROVED", "REJECTED" })
            .HasPostgresEnum("employee_role", new[] { "LOGISTICS_MANAGER", "TRANSPORT_SPECIALIST", "FLEET_ROUTE_COORDINATOR", "DRIVER", "ESCORT" })
            .HasPostgresEnum("handover_party_type", new[] { "CUSTOMER", "EMPLOYEE", "PROVIDER" })
            .HasPostgresEnum("handover_status", new[] { "PLANNED", "IN_PROGRESS", "COMPLETED", "CANCELLED" })
            .HasPostgresEnum("handover_type", new[] { "PICKUP", "TRANSFER", "BORDER_HANDOVER", "CARRIER_HANDOVER", "DELIVERY", "OTHER" })
            .HasPostgresEnum("horse_health_log_type", new[] { "PRE_TRIP_CHECK", "ROUTINE_CHECK", "BORDER_CHECK", "REST_STOP_CHECK", "INCIDENT_FOLLOWUP", "POST_TRIP_CHECK", "OTHER" })
            .HasPostgresEnum("horse_health_status", new[] { "NORMAL", "OBSERVATION_REQUIRED", "AT_RISK", "CRITICAL" })
            .HasPostgresEnum("horse_incident_type", new[] { "INJURY", "DEHYDRATION", "RESPIRATORY_ISSUE", "FEVER", "STRESS", "COLIC", "FATIGUE", "OTHER" })
            .HasPostgresEnum("horse_sex", new[] { "MALE", "FEMALE", "GELDING" })
            .HasPostgresEnum("hydration_status", new[] { "NORMAL", "MILD_DEHYDRATION", "MODERATE_DEHYDRATION", "SEVERE_DEHYDRATION" })
            .HasPostgresEnum("incident_category", new[] { "VEHICLE", "HORSE_HEALTH", "BOTH", "ROUTE_CHANGE" })
            .HasPostgresEnum("incident_severity", new[] { "LOW", "MEDIUM", "HIGH", "CRITICAL" })
            .HasPostgresEnum("incident_status", new[] { "REPORTED", "PENDING", "RESOLVED", "CANCELLED" })
            .HasPostgresEnum("invoice_history_type", new[] { "CREATE", "ISSUE", "RECORD_PAYMENT", "MARK_OVERDUE", "CANCEL" })
            .HasPostgresEnum("invoice_item_type", new[] { "TRANSPORT_FEE", "HORSE_SERVICE_FEE", "HANDLING_FEE", "COMPLIANCE_FEE", "EMERGENCY_COST", "OTHER" })
            .HasPostgresEnum("invoice_type", new[] { "QUOTATION", "DEPOSIT", "FINAL", "SUPPLEMENTARY", "CREDIT_NOTE" })
            .HasPostgresEnum("invoice_status", new[] { "DRAFT", "ISSUED", "PARTIALLY_PAID", "PAID", "OVERDUE", "CANCELLED" })
            .HasPostgresEnum("location_type", new[] { "CITY", "BORDER_GATE", "AIRPORT", "SEAPORT", "STABLE", "REST_STOP", "OTHER" })
            .HasPostgresEnum("notification_priority", new[] { "LOW", "NORMAL", "HIGH", "URGENT" })
            .HasPostgresEnum("notification_type", new[] { "BOOKING", "TRANSPORT", "ROUTE", "HANDOVER", "HORSE_HEALTH", "INCIDENT", "EMERGENCY_COST", "PAYMENT", "CLAIM", "COMPLIANCE", "DOCUMENT", "SYSTEM" })
            .HasPostgresEnum("payment_method", new[] { "CASH", "BANK_TRANSFER", "OTHER" })
            .HasPostgresEnum("provider_status", new[] { "ACTIVE", "INACTIVE", "SUSPENDED" })
            .HasPostgresEnum("quarantine_proof_status", new[] { "PendingVerification", "Passed", "PassedWithConditions", "Holdback", "Failed", "Rejected" })
            .HasPostgresEnum("receipt_status", new[] { "PENDING", "CONFIRMED", "FAILED", "REFUNDED" })
            .HasPostgresEnum("recipient_status", new[] { "PENDING", "DELIVERED", "READ", "FAILED" })
            .HasPostgresEnum("route_plan_status", new[] { "DRAFT", "CONFIRMED", "ACTIVE", "COMPLETED", "CANCELLED", "PENDING_LEGAL_REVIEW", "PENDING_MANAGER_APPROVAL", "REJECTED_BY_MANAGER", "APPROVED" })
            .HasPostgresEnum("route_segment_status", new[] { "PLANNED", "IN_TRANSIT", "COMPLETED" })
            .HasPostgresEnum("route_type", new[] { "PRIMARY", "BACKUP", "RETURN" })
            .HasPostgresEnum("transport_mode", new[] { "ROAD", "SEA", "AIR" })
            .HasPostgresEnum("transport_plan_status", new[] { "DRAFT", "PENDING", "APPROVED", "IN_PROGRESS", "COMPLETED", "CANCELLED" })
            .HasPostgresEnum("transport_status", new[] { "CREATED", "PLANNING", "READY", "IN_TRANSIT", "COMPLETED", "CANCELLED" })
            .HasPostgresEnum("vehicle_incident_type", new[] { "BREAKDOWN", "ACCIDENT", "TIRE_DAMAGE", "ENGINE_FAILURE", "TEMPERATURE_CONTROL_FAILURE", "EQUIPMENT_FAILURE", "OTHER" })
            .HasPostgresEnum("vehicle_status", new[] { "AVAILABLE", "ASSIGNED", "IN_USE", "MAINTENANCE", "INACTIVE", "SUSPENDED", "OUT_OF_SERVICE" });

        return modelBuilder;
    }
}
