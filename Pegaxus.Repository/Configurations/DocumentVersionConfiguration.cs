using System.Collections.Generic;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pegaxus.Repository.Entities;

namespace Pegaxus.Repository.Configurations;

public sealed class DocumentVersionConfiguration : IEntityTypeConfiguration<DocumentVersion>
{
    public void Configure(EntityTypeBuilder<DocumentVersion> entity)
    {
        entity.HasKey(e => e.DocumentVersionId).HasName("DocumentVersion_pkey");

        entity.ToTable("DocumentVersion", tb => tb.HasComment("DocumentVersion: Một phiên bản file cụ thể của ComplianceDocument. Khi giấy tờ bị sửa hoặc upload lại thì tạo version mới, không tạo ComplianceDocument mới."));

        entity.HasIndex(e => e.ComplianceDocumentId, "DocumentVersion_compliance_document_id_idx");

        entity.HasIndex(e => new { e.ComplianceDocumentId, e.VersionNo }, "DocumentVersion_compliance_document_id_version_no_idx").IsUnique();

        entity.Property(e => e.DocumentVersionId).HasColumnName("document_version_id");
        entity.Property(e => e.ComplianceDocumentId).HasColumnName("compliance_document_id");
        entity.Property(e => e.UploadedByAccountId).HasColumnName("uploaded_by_account_id");
        entity.Property(e => e.CreatedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("created_at");
        entity.Property(e => e.FileName)
            .HasMaxLength(255)
            .HasColumnName("file_name");
        entity.Property(e => e.FileSizeBytes).HasColumnName("file_size_bytes");
        entity.Property(e => e.FileUrl)
            .HasMaxLength(1000)
            .HasColumnName("file_url");
        entity.Property(e => e.MimeType)
            .HasMaxLength(100)
            .HasColumnName("mime_type");
        entity.Property(e => e.UploadNote).HasColumnName("upload_note");
        entity.Property(e => e.UploadedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("uploaded_at");
        entity.Property(e => e.VersionNo).HasColumnName("version_no");

        entity.HasOne(d => d.ComplianceDocument).WithMany(p => p.DocumentVersions)
            .HasForeignKey(d => d.ComplianceDocumentId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("DocumentVersion_compliance_document_id_fkey");
        entity.HasOne(d => d.UploadedByAccount).WithMany()
            .HasForeignKey(d => d.UploadedByAccountId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("DocumentVersion_uploaded_by_account_id_fkey");
    }
}
