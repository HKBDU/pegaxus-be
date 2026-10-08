using Microsoft.EntityFrameworkCore;
using Npgsql.NameTranslation;
using Pegaxus.Repository.Entities;

namespace Pegaxus.Repository;

public partial class AppDbContext
{
    public DbSet<RouteChangeRequest> RouteChangeRequests { get; set; }

    private static void ConfigureRouteChanges(ModelBuilder builder)
    {
        var entity = builder.Entity<RouteChangeRequest>();
        entity.ToTable("RouteChangeRequest", table => table.HasCheckConstraint("CK_RouteChangeRequest_Status",
            "status IN ('PENDING', 'APPROVED', 'REJECTED', 'EMERGENCY_RETURN')"));
        entity.HasKey(x => x.RouteChangeRequestId);
        var names = new NpgsqlSnakeCaseNameTranslator();
        foreach (var property in entity.Metadata.GetProperties())
        {
            property.SetColumnName(names.TranslateMemberName(property.Name));
            if (property.ClrType == typeof(DateTime) || property.ClrType == typeof(DateTime?))
                property.SetColumnType("timestamp without time zone");
        }
        entity.Property(x => x.UpdatedAt).IsConcurrencyToken();
        entity.Property(x => x.Status).HasMaxLength(30);
        entity.Property(x => x.Currency).HasMaxLength(3);
        entity.Property(x => x.EstimatedExtraCost).HasPrecision(18, 2);
        entity.Property(x => x.CurrentSegments).HasColumnType("jsonb");
        entity.Property(x => x.SubmittedRequest).HasColumnType("jsonb");
        entity.Property(x => x.ProposedSegments).HasColumnType("jsonb");
        entity.Property(x => x.Comparison).HasColumnType("jsonb");
        entity.Property(x => x.DecisionRequest).HasColumnType("jsonb");
        entity.Property(x => x.DecisionResponse).HasColumnType("jsonb");
        entity.HasIndex(x => x.RoutePlanId).IsUnique().HasFilter("status = 'PENDING'")
            .HasDatabaseName("RouteChangeRequest_pending_route_key");
        entity.HasIndex(x => x.CreatedAt);
        entity.HasIndex(x => new { x.RoutePlanId, x.ClientRequestId }).IsUnique().HasFilter("client_request_id IS NOT NULL")
            .HasDatabaseName("RouteChangeRequest_client_request_key");
        entity.HasOne(x => x.RoutePlan).WithMany().HasForeignKey(x => x.RoutePlanId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<RoutePlan>().WithMany().HasForeignKey(x => x.BackupRoutePlanId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(x => x.RequestedByEmployee).WithMany().HasForeignKey(x => x.RequestedByEmployeeId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<Employee>().WithMany().HasForeignKey(x => x.DecidedByEmployeeId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<IncidentReport>().WithMany().HasForeignKey(x => x.IncidentReportId).OnDelete(DeleteBehavior.Restrict);

        builder.Entity<RoutePlan>().Property(x => x.PrimaryRoutePlanId).HasColumnName("primary_route_plan_id");
        builder.Entity<RoutePlan>().HasOne<RoutePlan>().WithMany().HasForeignKey(x => x.PrimaryRoutePlanId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<RoutePlanRoute>(segment =>
        {
            segment.Property(x => x.ServiceType).HasColumnName("service_type").HasMaxLength(100);
            segment.Property(x => x.ProviderReference).HasColumnName("provider_reference").HasMaxLength(150);
            segment.Property(x => x.SupersededAt).HasColumnName("superseded_at").HasColumnType("timestamp without time zone");
            segment.Property(x => x.SupersededByChangeRequestId).HasColumnName("superseded_by_change_request_id");
            segment.HasOne<RouteChangeRequest>().WithMany().HasForeignKey(x => x.SupersededByChangeRequestId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<IncidentReport>().Property(x => x.TransportId).HasColumnName("transport_id");
        builder.Entity<IncidentReport>().Property(x => x.EmergencySettlementRequired).HasColumnName("emergency_settlement_required");
        builder.Entity<IncidentReport>().HasOne<Transport>().WithMany().HasForeignKey(x => x.TransportId).OnDelete(DeleteBehavior.Restrict);
    }
}
