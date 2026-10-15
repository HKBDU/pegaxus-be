using System.Collections.Generic;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pegaxus.Repository.Entities;

namespace Pegaxus.Repository.Configurations;

public sealed class CatalogAuditLogConfiguration : IEntityTypeConfiguration<CatalogAuditLog>
{
    public void Configure(EntityTypeBuilder<CatalogAuditLog> entity)
    {
        entity.HasKey(e => e.CatalogAuditLogId).HasName("CatalogAuditLog_pkey");
        entity.ToTable("CatalogAuditLog");
        entity.HasIndex(e => new { e.EntityType, e.EntityId }, "CatalogAuditLog_entity_type_entity_id_idx");
        entity.HasIndex(e => e.ActorAccountId, "CatalogAuditLog_actor_account_id_idx");
        entity.HasIndex(e => e.CreatedAt, "CatalogAuditLog_created_at_idx");
        entity.Property(e => e.CatalogAuditLogId).HasColumnName("catalog_audit_log_id");
        entity.Property(e => e.EntityType).HasMaxLength(100).HasColumnName("entity_type");
        entity.Property(e => e.EntityId).HasColumnName("entity_id");
        entity.Property(e => e.ActionType).HasMaxLength(100).HasColumnName("action_type");
        entity.Property(e => e.ActorAccountId).HasColumnName("actor_account_id");
        entity.Property(e => e.Summary).HasColumnName("summary");
        entity.Property(e => e.BeforeData)
            .HasConversion(value => value.RootElement.GetRawText(), value => JsonDocument.Parse(value, default))
            .HasColumnType("jsonb")
            .HasColumnName("before_data");
        entity.Property(e => e.AfterData)
            .HasConversion(value => value.RootElement.GetRawText(), value => JsonDocument.Parse(value, default))
            .HasColumnType("jsonb")
            .HasColumnName("after_data");
        entity.Property(e => e.ReferenceCountSnapshot)
            .HasConversion(value => value == null ? null : value.RootElement.GetRawText(), value => value == null ? null : JsonDocument.Parse(value, default))
            .HasColumnType("jsonb")
            .HasColumnName("reference_count_snapshot");
        entity.Property(e => e.CreatedAt).HasColumnType("timestamp without time zone").HasColumnName("created_at");
        entity.HasOne(e => e.ActorAccount).WithMany()
            .HasForeignKey(e => e.ActorAccountId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("CatalogAuditLog_actor_account_id_fkey");
    }
}
