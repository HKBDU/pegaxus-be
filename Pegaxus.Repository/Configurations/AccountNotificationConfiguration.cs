using System.Collections.Generic;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pegaxus.Repository.Entities;

namespace Pegaxus.Repository.Configurations;

public sealed class AccountNotificationConfiguration : IEntityTypeConfiguration<AccountNotification>
{
    public void Configure(EntityTypeBuilder<AccountNotification> entity)
    {
        entity.HasKey(e => new { e.AccountId, e.NotificationId }).HasName("AccountNotification_pkey");

        entity.ToTable("AccountNotification");

        entity.HasIndex(e => e.NotificationId, "AccountNotification_notification_id_idx");

        entity.HasIndex(e => e.ReadAt, "AccountNotification_read_at_idx");

        entity.Property(e => e.AccountId).HasColumnName("account_id");
        entity.Property(e => e.NotificationId).HasColumnName("notification_id");
        entity.Property(e => e.CreatedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("created_at");
        entity.Property(e => e.DeliveredAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("delivered_at");
        entity.Property(e => e.ReadAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("read_at");

        entity.HasOne(d => d.Account).WithMany(p => p.AccountNotifications)
            .HasForeignKey(d => d.AccountId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("AccountNotification_account_id_fkey");

        entity.HasOne(d => d.Notification).WithMany(p => p.AccountNotifications)
            .HasForeignKey(d => d.NotificationId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("AccountNotification_notification_id_fkey");
    }
}
