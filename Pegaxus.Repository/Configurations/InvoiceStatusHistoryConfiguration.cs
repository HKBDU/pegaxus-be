using System.Collections.Generic;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pegaxus.Repository.Entities;

namespace Pegaxus.Repository.Configurations;

public sealed class InvoiceStatusHistoryConfiguration : IEntityTypeConfiguration<InvoiceStatusHistory>
{
    public void Configure(EntityTypeBuilder<InvoiceStatusHistory> entity)
    {
        entity.HasKey(e => e.InvoiceStatusHistoryId).HasName("InvoiceStatusHistory_pkey");

        entity.ToTable("InvoiceStatusHistory", tb => tb.HasComment("InvoiceStatusHistory: Lịch sử thay đổi trạng thái Invoice. type cho biết hành động nghiệp vụ gây ra thay đổi; changed_by_account_id có thể null nếu hệ thống tự động thực hiện, ví dụ MARK_OVERDUE."));

        entity.HasIndex(e => e.ChangedAt, "InvoiceStatusHistory_changed_at_idx");

        entity.HasIndex(e => e.ChangedByAccountId, "InvoiceStatusHistory_changed_by_account_id_idx");

        entity.HasIndex(e => e.InvoiceId, "InvoiceStatusHistory_invoice_id_idx");

        entity.Property(e => e.InvoiceStatusHistoryId).HasColumnName("invoice_status_history_id");
        entity.Property(e => e.ChangedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("changed_at");
        entity.Property(e => e.ChangedByAccountId).HasColumnName("changed_by_account_id");
        entity.Property(e => e.CreatedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("created_at");
        entity.Property(e => e.InvoiceId).HasColumnName("invoice_id");
        entity.Property(e => e.Reason).HasColumnName("reason");

        entity.HasOne(d => d.ChangedByAccount).WithMany(p => p.InvoiceStatusHistories)
            .HasForeignKey(d => d.ChangedByAccountId)
            .HasConstraintName("InvoiceStatusHistory_changed_by_account_id_fkey");

        entity.HasOne(d => d.Invoice).WithMany(p => p.InvoiceStatusHistories)
            .HasForeignKey(d => d.InvoiceId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("InvoiceStatusHistory_invoice_id_fkey");
    }
}
