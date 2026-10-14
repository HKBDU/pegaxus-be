using System.Collections.Generic;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pegaxus.Repository.Entities;

namespace Pegaxus.Repository.Configurations;

public sealed class ComplianceDossierConfiguration : IEntityTypeConfiguration<ComplianceDossier>
{
    public void Configure(EntityTypeBuilder<ComplianceDossier> entity)
    {
        entity.HasKey(e => e.ComplianceDossierId).HasName("ComplianceDossier_pkey");

        entity.ToTable("ComplianceDossier", tb => tb.HasComment("ComplianceDossier: Bộ hồ sơ pháp lý thực tế của một chuyến đi, tức của một Transport cụ thể."));

        entity.HasIndex(e => e.DossierCode, "ComplianceDossier_dossier_code_key").IsUnique();

        entity.HasIndex(e => e.TransportId, "ComplianceDossier_transport_id_key").IsUnique();

        entity.HasIndex(e => e.DossierTemplateId, "ComplianceDossier_dossier_template_id_idx");
        entity.Property(e => e.DossierTemplateId).HasColumnName("dossier_template_id");
        entity.HasOne(e => e.DossierTemplate).WithMany()
            .HasForeignKey(e => e.DossierTemplateId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("ComplianceDossier_dossier_template_id_fkey");

        entity.Property(e => e.ComplianceDossierId).HasColumnName("compliance_dossier_id");
        entity.Property(e => e.ApprovedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("approved_at");
        entity.Property(e => e.ClosedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("closed_at");
        entity.Property(e => e.CreatedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("created_at");
        entity.Property(e => e.DossierCode)
            .HasMaxLength(50)
            .HasColumnName("dossier_code");
        entity.Property(e => e.Note).HasColumnName("note");
        entity.Property(e => e.OpenedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("opened_at");
        entity.Property(e => e.ReadyForSubmissionAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("ready_for_submission_at");
        entity.Property(e => e.RejectedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("rejected_at");
        entity.Property(e => e.RejectionReason).HasColumnName("rejection_reason");
        entity.Property(e => e.TransportId).HasColumnName("transport_id");
        entity.Property(e => e.UpdatedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("updated_at");

        entity.HasOne(d => d.Transport).WithOne(p => p.ComplianceDossier)
            .HasForeignKey<ComplianceDossier>(d => d.TransportId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("ComplianceDossier_transport_id_fkey");
    }
}
