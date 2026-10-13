using System.Collections.Generic;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pegaxus.Repository.Entities;

namespace Pegaxus.Repository.Configurations;

public sealed class TransportPlanProviderConfiguration : IEntityTypeConfiguration<TransportPlanProvider>
{
    public void Configure(EntityTypeBuilder<TransportPlanProvider> entity)
    {
        entity.HasKey(e => new { e.TransportPlanId, e.TransportProviderId }).HasName("TransportPlanProvider_pkey");

        entity.ToTable("TransportPlanProvider");

        entity.HasIndex(e => e.TransportProviderId, "TransportPlanProvider_transport_provider_id_idx");

        entity.Property(e => e.TransportPlanId).HasColumnName("transport_plan_id");
        entity.Property(e => e.TransportProviderId).HasColumnName("transport_provider_id");
        entity.Property(e => e.AssignedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("assigned_at");
        entity.Property(e => e.Note).HasColumnName("note");
        entity.Property(e => e.ServiceType)
            .HasMaxLength(100)
            .HasColumnName("service_type");

        entity.HasOne(d => d.TransportPlan).WithMany(p => p.TransportPlanProviders)
            .HasForeignKey(d => d.TransportPlanId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("TransportPlanProvider_transport_plan_id_fkey");

        entity.HasOne(d => d.TransportProvider).WithMany(p => p.TransportPlanProviders)
            .HasForeignKey(d => d.TransportProviderId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("TransportPlanProvider_transport_provider_id_fkey");
    }
}
