using System.Collections.Generic;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pegaxus.Repository.Entities;

namespace Pegaxus.Repository.Configurations;

public sealed class DossierTemplateItemConfiguration : IEntityTypeConfiguration<DossierTemplateItem>
{
    public void Configure(EntityTypeBuilder<DossierTemplateItem> entity)
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
    }
}
