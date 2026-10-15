using System.Collections.Generic;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pegaxus.Repository.Entities;

namespace Pegaxus.Repository.Configurations;

public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> entity)
    {
        entity.HasKey(e => e.OutboxMessageId).HasName("OutboxMessage_pkey");
        entity.ToTable("OutboxMessage");
        entity.HasIndex(e => new { e.Status, e.AvailableAt }, "OutboxMessage_pending_idx");
        entity.HasIndex(e => e.CreatedAt, "OutboxMessage_created_at_idx");
        entity.Property(e => e.OutboxMessageId).HasColumnName("outbox_message_id");
        entity.Property(e => e.MessageType).HasMaxLength(100).HasColumnName("message_type");
        entity.Property(e => e.Payload).HasColumnName("payload");
        entity.Property(e => e.Status).HasMaxLength(20).HasColumnName("status");
        entity.Property(e => e.Attempts).HasColumnName("attempts");
        entity.Property(e => e.AvailableAt).HasColumnType("timestamp without time zone").HasColumnName("available_at");
        entity.Property(e => e.LockedAt).HasColumnType("timestamp without time zone").HasColumnName("locked_at");
        entity.Property(e => e.ProcessedAt).HasColumnType("timestamp without time zone").HasColumnName("processed_at");
        entity.Property(e => e.LastError).HasColumnName("last_error");
        entity.Property(e => e.CreatedAt).HasColumnType("timestamp without time zone").HasColumnName("created_at");
        entity.Property(e => e.UpdatedAt).HasColumnType("timestamp without time zone").HasColumnName("updated_at");
    }
}
