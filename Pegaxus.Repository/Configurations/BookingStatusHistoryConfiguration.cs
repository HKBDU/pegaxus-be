using System.Collections.Generic;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pegaxus.Repository.Entities;

namespace Pegaxus.Repository.Configurations;

public sealed class BookingStatusHistoryConfiguration : IEntityTypeConfiguration<BookingStatusHistory>
{
    public void Configure(EntityTypeBuilder<BookingStatusHistory> entity)
    {
        entity.HasKey(e => e.BookingStatusHistoryId).HasName("BookingStatusHistory_pkey");

        entity.ToTable("BookingStatusHistory", tb => tb.HasComment("BookingStatusHistory: Lịch sử thay đổi trạng thái Booking. type cho biết hành động nghiệp vụ nào đã gây ra thay đổi status."));

        entity.HasIndex(e => e.BookingId, "BookingStatusHistory_booking_id_idx");

        entity.HasIndex(e => e.ChangedAt, "BookingStatusHistory_changed_at_idx");

        entity.HasIndex(e => e.ChangedByAccountId, "BookingStatusHistory_changed_by_account_id_idx");

        entity.Property(e => e.BookingStatusHistoryId).HasColumnName("booking_status_history_id");
        entity.Property(e => e.BookingId).HasColumnName("booking_id");
        entity.Property(e => e.ChangedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("changed_at");
        entity.Property(e => e.ChangedByAccountId).HasColumnName("changed_by_account_id");
        entity.Property(e => e.Reason).HasColumnName("reason");

        entity.HasOne(d => d.Booking).WithMany(p => p.BookingStatusHistories)
            .HasForeignKey(d => d.BookingId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("BookingStatusHistory_booking_id_fkey");

        entity.HasOne(d => d.ChangedByAccount).WithMany(p => p.BookingStatusHistories)
            .HasForeignKey(d => d.ChangedByAccountId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("BookingStatusHistory_changed_by_account_id_fkey");
    }
}
