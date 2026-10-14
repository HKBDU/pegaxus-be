using System.Collections.Generic;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pegaxus.Repository.Entities;

namespace Pegaxus.Repository.Configurations;

public sealed class AuthoritySubmissionConfiguration : IEntityTypeConfiguration<AuthoritySubmission>
{
    public void Configure(EntityTypeBuilder<AuthoritySubmission> entity)
    {
        entity.HasKey(e => e.AuthoritySubmissionId).HasName("AuthoritySubmission_pkey");

        entity.ToTable("AuthoritySubmission", tb => tb.HasComment("AuthoritySubmission: Một lần thực tế gửi ComplianceDossier tới cơ quan có thẩm quyền. Một dossier có thể được gửi nhiều lần nếu bị trả về hoặc từ chối."));

        entity.HasIndex(e => e.ComplianceDossierId, "AuthoritySubmission_compliance_dossier_id_idx");

        entity.HasIndex(e => new { e.ComplianceDossierId, e.SubmissionNo }, "AuthoritySubmission_compliance_dossier_id_submission_no_idx").IsUnique();

        entity.HasIndex(e => e.SubmissionReference, "AuthoritySubmission_submission_reference_idx");

        entity.Property(e => e.AuthoritySubmissionId).HasColumnName("authority_submission_id");
        entity.Property(e => e.AuthorityName)
            .HasMaxLength(200)
            .HasColumnName("authority_name");
        entity.Property(e => e.ComplianceDossierId).HasColumnName("compliance_dossier_id");
        entity.Property(e => e.SubmittedByAccountId).HasColumnName("submitted_by_account_id");
        entity.Property(e => e.CreatedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("created_at");
        entity.Property(e => e.DecisionNote).HasColumnName("decision_note");
        entity.Property(e => e.RejectionReason).HasColumnName("rejection_reason");
        entity.Property(e => e.ResponseReceivedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("response_received_at");
        entity.Property(e => e.SubmissionMethod)
            .HasMaxLength(100)
            .HasColumnName("submission_method");
        entity.Property(e => e.SubmissionNo).HasColumnName("submission_no");
        entity.Property(e => e.SubmissionReference)
            .HasMaxLength(150)
            .HasColumnName("submission_reference");
        entity.Property(e => e.IsRequired).HasDefaultValue(true).HasColumnName("is_required");
        entity.Property(e => e.PermitNumber).HasMaxLength(100).HasColumnName("permit_number");
        entity.Property(e => e.PermitIssuedDate).HasColumnName("permit_issued_date");
        entity.Property(e => e.PermitExpiryDate).HasColumnName("permit_expiry_date");
        entity.Property(e => e.RevisionDueDate).HasColumnName("revision_due_date");
        entity.Property(e => e.SubmittedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("submitted_at");
        entity.Property(e => e.UpdatedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("updated_at");

        entity.HasOne(d => d.ComplianceDossier).WithMany(p => p.AuthoritySubmissions)
            .HasForeignKey(d => d.ComplianceDossierId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("AuthoritySubmission_compliance_dossier_id_fkey");
        entity.HasOne(d => d.SubmittedByAccount).WithMany()
            .HasForeignKey(d => d.SubmittedByAccountId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("AuthoritySubmission_submitted_by_account_id_fkey");
    }
}
