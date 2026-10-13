using System.Collections.Generic;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pegaxus.Repository.Entities;

namespace Pegaxus.Repository.Configurations;

public sealed class ComplianceRequirementConfiguration : IEntityTypeConfiguration<ComplianceRequirement>
{
    public void Configure(EntityTypeBuilder<ComplianceRequirement> entity)
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
    }
}
