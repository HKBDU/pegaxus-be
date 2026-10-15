using System.Collections.Generic;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pegaxus.Repository.Entities;

namespace Pegaxus.Repository.Configurations;

public sealed class DocumentAttachmentConfiguration : IEntityTypeConfiguration<DocumentAttachment>
{
    public void Configure(EntityTypeBuilder<DocumentAttachment> entity)
    {
        entity.HasKey(e => e.DocumentAttachmentId).HasName("DocumentAttachment_pkey");

        entity.ToTable("DocumentAttachment", tb => tb.HasComment("DocumentAttachment: Một file cụ thể thuộc cùng một lần nộp giấy tờ."));

        entity.HasIndex(e => e.DocumentVersionId, "DocumentAttachment_document_version_id_idx");

        entity.HasIndex(e => new { e.DocumentVersionId, e.SortOrder }, "DocumentAttachment_document_version_id_sort_order_key")
            .IsUnique();

        entity.Property(e => e.DocumentAttachmentId).HasColumnName("document_attachment_id");
        entity.Property(e => e.DocumentVersionId).HasColumnName("document_version_id");
        entity.Property(e => e.FileName)
            .HasMaxLength(255)
            .HasColumnName("file_name");
        entity.Property(e => e.FileUrl)
            .HasMaxLength(1000)
            .HasColumnName("file_url");
        entity.Property(e => e.MimeType)
            .HasMaxLength(100)
            .HasColumnName("mime_type");
        entity.Property(e => e.FileSizeBytes).HasColumnName("file_size_bytes");
        entity.Property(e => e.SortOrder).HasColumnName("sort_order");
        entity.Property(e => e.CreatedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("created_at");

        entity.HasOne(d => d.DocumentVersion).WithMany(p => p.DocumentAttachments)
            .HasForeignKey(d => d.DocumentVersionId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("DocumentAttachment_document_version_id_fkey");
    }
}
