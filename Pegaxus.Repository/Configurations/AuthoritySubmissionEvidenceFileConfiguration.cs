using System.Collections.Generic;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pegaxus.Repository.Entities;

namespace Pegaxus.Repository.Configurations;

public sealed class AuthoritySubmissionEvidenceFileConfiguration : IEntityTypeConfiguration<AuthoritySubmissionEvidenceFile>
{
    public void Configure(EntityTypeBuilder<AuthoritySubmissionEvidenceFile> entity)
    {
        entity.HasKey(e => e.AuthoritySubmissionEvidenceFileId).HasName("AuthoritySubmissionEvidenceFile_pkey");
        entity.ToTable("AuthoritySubmissionEvidenceFile");
        entity.HasIndex(e => e.AuthoritySubmissionId, "AuthoritySubmissionEvidenceFile_authority_submission_id_idx");
        entity.HasIndex(e => e.UploadedByEmployeeId, "AuthoritySubmissionEvidenceFile_uploaded_by_employee_id_idx");
        entity.Property(e => e.AuthoritySubmissionEvidenceFileId).HasColumnName("authority_submission_evidence_file_id");
        entity.Property(e => e.AuthoritySubmissionId).HasColumnName("authority_submission_id");
        entity.Property(e => e.UploadedByEmployeeId).HasColumnName("uploaded_by_employee_id");
        entity.Property(e => e.FileName).HasMaxLength(255).HasColumnName("file_name");
        entity.Property(e => e.FileUrl).HasMaxLength(1000).HasColumnName("file_url");
        entity.Property(e => e.MimeType).HasMaxLength(100).HasColumnName("mime_type");
        entity.Property(e => e.FileSizeBytes).HasColumnName("file_size_bytes");
        entity.Property(e => e.Note).HasColumnName("note");
        entity.Property(e => e.CreatedAt).HasColumnType("timestamp without time zone").HasColumnName("created_at");
        entity.HasOne(e => e.AuthoritySubmission).WithMany(e => e.EvidenceFiles)
            .HasForeignKey(e => e.AuthoritySubmissionId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("AuthoritySubmissionEvidenceFile_authority_submission_id_fkey");
        entity.HasOne(e => e.UploadedByEmployee).WithMany()
            .HasForeignKey(e => e.UploadedByEmployeeId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("AuthoritySubmissionEvidenceFile_uploaded_by_employee_id_fkey");
    }
}
