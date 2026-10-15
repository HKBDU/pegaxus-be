using System.Collections.Generic;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pegaxus.Repository.Entities;

namespace Pegaxus.Repository.Configurations;

public sealed class DossierTemplateConfiguration : IEntityTypeConfiguration<DossierTemplate>
{
    public void Configure(EntityTypeBuilder<DossierTemplate> entity)
    {
        entity.HasKey(e => e.DossierTemplateId).HasName("DossierTemplate_pkey");

        entity.ToTable("DossierTemplate", tb => tb.HasComment("DossierTemplate: Bộ hồ sơ pháp lý mẫu cho một kịch bản vận chuyển. Template xác định thông thường cần những loại yêu cầu nào."));

        entity.HasIndex(e => e.IsDeleted, "DossierTemplate_is_deleted_idx");

        entity.HasIndex(e => e.TemplateCode, "DossierTemplate_template_code_key").IsUnique();

        entity.HasIndex(e => e.TemplateName, "DossierTemplate_template_name_idx");

        entity.Property(e => e.DossierTemplateId).HasColumnName("dossier_template_id");
        entity.Property(e => e.CreatedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("created_at");
        entity.Property(e => e.DeletedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("deleted_at");
        entity.Property(e => e.DeletedByAccountId).HasColumnName("deleted_by_account_id");
        entity.Property(e => e.Description).HasColumnName("description");
        entity.Property(e => e.EffectiveFrom).HasColumnName("effective_from");
        entity.Property(e => e.EffectiveTo).HasColumnName("effective_to");
        entity.Property(e => e.IsDeleted)
            .HasDefaultValue(false)
            .HasColumnName("is_deleted");
        entity.Property(e => e.TemplateCode)
            .HasMaxLength(50)
            .HasColumnName("template_code");
        entity.Property(e => e.TemplateName)
            .HasMaxLength(200)
            .HasColumnName("template_name");
        entity.Property(e => e.UpdatedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("updated_at");
        entity.Property(e => e.VersionNo)
            .IsConcurrencyToken()
            .HasDefaultValue(1)
            .HasColumnName("version_no");

        entity.HasOne(d => d.DeletedByAccount).WithMany(p => p.DossierTemplates)
            .HasForeignKey(d => d.DeletedByAccountId)
            .HasConstraintName("DossierTemplate_deleted_by_account_id_fkey");
    }
}
