using System.Collections.Generic;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pegaxus.Repository.Entities;

namespace Pegaxus.Repository.Configurations;

public sealed class TransportPlanConfiguration : IEntityTypeConfiguration<TransportPlan>
{
    public void Configure(EntityTypeBuilder<TransportPlan> entity)
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
    }
}
