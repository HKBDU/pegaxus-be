using System.Collections.Generic;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pegaxus.Repository.Entities;

namespace Pegaxus.Repository.Configurations;

public sealed class AuditDossierConfiguration : IEntityTypeConfiguration<AuditDossier>
{
    public void Configure(EntityTypeBuilder<AuditDossier> entity)
    {
        entity.HasKey(e => e.AuditDossierId).HasName("AuditDossier_pkey");
        entity.ToTable("AuditDossier");
        entity.HasIndex(e => e.ComplianceDossierId, "AuditDossier_compliance_dossier_id_idx");
        entity.HasIndex(e => e.ActorAccountId, "AuditDossier_actor_account_id_idx");
        entity.HasIndex(e => new { e.TargetEntity, e.TargetId }, "AuditDossier_target_entity_target_id_idx");
        entity.Property(e => e.AuditDossierId).HasColumnName("audit_dossier_id");
        entity.Property(e => e.ComplianceDossierId).HasColumnName("compliance_dossier_id");
        entity.Property(e => e.ActionType).HasMaxLength(100).HasColumnName("action_type");
        entity.Property(e => e.TargetEntity).HasMaxLength(100).HasColumnName("target_entity");
        entity.Property(e => e.TargetId).HasColumnName("target_id");
        entity.Property(e => e.ActorAccountId).HasColumnName("actor_account_id");
        entity.Property(e => e.Summary).HasColumnName("summary");
        entity.Property(e => e.Details)
            .HasConversion(
                value => value == null ? null : value.RootElement.GetRawText(),
                value => value == null ? null : JsonDocument.Parse(value, default))
            .HasColumnType("jsonb")
            .HasColumnName("details");
        entity.Property(e => e.CreatedAt).HasColumnType("timestamp without time zone").HasColumnName("created_at");
        entity.HasOne(e => e.ComplianceDossier).WithMany()
            .HasForeignKey(e => e.ComplianceDossierId).OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("AuditDossier_compliance_dossier_id_fkey");
        entity.HasOne(e => e.ActorAccount).WithMany()
            .HasForeignKey(e => e.ActorAccountId).OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("AuditDossier_actor_account_id_fkey");
    }
}
