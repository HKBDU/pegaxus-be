using System.Collections.Generic;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pegaxus.Repository.Entities;

namespace Pegaxus.Repository.Configurations;

public sealed class CustomsClearanceProofConfiguration : IEntityTypeConfiguration<CustomsClearanceProof>
{
    public void Configure(EntityTypeBuilder<CustomsClearanceProof> entity)
    {
        entity.HasKey(e => e.CustomsClearanceProofId).HasName("CustomsClearanceProof_pkey");
        entity.ToTable("CustomsClearanceProof");
        entity.HasIndex(e => e.ComplianceDossierId, "CustomsClearanceProof_compliance_dossier_id_idx");
        entity.HasIndex(e => e.SubmittedByEmployeeId, "CustomsClearanceProof_submitted_by_employee_id_idx");
        entity.HasIndex(e => e.VerifiedByEmployeeId, "CustomsClearanceProof_verified_by_employee_id_idx");
        entity.HasIndex(e => e.LocationId, "CustomsClearanceProof_location_id_idx");
        entity.Property(e => e.CustomsClearanceProofId).HasColumnName("customs_clearance_proof_id");
        entity.Property(e => e.ComplianceDossierId).HasColumnName("compliance_dossier_id");
        entity.Property(e => e.SubmittedByEmployeeId).HasColumnName("submitted_by_employee_id");
        entity.Property(e => e.VerifiedByEmployeeId).HasColumnName("verified_by_employee_id");
        entity.Property(e => e.LocationId).HasColumnName("location_id");
        entity.Property(e => e.CustomsPortName).HasMaxLength(200).HasColumnName("customs_port_name");
        entity.Property(e => e.MrnNumber).HasMaxLength(100).HasColumnName("mrn_number");
        entity.Property(e => e.SubmittedNote).HasColumnName("submitted_note");
        entity.Property(e => e.AtaCarnetExpiryDate).HasColumnName("ata_carnet_expiry_date");
        entity.Property(e => e.ClearanceAt).HasColumnType("timestamp without time zone").HasColumnName("clearance_at");
        entity.Property(e => e.SubmittedAt).HasColumnType("timestamp without time zone").HasColumnName("submitted_at");
        entity.Property(e => e.VerifiedAt).HasColumnType("timestamp without time zone").HasColumnName("verified_at");
        entity.Property(e => e.VerificationNote).HasColumnName("verification_note");
        entity.Property(e => e.RejectionReason).HasColumnName("rejection_reason");
        entity.Property(e => e.IsFinalized).HasDefaultValue(false).HasColumnName("is_finalized");
        entity.Property(e => e.FinalizedAt).HasColumnType("timestamp without time zone").HasColumnName("finalized_at");
        entity.Property(e => e.CreatedAt).HasColumnType("timestamp without time zone").HasColumnName("created_at");
        entity.Property(e => e.UpdatedAt).HasColumnType("timestamp without time zone").HasColumnName("updated_at");
        entity.HasOne(e => e.ComplianceDossier).WithMany()
            .HasForeignKey(e => e.ComplianceDossierId).OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("CustomsClearanceProof_compliance_dossier_id_fkey");
        entity.HasOne(e => e.SubmittedByEmployee).WithMany()
            .HasForeignKey(e => e.SubmittedByEmployeeId).OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("CustomsClearanceProof_submitted_by_employee_id_fkey");
        entity.HasOne(e => e.VerifiedByEmployee).WithMany()
            .HasForeignKey(e => e.VerifiedByEmployeeId).OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("CustomsClearanceProof_verified_by_employee_id_fkey");
        entity.HasOne<Location>().WithMany()
            .HasForeignKey(e => e.LocationId).OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("CustomsClearanceProof_location_id_fkey");
    }
}
