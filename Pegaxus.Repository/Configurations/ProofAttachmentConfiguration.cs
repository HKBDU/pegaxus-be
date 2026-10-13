using System.Collections.Generic;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pegaxus.Repository.Entities;

namespace Pegaxus.Repository.Configurations;

public sealed class ProofAttachmentConfiguration : IEntityTypeConfiguration<ProofAttachment>
{
    public void Configure(EntityTypeBuilder<ProofAttachment> entity)
    {
        entity.HasKey(e => e.ProofAttachmentId).HasName("ProofAttachment_pkey");
        entity.ToTable("ProofAttachment", table => table.HasCheckConstraint(
            "ProofAttachment_exactly_one_proof_check",
            "(quarantine_proof_id IS NOT NULL) <> (customs_clearance_proof_id IS NOT NULL)"));
        entity.HasIndex(e => new { e.QuarantineProofId, e.SortOrder }, "ProofAttachment_quarantine_proof_id_sort_order_key")
            .IsUnique().HasFilter("quarantine_proof_id IS NOT NULL");
        entity.HasIndex(e => new { e.CustomsClearanceProofId, e.SortOrder }, "ProofAttachment_customs_clearance_proof_id_sort_order_key")
            .IsUnique().HasFilter("customs_clearance_proof_id IS NOT NULL");
        entity.Property(e => e.ProofAttachmentId).HasColumnName("proof_attachment_id");
        entity.Property(e => e.QuarantineProofId).HasColumnName("quarantine_proof_id");
        entity.Property(e => e.CustomsClearanceProofId).HasColumnName("customs_clearance_proof_id");
        entity.Property(e => e.FileName).HasMaxLength(255).HasColumnName("file_name");
        entity.Property(e => e.FileUrl).HasMaxLength(1000).HasColumnName("file_url");
        entity.Property(e => e.MimeType).HasMaxLength(100).HasColumnName("mime_type");
        entity.Property(e => e.FileSizeBytes).HasColumnName("file_size_bytes");
        entity.Property(e => e.SortOrder).HasColumnName("sort_order");
        entity.Property(e => e.CreatedAt).HasColumnType("timestamp without time zone").HasColumnName("created_at");
        entity.HasOne(e => e.QuarantineProof).WithMany(e => e.Attachments)
            .HasForeignKey(e => e.QuarantineProofId).OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("ProofAttachment_quarantine_proof_id_fkey");
        entity.HasOne(e => e.CustomsClearanceProof).WithMany(e => e.Attachments)
            .HasForeignKey(e => e.CustomsClearanceProofId).OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("ProofAttachment_customs_clearance_proof_id_fkey");
    }
}
