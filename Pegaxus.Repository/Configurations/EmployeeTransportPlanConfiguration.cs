using System.Collections.Generic;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pegaxus.Repository.Entities;

namespace Pegaxus.Repository.Configurations;

public sealed class EmployeeTransportPlanConfiguration : IEntityTypeConfiguration<EmployeeTransportPlan>
{
    public void Configure(EntityTypeBuilder<EmployeeTransportPlan> entity)
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
    }
}
