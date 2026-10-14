using System.Collections.Generic;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pegaxus.Repository.Entities;

namespace Pegaxus.Repository.Configurations;

public sealed class EmergencyCostRequestConfiguration : IEntityTypeConfiguration<EmergencyCostRequest>
{
    public void Configure(EntityTypeBuilder<EmergencyCostRequest> entity)
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
    }
}
