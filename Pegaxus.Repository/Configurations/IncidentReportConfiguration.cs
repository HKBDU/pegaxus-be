using System.Collections.Generic;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pegaxus.Repository.Entities;

namespace Pegaxus.Repository.Configurations;

public sealed class IncidentReportConfiguration : IEntityTypeConfiguration<IncidentReport>
{
    public void Configure(EntityTypeBuilder<IncidentReport> entity)
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
    }
}
