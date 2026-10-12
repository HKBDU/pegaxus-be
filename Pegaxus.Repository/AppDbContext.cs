using System;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Entities;

namespace Pegaxus.Repository;

public partial class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Account> Accounts { get; set; }

    public virtual DbSet<AccountNotification> AccountNotifications { get; set; }

    public virtual DbSet<AuthoritySubmission> AuthoritySubmissions { get; set; }

    public virtual DbSet<AuthoritySubmissionEvidenceFile> AuthoritySubmissionEvidenceFiles { get; set; }

    public virtual DbSet<Booking> Bookings { get; set; }
    public virtual DbSet<BookingHorse> BookingHorses { get; set; }

    public virtual DbSet<BookingStatusHistory> BookingStatusHistories { get; set; }

    public virtual DbSet<Claim> Claims { get; set; }

    public virtual DbSet<ClaimResolution> ClaimResolutions { get; set; }

    public virtual DbSet<ComplianceDocument> ComplianceDocuments { get; set; }

    public virtual DbSet<ComplianceDossier> ComplianceDossiers { get; set; }

    public virtual DbSet<ComplianceRequirement> ComplianceRequirements { get; set; }

    public virtual DbSet<CustomsClearanceProof> CustomsClearanceProofs { get; set; }

    public virtual DbSet<CostApprovalDecision> CostApprovalDecisions { get; set; }

    public virtual DbSet<Customer> Customers { get; set; }

    public virtual DbSet<DocumentRequest> DocumentRequests { get; set; }
    public virtual DbSet<DocumentRequestComment> DocumentRequestComments { get; set; }
    public virtual DbSet<MissingDocumentReminder> MissingDocumentReminders { get; set; }

    public virtual DbSet<DocumentReview> DocumentReviews { get; set; }

    public virtual DbSet<DocumentAttachment> DocumentAttachments { get; set; }

    public virtual DbSet<DocumentVersion> DocumentVersions { get; set; }

    public virtual DbSet<AuditDossier> AuditDossiers { get; set; }

    public virtual DbSet<CatalogAuditLog> CatalogAuditLogs { get; set; }

    public virtual DbSet<DossierRequirement> DossierRequirements { get; set; }

    public virtual DbSet<ProofAttachment> ProofAttachments { get; set; }

    public virtual DbSet<QuarantineProof> QuarantineProofs { get; set; }

    public virtual DbSet<DossierTemplate> DossierTemplates { get; set; }

    public virtual DbSet<DossierTemplateItem> DossierTemplateItems { get; set; }

    public virtual DbSet<EmergencyCostRequest> EmergencyCostRequests { get; set; }

    public virtual DbSet<Employee> Employees { get; set; }

    public virtual DbSet<EmployeeTransportPlan> EmployeeTransportPlans { get; set; }

    public virtual DbSet<FinancialReport> FinancialReports { get; set; }

    public virtual DbSet<HandoverRecord> HandoverRecords { get; set; }

    public virtual DbSet<Horse> Horses { get; set; }

    public virtual DbSet<HorseHealthIncidentDetail> HorseHealthIncidentDetails { get; set; }

    public virtual DbSet<HorseHealthLog> HorseHealthLogs { get; set; }

    public virtual DbSet<IncidentReport> IncidentReports { get; set; }

    public virtual DbSet<Invoice> Invoices { get; set; }

    public virtual DbSet<InvoiceItem> InvoiceItems { get; set; }

    public virtual DbSet<InvoiceStatusHistory> InvoiceStatusHistories { get; set; }

    public virtual DbSet<Location> Locations { get; set; }

    public virtual DbSet<Notification> Notifications { get; set; }

    public virtual DbSet<OutboxMessage> OutboxMessages { get; set; }

    public virtual DbSet<Receipt> Receipts { get; set; }

    public virtual DbSet<RefreshToken> RefreshTokens { get; set; }

    public virtual DbSet<Route> Routes { get; set; }

    public virtual DbSet<RoutePlan> RoutePlans { get; set; }

    public virtual DbSet<RoutePlanRoute> RoutePlanRoutes { get; set; }

    public virtual DbSet<RoutePlanRouteVehicle> RoutePlanRouteVehicles { get; set; }

    public virtual DbSet<Transport> Transports { get; set; }

    public virtual DbSet<TransportPlan> TransportPlans { get; set; }

    public virtual DbSet<TransportPlanProvider> TransportPlanProviders { get; set; }

    public virtual DbSet<TransportProvider> TransportProviders { get; set; }

    public virtual DbSet<Vehicle> Vehicles { get; set; }

    public virtual DbSet<VehicleIncidentDetail> VehicleIncidentDetails { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
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
            .HasPostgresEnum("dossier_requirement_status", new[] { "PENDING", "REQUESTED", "PROVIDED", "SATISFIED", "REJECTED", "WAIVED" })
            .HasPostgresEnum("dossier_template_status", new[] { "DRAFT", "ACTIVE", "INACTIVE" })
            .HasPostgresEnum("emergency_cost_category", new[] { "VETERINARY", "MEDICATION", "VEHICLE_REPAIR", "TOWING", "REPLACEMENT_VEHICLE", "EMERGENCY_STABLE", "ROUTE_CHANGE", "ACCOMMODATION", "OTHER" })
            .HasPostgresEnum("emergency_cost_status", new[] { "PENDING", "APPROVED", "REJECTED", "CANCELLED" })
            .HasPostgresEnum("employee_role", new[] { "LOGISTICS_MANAGER", "TRANSPORT_SPECIALIST", "FLEET_ROUTE_COORDINATOR", "DRIVER", "ESCORT" })
            .HasPostgresEnum("handover_party_type", new[] { "CUSTOMER", "EMPLOYEE" })
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
            .HasPostgresEnum("route_type", new[] { "PRIMARY", "BACKUP", "RETURN" })
            .HasPostgresEnum("transport_mode", new[] { "ROAD", "SEA", "AIR" })
            .HasPostgresEnum("transport_plan_status", new[] { "DRAFT", "PENDING", "APPROVED", "IN_PROGRESS", "COMPLETED", "CANCELLED" })
            .HasPostgresEnum("transport_status", new[] { "CREATED", "PLANNING", "READY", "IN_TRANSIT", "COMPLETED", "CANCELLED" })
            .HasPostgresEnum("vehicle_incident_type", new[] { "BREAKDOWN", "ACCIDENT", "TIRE_DAMAGE", "ENGINE_FAILURE", "TEMPERATURE_CONTROL_FAILURE", "EQUIPMENT_FAILURE", "OTHER" })
            .HasPostgresEnum("vehicle_status", new[] { "AVAILABLE", "ASSIGNED", "IN_USE", "MAINTENANCE", "INACTIVE", "SUSPENDED", "OUT_OF_SERVICE" });

        modelBuilder.Entity<Account>(entity =>
        {
            entity.HasKey(e => e.AccountId).HasName("Account_pkey");

            entity.ToTable("Account");

            entity.HasIndex(e => e.Email, "Account_email_key").IsUnique();

            entity.HasIndex(e => e.IsDeleted, "Account_is_deleted_idx");

            entity.Property(e => e.AccountId).HasColumnName("account_id");
            entity.Property(e => e.AvatarUrl)
                .HasMaxLength(500)
                .HasColumnName("avatar_url");
            entity.Property(e => e.CreatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.DeletedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("deleted_at");
            entity.Property(e => e.DeletedByAccountId).HasColumnName("deleted_by_account_id");
            entity.Property(e => e.Email)
                .HasMaxLength(255)
                .HasColumnName("email");
            entity.Property(e => e.EmailVerifiedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("email_verified_at");
            entity.Property(e => e.IsDeleted)
                .HasDefaultValue(false)
                .HasColumnName("is_deleted");
            entity.Property(e => e.LastLoginAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("last_login_at");
            entity.Property(e => e.PasswordHash)
                .HasMaxLength(255)
                .HasColumnName("password_hash");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.DeletedByAccount).WithMany(p => p.InverseDeletedByAccount)
                .HasForeignKey(d => d.DeletedByAccountId)
                .HasConstraintName("Account_deleted_by_account_id_fkey");
        });

        modelBuilder.Entity<AccountNotification>(entity =>
        {
            entity.HasKey(e => new { e.AccountId, e.NotificationId }).HasName("AccountNotification_pkey");

            entity.ToTable("AccountNotification");

            entity.HasIndex(e => e.NotificationId, "AccountNotification_notification_id_idx");

            entity.HasIndex(e => e.ReadAt, "AccountNotification_read_at_idx");

            entity.Property(e => e.AccountId).HasColumnName("account_id");
            entity.Property(e => e.NotificationId).HasColumnName("notification_id");
            entity.Property(e => e.CreatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.DeliveredAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("delivered_at");
            entity.Property(e => e.ReadAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("read_at");

            entity.HasOne(d => d.Account).WithMany(p => p.AccountNotifications)
                .HasForeignKey(d => d.AccountId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("AccountNotification_account_id_fkey");

            entity.HasOne(d => d.Notification).WithMany(p => p.AccountNotifications)
                .HasForeignKey(d => d.NotificationId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("AccountNotification_notification_id_fkey");
        });

        modelBuilder.Entity<AuthoritySubmission>(entity =>
        {
            entity.HasKey(e => e.AuthoritySubmissionId).HasName("AuthoritySubmission_pkey");

            entity.ToTable("AuthoritySubmission", tb => tb.HasComment("AuthoritySubmission: Một lần thực tế gửi ComplianceDossier tới cơ quan có thẩm quyền. Một dossier có thể được gửi nhiều lần nếu bị trả về hoặc từ chối."));

            entity.HasIndex(e => e.ComplianceDossierId, "AuthoritySubmission_compliance_dossier_id_idx");

            entity.HasIndex(e => new { e.ComplianceDossierId, e.SubmissionNo }, "AuthoritySubmission_compliance_dossier_id_submission_no_idx").IsUnique();

            entity.HasIndex(e => e.SubmissionReference, "AuthoritySubmission_submission_reference_idx");

            entity.Property(e => e.AuthoritySubmissionId).HasColumnName("authority_submission_id");
            entity.Property(e => e.AuthorityName)
                .HasMaxLength(200)
                .HasColumnName("authority_name");
            entity.Property(e => e.ComplianceDossierId).HasColumnName("compliance_dossier_id");
            entity.Property(e => e.SubmittedByAccountId).HasColumnName("submitted_by_account_id");
            entity.Property(e => e.CreatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.DecisionNote).HasColumnName("decision_note");
            entity.Property(e => e.RejectionReason).HasColumnName("rejection_reason");
            entity.Property(e => e.ResponseReceivedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("response_received_at");
            entity.Property(e => e.SubmissionMethod)
                .HasMaxLength(100)
                .HasColumnName("submission_method");
            entity.Property(e => e.SubmissionNo).HasColumnName("submission_no");
            entity.Property(e => e.SubmissionReference)
                .HasMaxLength(150)
                .HasColumnName("submission_reference");
            entity.Property(e => e.IsRequired).HasDefaultValue(true).HasColumnName("is_required");
            entity.Property(e => e.PermitNumber).HasMaxLength(100).HasColumnName("permit_number");
            entity.Property(e => e.PermitIssuedDate).HasColumnName("permit_issued_date");
            entity.Property(e => e.PermitExpiryDate).HasColumnName("permit_expiry_date");
            entity.Property(e => e.RevisionDueDate).HasColumnName("revision_due_date");
            entity.Property(e => e.SubmittedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("submitted_at");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.ComplianceDossier).WithMany(p => p.AuthoritySubmissions)
                .HasForeignKey(d => d.ComplianceDossierId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("AuthoritySubmission_compliance_dossier_id_fkey");
            entity.HasOne(d => d.SubmittedByAccount).WithMany()
                .HasForeignKey(d => d.SubmittedByAccountId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("AuthoritySubmission_submitted_by_account_id_fkey");
        });

        modelBuilder.Entity<AuthoritySubmissionEvidenceFile>(entity =>
        {
            entity.HasKey(e => e.AuthoritySubmissionEvidenceFileId).HasName("AuthoritySubmissionEvidenceFile_pkey");
            entity.ToTable("AuthoritySubmissionEvidenceFile");
            entity.HasIndex(e => e.AuthoritySubmissionId, "AuthoritySubmissionEvidenceFile_authority_submission_id_idx");
            entity.HasIndex(e => e.UploadedByEmployeeId, "AuthoritySubmissionEvidenceFile_uploaded_by_employee_id_idx");
            entity.Property(e => e.AuthoritySubmissionEvidenceFileId).HasColumnName("authority_submission_evidence_file_id");
            entity.Property(e => e.AuthoritySubmissionId).HasColumnName("authority_submission_id");
            entity.Property(e => e.UploadedByEmployeeId).HasColumnName("uploaded_by_employee_id");
            entity.Property(e => e.FileName).HasMaxLength(255).HasColumnName("file_name");
            entity.Property(e => e.FileUrl).HasMaxLength(1000).HasColumnName("file_url");
            entity.Property(e => e.MimeType).HasMaxLength(100).HasColumnName("mime_type");
            entity.Property(e => e.FileSizeBytes).HasColumnName("file_size_bytes");
            entity.Property(e => e.Note).HasColumnName("note");
            entity.Property(e => e.CreatedAt).HasColumnType("timestamp without time zone").HasColumnName("created_at");
            entity.HasOne(e => e.AuthoritySubmission).WithMany(e => e.EvidenceFiles)
                .HasForeignKey(e => e.AuthoritySubmissionId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("AuthoritySubmissionEvidenceFile_authority_submission_id_fkey");
            entity.HasOne(e => e.UploadedByEmployee).WithMany()
                .HasForeignKey(e => e.UploadedByEmployeeId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("AuthoritySubmissionEvidenceFile_uploaded_by_employee_id_fkey");
        });

        modelBuilder.Entity<Booking>(entity =>
        {
            entity.HasKey(e => e.BookingId).HasName("Booking_pkey");
            entity.Property(e => e.Status).IsConcurrencyToken();

            entity.ToTable("Booking");

            entity.HasIndex(e => e.AssignedEmployeeId, "Booking_assigned_employee_id_idx");

            entity.HasIndex(e => e.BookingCode, "Booking_booking_code_key").IsUnique();

            entity.HasIndex(e => e.CustomerId, "Booking_customer_id_idx");

            entity.HasIndex(e => e.RequestedDepartureDate, "Booking_requested_departure_date_idx");

            entity.Property(e => e.BookingId).HasColumnName("booking_id");
            entity.Property(e => e.ApprovedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("approved_at");
            entity.Property(e => e.AssignedEmployeeId).HasColumnName("assigned_employee_id");
            entity.Property(e => e.BookingCode)
                .HasMaxLength(50)
                .HasColumnName("booking_code");
            entity.Property(e => e.CancellationReason).HasColumnName("cancellation_reason");
            entity.Property(e => e.CancelledAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("cancelled_at");
            entity.Property(e => e.CreatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.CustomerId).HasColumnName("customer_id");
            entity.Property(e => e.CustomerNote).HasColumnName("customer_note");
            entity.Property(e => e.DestinationAddress)
                .HasMaxLength(500)
                .HasColumnName("destination_address");
            entity.Property(e => e.DestinationCountry)
                .HasMaxLength(100)
                .HasColumnName("destination_country");
            entity.Property(e => e.OriginAddress)
                .HasMaxLength(500)
                .HasColumnName("origin_address");
            entity.Property(e => e.OriginCountry)
                .HasMaxLength(100)
                .HasColumnName("origin_country");
            entity.Property(e => e.RejectedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("rejected_at");
            entity.Property(e => e.RejectionReason).HasColumnName("rejection_reason");
            entity.Property(e => e.RequestedArrivalDate).HasColumnName("requested_arrival_date");
            entity.Property(e => e.RequestedDepartureDate).HasColumnName("requested_departure_date");
            entity.Property(e => e.SpecialRequirements).HasColumnName("special_requirements");
            entity.Property(e => e.SubmittedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("submitted_at");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.AssignedEmployee).WithMany(p => p.Bookings)
                .HasForeignKey(d => d.AssignedEmployeeId)
                .HasConstraintName("Booking_assigned_employee_id_fkey");

            entity.HasOne(d => d.Customer).WithMany(p => p.Bookings)
                .HasForeignKey(d => d.CustomerId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("Booking_customer_id_fkey");
        });

        modelBuilder.Entity<BookingHorse>(entity =>
        {
            entity.HasKey(e => new { e.BookingId, e.HorseId }).HasName("BookingHorse_pkey");

            entity.ToTable("BookingHorse");

            entity.HasIndex(e => e.HorseId, "BookingHorse_horse_id_idx");

            entity.Property(e => e.BookingId).HasColumnName("booking_id");
            entity.Property(e => e.HorseId).HasColumnName("horse_id");
            entity.Property(e => e.CreatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.SpecialHandlingNotes).HasColumnName("special_handling_notes");

            entity.HasOne(d => d.Booking).WithMany(p => p.BookingHorses)
                .HasForeignKey(d => d.BookingId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("BookingHorse_booking_id_fkey");

            entity.HasOne(d => d.Horse).WithMany(p => p.BookingHorses)
                .HasForeignKey(d => d.HorseId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("BookingHorse_horse_id_fkey");
        });

        modelBuilder.Entity<BookingStatusHistory>(entity =>
        {
            entity.HasKey(e => e.BookingStatusHistoryId).HasName("BookingStatusHistory_pkey");

            entity.ToTable("BookingStatusHistory", tb => tb.HasComment("BookingStatusHistory: Lịch sử thay đổi trạng thái Booking. type cho biết hành động nghiệp vụ nào đã gây ra thay đổi status."));

            entity.HasIndex(e => e.BookingId, "BookingStatusHistory_booking_id_idx");

            entity.HasIndex(e => e.ChangedAt, "BookingStatusHistory_changed_at_idx");

            entity.HasIndex(e => e.ChangedByAccountId, "BookingStatusHistory_changed_by_account_id_idx");

            entity.Property(e => e.BookingStatusHistoryId).HasColumnName("booking_status_history_id");
            entity.Property(e => e.BookingId).HasColumnName("booking_id");
            entity.Property(e => e.ChangedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("changed_at");
            entity.Property(e => e.ChangedByAccountId).HasColumnName("changed_by_account_id");
            entity.Property(e => e.Reason).HasColumnName("reason");

            entity.HasOne(d => d.Booking).WithMany(p => p.BookingStatusHistories)
                .HasForeignKey(d => d.BookingId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("BookingStatusHistory_booking_id_fkey");

            entity.HasOne(d => d.ChangedByAccount).WithMany(p => p.BookingStatusHistories)
                .HasForeignKey(d => d.ChangedByAccountId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("BookingStatusHistory_changed_by_account_id_fkey");
        });

        modelBuilder.Entity<Claim>(entity =>
        {
            entity.HasKey(e => e.ClaimId).HasName("Claim_pkey");

            entity.ToTable("Claim", tb => tb.HasComment("Claim: Khiếu nại do Customer gửi liên quan đến một lần bàn giao. Hệ thống chỉ lưu khiếu nại và kết quả; việc trao đổi/thương lượng giữa Customer và Manager diễn ra ngoài hệ thống."));

            entity.HasIndex(e => e.ClaimCode, "Claim_claim_code_key").IsUnique();

            entity.HasIndex(e => e.CustomerId, "Claim_customer_id_idx");

            entity.HasIndex(e => e.HandoverRecordId, "Claim_handover_record_id_idx");

            entity.HasIndex(e => e.SubmittedAt, "Claim_submitted_at_idx");

            entity.Property(e => e.ClaimId).HasColumnName("claim_id");
            entity.Property(e => e.CancellationReason).HasColumnName("cancellation_reason");
            entity.Property(e => e.CancelledAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("cancelled_at");
            entity.Property(e => e.ClaimCode)
                .HasMaxLength(50)
                .HasColumnName("claim_code");
            entity.Property(e => e.CreatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Currency)
                .HasMaxLength(3)
                .HasColumnName("currency");
            entity.Property(e => e.CustomerId).HasColumnName("customer_id");
            entity.Property(e => e.CustomerNote).HasColumnName("customer_note");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.EvidenceNote).HasColumnName("evidence_note");
            entity.Property(e => e.HandoverRecordId).HasColumnName("handover_record_id");
            entity.Property(e => e.RequestedCompensationAmount)
                .HasPrecision(18, 2)
                .HasColumnName("requested_compensation_amount");
            entity.Property(e => e.ResolvedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("resolved_at");
            entity.Property(e => e.ReviewedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("reviewed_at");
            entity.Property(e => e.SubmittedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("submitted_at");
            entity.Property(e => e.Title)
                .HasMaxLength(200)
                .HasColumnName("title");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Customer).WithMany(p => p.Claims)
                .HasForeignKey(d => d.CustomerId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("Claim_customer_id_fkey");

            entity.HasOne(d => d.HandoverRecord).WithMany(p => p.Claims)
                .HasForeignKey(d => d.HandoverRecordId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("Claim_handover_record_id_fkey");
        });

        modelBuilder.Entity<ClaimResolution>(entity =>
        {
            entity.HasKey(e => e.ClaimResolutionId).HasName("ClaimResolution_pkey");

            entity.ToTable("ClaimResolution", tb => tb.HasComment("ClaimResolution: Kết quả xử lý/thỏa thuận cuối cùng của một Claim, ví dụ mức bồi thường thực tế sau khi hai bên trao đổi trực tiếp."));

            entity.HasIndex(e => e.ClaimId, "ClaimResolution_claim_id_key").IsUnique();

            entity.HasIndex(e => e.ResolvedAt, "ClaimResolution_resolved_at_idx");

            entity.Property(e => e.ClaimResolutionId).HasColumnName("claim_resolution_id");
            entity.Property(e => e.ApprovedCompensationAmount)
                .HasPrecision(18, 2)
                .HasColumnName("approved_compensation_amount");
            entity.Property(e => e.ClaimId).HasColumnName("claim_id");
            entity.Property(e => e.CorrectiveAction).HasColumnName("corrective_action");
            entity.Property(e => e.CreatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Currency)
                .HasMaxLength(3)
                .HasColumnName("currency");
            entity.Property(e => e.InternalNote).HasColumnName("internal_note");
            entity.Property(e => e.ResolutionSummary).HasColumnName("resolution_summary");
            entity.Property(e => e.ResolvedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("resolved_at");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Claim).WithOne(p => p.ClaimResolution)
                .HasForeignKey<ClaimResolution>(d => d.ClaimId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("ClaimResolution_claim_id_fkey");
        });

        modelBuilder.Entity<ComplianceDocument>(entity =>
        {
            entity.Property(e => e.CreatedByAccountId).HasColumnName("created_by_account_id");

            entity.Property(e => e.UpdatedByAccountId).HasColumnName("updated_by_account_id");
            entity.HasKey(e => e.ComplianceDocumentId).HasName("ComplianceDocument_pkey");

            entity.ToTable("ComplianceDocument", tb => tb.HasComment("ComplianceDocument: Giấy tờ thực tế được cung cấp để đáp ứng một DossierRequirement. Đây là document logic, không phải từng file upload."));

            entity.HasIndex(e => e.DocumentCode, "ComplianceDocument_document_code_key").IsUnique();

            entity.HasIndex(e => e.DocumentNumber, "ComplianceDocument_document_number_idx");

            entity.HasIndex(e => e.DossierRequirementId, "ComplianceDocument_dossier_requirement_id_key").IsUnique();

            entity.HasIndex(e => e.ExpiryDate, "ComplianceDocument_expiry_date_idx");

            entity.Property(e => e.ComplianceDocumentId).HasColumnName("compliance_document_id");
            entity.Property(e => e.CreatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.DocumentCode)
                .HasMaxLength(50)
                .HasColumnName("document_code");
            entity.Property(e => e.DocumentName)
                .HasMaxLength(200)
                .HasColumnName("document_name");
            entity.Property(e => e.DocumentNumber)
                .HasMaxLength(150)
                .HasColumnName("document_number");
            entity.Property(e => e.DossierRequirementId).HasColumnName("dossier_requirement_id");
            entity.Property(e => e.ExpiryDate).HasColumnName("expiry_date");
            entity.Property(e => e.IssuedDate).HasColumnName("issued_date");
            entity.Property(e => e.IssuingAuthority)
                .HasMaxLength(200)
                .HasColumnName("issuing_authority");
            entity.Property(e => e.Note).HasColumnName("note");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.DossierRequirement).WithOne(p => p.ComplianceDocument)
                .HasForeignKey<ComplianceDocument>(d => d.DossierRequirementId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("ComplianceDocument_dossier_requirement_id_fkey");
            entity.HasOne(d => d.CreatedByAccount).WithMany()
                .HasForeignKey(d => d.CreatedByAccountId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("ComplianceDocument_created_by_account_id_fkey");
            entity.HasOne(d => d.UpdatedByAccount).WithMany()
                .HasForeignKey(d => d.UpdatedByAccountId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("ComplianceDocument_updated_by_account_id_fkey");
        });

        modelBuilder.Entity<ComplianceDossier>(entity =>
        {
            entity.HasKey(e => e.ComplianceDossierId).HasName("ComplianceDossier_pkey");

            entity.ToTable("ComplianceDossier", tb => tb.HasComment("ComplianceDossier: Bộ hồ sơ pháp lý thực tế của một chuyến đi, tức của một Transport cụ thể."));

            entity.HasIndex(e => e.DossierCode, "ComplianceDossier_dossier_code_key").IsUnique();

            entity.HasIndex(e => e.TransportId, "ComplianceDossier_transport_id_key").IsUnique();

            entity.HasIndex(e => e.DossierTemplateId, "ComplianceDossier_dossier_template_id_idx");
            entity.Property(e => e.DossierTemplateId).HasColumnName("dossier_template_id");
            entity.HasOne(e => e.DossierTemplate).WithMany()
                .HasForeignKey(e => e.DossierTemplateId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("ComplianceDossier_dossier_template_id_fkey");

            entity.Property(e => e.ComplianceDossierId).HasColumnName("compliance_dossier_id");
            entity.Property(e => e.ApprovedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("approved_at");
            entity.Property(e => e.ClosedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("closed_at");
            entity.Property(e => e.CreatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.DossierCode)
                .HasMaxLength(50)
                .HasColumnName("dossier_code");
            entity.Property(e => e.Note).HasColumnName("note");
            entity.Property(e => e.OpenedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("opened_at");
            entity.Property(e => e.ReadyForSubmissionAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("ready_for_submission_at");
            entity.Property(e => e.RejectedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("rejected_at");
            entity.Property(e => e.RejectionReason).HasColumnName("rejection_reason");
            entity.Property(e => e.TransportId).HasColumnName("transport_id");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Transport).WithOne(p => p.ComplianceDossier)
                .HasForeignKey<ComplianceDossier>(d => d.TransportId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("ComplianceDossier_transport_id_fkey");
        });

        modelBuilder.Entity<ComplianceRequirement>(entity =>
        {
            entity.HasKey(e => e.ComplianceRequirementId).HasName("ComplianceRequirement_pkey");

            entity.ToTable("ComplianceRequirement", tb => tb.HasComment("ComplianceRequirement: Danh mục master các loại yêu cầu pháp lý hoặc loại giấy tờ mà hệ thống biết có thể cần trong hồ sơ."));

            entity.HasIndex(e => e.IsDeleted, "ComplianceRequirement_is_deleted_idx");

            entity.HasIndex(e => e.RequirementCode, "ComplianceRequirement_requirement_code_key").IsUnique();

            entity.HasIndex(e => e.RequirementName, "ComplianceRequirement_requirement_name_idx");

            entity.Property(e => e.ComplianceRequirementId).HasColumnName("compliance_requirement_id");
            entity.Property(e => e.Country)
                .HasMaxLength(100)
                .HasColumnName("country");
            entity.Property(e => e.CreatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.DeletedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("deleted_at");
            entity.Property(e => e.DeletedByAccountId).HasColumnName("deleted_by_account_id");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.IsDeleted)
                .HasDefaultValue(false)
                .HasColumnName("is_deleted");
            entity.Property(e => e.IssuingAuthority)
                .HasMaxLength(200)
                .HasColumnName("issuing_authority");
            entity.Property(e => e.BcpCode).HasMaxLength(100).HasColumnName("bcp_code");
            entity.Property(e => e.LegalBasis).HasColumnName("legal_basis");
            entity.Property(e => e.RequirementCode)
                .HasMaxLength(50)
                .HasColumnName("requirement_code");
            entity.Property(e => e.RequirementName)
                .HasMaxLength(200)
                .HasColumnName("requirement_name");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("updated_at");
            entity.Property(e => e.ValidityRequired)
                .HasDefaultValue(false)
                .HasColumnName("validity_required");
            entity.Property(e => e.ValidityPeriodDays).HasColumnName("validity_period_days");

            entity.HasOne(d => d.DeletedByAccount).WithMany(p => p.ComplianceRequirements)
                .HasForeignKey(d => d.DeletedByAccountId)
                .HasConstraintName("ComplianceRequirement_deleted_by_account_id_fkey");
        });

        modelBuilder.Entity<CostApprovalDecision>(entity =>
        {
            entity.HasKey(e => e.CostApprovalDecisionId).HasName("CostApprovalDecision_pkey");

            entity.ToTable("CostApprovalDecision");

            entity.HasIndex(e => e.DecidedAt, "CostApprovalDecision_decided_at_idx");

            entity.HasIndex(e => e.EmergencyCostRequestId, "CostApprovalDecision_emergency_cost_request_id_key").IsUnique();

            entity.HasIndex(e => e.EmployeeId, "CostApprovalDecision_employee_id_idx");

            entity.Property(e => e.CostApprovalDecisionId).HasColumnName("cost_approval_decision_id");
            entity.Property(e => e.ApprovedAmount)
                .HasPrecision(18, 2)
                .HasColumnName("approved_amount");
            entity.Property(e => e.CreatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.DecidedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("decided_at");
            entity.Property(e => e.DecisionReason).HasColumnName("decision_reason");
            entity.Property(e => e.EmergencyCostRequestId).HasColumnName("emergency_cost_request_id");
            entity.Property(e => e.EmployeeId).HasColumnName("employee_id");

            entity.HasOne(d => d.EmergencyCostRequest).WithOne(p => p.CostApprovalDecision)
                .HasForeignKey<CostApprovalDecision>(d => d.EmergencyCostRequestId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("CostApprovalDecision_emergency_cost_request_id_fkey");

            entity.HasOne(d => d.Employee).WithMany(p => p.CostApprovalDecisions)
                .HasForeignKey(d => d.EmployeeId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("CostApprovalDecision_employee_id_fkey");
        });

        modelBuilder.Entity<Customer>(entity =>
        {
            entity.HasKey(e => e.CustomerId).HasName("Customer_pkey");

            entity.ToTable("Customer");

            entity.HasIndex(e => e.AccountId, "Customer_account_id_key").IsUnique();

            entity.HasIndex(e => e.CustomerCode, "Customer_customer_code_key").IsUnique();

            entity.HasIndex(e => e.IsDeleted, "Customer_is_deleted_idx");

            entity.HasIndex(e => e.Phone, "Customer_phone_idx");

            entity.Property(e => e.CustomerId).HasColumnName("customer_id");
            entity.Property(e => e.AccountId).HasColumnName("account_id");
            entity.Property(e => e.CustomerCode)
                .HasMaxLength(20)
                .HasColumnName("customer_code");
            entity.Property(e => e.Address)
                .HasMaxLength(500)
                .HasColumnName("address");
            entity.Property(e => e.Country)
                .HasMaxLength(100)
                .HasColumnName("country");
            entity.Property(e => e.CreatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.DateOfBirth).HasColumnName("date_of_birth");
            entity.Property(e => e.DeletedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("deleted_at");
            entity.Property(e => e.DeletedByAccountId).HasColumnName("deleted_by_account_id");
            entity.Property(e => e.FullName)
                .HasMaxLength(150)
                .HasColumnName("full_name");
            entity.Property(e => e.IdentityNumber)
                .HasMaxLength(100)
                .HasColumnName("identity_number");
            entity.Property(e => e.IsDeleted)
                .HasDefaultValue(false)
                .HasColumnName("is_deleted");
            entity.Property(e => e.Phone)
                .HasMaxLength(30)
                .HasColumnName("phone");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Account).WithOne(p => p.CustomerAccount)
                .HasForeignKey<Customer>(d => d.AccountId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("Customer_account_id_fkey");

            entity.HasOne(d => d.DeletedByAccount).WithMany(p => p.CustomerDeletedByAccounts)
                .HasForeignKey(d => d.DeletedByAccountId)
                .HasConstraintName("Customer_deleted_by_account_id_fkey");
        });

        modelBuilder.Entity<MissingDocumentReminder>(entity =>
        {
            entity.ToTable("MissingDocumentReminder");
            entity.HasKey(e => e.MissingDocumentReminderId).HasName("MissingDocumentReminder_pkey");
            entity.Property(e => e.MissingDocumentReminderId).HasColumnName("missing_document_reminder_id");
            entity.Property(e => e.ComplianceDossierId).HasColumnName("compliance_dossier_id");
            entity.Property(e => e.RecipientAccountId).HasColumnName("recipient_account_id");
            entity.Property(e => e.RequirementIds).HasColumnType("uuid[]").HasColumnName("requirement_ids");
            entity.Property(e => e.DueDate).HasColumnName("due_date");
            entity.Property(e => e.Note).HasMaxLength(2000).HasColumnName("note");
            entity.Property(e => e.AutomaticDate).HasColumnName("automatic_date");
            entity.Property(e => e.CreatedAt).HasColumnType("timestamp without time zone").HasColumnName("created_at");
            entity.Property(e => e.LastAttemptAt).HasColumnType("timestamp without time zone").HasColumnName("last_attempt_at");
            entity.Property(e => e.EmailSentAt).HasColumnType("timestamp without time zone").HasColumnName("email_sent_at");
            entity.Property(e => e.CancelledAt).HasColumnType("timestamp without time zone").HasColumnName("cancelled_at");
            entity.Property(e => e.EmailAttempts).HasColumnName("email_attempts");
            entity.HasIndex(e => new { e.ComplianceDossierId, e.AutomaticDate }, "MissingDocumentReminder_daily_key")
                .IsUnique().HasFilter("automatic_date IS NOT NULL");
            entity.HasIndex(e => new { e.EmailSentAt, e.CancelledAt, e.LastAttemptAt }, "MissingDocumentReminder_pending_idx");
            entity.HasIndex(e => e.RecipientAccountId, "MissingDocumentReminder_recipient_idx");
            entity.HasOne(e => e.ComplianceDossier).WithMany().HasForeignKey(e => e.ComplianceDossierId)
                .OnDelete(DeleteBehavior.Restrict).HasConstraintName("MissingDocumentReminder_dossier_fkey");
            entity.HasOne(e => e.RecipientAccount).WithMany().HasForeignKey(e => e.RecipientAccountId)
                .OnDelete(DeleteBehavior.Restrict).HasConstraintName("MissingDocumentReminder_recipient_fkey");
        });

        modelBuilder.Entity<DocumentRequest>(entity =>
        {
            entity.Property(e => e.DocumentReviewId).HasColumnName("document_review_id");
            entity.Property(e => e.ErrorCategory).HasMaxLength(40).HasColumnName("error_category");
            entity.HasIndex(e => e.DocumentReviewId, "DocumentRequest_document_review_id_key").IsUnique();
            entity.HasOne(e => e.DocumentReview).WithMany().HasForeignKey(e => e.DocumentReviewId)
                .OnDelete(DeleteBehavior.Restrict).HasConstraintName("DocumentRequest_document_review_id_fkey");
            entity.HasKey(e => e.DocumentRequestId).HasName("DocumentRequest_pkey");

            entity.ToTable("DocumentRequest", tb => tb.HasComment("DocumentRequest: Yêu cầu gửi tới Customer để cung cấp, sửa, upload lại hoặc bổ sung thông tin cho một DossierRequirement."));

            entity.HasIndex(e => e.DossierRequirementId, "DocumentRequest_dossier_requirement_id_idx");

            entity.HasIndex(e => e.RequestCode, "DocumentRequest_request_code_key").IsUnique();

            entity.HasIndex(e => e.RequestedAt, "DocumentRequest_requested_at_idx");

            entity.Property(e => e.DocumentRequestId).HasColumnName("document_request_id");
            entity.Property(e => e.CancellationReason).HasColumnName("cancellation_reason");
            entity.Property(e => e.CancelledAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("cancelled_at");
            entity.Property(e => e.CreatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.DossierRequirementId).HasColumnName("dossier_requirement_id");
            entity.Property(e => e.RequestedByAccountId).HasColumnName("requested_by_account_id");
            entity.Property(e => e.DueDate).HasColumnName("due_date");
            entity.Property(e => e.FulfilledAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("fulfilled_at");
            entity.Property(e => e.Message).HasColumnName("message");
            entity.Property(e => e.RequestCode)
                .HasMaxLength(50)
                .HasColumnName("request_code");
            entity.Property(e => e.RequestedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("requested_at");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.DossierRequirement).WithMany(p => p.DocumentRequests)
                .HasForeignKey(d => d.DossierRequirementId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("DocumentRequest_dossier_requirement_id_fkey");
            entity.HasOne(d => d.RequestedByAccount).WithMany()
                .HasForeignKey(d => d.RequestedByAccountId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("DocumentRequest_requested_by_account_id_fkey");
        });

        modelBuilder.Entity<DocumentRequestComment>(entity =>
        {
            entity.ToTable("DocumentRequestComment");
            entity.HasKey(e => e.DocumentRequestCommentId).HasName("DocumentRequestComment_pkey");
            entity.Property(e => e.DocumentRequestCommentId).HasColumnName("document_request_comment_id");
            entity.Property(e => e.DocumentRequestId).HasColumnName("document_request_id");
            entity.Property(e => e.AuthorAccountId).HasColumnName("author_account_id");
            entity.Property(e => e.Message).HasMaxLength(2000).HasColumnName("message");
            entity.Property(e => e.CreatedAt).HasColumnType("timestamp without time zone").HasColumnName("created_at");
            entity.HasIndex(e => new { e.DocumentRequestId, e.CreatedAt }, "DocumentRequestComment_request_created_idx");
            entity.HasIndex(e => e.AuthorAccountId, "DocumentRequestComment_author_account_id_idx");
            entity.HasOne(e => e.DocumentRequest).WithMany().HasForeignKey(e => e.DocumentRequestId)
                .OnDelete(DeleteBehavior.Restrict).HasConstraintName("DocumentRequestComment_document_request_id_fkey");
            entity.HasOne(e => e.AuthorAccount).WithMany().HasForeignKey(e => e.AuthorAccountId)
                .OnDelete(DeleteBehavior.Restrict).HasConstraintName("DocumentRequestComment_author_account_id_fkey");
        });

        modelBuilder.Entity<DocumentReview>(entity =>
        {
            entity.HasKey(e => e.DocumentReviewId).HasName("DocumentReview_pkey");

            entity.ToTable("DocumentReview", tb => tb.HasComment("DocumentReview: Kết quả kiểm tra của một DocumentVersion cụ thể, ví dụ APPROVED, REJECTED hoặc REVISION_REQUIRED."));

            entity.HasIndex(e => e.DocumentVersionId, "DocumentReview_document_version_id_idx");

            entity.HasIndex(e => e.ReviewedAt, "DocumentReview_reviewed_at_idx");

            entity.Property(e => e.DocumentReviewId).HasColumnName("document_review_id");
            entity.Property(e => e.CreatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.DocumentVersionId).HasColumnName("document_version_id");
            entity.Property(e => e.ReviewedByAccountId).HasColumnName("reviewed_by_account_id");
            entity.Property(e => e.ReviewComment).HasColumnName("review_comment");
            entity.Property(e => e.ReviewedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("reviewed_at");

            entity.HasOne(d => d.DocumentVersion).WithMany(p => p.DocumentReviews)
                .HasForeignKey(d => d.DocumentVersionId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("DocumentReview_document_version_id_fkey");
            entity.HasOne(d => d.ReviewedByAccount).WithMany()
                .HasForeignKey(d => d.ReviewedByAccountId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("DocumentReview_reviewed_by_account_id_fkey");
        });

        modelBuilder.Entity<DocumentVersion>(entity =>
        {
            entity.HasKey(e => e.DocumentVersionId).HasName("DocumentVersion_pkey");

            entity.ToTable("DocumentVersion", tb => tb.HasComment("DocumentVersion: Một phiên bản file cụ thể của ComplianceDocument. Khi giấy tờ bị sửa hoặc upload lại thì tạo version mới, không tạo ComplianceDocument mới."));

            entity.HasIndex(e => e.ComplianceDocumentId, "DocumentVersion_compliance_document_id_idx");

            entity.HasIndex(e => new { e.ComplianceDocumentId, e.VersionNo }, "DocumentVersion_compliance_document_id_version_no_idx").IsUnique();

            entity.Property(e => e.DocumentVersionId).HasColumnName("document_version_id");
            entity.Property(e => e.ComplianceDocumentId).HasColumnName("compliance_document_id");
            entity.Property(e => e.UploadedByAccountId).HasColumnName("uploaded_by_account_id");
            entity.Property(e => e.CreatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.FileName)
                .HasMaxLength(255)
                .HasColumnName("file_name");
            entity.Property(e => e.FileSizeBytes).HasColumnName("file_size_bytes");
            entity.Property(e => e.FileUrl)
                .HasMaxLength(1000)
                .HasColumnName("file_url");
            entity.Property(e => e.MimeType)
                .HasMaxLength(100)
                .HasColumnName("mime_type");
            entity.Property(e => e.UploadNote).HasColumnName("upload_note");
            entity.Property(e => e.UploadedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("uploaded_at");
            entity.Property(e => e.VersionNo).HasColumnName("version_no");

            entity.HasOne(d => d.ComplianceDocument).WithMany(p => p.DocumentVersions)
                .HasForeignKey(d => d.ComplianceDocumentId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("DocumentVersion_compliance_document_id_fkey");
            entity.HasOne(d => d.UploadedByAccount).WithMany()
                .HasForeignKey(d => d.UploadedByAccountId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("DocumentVersion_uploaded_by_account_id_fkey");
        });

        modelBuilder.Entity<DocumentAttachment>(entity =>
        {
            entity.HasKey(e => e.DocumentAttachmentId).HasName("DocumentAttachment_pkey");

            entity.ToTable("DocumentAttachment", tb => tb.HasComment("DocumentAttachment: Một file cụ thể thuộc cùng một lần nộp giấy tờ."));

            entity.HasIndex(e => e.DocumentVersionId, "DocumentAttachment_document_version_id_idx");

            entity.HasIndex(e => new { e.DocumentVersionId, e.SortOrder }, "DocumentAttachment_document_version_id_sort_order_key")
                .IsUnique();

            entity.Property(e => e.DocumentAttachmentId).HasColumnName("document_attachment_id");
            entity.Property(e => e.DocumentVersionId).HasColumnName("document_version_id");
            entity.Property(e => e.FileName)
                .HasMaxLength(255)
                .HasColumnName("file_name");
            entity.Property(e => e.FileUrl)
                .HasMaxLength(1000)
                .HasColumnName("file_url");
            entity.Property(e => e.MimeType)
                .HasMaxLength(100)
                .HasColumnName("mime_type");
            entity.Property(e => e.FileSizeBytes).HasColumnName("file_size_bytes");
            entity.Property(e => e.SortOrder).HasColumnName("sort_order");
            entity.Property(e => e.CreatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");

            entity.HasOne(d => d.DocumentVersion).WithMany(p => p.DocumentAttachments)
                .HasForeignKey(d => d.DocumentVersionId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("DocumentAttachment_document_version_id_fkey");
        });

        modelBuilder.Entity<DossierRequirement>(entity =>
        {
            entity.HasKey(e => e.DossierRequirementId).HasName("DossierRequirement_pkey");

            entity.ToTable("DossierRequirement", tb => tb.HasComment("DossierRequirement: Một yêu cầu pháp lý thực tế đã được sinh ra trong ComplianceDossier của chuyến đi cụ thể, dựa trên loại ComplianceRequirement."));

            entity.HasIndex(e => new { e.ComplianceDossierId, e.ComplianceRequirementId }, "DossierRequirement_dossier_requirement_global_key")
                .IsUnique()
                .HasFilter("horse_id IS NULL AND is_deleted = false");

            entity.HasIndex(e => new { e.ComplianceDossierId, e.ComplianceRequirementId, e.HorseId }, "DossierRequirement_dossier_requirement_horse_key")
                .IsUnique()
                .HasFilter("horse_id IS NOT NULL AND is_deleted = false");

            entity.HasIndex(e => e.ComplianceDossierId, "DossierRequirement_compliance_dossier_id_idx");

            entity.HasIndex(e => e.ComplianceRequirementId, "DossierRequirement_compliance_requirement_id_idx");

            entity.Property(e => e.DossierRequirementId).HasColumnName("dossier_requirement_id");
            entity.Property(e => e.ComplianceDossierId).HasColumnName("compliance_dossier_id");
            entity.Property(e => e.ComplianceRequirementId).HasColumnName("compliance_requirement_id");
            entity.Property(e => e.HorseId).HasColumnName("horse_id");
            entity.Property(e => e.IsDeleted).HasDefaultValue(false).HasColumnName("is_deleted");
            entity.Property(e => e.DeletedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("deleted_at");
            entity.Property(e => e.DeletedByAccountId).HasColumnName("deleted_by_account_id");
            entity.Property(e => e.CreatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.DueDate).HasColumnName("due_date");
            entity.Property(e => e.Instruction).HasColumnName("instruction");
            entity.Property(e => e.IsMandatory)
                .HasDefaultValue(true)
                .HasColumnName("is_mandatory");
            entity.Property(e => e.Note).HasColumnName("note");
            entity.Property(e => e.SatisfiedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("satisfied_at");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("updated_at");
            entity.Property(e => e.ValidUntil).HasColumnName("valid_until");
            entity.Property(e => e.WaivedReason).HasColumnName("waived_reason");

            entity.HasOne(d => d.ComplianceDossier).WithMany(p => p.DossierRequirements)
                .HasForeignKey(d => d.ComplianceDossierId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("DossierRequirement_compliance_dossier_id_fkey");

            entity.HasOne(d => d.ComplianceRequirement).WithMany(p => p.DossierRequirements)
                .HasForeignKey(d => d.ComplianceRequirementId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("DossierRequirement_compliance_requirement_id_fkey");

            entity.HasOne(d => d.Horse).WithMany()
                .HasForeignKey(d => d.HorseId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("DossierRequirement_horse_id_fkey");
            entity.HasOne(d => d.DeletedByAccount).WithMany()
                .HasForeignKey(d => d.DeletedByAccountId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("DossierRequirement_deleted_by_account_id_fkey");
        });

        modelBuilder.Entity<QuarantineProof>(entity =>
        {
            entity.HasKey(e => e.QuarantineProofId).HasName("QuarantineProof_pkey");
            entity.ToTable("QuarantineProof");
            entity.HasIndex(e => e.ComplianceDossierId, "QuarantineProof_compliance_dossier_id_idx");
            entity.HasIndex(e => e.SubmittedByEmployeeId, "QuarantineProof_submitted_by_employee_id_idx");
            entity.HasIndex(e => e.VerifiedByEmployeeId, "QuarantineProof_verified_by_employee_id_idx");
            entity.HasIndex(e => e.LocationId, "QuarantineProof_location_id_idx");
            entity.Property(e => e.QuarantineProofId).HasColumnName("quarantine_proof_id");
            entity.Property(e => e.ComplianceDossierId).HasColumnName("compliance_dossier_id");
            entity.Property(e => e.SubmittedByEmployeeId).HasColumnName("submitted_by_employee_id");
            entity.Property(e => e.VerifiedByEmployeeId).HasColumnName("verified_by_employee_id");
            entity.Property(e => e.LocationId).HasColumnName("location_id");
            entity.Property(e => e.BcpName).HasMaxLength(200).HasColumnName("bcp_name");
            entity.Property(e => e.OfficialVeterinarianName).HasMaxLength(200).HasColumnName("official_veterinarian_name");
            entity.Property(e => e.BcpReceiptNumber).HasMaxLength(100).HasColumnName("bcp_receipt_number");
            entity.Property(e => e.SubmittedNote).HasColumnName("submitted_note");
            entity.Property(e => e.MicrochipChecklist)
                .HasConversion(
                    value => value == null ? null : value.RootElement.GetRawText(),
                    value => value == null ? null : JsonDocument.Parse(value, default))
                .HasColumnType("jsonb")
                .HasColumnName("microchip_checklist");
            entity.Property(e => e.InspectionAt).HasColumnType("timestamp without time zone").HasColumnName("inspection_at");
            entity.Property(e => e.SubmittedAt).HasColumnType("timestamp without time zone").HasColumnName("submitted_at");
            entity.Property(e => e.VerifiedAt).HasColumnType("timestamp without time zone").HasColumnName("verified_at");
            entity.Property(e => e.Conditions).HasColumnName("conditions");
            entity.Property(e => e.VerificationNote).HasColumnName("verification_note");
            entity.Property(e => e.RejectionReason).HasColumnName("rejection_reason");
            entity.Property(e => e.IsFinalized).HasDefaultValue(false).HasColumnName("is_finalized");
            entity.Property(e => e.FinalizedAt).HasColumnType("timestamp without time zone").HasColumnName("finalized_at");
            entity.Property(e => e.CreatedAt).HasColumnType("timestamp without time zone").HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnType("timestamp without time zone").HasColumnName("updated_at");
            entity.HasOne(e => e.ComplianceDossier).WithMany()
                .HasForeignKey(e => e.ComplianceDossierId).OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("QuarantineProof_compliance_dossier_id_fkey");
            entity.HasOne(e => e.SubmittedByEmployee).WithMany()
                .HasForeignKey(e => e.SubmittedByEmployeeId).OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("QuarantineProof_submitted_by_employee_id_fkey");
            entity.HasOne(e => e.VerifiedByEmployee).WithMany()
                .HasForeignKey(e => e.VerifiedByEmployeeId).OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("QuarantineProof_verified_by_employee_id_fkey");
            entity.HasOne<Location>().WithMany()
                .HasForeignKey(e => e.LocationId).OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("QuarantineProof_location_id_fkey");
        });

        modelBuilder.Entity<CustomsClearanceProof>(entity =>
        {
            entity.HasKey(e => e.CustomsClearanceProofId).HasName("CustomsClearanceProof_pkey");
            entity.ToTable("CustomsClearanceProof");
            entity.HasIndex(e => e.ComplianceDossierId, "CustomsClearanceProof_compliance_dossier_id_idx");
            entity.HasIndex(e => e.SubmittedByEmployeeId, "CustomsClearanceProof_submitted_by_employee_id_idx");
            entity.HasIndex(e => e.VerifiedByEmployeeId, "CustomsClearanceProof_verified_by_employee_id_idx");
            entity.HasIndex(e => e.LocationId, "CustomsClearanceProof_location_id_idx");
            entity.Property(e => e.CustomsClearanceProofId).HasColumnName("customs_clearance_proof_id");
            entity.Property(e => e.ComplianceDossierId).HasColumnName("compliance_dossier_id");
            entity.Property(e => e.SubmittedByEmployeeId).HasColumnName("submitted_by_employee_id");
            entity.Property(e => e.VerifiedByEmployeeId).HasColumnName("verified_by_employee_id");
            entity.Property(e => e.LocationId).HasColumnName("location_id");
            entity.Property(e => e.CustomsPortName).HasMaxLength(200).HasColumnName("customs_port_name");
            entity.Property(e => e.MrnNumber).HasMaxLength(100).HasColumnName("mrn_number");
            entity.Property(e => e.SubmittedNote).HasColumnName("submitted_note");
            entity.Property(e => e.AtaCarnetExpiryDate).HasColumnName("ata_carnet_expiry_date");
            entity.Property(e => e.ClearanceAt).HasColumnType("timestamp without time zone").HasColumnName("clearance_at");
            entity.Property(e => e.SubmittedAt).HasColumnType("timestamp without time zone").HasColumnName("submitted_at");
            entity.Property(e => e.VerifiedAt).HasColumnType("timestamp without time zone").HasColumnName("verified_at");
            entity.Property(e => e.VerificationNote).HasColumnName("verification_note");
            entity.Property(e => e.RejectionReason).HasColumnName("rejection_reason");
            entity.Property(e => e.IsFinalized).HasDefaultValue(false).HasColumnName("is_finalized");
            entity.Property(e => e.FinalizedAt).HasColumnType("timestamp without time zone").HasColumnName("finalized_at");
            entity.Property(e => e.CreatedAt).HasColumnType("timestamp without time zone").HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnType("timestamp without time zone").HasColumnName("updated_at");
            entity.HasOne(e => e.ComplianceDossier).WithMany()
                .HasForeignKey(e => e.ComplianceDossierId).OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("CustomsClearanceProof_compliance_dossier_id_fkey");
            entity.HasOne(e => e.SubmittedByEmployee).WithMany()
                .HasForeignKey(e => e.SubmittedByEmployeeId).OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("CustomsClearanceProof_submitted_by_employee_id_fkey");
            entity.HasOne(e => e.VerifiedByEmployee).WithMany()
                .HasForeignKey(e => e.VerifiedByEmployeeId).OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("CustomsClearanceProof_verified_by_employee_id_fkey");
            entity.HasOne<Location>().WithMany()
                .HasForeignKey(e => e.LocationId).OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("CustomsClearanceProof_location_id_fkey");
        });

        modelBuilder.Entity<ProofAttachment>(entity =>
        {
            entity.HasKey(e => e.ProofAttachmentId).HasName("ProofAttachment_pkey");
            entity.ToTable("ProofAttachment", table => table.HasCheckConstraint(
                "ProofAttachment_exactly_one_proof_check",
                "(quarantine_proof_id IS NOT NULL) <> (customs_clearance_proof_id IS NOT NULL)"));
            entity.HasIndex(e => new { e.QuarantineProofId, e.SortOrder }, "ProofAttachment_quarantine_proof_id_sort_order_key")
                .IsUnique().HasFilter("quarantine_proof_id IS NOT NULL");
            entity.HasIndex(e => new { e.CustomsClearanceProofId, e.SortOrder }, "ProofAttachment_customs_clearance_proof_id_sort_order_key")
                .IsUnique().HasFilter("customs_clearance_proof_id IS NOT NULL");
            entity.Property(e => e.ProofAttachmentId).HasColumnName("proof_attachment_id");
            entity.Property(e => e.QuarantineProofId).HasColumnName("quarantine_proof_id");
            entity.Property(e => e.CustomsClearanceProofId).HasColumnName("customs_clearance_proof_id");
            entity.Property(e => e.FileName).HasMaxLength(255).HasColumnName("file_name");
            entity.Property(e => e.FileUrl).HasMaxLength(1000).HasColumnName("file_url");
            entity.Property(e => e.MimeType).HasMaxLength(100).HasColumnName("mime_type");
            entity.Property(e => e.FileSizeBytes).HasColumnName("file_size_bytes");
            entity.Property(e => e.SortOrder).HasColumnName("sort_order");
            entity.Property(e => e.CreatedAt).HasColumnType("timestamp without time zone").HasColumnName("created_at");
            entity.HasOne(e => e.QuarantineProof).WithMany(e => e.Attachments)
                .HasForeignKey(e => e.QuarantineProofId).OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("ProofAttachment_quarantine_proof_id_fkey");
            entity.HasOne(e => e.CustomsClearanceProof).WithMany(e => e.Attachments)
                .HasForeignKey(e => e.CustomsClearanceProofId).OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("ProofAttachment_customs_clearance_proof_id_fkey");
        });

        modelBuilder.Entity<CatalogAuditLog>(entity =>
        {
            entity.HasKey(e => e.CatalogAuditLogId).HasName("CatalogAuditLog_pkey");
            entity.ToTable("CatalogAuditLog");
            entity.HasIndex(e => new { e.EntityType, e.EntityId }, "CatalogAuditLog_entity_type_entity_id_idx");
            entity.HasIndex(e => e.ActorAccountId, "CatalogAuditLog_actor_account_id_idx");
            entity.HasIndex(e => e.CreatedAt, "CatalogAuditLog_created_at_idx");
            entity.Property(e => e.CatalogAuditLogId).HasColumnName("catalog_audit_log_id");
            entity.Property(e => e.EntityType).HasMaxLength(100).HasColumnName("entity_type");
            entity.Property(e => e.EntityId).HasColumnName("entity_id");
            entity.Property(e => e.ActionType).HasMaxLength(100).HasColumnName("action_type");
            entity.Property(e => e.ActorAccountId).HasColumnName("actor_account_id");
            entity.Property(e => e.Summary).HasColumnName("summary");
            entity.Property(e => e.BeforeData)
                .HasConversion(value => value.RootElement.GetRawText(), value => JsonDocument.Parse(value, default))
                .HasColumnType("jsonb")
                .HasColumnName("before_data");
            entity.Property(e => e.AfterData)
                .HasConversion(value => value.RootElement.GetRawText(), value => JsonDocument.Parse(value, default))
                .HasColumnType("jsonb")
                .HasColumnName("after_data");
            entity.Property(e => e.ReferenceCountSnapshot)
                .HasConversion(value => value == null ? null : value.RootElement.GetRawText(), value => value == null ? null : JsonDocument.Parse(value, default))
                .HasColumnType("jsonb")
                .HasColumnName("reference_count_snapshot");
            entity.Property(e => e.CreatedAt).HasColumnType("timestamp without time zone").HasColumnName("created_at");
            entity.HasOne(e => e.ActorAccount).WithMany()
                .HasForeignKey(e => e.ActorAccountId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("CatalogAuditLog_actor_account_id_fkey");
        });

        modelBuilder.Entity<AuditDossier>(entity =>
        {
            entity.HasKey(e => e.AuditDossierId).HasName("AuditDossier_pkey");
            entity.ToTable("AuditDossier");
            entity.HasIndex(e => e.ComplianceDossierId, "AuditDossier_compliance_dossier_id_idx");
            entity.HasIndex(e => e.ActorAccountId, "AuditDossier_actor_account_id_idx");
            entity.HasIndex(e => new { e.TargetEntity, e.TargetId }, "AuditDossier_target_entity_target_id_idx");
            entity.Property(e => e.AuditDossierId).HasColumnName("audit_dossier_id");
            entity.Property(e => e.ComplianceDossierId).HasColumnName("compliance_dossier_id");
            entity.Property(e => e.ActionType).HasMaxLength(100).HasColumnName("action_type");
            entity.Property(e => e.TargetEntity).HasMaxLength(100).HasColumnName("target_entity");
            entity.Property(e => e.TargetId).HasColumnName("target_id");
            entity.Property(e => e.ActorAccountId).HasColumnName("actor_account_id");
            entity.Property(e => e.Summary).HasColumnName("summary");
            entity.Property(e => e.Details)
                .HasConversion(
                    value => value == null ? null : value.RootElement.GetRawText(),
                    value => value == null ? null : JsonDocument.Parse(value, default))
                .HasColumnType("jsonb")
                .HasColumnName("details");
            entity.Property(e => e.CreatedAt).HasColumnType("timestamp without time zone").HasColumnName("created_at");
            entity.HasOne(e => e.ComplianceDossier).WithMany()
                .HasForeignKey(e => e.ComplianceDossierId).OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("AuditDossier_compliance_dossier_id_fkey");
            entity.HasOne(e => e.ActorAccount).WithMany()
                .HasForeignKey(e => e.ActorAccountId).OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("AuditDossier_actor_account_id_fkey");
        });

        modelBuilder.Entity<DossierTemplate>(entity =>
        {
            entity.HasKey(e => e.DossierTemplateId).HasName("DossierTemplate_pkey");

            entity.ToTable("DossierTemplate", tb => tb.HasComment("DossierTemplate: Bộ hồ sơ pháp lý mẫu cho một kịch bản vận chuyển. Template xác định thông thường cần những loại yêu cầu nào."));

            entity.HasIndex(e => e.IsDeleted, "DossierTemplate_is_deleted_idx");

            entity.HasIndex(e => e.TemplateCode, "DossierTemplate_template_code_key").IsUnique();

            entity.HasIndex(e => e.TemplateName, "DossierTemplate_template_name_idx");

            entity.Property(e => e.DossierTemplateId).HasColumnName("dossier_template_id");
            entity.Property(e => e.CreatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.DeletedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("deleted_at");
            entity.Property(e => e.DeletedByAccountId).HasColumnName("deleted_by_account_id");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.EffectiveFrom).HasColumnName("effective_from");
            entity.Property(e => e.EffectiveTo).HasColumnName("effective_to");
            entity.Property(e => e.IsDeleted)
                .HasDefaultValue(false)
                .HasColumnName("is_deleted");
            entity.Property(e => e.TemplateCode)
                .HasMaxLength(50)
                .HasColumnName("template_code");
            entity.Property(e => e.TemplateName)
                .HasMaxLength(200)
                .HasColumnName("template_name");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("updated_at");
            entity.Property(e => e.VersionNo)
                .IsConcurrencyToken()
                .HasDefaultValue(1)
                .HasColumnName("version_no");

            entity.HasOne(d => d.DeletedByAccount).WithMany(p => p.DossierTemplates)
                .HasForeignKey(d => d.DeletedByAccountId)
                .HasConstraintName("DossierTemplate_deleted_by_account_id_fkey");
        });

        modelBuilder.Entity<DossierTemplateItem>(entity =>
        {
            entity.HasKey(e => e.DossierTemplateItemId).HasName("DossierTemplateItem_pkey");

            entity.ToTable("DossierTemplateItem", tb => tb.HasComment("DossierTemplateItem: Một dòng yêu cầu nằm trong DossierTemplate, dùng để liên kết Template với ComplianceRequirement và đánh dấu yêu cầu bắt buộc hay không."));

            entity.HasIndex(e => e.ComplianceRequirementId, "DossierTemplateItem_compliance_requirement_id_idx");

            entity.HasIndex(e => new { e.DossierTemplateId, e.ComplianceRequirementId }, "DossierTemplateItem_dossier_template_id_compliance_requirem_idx").IsUnique();

            entity.HasIndex(e => e.DossierTemplateId, "DossierTemplateItem_dossier_template_id_idx");

            entity.HasIndex(e => new { e.DossierTemplateId, e.SequenceNo }, "DossierTemplateItem_dossier_template_id_sequence_no_idx").IsUnique();

            entity.Property(e => e.DossierTemplateItemId).HasColumnName("dossier_template_item_id");
            entity.Property(e => e.ComplianceRequirementId).HasColumnName("compliance_requirement_id");
            entity.Property(e => e.CreatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.DossierTemplateId).HasColumnName("dossier_template_id");
            entity.Property(e => e.Instruction).HasColumnName("instruction");
            entity.Property(e => e.AppliesPerHorse).HasDefaultValue(false).HasColumnName("applies_per_horse");
            entity.Property(e => e.IsMandatory)
                .HasDefaultValue(true)
                .HasColumnName("is_mandatory");
            entity.Property(e => e.SequenceNo).HasColumnName("sequence_no");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.ComplianceRequirement).WithMany(p => p.DossierTemplateItems)
                .HasForeignKey(d => d.ComplianceRequirementId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("DossierTemplateItem_compliance_requirement_id_fkey");

            entity.HasOne(d => d.DossierTemplate).WithMany(p => p.DossierTemplateItems)
                .HasForeignKey(d => d.DossierTemplateId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("DossierTemplateItem_dossier_template_id_fkey");
        });

        modelBuilder.Entity<EmergencyCostRequest>(entity =>
        {
            entity.HasKey(e => e.EmergencyCostRequestId).HasName("EmergencyCostRequest_pkey");

            entity.ToTable("EmergencyCostRequest");

            entity.HasIndex(e => e.IncidentReportId, "EmergencyCostRequest_incident_report_id_idx");

            entity.HasIndex(e => e.RequestCode, "EmergencyCostRequest_request_code_key").IsUnique();

            entity.Property(e => e.EmergencyCostRequestId).HasColumnName("emergency_cost_request_id");
            entity.Property(e => e.ApprovedAmount)
                .HasPrecision(18, 2)
                .HasColumnName("approved_amount");
            entity.Property(e => e.CancellationReason).HasColumnName("cancellation_reason");
            entity.Property(e => e.CancelledAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("cancelled_at");
            entity.Property(e => e.CreatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Currency)
                .HasMaxLength(3)
                .HasColumnName("currency");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.IncidentReportId).HasColumnName("incident_report_id");
            entity.Property(e => e.Justification).HasColumnName("justification");
            entity.Property(e => e.RequestCode)
                .HasMaxLength(50)
                .HasColumnName("request_code");
            entity.Property(e => e.RequestedAmount)
                .HasPrecision(18, 2)
                .HasColumnName("requested_amount");
            entity.Property(e => e.RequestedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("requested_at");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.IncidentReport).WithMany(p => p.EmergencyCostRequests)
                .HasForeignKey(d => d.IncidentReportId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("EmergencyCostRequest_incident_report_id_fkey");
        });

        modelBuilder.Entity<Employee>(entity =>
        {
            entity.HasKey(e => e.EmployeeId).HasName("Employee_pkey");

            entity.ToTable("Employee");

            entity.HasIndex(e => e.AccountId, "Employee_account_id_key").IsUnique();

            entity.HasIndex(e => e.EmployeeCode, "Employee_employee_code_key").IsUnique();

            entity.HasIndex(e => e.IsActive, "Employee_is_active_idx");

            entity.HasIndex(e => e.IsDeleted, "Employee_is_deleted_idx");

            entity.Property(e => e.EmployeeId).HasColumnName("employee_id");
            entity.Property(e => e.AccountId).HasColumnName("account_id");
            entity.Property(e => e.Address)
                .HasMaxLength(500)
                .HasColumnName("address");
            entity.Property(e => e.Country)
                .HasMaxLength(100)
                .HasColumnName("country");
            entity.Property(e => e.DateOfBirth)
                .HasColumnName("date_of_birth");
            entity.Property(e => e.CreatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.DeletedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("deleted_at");
            entity.Property(e => e.DeletedByAccountId).HasColumnName("deleted_by_account_id");
            entity.Property(e => e.EmployeeCode)
                .HasMaxLength(50)
                .HasColumnName("employee_code");
            entity.Property(e => e.FullName)
                .HasMaxLength(150)
                .HasColumnName("full_name");
            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");
            entity.Property(e => e.IsDeleted)
                .HasDefaultValue(false)
                .HasColumnName("is_deleted");
            entity.Property(e => e.Phone)
                .HasMaxLength(30)
                .HasColumnName("phone");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Account).WithOne(p => p.EmployeeAccount)
                .HasForeignKey<Employee>(d => d.AccountId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("Employee_account_id_fkey");

            entity.HasOne(d => d.DeletedByAccount).WithMany(p => p.EmployeeDeletedByAccounts)
                .HasForeignKey(d => d.DeletedByAccountId)
                .HasConstraintName("Employee_deleted_by_account_id_fkey");
        });

        modelBuilder.Entity<EmployeeTransportPlan>(entity =>
        {
            entity.HasKey(e => new { e.EmployeeId, e.TransportPlanId }).HasName("EmployeeTransportPlan_pkey");

            entity.ToTable("EmployeeTransportPlan");

            entity.HasIndex(e => e.TransportPlanId, "EmployeeTransportPlan_transport_plan_id_idx");

            entity.Property(e => e.EmployeeId).HasColumnName("employee_id");
            entity.Property(e => e.TransportPlanId).HasColumnName("transport_plan_id");
            entity.Property(e => e.AssignedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("assigned_at");
            entity.Property(e => e.IsPrimary)
                .HasDefaultValue(false)
                .HasColumnName("is_primary");
            entity.Property(e => e.Note).HasColumnName("note");
            entity.Property(e => e.Responsibility)
                .HasMaxLength(150)
                .HasColumnName("responsibility");

            entity.HasOne(d => d.Employee).WithMany(p => p.EmployeeTransportPlans)
                .HasForeignKey(d => d.EmployeeId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("EmployeeTransportPlan_employee_id_fkey");

            entity.HasOne(d => d.TransportPlan).WithMany(p => p.EmployeeTransportPlans)
                .HasForeignKey(d => d.TransportPlanId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("EmployeeTransportPlan_transport_plan_id_fkey");
        });

        modelBuilder.Entity<FinancialReport>(entity =>
        {
            entity.HasKey(e => e.FinancialReportId).HasName("FinancialReport_pkey");

            entity.ToTable("FinancialReport", tb => tb.HasComment("FinancialReport: Báo cáo tài chính tổng hợp theo một Transport, được hệ thống sinh từ dữ liệu Invoice, InvoiceItem, Receipt và chi phí liên quan; không có trạng thái lưu nháp."));

            entity.HasIndex(e => e.ReportCode, "FinancialReport_report_code_key").IsUnique();

            entity.HasIndex(e => e.TransportId, "FinancialReport_transport_id_key").IsUnique();

            entity.Property(e => e.FinancialReportId).HasColumnName("financial_report_id");
            entity.Property(e => e.CreatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Currency)
                .HasMaxLength(3)
                .HasColumnName("currency");
            entity.Property(e => e.GeneratedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("generated_at");
            entity.Property(e => e.OutstandingAmount)
                .HasPrecision(18, 2)
                .HasColumnName("outstanding_amount");
            entity.Property(e => e.ReportCode)
                .HasMaxLength(50)
                .HasColumnName("report_code");
            entity.Property(e => e.SummaryNote).HasColumnName("summary_note");
            entity.Property(e => e.TotalEmergencyCost)
                .HasPrecision(18, 2)
                .HasColumnName("total_emergency_cost");
            entity.Property(e => e.TotalInvoicedAmount)
                .HasPrecision(18, 2)
                .HasColumnName("total_invoiced_amount");
            entity.Property(e => e.TotalPaidAmount)
                .HasPrecision(18, 2)
                .HasColumnName("total_paid_amount");
            entity.Property(e => e.TransportId).HasColumnName("transport_id");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Transport).WithOne(p => p.FinancialReport)
                .HasForeignKey<FinancialReport>(d => d.TransportId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FinancialReport_transport_id_fkey");
        });

        modelBuilder.Entity<HandoverRecord>(entity =>
        {
            entity.HasKey(e => e.HandoverRecordId).HasName("HandoverRecord_pkey");

            entity.ToTable("HandoverRecord", tb => tb.HasComment("HandoverRecord: Một lần bàn giao trong Transport. from_type/to_type xác định bên giao/nhận là CUSTOMER hay EMPLOYEE; from/to_route_plan_route_id xác định chặng vừa kết thúc và chặng tiếp theo."));

            entity.HasIndex(e => e.ActualHandoverAt, "HandoverRecord_actual_handover_at_idx");

            entity.HasIndex(e => e.FromCustomerId, "HandoverRecord_from_customer_id_idx");

            entity.HasIndex(e => e.FromEmployeeId, "HandoverRecord_from_employee_id_idx");

            entity.HasIndex(e => e.FromRoutePlanRouteId, "HandoverRecord_from_route_plan_route_id_idx");

            entity.HasIndex(e => e.HandoverCode, "HandoverRecord_handover_code_key").IsUnique();

            entity.HasIndex(e => e.ToCustomerId, "HandoverRecord_to_customer_id_idx");

            entity.HasIndex(e => e.ToEmployeeId, "HandoverRecord_to_employee_id_idx");

            entity.HasIndex(e => e.ToRoutePlanRouteId, "HandoverRecord_to_route_plan_route_id_idx");

            entity.HasIndex(e => e.TransportId, "HandoverRecord_transport_id_idx");

            entity.HasIndex(e => new { e.TransportId, e.SequenceNo }, "HandoverRecord_transport_id_sequence_no_idx").IsUnique();

            entity.Property(e => e.HandoverRecordId).HasColumnName("handover_record_id");
            entity.Property(e => e.ActualHandoverAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("actual_handover_at");
            entity.Property(e => e.Address)
                .HasMaxLength(500)
                .HasColumnName("address");
            entity.Property(e => e.CancellationReason).HasColumnName("cancellation_reason");
            entity.Property(e => e.CancelledAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("cancelled_at");
            entity.Property(e => e.CompletedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("completed_at");
            entity.Property(e => e.Country)
                .HasMaxLength(100)
                .HasColumnName("country");
            entity.Property(e => e.CreatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.DiscrepancyNote).HasColumnName("discrepancy_note");
            entity.Property(e => e.DocumentHandoverNote).HasColumnName("document_handover_note");
            entity.Property(e => e.EquipmentHandoverNote).HasColumnName("equipment_handover_note");
            entity.Property(e => e.FromContactName)
                .HasMaxLength(150)
                .HasColumnName("from_contact_name");
            entity.Property(e => e.FromContactPhone)
                .HasMaxLength(30)
                .HasColumnName("from_contact_phone");
            entity.Property(e => e.FromCustomerId).HasColumnName("from_customer_id");
            entity.Property(e => e.FromEmployeeId).HasColumnName("from_employee_id");
            entity.Property(e => e.FromPartyName)
                .HasMaxLength(200)
                .HasColumnName("from_party_name");
            entity.Property(e => e.FromRoutePlanRouteId).HasColumnName("from_route_plan_route_id");
            entity.Property(e => e.HandoverCode)
                .HasMaxLength(50)
                .HasColumnName("handover_code");
            entity.Property(e => e.HorseConditionNote).HasColumnName("horse_condition_note");
            entity.Property(e => e.LocationName)
                .HasMaxLength(200)
                .HasColumnName("location_name");
            entity.Property(e => e.PlannedHandoverAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("planned_handover_at");
            entity.Property(e => e.ReceiverSignatureUrl)
                .HasMaxLength(500)
                .HasColumnName("receiver_signature_url");
            entity.Property(e => e.SenderSignatureUrl)
                .HasMaxLength(500)
                .HasColumnName("sender_signature_url");
            entity.Property(e => e.SequenceNo).HasColumnName("sequence_no");
            entity.Property(e => e.ToContactName)
                .HasMaxLength(150)
                .HasColumnName("to_contact_name");
            entity.Property(e => e.ToContactPhone)
                .HasMaxLength(30)
                .HasColumnName("to_contact_phone");
            entity.Property(e => e.ToCustomerId).HasColumnName("to_customer_id");
            entity.Property(e => e.ToEmployeeId).HasColumnName("to_employee_id");
            entity.Property(e => e.ToPartyName)
                .HasMaxLength(200)
                .HasColumnName("to_party_name");
            entity.Property(e => e.ToRoutePlanRouteId).HasColumnName("to_route_plan_route_id");
            entity.Property(e => e.TransportId).HasColumnName("transport_id");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.FromCustomer).WithMany(p => p.HandoverRecordFromCustomers)
                .HasForeignKey(d => d.FromCustomerId)
                .HasConstraintName("HandoverRecord_from_customer_id_fkey");

            entity.HasOne(d => d.FromEmployee).WithMany(p => p.HandoverRecordFromEmployees)
                .HasForeignKey(d => d.FromEmployeeId)
                .HasConstraintName("HandoverRecord_from_employee_id_fkey");

            entity.HasOne(d => d.FromRoutePlanRoute).WithMany(p => p.HandoverRecordFromRoutePlanRoutes)
                .HasForeignKey(d => d.FromRoutePlanRouteId)
                .HasConstraintName("HandoverRecord_from_route_plan_route_id_fkey");

            entity.HasOne(d => d.ToCustomer).WithMany(p => p.HandoverRecordToCustomers)
                .HasForeignKey(d => d.ToCustomerId)
                .HasConstraintName("HandoverRecord_to_customer_id_fkey");

            entity.HasOne(d => d.ToEmployee).WithMany(p => p.HandoverRecordToEmployees)
                .HasForeignKey(d => d.ToEmployeeId)
                .HasConstraintName("HandoverRecord_to_employee_id_fkey");

            entity.HasOne(d => d.ToRoutePlanRoute).WithMany(p => p.HandoverRecordToRoutePlanRoutes)
                .HasForeignKey(d => d.ToRoutePlanRouteId)
                .HasConstraintName("HandoverRecord_to_route_plan_route_id_fkey");

            entity.HasOne(d => d.Transport).WithMany(p => p.HandoverRecords)
                .HasForeignKey(d => d.TransportId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("HandoverRecord_transport_id_fkey");
        });

        modelBuilder.Entity<Horse>(entity =>
        {
            entity.HasKey(e => e.HorseId).HasName("Horse_pkey");

            entity.ToTable("Horse");

            entity.HasIndex(e => e.CustomerId, "Horse_customer_id_idx");

            entity.HasIndex(e => e.HorseName, "Horse_horse_name_idx");

            entity.HasIndex(e => e.IsDeleted, "Horse_is_deleted_idx");

            entity.HasIndex(e => e.MicrochipNumber, "Horse_microchip_number_key").IsUnique();

            entity.HasIndex(e => e.PassportNumber, "Horse_passport_number_key").IsUnique();

            entity.Property(e => e.HorseId).HasColumnName("horse_id");
            entity.Property(e => e.Breed)
                .HasMaxLength(100)
                .HasColumnName("breed");
            entity.Property(e => e.Color)
                .HasMaxLength(100)
                .HasColumnName("color");
            entity.Property(e => e.CountryOfOrigin)
                .HasMaxLength(100)
                .HasColumnName("country_of_origin");
            entity.Property(e => e.CreatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.CustomerId).HasColumnName("customer_id");
            entity.Property(e => e.DateOfBirth).HasColumnName("date_of_birth");
            entity.Property(e => e.DeletedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("deleted_at");
            entity.Property(e => e.DeletedByAccountId).HasColumnName("deleted_by_account_id");
            entity.Property(e => e.HeightCm)
                .HasPrecision(5, 2)
                .HasColumnName("height_cm");
            entity.Property(e => e.HorseName)
                .HasMaxLength(150)
                .HasColumnName("horse_name");
            entity.Property(e => e.IdentificationNotes).HasColumnName("identification_notes");
            entity.Property(e => e.IsDeleted)
                .HasDefaultValue(false)
                .HasColumnName("is_deleted");
            entity.Property(e => e.MicrochipNumber)
                .HasMaxLength(100)
                .HasColumnName("microchip_number");
            entity.Property(e => e.PassportNumber)
                .HasMaxLength(100)
                .HasColumnName("passport_number");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("updated_at");
            entity.Property(e => e.WeightKg)
                .HasPrecision(6, 2)
                .HasColumnName("weight_kg");

            entity.HasOne(d => d.Customer).WithMany(p => p.Horses)
                .HasForeignKey(d => d.CustomerId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("Horse_customer_id_fkey");

            entity.HasOne(d => d.DeletedByAccount).WithMany(p => p.Horses)
                .HasForeignKey(d => d.DeletedByAccountId)
                .HasConstraintName("Horse_deleted_by_account_id_fkey");
        });

        modelBuilder.Entity<HorseHealthIncidentDetail>(entity =>
        {
            entity.HasKey(e => e.HorseHealthIncidentDetailId).HasName("HorseHealthIncidentDetail_pkey");

            entity.ToTable("HorseHealthIncidentDetail");

            entity.HasIndex(e => e.HorseId, "HorseHealthIncidentDetail_horse_id_idx");

            entity.HasIndex(e => new { e.IncidentReportId, e.HorseId }, "HorseHealthIncidentDetail_incident_report_id_horse_id_idx");

            entity.HasIndex(e => e.IncidentReportId, "HorseHealthIncidentDetail_incident_report_id_idx");

            entity.Property(e => e.HorseHealthIncidentDetailId).HasColumnName("horse_health_incident_detail_id");
            entity.Property(e => e.BodyTemperatureCelsius)
                .HasPrecision(4, 1)
                .HasColumnName("body_temperature_celsius");
            entity.Property(e => e.ConditionDescription).HasColumnName("condition_description");
            entity.Property(e => e.CreatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.FirstAidProvided).HasColumnName("first_aid_provided");
            entity.Property(e => e.HeartRateBpm).HasColumnName("heart_rate_bpm");
            entity.Property(e => e.HorseId).HasColumnName("horse_id");
            entity.Property(e => e.HorseTransportable).HasColumnName("horse_transportable");
            entity.Property(e => e.IncidentReportId).HasColumnName("incident_report_id");
            entity.Property(e => e.MedicationGiven).HasColumnName("medication_given");
            entity.Property(e => e.OutcomeNote).HasColumnName("outcome_note");
            entity.Property(e => e.RespiratoryRateBpm).HasColumnName("respiratory_rate_bpm");
            entity.Property(e => e.Symptoms).HasColumnName("symptoms");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("updated_at");
            entity.Property(e => e.VeterinarianContactedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("veterinarian_contacted_at");
            entity.Property(e => e.VeterinarianInstruction).HasColumnName("veterinarian_instruction");
            entity.Property(e => e.VeterinarianRequired)
                .HasDefaultValue(false)
                .HasColumnName("veterinarian_required");

            entity.HasOne(d => d.Horse).WithMany(p => p.HorseHealthIncidentDetails)
                .HasForeignKey(d => d.HorseId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("HorseHealthIncidentDetail_horse_id_fkey");

            entity.HasOne(d => d.IncidentReport).WithMany(p => p.HorseHealthIncidentDetails)
                .HasForeignKey(d => d.IncidentReportId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("HorseHealthIncidentDetail_incident_report_id_fkey");
        });

        modelBuilder.Entity<HorseHealthLog>(entity =>
        {
            entity.HasKey(e => e.HorseHealthLogId).HasName("HorseHealthLog_pkey");

            entity.ToTable("HorseHealthLog");

            entity.HasIndex(e => e.CheckedAt, "HorseHealthLog_checked_at_idx");

            entity.HasIndex(e => e.EmployeeId, "HorseHealthLog_employee_id_idx");

            entity.HasIndex(e => e.HorseId, "HorseHealthLog_horse_id_idx");

            entity.HasIndex(e => new { e.TransportId, e.HorseId }, "HorseHealthLog_transport_id_horse_id_idx");

            entity.HasIndex(e => e.TransportId, "HorseHealthLog_transport_id_idx");

            entity.Property(e => e.HorseHealthLogId).HasColumnName("horse_health_log_id");
            entity.Property(e => e.BehaviorNote).HasColumnName("behavior_note");
            entity.Property(e => e.CheckedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("checked_at");
            entity.Property(e => e.CreatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.EmployeeId).HasColumnName("employee_id");
            entity.Property(e => e.FeedingNote).HasColumnName("feeding_note");
            entity.Property(e => e.GeneralNote).HasColumnName("general_note");
            entity.Property(e => e.HeartRateBpm).HasColumnName("heart_rate_bpm");
            entity.Property(e => e.HorseId).HasColumnName("horse_id");
            entity.Property(e => e.HydrationNote).HasColumnName("hydration_note");
            entity.Property(e => e.InjuryNote).HasColumnName("injury_note");
            entity.Property(e => e.LocationDescription)
                .HasMaxLength(500)
                .HasColumnName("location_description");
            entity.Property(e => e.MedicationGiven).HasColumnName("medication_given");
            entity.Property(e => e.PhotoUrl)
                .HasMaxLength(500)
                .HasColumnName("photo_url");
            entity.Property(e => e.RespiratoryRateBpm).HasColumnName("respiratory_rate_bpm");
            entity.Property(e => e.SymptomNote).HasColumnName("symptom_note");
            entity.Property(e => e.TemperatureCelsius)
                .HasPrecision(4, 1)
                .HasColumnName("temperature_celsius");
            entity.Property(e => e.TransportId).HasColumnName("transport_id");
            entity.Property(e => e.TreatmentProvided).HasColumnName("treatment_provided");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("updated_at");
            entity.Property(e => e.VeterinarianNote).HasColumnName("veterinarian_note");
            entity.Property(e => e.VeterinarianRequired)
                .HasDefaultValue(false)
                .HasColumnName("veterinarian_required");

            entity.HasOne(d => d.Employee).WithMany(p => p.HorseHealthLogs)
                .HasForeignKey(d => d.EmployeeId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("HorseHealthLog_employee_id_fkey");

            entity.HasOne(d => d.Horse).WithMany(p => p.HorseHealthLogs)
                .HasForeignKey(d => d.HorseId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("HorseHealthLog_horse_id_fkey");

            entity.HasOne(d => d.Transport).WithMany(p => p.HorseHealthLogs)
                .HasForeignKey(d => d.TransportId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("HorseHealthLog_transport_id_fkey");
        });

        modelBuilder.Entity<IncidentReport>(entity =>
        {
            entity.HasKey(e => e.IncidentReportId).HasName("IncidentReport_pkey");

            entity.ToTable("IncidentReport", tb => tb.HasComment("IncidentReport: Báo cáo sự cố phát sinh trong quá trình vận chuyển. category gồm VEHICLE, HORSE_HEALTH hoặc BOTH."));

            entity.HasIndex(e => e.EmployeeId, "IncidentReport_employee_id_idx");

            entity.HasIndex(e => e.IncidentCode, "IncidentReport_incident_code_key").IsUnique();

            entity.HasIndex(e => e.LocationId, "IncidentReport_location_id_idx");

            entity.HasIndex(e => e.OccurredAt, "IncidentReport_occurred_at_idx");

            entity.Property(e => e.IncidentReportId).HasColumnName("incident_report_id");
            entity.Property(e => e.CancellationReason).HasColumnName("cancellation_reason");
            entity.Property(e => e.CancelledAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("cancelled_at");
            entity.Property(e => e.CreatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.EmployeeId).HasColumnName("employee_id");
            entity.Property(e => e.ImmediateAction).HasColumnName("immediate_action");
            entity.Property(e => e.IncidentCode)
                .HasMaxLength(50)
                .HasColumnName("incident_code");
            entity.Property(e => e.LocationId).HasColumnName("location_id");
            entity.Property(e => e.OccurredAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("occurred_at");
            entity.Property(e => e.ReportedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("reported_at");
            entity.Property(e => e.ResolutionSummary).HasColumnName("resolution_summary");
            entity.Property(e => e.ResolvedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("resolved_at");
            entity.Property(e => e.Title)
                .HasMaxLength(200)
                .HasColumnName("title");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Employee).WithMany(p => p.IncidentReports)
                .HasForeignKey(d => d.EmployeeId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("IncidentReport_employee_id_fkey");

            entity.HasOne(d => d.Location).WithMany(p => p.IncidentReports)
                .HasForeignKey(d => d.LocationId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("IncidentReport_location_id_fkey");
        });

        modelBuilder.Entity<Invoice>(entity =>
        {
            entity.HasKey(e => e.InvoiceId).HasName("Invoice_pkey");

            entity.ToTable("Invoice");

            entity.HasIndex(e => e.BookingId, "Invoice_booking_id_idx");

            entity.HasIndex(e => e.DueDate, "Invoice_due_date_idx");

            entity.HasIndex(e => e.EmployeeId, "Invoice_employee_id_idx");

            entity.HasIndex(e => e.InvoiceCode, "Invoice_invoice_code_key").IsUnique();

            entity.Property(e => e.InvoiceId).HasColumnName("invoice_id");
            entity.Property(e => e.BillingNote).HasColumnName("billing_note");
            entity.Property(e => e.BookingId).HasColumnName("booking_id");
            entity.Property(e => e.CancellationReason).HasColumnName("cancellation_reason");
            entity.Property(e => e.CancelledAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("cancelled_at");
            entity.Property(e => e.CreatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Currency)
                .HasMaxLength(3)
                .HasColumnName("currency");
            entity.Property(e => e.DiscountAmount)
                .HasPrecision(18, 2)
                .HasColumnName("discount_amount");
            entity.Property(e => e.DueDate).HasColumnName("due_date");
            entity.Property(e => e.EmployeeId).HasColumnName("employee_id");
            entity.Property(e => e.InvoiceCode)
                .HasMaxLength(50)
                .HasColumnName("invoice_code");
            entity.Property(e => e.Type)
                .HasColumnName("type")
                .HasColumnType("invoice_type");
            entity.Property(e => e.IssuedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("issued_at");
            entity.Property(e => e.PaidAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("paid_at");
            entity.Property(e => e.Subtotal)
                .HasPrecision(18, 2)
                .HasColumnName("subtotal");
            entity.Property(e => e.TaxAmount)
                .HasPrecision(18, 2)
                .HasColumnName("tax_amount");
            entity.Property(e => e.TotalAmount)
                .HasPrecision(18, 2)
                .HasColumnName("total_amount");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Booking).WithMany(p => p.Invoices)
                .HasForeignKey(d => d.BookingId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("Invoice_booking_id_fkey");

            entity.HasOne(d => d.Employee).WithMany(p => p.Invoices)
                .HasForeignKey(d => d.EmployeeId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("Invoice_employee_id_fkey");
        });

        modelBuilder.Entity<InvoiceItem>(entity =>
        {
            entity.HasKey(e => e.InvoiceItemId).HasName("InvoiceItem_pkey");

            entity.ToTable("InvoiceItem");

            entity.HasIndex(e => e.EmergencyCostRequestId, "InvoiceItem_emergency_cost_request_id_key").IsUnique();

            entity.HasIndex(e => e.HorseId, "InvoiceItem_horse_id_idx");

            entity.HasIndex(e => e.InvoiceId, "InvoiceItem_invoice_id_idx");

            entity.Property(e => e.InvoiceItemId).HasColumnName("invoice_item_id");
            entity.Property(e => e.CreatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Description)
                .HasMaxLength(500)
                .HasColumnName("description");
            entity.Property(e => e.EmergencyCostRequestId).HasColumnName("emergency_cost_request_id");
            entity.Property(e => e.HorseId).HasColumnName("horse_id");
            entity.Property(e => e.InvoiceId).HasColumnName("invoice_id");
            entity.Property(e => e.LineAmount)
                .HasPrecision(18, 2)
                .HasColumnName("line_amount");
            entity.Property(e => e.Note).HasColumnName("note");
            entity.Property(e => e.Quantity)
                .HasPrecision(10, 2)
                .HasDefaultValueSql("1")
                .HasColumnName("quantity");
            entity.Property(e => e.UnitPrice)
                .HasPrecision(18, 2)
                .HasColumnName("unit_price");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.EmergencyCostRequest).WithOne(p => p.InvoiceItem)
                .HasForeignKey<InvoiceItem>(d => d.EmergencyCostRequestId)
                .HasConstraintName("InvoiceItem_emergency_cost_request_id_fkey");

            entity.HasOne(d => d.Horse).WithMany(p => p.InvoiceItems)
                .HasForeignKey(d => d.HorseId)
                .HasConstraintName("InvoiceItem_horse_id_fkey");

            entity.HasOne(d => d.Invoice).WithMany(p => p.InvoiceItems)
                .HasForeignKey(d => d.InvoiceId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("InvoiceItem_invoice_id_fkey");
        });

        modelBuilder.Entity<InvoiceStatusHistory>(entity =>
        {
            entity.HasKey(e => e.InvoiceStatusHistoryId).HasName("InvoiceStatusHistory_pkey");

            entity.ToTable("InvoiceStatusHistory", tb => tb.HasComment("InvoiceStatusHistory: Lịch sử thay đổi trạng thái Invoice. type cho biết hành động nghiệp vụ gây ra thay đổi; changed_by_account_id có thể null nếu hệ thống tự động thực hiện, ví dụ MARK_OVERDUE."));

            entity.HasIndex(e => e.ChangedAt, "InvoiceStatusHistory_changed_at_idx");

            entity.HasIndex(e => e.ChangedByAccountId, "InvoiceStatusHistory_changed_by_account_id_idx");

            entity.HasIndex(e => e.InvoiceId, "InvoiceStatusHistory_invoice_id_idx");

            entity.Property(e => e.InvoiceStatusHistoryId).HasColumnName("invoice_status_history_id");
            entity.Property(e => e.ChangedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("changed_at");
            entity.Property(e => e.ChangedByAccountId).HasColumnName("changed_by_account_id");
            entity.Property(e => e.CreatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.InvoiceId).HasColumnName("invoice_id");
            entity.Property(e => e.Reason).HasColumnName("reason");

            entity.HasOne(d => d.ChangedByAccount).WithMany(p => p.InvoiceStatusHistories)
                .HasForeignKey(d => d.ChangedByAccountId)
                .HasConstraintName("InvoiceStatusHistory_changed_by_account_id_fkey");

            entity.HasOne(d => d.Invoice).WithMany(p => p.InvoiceStatusHistories)
                .HasForeignKey(d => d.InvoiceId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("InvoiceStatusHistory_invoice_id_fkey");
        });

        modelBuilder.Entity<Location>(entity =>
        {
            entity.HasKey(e => e.LocationId).HasName("Location_pkey");

            entity.ToTable("Location", tb => tb.HasComment("Location: Địa điểm master trên bản đồ. Một địa điểm chỉ tạo một lần và có thể được nhiều Route tái sử dụng, ví dụ Thành phố Hồ Chí Minh."));

            entity.HasIndex(e => e.Country, "Location_country_idx");

            entity.HasIndex(e => e.IsDeleted, "Location_is_deleted_idx");

            entity.HasIndex(e => e.LocationName, "Location_location_name_idx");

            entity.Property(e => e.LocationId).HasColumnName("location_id");
            entity.Property(e => e.Address)
                .HasMaxLength(500)
                .HasColumnName("address");
            entity.Property(e => e.City)
                .HasMaxLength(100)
                .HasColumnName("city");
            entity.Property(e => e.Country)
                .HasMaxLength(100)
                .HasColumnName("country");
            entity.Property(e => e.CreatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.DeletedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("deleted_at");
            entity.Property(e => e.DeletedByAccountId).HasColumnName("deleted_by_account_id");
            entity.Property(e => e.IsDeleted)
                .HasDefaultValue(false)
                .HasColumnName("is_deleted");
            entity.Property(e => e.Latitude)
                .HasPrecision(10, 4)
                .HasColumnName("latitude");
            entity.Property(e => e.LocationName)
                .HasMaxLength(200)
                .HasColumnName("location_name");
            entity.Property(e => e.LocationNote).HasColumnName("location_note");
            entity.Property(e => e.Longitude)
                .HasPrecision(10, 4)
                .HasColumnName("longitude");
            entity.Property(e => e.StateProvince)
                .HasMaxLength(100)
                .HasColumnName("state_province");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.DeletedByAccount).WithMany(p => p.Locations)
                .HasForeignKey(d => d.DeletedByAccountId)
                .HasConstraintName("Location_deleted_by_account_id_fkey");
        });

        modelBuilder.Entity<Notification>(entity =>
        {
            entity.HasKey(e => e.NotificationId).HasName("Notification_pkey");

            entity.ToTable("Notification");

            entity.HasIndex(e => e.CreatedAt, "Notification_created_at_idx");

            entity.HasIndex(e => new { e.ReferenceType, e.ReferenceId }, "Notification_reference_type_reference_id_idx");

            entity.HasIndex(e => e.TransportId, "Notification_transport_id_idx");

            entity.Property(e => e.NotificationId).HasColumnName("notification_id");
            entity.Property(e => e.ActionUrl)
                .HasMaxLength(1000)
                .HasColumnName("action_url");
            entity.Property(e => e.CreatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.ExpiresAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("expires_at");
            entity.Property(e => e.Message).HasColumnName("message");
            entity.Property(e => e.ReferenceId).HasColumnName("reference_id");
            entity.Property(e => e.ReferenceType)
                .HasMaxLength(100)
                .HasColumnName("reference_type");
            entity.Property(e => e.Title)
                .HasMaxLength(200)
                .HasColumnName("title");
            entity.Property(e => e.TransportId).HasColumnName("transport_id");

            entity.HasOne(d => d.Transport).WithMany(p => p.Notifications)
                .HasForeignKey(d => d.TransportId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("Notification_transport_id_fkey");
        });

        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.HasKey(e => e.OutboxMessageId).HasName("OutboxMessage_pkey");
            entity.ToTable("OutboxMessage");
            entity.HasIndex(e => new { e.Status, e.AvailableAt }, "OutboxMessage_pending_idx");
            entity.HasIndex(e => e.CreatedAt, "OutboxMessage_created_at_idx");
            entity.Property(e => e.OutboxMessageId).HasColumnName("outbox_message_id");
            entity.Property(e => e.MessageType).HasMaxLength(100).HasColumnName("message_type");
            entity.Property(e => e.Payload).HasColumnName("payload");
            entity.Property(e => e.Status).HasMaxLength(20).HasColumnName("status");
            entity.Property(e => e.Attempts).HasColumnName("attempts");
            entity.Property(e => e.AvailableAt).HasColumnType("timestamp without time zone").HasColumnName("available_at");
            entity.Property(e => e.LockedAt).HasColumnType("timestamp without time zone").HasColumnName("locked_at");
            entity.Property(e => e.ProcessedAt).HasColumnType("timestamp without time zone").HasColumnName("processed_at");
            entity.Property(e => e.LastError).HasColumnName("last_error");
            entity.Property(e => e.CreatedAt).HasColumnType("timestamp without time zone").HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnType("timestamp without time zone").HasColumnName("updated_at");
        });

        modelBuilder.Entity<Receipt>(entity =>
        {
            entity.HasKey(e => e.ReceiptId).HasName("Receipt_pkey");

            entity.ToTable("Receipt");

            entity.HasIndex(e => e.InvoiceId, "Receipt_invoice_id_idx");

            entity.HasIndex(e => e.PaidAt, "Receipt_paid_at_idx");

            entity.HasIndex(e => e.ReceiptCode, "Receipt_receipt_code_key").IsUnique();

            entity.HasIndex(e => e.TransactionReference, "Receipt_transaction_reference_idx");

            entity.Property(e => e.ReceiptId).HasColumnName("receipt_id");
            entity.Property(e => e.Amount)
                .HasPrecision(18, 2)
                .HasColumnName("amount");
            entity.Property(e => e.ConfirmedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("confirmed_at");
            entity.Property(e => e.CreatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Currency)
                .HasMaxLength(3)
                .HasColumnName("currency");
            entity.Property(e => e.InvoiceId).HasColumnName("invoice_id");
            entity.Property(e => e.Note).HasColumnName("note");
            entity.Property(e => e.PaidAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("paid_at");
            entity.Property(e => e.ReceiptCode)
                .HasMaxLength(50)
                .HasColumnName("receipt_code");
            entity.Property(e => e.TransactionReference)
                .HasMaxLength(150)
                .HasColumnName("transaction_reference");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Invoice).WithMany(p => p.Receipts)
                .HasForeignKey(d => d.InvoiceId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("Receipt_invoice_id_fkey");
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(e => e.RefreshTokenId).HasName("RefreshToken_pkey");

            entity.ToTable("RefreshToken", tb => tb.HasComment("RefreshToken: Token dùng để cấp lại Access Token cho một Account. Một Account có thể có nhiều RefreshToken cho nhiều thiết bị/phiên đăng nhập. Database chỉ lưu token_hash, không lưu raw token."));

            entity.HasIndex(e => e.AccountId, "RefreshToken_account_id_idx");

            entity.HasIndex(e => e.ExpiresAt, "RefreshToken_expires_at_idx");

            entity.HasIndex(e => e.RevokedAt, "RefreshToken_revoked_at_idx");

            entity.HasIndex(e => e.TokenHash, "RefreshToken_token_hash_key").IsUnique();

            entity.Property(e => e.RefreshTokenId).HasColumnName("refresh_token_id");
            entity.Property(e => e.AccountId).HasColumnName("account_id");
            entity.Property(e => e.CreatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.CreatedByIp)
                .HasMaxLength(64)
                .HasColumnName("created_by_ip");
            entity.Property(e => e.DeviceInfo)
                .HasMaxLength(500)
                .HasColumnName("device_info");
            entity.Property(e => e.ExpiresAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("expires_at");
            entity.Property(e => e.ReplacedByTokenId).HasColumnName("replaced_by_token_id");
            entity.Property(e => e.RevokedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("revoked_at");
            entity.Property(e => e.RevokedByIp)
                .HasMaxLength(64)
                .HasColumnName("revoked_by_ip");
            entity.Property(e => e.TokenHash)
                .HasMaxLength(255)
                .HasColumnName("token_hash");

            entity.HasOne(d => d.Account).WithMany(p => p.RefreshTokens)
                .HasForeignKey(d => d.AccountId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("RefreshToken_account_id_fkey");

            entity.HasOne(d => d.ReplacedByToken).WithMany(p => p.InverseReplacedByToken)
                .HasForeignKey(d => d.ReplacedByTokenId)
                .HasConstraintName("RefreshToken_replaced_by_token_id_fkey");
        });

        modelBuilder.Entity<Route>(entity =>
        {
            entity.HasKey(e => e.RouteId).HasName("Route_pkey");

            entity.ToTable("Route", tb => tb.HasComment("Route: Một tuyến đường có hướng nối từ một Location bắt đầu đến một Location kết thúc. Route có thể được tái sử dụng trong nhiều RoutePlan."));

            entity.HasIndex(e => e.FromLocationId, "Route_from_location_id_idx");

            entity.HasIndex(e => e.IsDeleted, "Route_is_deleted_idx");

            entity.HasIndex(e => e.RouteCode, "Route_route_code_key").IsUnique();

            entity.HasIndex(e => e.ToLocationId, "Route_to_location_id_idx");

            entity.Property(e => e.RouteId).HasColumnName("route_id");
            entity.Property(e => e.CreatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.DeletedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("deleted_at");
            entity.Property(e => e.DeletedByAccountId).HasColumnName("deleted_by_account_id");
            entity.Property(e => e.DistanceKm)
                .HasPrecision(10, 2)
                .HasColumnName("distance_km");
            entity.Property(e => e.EstimatedDurationMinutes).HasColumnName("estimated_duration_minutes");
            entity.Property(e => e.FromLocationId).HasColumnName("from_location_id");
            entity.Property(e => e.IsDeleted)
                .HasDefaultValue(false)
                .HasColumnName("is_deleted");
            entity.Property(e => e.RouteCode)
                .HasMaxLength(50)
                .HasColumnName("route_code");
            entity.Property(e => e.RouteName)
                .HasMaxLength(200)
                .HasColumnName("route_name");
            entity.Property(e => e.RouteNote).HasColumnName("route_note");
            entity.Property(e => e.ToLocationId).HasColumnName("to_location_id");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.DeletedByAccount).WithMany(p => p.Routes)
                .HasForeignKey(d => d.DeletedByAccountId)
                .HasConstraintName("Route_deleted_by_account_id_fkey");

            entity.HasOne(d => d.FromLocation).WithMany(p => p.RouteFromLocations)
                .HasForeignKey(d => d.FromLocationId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("Route_from_location_id_fkey");

            entity.HasOne(d => d.ToLocation).WithMany(p => p.RouteToLocations)
                .HasForeignKey(d => d.ToLocationId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("Route_to_location_id_fkey");
        });

        modelBuilder.Entity<RoutePlan>(entity =>
        {
            entity.HasKey(e => e.RoutePlanId).HasName("RoutePlan_pkey");

            entity.ToTable("RoutePlan", tb => tb.HasComment("RoutePlan: Kế hoạch tuyến đường của một TransportPlan. Đây là tập hợp các Route được sắp xếp nối tiếp từ điểm đầu đến điểm cuối."));

            entity.HasIndex(e => e.RoutePlanCode, "RoutePlan_route_plan_code_key").IsUnique();

            entity.HasIndex(e => e.TransportPlanId, "RoutePlan_transport_plan_id_idx");

            entity.Property(e => e.RoutePlanId).HasColumnName("route_plan_id");
            entity.Property(e => e.CreatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.EstimatedDurationMinutes).HasColumnName("estimated_duration_minutes");
            entity.Property(e => e.RouteNote).HasColumnName("route_note");
            entity.Property(e => e.RoutePlanCode)
                .HasMaxLength(50)
                .HasColumnName("route_plan_code");
            entity.Property(e => e.TotalDistanceKm)
                .HasPrecision(10, 2)
                .HasColumnName("total_distance_km");
            entity.Property(e => e.TransportPlanId).HasColumnName("transport_plan_id");
            entity.Property(e => e.UpdatedAt)
                .IsConcurrencyToken()
                .HasColumnType("timestamp without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.TransportPlan).WithMany(p => p.RoutePlans)
                .HasForeignKey(d => d.TransportPlanId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("RoutePlan_transport_plan_id_fkey");
        });

        modelBuilder.Entity<RoutePlanRoute>(entity =>
        {
            entity.HasKey(e => e.RoutePlanRouteId).HasName("RoutePlanRoute_pkey");

            entity.ToTable("RoutePlanRoute", tb => tb.HasComment("RoutePlanRoute: Bảng trung gian biểu diễn một Route được dùng trong một RoutePlan, gồm thứ tự đi, Driver phụ trách và thời gian dự kiến/thực tế của chính lần sử dụng Route đó."));

            entity.HasIndex(e => e.DriverEmployeeId, "RoutePlanRoute_driver_employee_id_idx");

            entity.HasIndex(e => e.EscortEmployeeId, "RoutePlanRoute_escort_employee_id_idx");

            entity.HasIndex(e => e.TransportProviderId, "RoutePlanRoute_transport_provider_id_idx");

            entity.HasIndex(e => e.RouteId, "RoutePlanRoute_route_id_idx");

            entity.HasIndex(e => e.RoutePlanId, "RoutePlanRoute_route_plan_id_idx");

            entity.HasIndex(e => new { e.RoutePlanId, e.SequenceNo }, "RoutePlanRoute_route_plan_id_sequence_no_idx").IsUnique();

            entity.Property(e => e.RoutePlanRouteId).HasColumnName("route_plan_route_id");
            entity.Property(e => e.ActualArrivalAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("actual_arrival_at");
            entity.Property(e => e.ActualDepartureAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("actual_departure_at");
            entity.Property(e => e.CreatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.DriverEmployeeId).HasColumnName("driver_employee_id");
            entity.Property(e => e.EscortEmployeeId).HasColumnName("escort_employee_id");
            entity.Property(e => e.Note).HasColumnName("note");
            entity.Property(e => e.PlannedArrivalAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("planned_arrival_at");
            entity.Property(e => e.PlannedDepartureAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("planned_departure_at");
            entity.Property(e => e.RouteId).HasColumnName("route_id");
            entity.Property(e => e.RoutePlanId).HasColumnName("route_plan_id");
            entity.Property(e => e.TransportProviderId).HasColumnName("transport_provider_id");
            entity.Property(e => e.ServiceType).HasMaxLength(100).HasColumnName("service_type");
            entity.Property(e => e.ProviderReference).HasMaxLength(255).HasColumnName("provider_reference");
            entity.Property(e => e.OutsourcingCostAmount).HasPrecision(18, 2).HasColumnName("outsourcing_cost_amount");
            entity.Property(e => e.OutsourcingCurrency).HasMaxLength(3).HasColumnName("outsourcing_currency");
            entity.Property(e => e.SequenceNo).HasColumnName("sequence_no");
            entity.Property(e => e.UpdatedAt)
                .IsConcurrencyToken()
                .HasColumnType("timestamp without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.DriverEmployee).WithMany(p => p.RoutePlanRoutes)
                .HasForeignKey(d => d.DriverEmployeeId)
                .HasConstraintName("RoutePlanRoute_driver_employee_id_fkey");

            entity.HasOne(d => d.EscortEmployee).WithMany(p => p.EscortRoutePlanRoutes)
                .HasForeignKey(d => d.EscortEmployeeId)
                .HasConstraintName("RoutePlanRoute_escort_employee_id_fkey");

            entity.HasOne(d => d.Route).WithMany(p => p.RoutePlanRoutes)
                .HasForeignKey(d => d.RouteId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("RoutePlanRoute_route_id_fkey");

            entity.HasOne(d => d.RoutePlan).WithMany(p => p.RoutePlanRoutes)
                .HasForeignKey(d => d.RoutePlanId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("RoutePlanRoute_route_plan_id_fkey");

            entity.HasOne(d => d.TransportProvider).WithMany(p => p.RoutePlanRoutes)
                .HasForeignKey(d => d.TransportProviderId)
                .HasConstraintName("RoutePlanRoute_transport_provider_id_fkey");
        });
        modelBuilder.Entity<RoutePlanRouteVehicle>(entity =>
        {
            entity.HasKey(e => new { e.RoutePlanRouteId, e.VehicleId }).HasName("RoutePlanRouteVehicle_pkey");

            entity.ToTable("RoutePlanRouteVehicle", tb => tb.HasComment("RoutePlanRouteVehicle: Bảng trung gian N-N gán Vehicle vào một chặng RoutePlanRoute cụ thể. Nhờ vậy một Transport có thể đi xe -> máy bay -> xe, và cùng một Vehicle có thể được tái sử dụng ở nhiều chuyến/chặng khác nhau."));

            entity.HasIndex(e => e.VehicleId, "RoutePlanRouteVehicle_vehicle_id_idx");

            entity.Property(e => e.RoutePlanRouteId).HasColumnName("route_plan_route_id");
            entity.Property(e => e.VehicleId).HasColumnName("vehicle_id");
            entity.Property(e => e.AssignedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("assigned_at");
            entity.Property(e => e.Note).HasColumnName("note");

            entity.HasOne(d => d.RoutePlanRoute).WithMany(p => p.RoutePlanRouteVehicles)
                .HasForeignKey(d => d.RoutePlanRouteId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("RoutePlanRouteVehicle_route_plan_route_id_fkey");

            entity.HasOne(d => d.Vehicle).WithMany(p => p.RoutePlanRouteVehicles)
                .HasForeignKey(d => d.VehicleId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("RoutePlanRouteVehicle_vehicle_id_fkey");
        });

        modelBuilder.Entity<Transport>(entity =>
        {
            entity.HasKey(e => e.TransportId).HasName("Transport_pkey");

            entity.ToTable("Transport");

            entity.HasIndex(e => e.BookingId, "Transport_booking_id_key").IsUnique();

            entity.HasIndex(e => e.TransportCode, "Transport_transport_code_key").IsUnique();

            entity.Property(e => e.TransportId).HasColumnName("transport_id");
            entity.Property(e => e.ActualArrivalAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("actual_arrival_at");
            entity.Property(e => e.ActualDepartureAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("actual_departure_at");
            entity.Property(e => e.BookingId).HasColumnName("booking_id");
            entity.Property(e => e.CancellationReason).HasColumnName("cancellation_reason");
            entity.Property(e => e.CancelledAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("cancelled_at");
            entity.Property(e => e.CompletedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("completed_at");
            entity.Property(e => e.CreatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.TransportCode)
                .HasMaxLength(50)
                .HasColumnName("transport_code");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Booking).WithOne(p => p.Transport)
                .HasForeignKey<Transport>(d => d.BookingId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("Transport_booking_id_fkey");
        });

        modelBuilder.Entity<TransportPlan>(entity =>
        {
            entity.HasKey(e => e.TransportPlanId).HasName("TransportPlan_pkey");

            entity.ToTable("TransportPlan");

            entity.HasIndex(e => e.ApprovedByEmployeeId, "TransportPlan_approved_by_employee_id_idx");

            entity.HasIndex(e => e.PlanCode, "TransportPlan_plan_code_key").IsUnique();

            entity.HasIndex(e => e.TransportId, "TransportPlan_transport_id_idx");

            entity.Property(e => e.TransportPlanId).HasColumnName("transport_plan_id");
            entity.Property(e => e.ApprovedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("approved_at");
            entity.Property(e => e.ApprovedByEmployeeId).HasColumnName("approved_by_employee_id");
            entity.Property(e => e.CreatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.EstimatedDistanceKm)
                .HasPrecision(10, 2)
                .HasColumnName("estimated_distance_km");
            entity.Property(e => e.EstimatedDurationMinutes).HasColumnName("estimated_duration_minutes");
            entity.Property(e => e.PlanCode)
                .HasMaxLength(50)
                .HasColumnName("plan_code");
            entity.Property(e => e.PlannedEndAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("planned_end_at");
            entity.Property(e => e.PlannedStartAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("planned_start_at");
            entity.Property(e => e.PlanningNote).HasColumnName("planning_note");
            entity.Property(e => e.TransportId).HasColumnName("transport_id");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.ApprovedByEmployee).WithMany(p => p.TransportPlans)
                .HasForeignKey(d => d.ApprovedByEmployeeId)
                .HasConstraintName("TransportPlan_approved_by_employee_id_fkey");

            entity.HasOne(d => d.Transport).WithMany(p => p.TransportPlans)
                .HasForeignKey(d => d.TransportId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("TransportPlan_transport_id_fkey");
        });

        modelBuilder.Entity<TransportPlanProvider>(entity =>
        {
            entity.HasKey(e => new { e.TransportPlanId, e.TransportProviderId }).HasName("TransportPlanProvider_pkey");

            entity.ToTable("TransportPlanProvider");

            entity.HasIndex(e => e.TransportProviderId, "TransportPlanProvider_transport_provider_id_idx");

            entity.Property(e => e.TransportPlanId).HasColumnName("transport_plan_id");
            entity.Property(e => e.TransportProviderId).HasColumnName("transport_provider_id");
            entity.Property(e => e.AssignedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("assigned_at");
            entity.Property(e => e.Note).HasColumnName("note");
            entity.Property(e => e.ServiceType)
                .HasMaxLength(100)
                .HasColumnName("service_type");

            entity.HasOne(d => d.TransportPlan).WithMany(p => p.TransportPlanProviders)
                .HasForeignKey(d => d.TransportPlanId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("TransportPlanProvider_transport_plan_id_fkey");

            entity.HasOne(d => d.TransportProvider).WithMany(p => p.TransportPlanProviders)
                .HasForeignKey(d => d.TransportProviderId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("TransportPlanProvider_transport_provider_id_fkey");
        });

        modelBuilder.Entity<TransportProvider>(entity =>
        {
            entity.HasKey(e => e.TransportProviderId).HasName("TransportProvider_pkey");

            entity.ToTable("TransportProvider");

            entity.HasIndex(e => e.IsDeleted, "TransportProvider_is_deleted_idx");

            entity.HasIndex(e => e.ProviderCode, "TransportProvider_provider_code_key").HasFilter("is_deleted = false").IsUnique();

            entity.HasIndex(e => e.TaxCode, "TransportProvider_tax_code_key").HasFilter("is_deleted = false").IsUnique();

            entity.HasIndex(e => e.ProviderName, "TransportProvider_provider_name_idx");

            entity.Property(e => e.TransportProviderId).HasColumnName("transport_provider_id");
            entity.Property(e => e.Address)
                .HasMaxLength(500)
                .HasColumnName("address");
            entity.Property(e => e.ContactName)
                .HasMaxLength(150)
                .HasColumnName("contact_name");
            entity.Property(e => e.Country)
                .HasMaxLength(100)
                .HasColumnName("country");
            entity.Property(e => e.CreatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.DeletedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("deleted_at");
            entity.Property(e => e.DeletedByAccountId).HasColumnName("deleted_by_account_id");
            entity.Property(e => e.Email)
                .HasMaxLength(255)
                .HasColumnName("email");
            entity.Property(e => e.IsDeleted)
                .HasDefaultValue(false)
                .HasColumnName("is_deleted");
            entity.Property(e => e.Note).HasColumnName("note");
            entity.Property(e => e.Phone)
                .HasMaxLength(30)
                .HasColumnName("phone");
            entity.Property(e => e.ProviderCode)
                .HasMaxLength(50)
                .HasColumnName("provider_code");
            entity.Property(e => e.ProviderName)
                .HasMaxLength(200)
                .HasColumnName("provider_name");
            entity.Property(e => e.SupportedModes)
                .HasMaxLength(100)
                .HasColumnName("supported_modes");
            entity.Property(e => e.TaxCode)
                .HasMaxLength(100)
                .HasColumnName("tax_code");
            entity.Property(e => e.UpdatedAt)
                .IsConcurrencyToken()
                .HasColumnType("timestamp without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.DeletedByAccount).WithMany(p => p.TransportProviders)
                .HasForeignKey(d => d.DeletedByAccountId)
                .HasConstraintName("TransportProvider_deleted_by_account_id_fkey");
        });

        modelBuilder.Entity<Vehicle>(entity =>
        {
            entity.HasKey(e => e.VehicleId).HasName("Vehicle_pkey");

            entity.ToTable("Vehicle", tb => tb.HasComment("Vehicle: Phương tiện vận chuyển master dùng chung cho ROAD, AIR hoặc SEA, ví dụ xe tải chở ngựa, máy bay hoặc tàu. Vehicle không thuộc cố định một TransportPlan; phương tiện được gán vào từng chặng RoutePlanRoute."));

            entity.HasIndex(e => e.IsDeleted, "Vehicle_is_deleted_idx");

            entity.HasIndex(e => e.LicensePlate, "Vehicle_license_plate_key").IsUnique().HasFilter("is_deleted = false");

            entity.HasIndex(e => e.VehicleCode, "Vehicle_vehicle_code_key").IsUnique().HasFilter("is_deleted = false");

            entity.Property(e => e.VehicleId).HasColumnName("vehicle_id");
            entity.Property(e => e.CapacityHorses).HasColumnName("capacity_horses");
            entity.Property(e => e.CreatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.DeletedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("deleted_at");
            entity.Property(e => e.DeletedByAccountId).HasColumnName("deleted_by_account_id");
            entity.Property(e => e.InsuranceExpiryDate).HasColumnName("insurance_expiry_date");
            entity.Property(e => e.InsuranceNumber)
                .HasMaxLength(100)
                .HasColumnName("insurance_number");
            entity.Property(e => e.IsDeleted)
                .HasDefaultValue(false)
                .HasColumnName("is_deleted");
            entity.Property(e => e.LicensePlate)
                .HasMaxLength(50)
                .HasColumnName("license_plate");
            entity.Property(e => e.Manufacturer)
                .HasMaxLength(100)
                .HasColumnName("manufacturer");
            entity.Property(e => e.Model)
                .HasMaxLength(100)
                .HasColumnName("model");
            entity.Property(e => e.RegistrationExpiryDate).HasColumnName("registration_expiry_date");
            entity.Property(e => e.RegistrationNumber)
                .HasMaxLength(100)
                .HasColumnName("registration_number");
            entity.Property(e => e.SpecialEquipment).HasColumnName("special_equipment");
            entity.Property(e => e.UpdatedAt)
                .IsConcurrencyToken()
                .HasColumnType("timestamp without time zone")
                .HasColumnName("updated_at");
            entity.Property(e => e.VehicleCode)
                .HasMaxLength(50)
                .HasColumnName("vehicle_code");
            entity.Property(e => e.VehicleName)
                .HasMaxLength(200)
                .HasColumnName("vehicle_name");
            entity.Property(e => e.VehicleType)
                .HasMaxLength(100)
                .HasColumnName("vehicle_type");

            entity.HasOne(d => d.DeletedByAccount).WithMany(p => p.Vehicles)
                .HasForeignKey(d => d.DeletedByAccountId)
                .HasConstraintName("Vehicle_deleted_by_account_id_fkey");
        });

        modelBuilder.Entity<VehicleIncidentDetail>(entity =>
        {
            entity.HasKey(e => e.VehicleIncidentDetailId).HasName("VehicleIncidentDetail_pkey");

            entity.ToTable("VehicleIncidentDetail");

            entity.HasIndex(e => e.IncidentReportId, "VehicleIncidentDetail_incident_report_id_key").IsUnique();

            entity.HasIndex(e => e.VehicleId, "VehicleIncidentDetail_vehicle_id_idx");

            entity.Property(e => e.VehicleIncidentDetailId).HasColumnName("vehicle_incident_detail_id");
            entity.Property(e => e.CreatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.DamageDescription).HasColumnName("damage_description");
            entity.Property(e => e.EstimatedRepairCost)
                .HasPrecision(18, 2)
                .HasColumnName("estimated_repair_cost");
            entity.Property(e => e.IncidentReportId).HasColumnName("incident_report_id");
            entity.Property(e => e.RepairNote).HasColumnName("repair_note");
            entity.Property(e => e.RepairRequired)
                .HasDefaultValue(false)
                .HasColumnName("repair_required");
            entity.Property(e => e.ReplacementVehicleRequired)
                .HasDefaultValue(false)
                .HasColumnName("replacement_vehicle_required");
            entity.Property(e => e.TowingRequired)
                .HasDefaultValue(false)
                .HasColumnName("towing_required");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("updated_at");
            entity.Property(e => e.VehicleCondition).HasColumnName("vehicle_condition");
            entity.Property(e => e.VehicleId).HasColumnName("vehicle_id");
            entity.Property(e => e.VehicleOperable)
                .HasDefaultValue(true)
                .HasColumnName("vehicle_operable");

            entity.HasOne(d => d.IncidentReport).WithOne(p => p.VehicleIncidentDetail)
                .HasForeignKey<VehicleIncidentDetail>(d => d.IncidentReportId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("VehicleIncidentDetail_incident_report_id_fkey");

            entity.HasOne(d => d.Vehicle).WithMany(p => p.VehicleIncidentDetails)
                .HasForeignKey(d => d.VehicleId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("VehicleIncidentDetail_vehicle_id_fkey");
        });

        OnModelCreatingPartial(modelBuilder);
        ConfigureRouteChanges(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}

