using System.Collections.Generic;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pegaxus.Repository.Entities;

namespace Pegaxus.Repository.Configurations;

public sealed class ClaimResolutionConfiguration : IEntityTypeConfiguration<ClaimResolution>
{
    public void Configure(EntityTypeBuilder<ClaimResolution> entity)
    {
        entity.HasKey(e => e.ClaimResolutionId).HasName("ClaimResolution_pkey");

        entity.ToTable("ClaimResolution", tb => tb.HasComment("ClaimResolution: Kết quả xử lý/thỏa thuận cuối cùng của một Claim, ví dụ mức bồi thường thực tế sau khi hai bên trao đổi trực tiếp."));

        entity.HasIndex(e => e.ClaimId, "ClaimResolution_claim_id_key").IsUnique();

        entity.HasIndex(e => e.ResolvedAt, "ClaimResolution_resolved_at_idx");

        entity.Property(e => e.ClaimResolutionId).HasColumnName("claim_resolution_id");
        entity.Property(e => e.ApprovedCompensationAmount)
            .HasPrecision(18, 2)
            .HasColumnName("approved_compensation_amount");
        entity.Property(e => e.ClaimId).HasColumnName("claim_id");
        entity.Property(e => e.CorrectiveAction).HasColumnName("corrective_action");
        entity.Property(e => e.CreatedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("created_at");
        entity.Property(e => e.Currency)
            .HasMaxLength(3)
            .HasColumnName("currency");
        entity.Property(e => e.InternalNote).HasColumnName("internal_note");
        entity.Property(e => e.ResolutionSummary).HasColumnName("resolution_summary");
        entity.Property(e => e.ResolvedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("resolved_at");
        entity.Property(e => e.UpdatedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("updated_at");

        entity.HasOne(d => d.Claim).WithOne(p => p.ClaimResolution)
            .HasForeignKey<ClaimResolution>(d => d.ClaimId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("ClaimResolution_claim_id_fkey");
    }
}
