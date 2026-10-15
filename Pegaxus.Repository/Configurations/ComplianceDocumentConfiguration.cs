using System.Collections.Generic;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pegaxus.Repository.Entities;

namespace Pegaxus.Repository.Configurations;

public sealed class ComplianceDocumentConfiguration : IEntityTypeConfiguration<ComplianceDocument>
{
    public void Configure(EntityTypeBuilder<ComplianceDocument> entity)
    {
        entity.Property(e => e.CreatedByAccountId).HasColumnName("created_by_account_id");

        entity.Property(e => e.UpdatedByAccountId).HasColumnName("updated_by_account_id");
        entity.HasKey(e => e.ComplianceDocumentId).HasName("ComplianceDocument_pkey");

        entity.ToTable("ComplianceDocument", tb => tb.HasComment("ComplianceDocument: Giấy tờ thực tế được cung cấp để đáp ứng một DossierRequirement. Đây là document logic, không phải từng file upload."));

        entity.HasIndex(e => e.DocumentCode, "ComplianceDocument_document_code_key").IsUnique();

        entity.HasIndex(e => e.DocumentNumber, "ComplianceDocument_document_number_idx");

        entity.HasIndex(e => e.DossierRequirementId, "ComplianceDocument_dossier_requirement_id_key").IsUnique();

        entity.HasIndex(e => e.ExpiryDate, "ComplianceDocument_expiry_date_idx");

        entity.Property(e => e.ComplianceDocumentId).HasColumnName("compliance_document_id");
        entity.Property(e => e.CreatedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("created_at");
        entity.Property(e => e.DocumentCode)
            .HasMaxLength(50)
            .HasColumnName("document_code");
        entity.Property(e => e.DocumentName)
            .HasMaxLength(200)
            .HasColumnName("document_name");
        entity.Property(e => e.DocumentNumber)
            .HasMaxLength(150)
            .HasColumnName("document_number");
        entity.Property(e => e.DossierRequirementId).HasColumnName("dossier_requirement_id");
        entity.Property(e => e.ExpiryDate).HasColumnName("expiry_date");
        entity.Property(e => e.IssuedDate).HasColumnName("issued_date");
        entity.Property(e => e.IssuingAuthority)
            .HasMaxLength(200)
            .HasColumnName("issuing_authority");
        entity.Property(e => e.Note).HasColumnName("note");
        entity.Property(e => e.UpdatedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("updated_at");

        entity.HasOne(d => d.DossierRequirement).WithOne(p => p.ComplianceDocument)
            .HasForeignKey<ComplianceDocument>(d => d.DossierRequirementId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("ComplianceDocument_dossier_requirement_id_fkey");
        entity.HasOne(d => d.CreatedByAccount).WithMany()
            .HasForeignKey(d => d.CreatedByAccountId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("ComplianceDocument_created_by_account_id_fkey");
        entity.HasOne(d => d.UpdatedByAccount).WithMany()
            .HasForeignKey(d => d.UpdatedByAccountId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("ComplianceDocument_updated_by_account_id_fkey");
    }
}
