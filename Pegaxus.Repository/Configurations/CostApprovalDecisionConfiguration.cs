using System.Collections.Generic;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pegaxus.Repository.Entities;

namespace Pegaxus.Repository.Configurations;

public sealed class CostApprovalDecisionConfiguration : IEntityTypeConfiguration<CostApprovalDecision>
{
    public void Configure(EntityTypeBuilder<CostApprovalDecision> entity)
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
    }
}
