using System.Collections.Generic;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pegaxus.Repository.Entities;

namespace Pegaxus.Repository.Configurations;

public sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> entity)
    {
        entity.HasKey(e => e.NotificationId).HasName("Notification_pkey");

        entity.ToTable("Notification");

        entity.HasIndex(e => e.CreatedAt, "Notification_created_at_idx");

        entity.HasIndex(e => new { e.ReferenceType, e.ReferenceId }, "Notification_reference_type_reference_id_idx");

        entity.HasIndex(e => e.TransportId, "Notification_transport_id_idx");

        entity.Property(e => e.NotificationId).HasColumnName("notification_id");
        entity.Property(e => e.ActionUrl)
            .HasMaxLength(1000)
            .HasColumnName("action_url");
        entity.Property(e => e.CreatedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("created_at");
        entity.Property(e => e.ExpiresAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("expires_at");
        entity.Property(e => e.Message).HasColumnName("message");
        entity.Property(e => e.ReferenceId).HasColumnName("reference_id");
        entity.Property(e => e.ReferenceType)
            .HasMaxLength(100)
            .HasColumnName("reference_type");
        entity.Property(e => e.Title)
            .HasMaxLength(200)
            .HasColumnName("title");
        entity.Property(e => e.TransportId).HasColumnName("transport_id");

        entity.HasOne(d => d.Transport).WithMany(p => p.Notifications)
            .HasForeignKey(d => d.TransportId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("Notification_transport_id_fkey");
    }
}
