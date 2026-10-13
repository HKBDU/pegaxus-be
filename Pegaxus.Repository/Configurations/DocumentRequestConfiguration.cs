using System.Collections.Generic;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pegaxus.Repository.Entities;

namespace Pegaxus.Repository.Configurations;

public sealed class DocumentRequestConfiguration : IEntityTypeConfiguration<DocumentRequest>
{
    public void Configure(EntityTypeBuilder<DocumentRequest> entity)
    {
        entity.Property(e => e.DocumentReviewId).HasColumnName("document_review_id");
        entity.Property(e => e.ErrorCategory).HasMaxLength(40).HasColumnName("error_category");
        entity.HasIndex(e => e.DocumentReviewId, "DocumentRequest_document_review_id_key").IsUnique();
        entity.HasOne(e => e.DocumentReview).WithMany().HasForeignKey(e => e.DocumentReviewId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("DocumentRequest_document_review_id_fkey");
        entity.HasKey(e => e.DocumentRequestId).HasName("DocumentRequest_pkey");

        entity.ToTable("DocumentRequest", tb => tb.HasComment("DocumentRequest: Yêu cầu gửi tới Customer để cung cấp, sửa, upload lại hoặc bổ sung thông tin cho một DossierRequirement."));

        entity.HasIndex(e => e.DossierRequirementId, "DocumentRequest_dossier_requirement_id_idx");

        entity.HasIndex(e => e.RequestCode, "DocumentRequest_request_code_key").IsUnique();

        entity.HasIndex(e => e.RequestedAt, "DocumentRequest_requested_at_idx");

        entity.Property(e => e.DocumentRequestId).HasColumnName("document_request_id");
        entity.Property(e => e.CancellationReason).HasColumnName("cancellation_reason");
        entity.Property(e => e.CancelledAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("cancelled_at");
        entity.Property(e => e.CreatedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("created_at");
        entity.Property(e => e.DossierRequirementId).HasColumnName("dossier_requirement_id");
        entity.Property(e => e.RequestedByAccountId).HasColumnName("requested_by_account_id");
        entity.Property(e => e.DueDate).HasColumnName("due_date");
        entity.Property(e => e.FulfilledAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("fulfilled_at");
        entity.Property(e => e.Message).HasColumnName("message");
        entity.Property(e => e.RequestCode)
            .HasMaxLength(50)
            .HasColumnName("request_code");
        entity.Property(e => e.RequestedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("requested_at");
        entity.Property(e => e.UpdatedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("updated_at");

        entity.HasOne(d => d.DossierRequirement).WithMany(p => p.DocumentRequests)
            .HasForeignKey(d => d.DossierRequirementId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("DocumentRequest_dossier_requirement_id_fkey");
        entity.HasOne(d => d.RequestedByAccount).WithMany()
            .HasForeignKey(d => d.RequestedByAccountId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("DocumentRequest_requested_by_account_id_fkey");
    }
}
