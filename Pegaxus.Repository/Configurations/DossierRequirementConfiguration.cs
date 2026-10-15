using System.Collections.Generic;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pegaxus.Repository.Entities;

namespace Pegaxus.Repository.Configurations;

public sealed class DossierRequirementConfiguration : IEntityTypeConfiguration<DossierRequirement>
{
    public void Configure(EntityTypeBuilder<DossierRequirement> entity)
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
    }
}
