using System.Collections.Generic;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pegaxus.Repository.Entities;

namespace Pegaxus.Repository.Configurations;

public sealed class QuarantineProofConfiguration : IEntityTypeConfiguration<QuarantineProof>
{
    public void Configure(EntityTypeBuilder<QuarantineProof> entity)
    {
        entity.HasKey(e => e.QuarantineProofId).HasName("QuarantineProof_pkey");
        entity.ToTable("QuarantineProof");
        entity.HasIndex(e => e.ComplianceDossierId, "QuarantineProof_compliance_dossier_id_idx");
        entity.HasIndex(e => e.SubmittedByEmployeeId, "QuarantineProof_submitted_by_employee_id_idx");
        entity.HasIndex(e => e.VerifiedByEmployeeId, "QuarantineProof_verified_by_employee_id_idx");
        entity.HasIndex(e => e.LocationId, "QuarantineProof_location_id_idx");
        entity.Property(e => e.QuarantineProofId).HasColumnName("quarantine_proof_id");
        entity.Property(e => e.ComplianceDossierId).HasColumnName("compliance_dossier_id");
        entity.Property(e => e.SubmittedByEmployeeId).HasColumnName("submitted_by_employee_id");
        entity.Property(e => e.VerifiedByEmployeeId).HasColumnName("verified_by_employee_id");
        entity.Property(e => e.LocationId).HasColumnName("location_id");
        entity.Property(e => e.BcpName).HasMaxLength(200).HasColumnName("bcp_name");
        entity.Property(e => e.OfficialVeterinarianName).HasMaxLength(200).HasColumnName("official_veterinarian_name");
        entity.Property(e => e.BcpReceiptNumber).HasMaxLength(100).HasColumnName("bcp_receipt_number");
        entity.Property(e => e.SubmittedNote).HasColumnName("submitted_note");
        entity.Property(e => e.MicrochipChecklist)
            .HasConversion(
                value => value == null ? null : value.RootElement.GetRawText(),
                value => value == null ? null : JsonDocument.Parse(value, default))
            .HasColumnType("jsonb")
            .HasColumnName("microchip_checklist");
        entity.Property(e => e.InspectionAt).HasColumnType("timestamp without time zone").HasColumnName("inspection_at");
        entity.Property(e => e.SubmittedAt).HasColumnType("timestamp without time zone").HasColumnName("submitted_at");
        entity.Property(e => e.VerifiedAt).HasColumnType("timestamp without time zone").HasColumnName("verified_at");
        entity.Property(e => e.Conditions).HasColumnName("conditions");
        entity.Property(e => e.VerificationNote).HasColumnName("verification_note");
        entity.Property(e => e.RejectionReason).HasColumnName("rejection_reason");
        entity.Property(e => e.IsFinalized).HasDefaultValue(false).HasColumnName("is_finalized");
        entity.Property(e => e.FinalizedAt).HasColumnType("timestamp without time zone").HasColumnName("finalized_at");
        entity.Property(e => e.CreatedAt).HasColumnType("timestamp without time zone").HasColumnName("created_at");
        entity.Property(e => e.UpdatedAt).HasColumnType("timestamp without time zone").HasColumnName("updated_at");
        entity.HasOne(e => e.ComplianceDossier).WithMany()
            .HasForeignKey(e => e.ComplianceDossierId).OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("QuarantineProof_compliance_dossier_id_fkey");
        entity.HasOne(e => e.SubmittedByEmployee).WithMany()
            .HasForeignKey(e => e.SubmittedByEmployeeId).OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("QuarantineProof_submitted_by_employee_id_fkey");
        entity.HasOne(e => e.VerifiedByEmployee).WithMany()
            .HasForeignKey(e => e.VerifiedByEmployeeId).OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("QuarantineProof_verified_by_employee_id_fkey");
        entity.HasOne<Location>().WithMany()
            .HasForeignKey(e => e.LocationId).OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("QuarantineProof_location_id_fkey");
    }
}
