using System.Collections.Generic;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Configurations;
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
        modelBuilder.HasPostgresEnums();

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        OnModelCreatingPartial(modelBuilder);
        ConfigureRouteChanges(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}

