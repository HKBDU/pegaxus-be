using System.Collections.Generic;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pegaxus.Repository.Entities;

namespace Pegaxus.Repository.Configurations;

public sealed class DocumentReviewConfiguration : IEntityTypeConfiguration<DocumentReview>
{
    public void Configure(EntityTypeBuilder<DocumentReview> entity)
    {
        entity.HasKey(e => e.DocumentReviewId).HasName("DocumentReview_pkey");

        entity.ToTable("DocumentReview", tb => tb.HasComment("DocumentReview: Kết quả kiểm tra của một DocumentVersion cụ thể, ví dụ APPROVED, REJECTED hoặc REVISION_REQUIRED."));

        entity.HasIndex(e => e.DocumentVersionId, "DocumentReview_document_version_id_idx");

        entity.HasIndex(e => e.ReviewedAt, "DocumentReview_reviewed_at_idx");

        entity.Property(e => e.DocumentReviewId).HasColumnName("document_review_id");
        entity.Property(e => e.CreatedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("created_at");
        entity.Property(e => e.DocumentVersionId).HasColumnName("document_version_id");
        entity.Property(e => e.ReviewedByAccountId).HasColumnName("reviewed_by_account_id");
        entity.Property(e => e.ReviewComment).HasColumnName("review_comment");
        entity.Property(e => e.ReviewedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("reviewed_at");

        entity.HasOne(d => d.DocumentVersion).WithMany(p => p.DocumentReviews)
            .HasForeignKey(d => d.DocumentVersionId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("DocumentReview_document_version_id_fkey");
        entity.HasOne(d => d.ReviewedByAccount).WithMany()
            .HasForeignKey(d => d.ReviewedByAccountId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("DocumentReview_reviewed_by_account_id_fkey");
    }
}
