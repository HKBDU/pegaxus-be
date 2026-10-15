using System.Collections.Generic;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pegaxus.Repository.Entities;

namespace Pegaxus.Repository.Configurations;

public sealed class ClaimConfiguration : IEntityTypeConfiguration<Claim>
{
    public void Configure(EntityTypeBuilder<Claim> entity)
    {
        entity.HasKey(e => e.ClaimId).HasName("Claim_pkey");

        entity.ToTable("Claim", tb => tb.HasComment("Claim: Khiếu nại do Customer gửi liên quan đến một lần bàn giao. Hệ thống chỉ lưu khiếu nại và kết quả; việc trao đổi/thương lượng giữa Customer và Manager diễn ra ngoài hệ thống."));

        entity.HasIndex(e => e.ClaimCode, "Claim_claim_code_key").IsUnique();

        entity.HasIndex(e => e.CustomerId, "Claim_customer_id_idx");

        entity.HasIndex(e => e.HandoverRecordId, "Claim_handover_record_id_idx");

        entity.HasIndex(e => e.SubmittedAt, "Claim_submitted_at_idx");

        entity.Property(e => e.ClaimId).HasColumnName("claim_id");
        entity.Property(e => e.CancellationReason).HasColumnName("cancellation_reason");
        entity.Property(e => e.CancelledAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("cancelled_at");
        entity.Property(e => e.ClaimCode)
            .HasMaxLength(50)
            .HasColumnName("claim_code");
        entity.Property(e => e.CreatedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("created_at");
        entity.Property(e => e.Currency)
            .HasMaxLength(3)
            .HasColumnName("currency");
        entity.Property(e => e.CustomerId).HasColumnName("customer_id");
        entity.Property(e => e.CustomerNote).HasColumnName("customer_note");
        entity.Property(e => e.Description).HasColumnName("description");
        entity.Property(e => e.EvidenceNote).HasColumnName("evidence_note");
        entity.Property(e => e.HandoverRecordId).HasColumnName("handover_record_id");
        entity.Property(e => e.RequestedCompensationAmount)
            .HasPrecision(18, 2)
            .HasColumnName("requested_compensation_amount");
        entity.Property(e => e.ResolvedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("resolved_at");
        entity.Property(e => e.ReviewedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("reviewed_at");
        entity.Property(e => e.SubmittedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("submitted_at");
        entity.Property(e => e.Title)
            .HasMaxLength(200)
            .HasColumnName("title");
        entity.Property(e => e.UpdatedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("updated_at");

        entity.HasOne(d => d.Customer).WithMany(p => p.Claims)
            .HasForeignKey(d => d.CustomerId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("Claim_customer_id_fkey");

        entity.HasOne(d => d.HandoverRecord).WithMany(p => p.Claims)
            .HasForeignKey(d => d.HandoverRecordId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("Claim_handover_record_id_fkey");
    }
}
