using System.Collections.Generic;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pegaxus.Repository.Entities;

namespace Pegaxus.Repository.Configurations;

public sealed class RoutePlanConfiguration : IEntityTypeConfiguration<RoutePlan>
{
    public void Configure(EntityTypeBuilder<RoutePlan> entity)
    {
        entity.HasKey(e => e.RoutePlanId).HasName("RoutePlan_pkey");

        entity.ToTable("RoutePlan", tb => tb.HasComment("RoutePlan: Kế hoạch tuyến đường của một TransportPlan. Đây là tập hợp các Route được sắp xếp nối tiếp từ điểm đầu đến điểm cuối."));

        entity.HasIndex(e => e.RoutePlanCode, "RoutePlan_route_plan_code_key").IsUnique();

        entity.HasIndex(e => e.TransportPlanId, "RoutePlan_transport_plan_id_idx");

        entity.Property(e => e.RoutePlanId).HasColumnName("route_plan_id");
        entity.Property(e => e.CreatedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("created_at");
        entity.Property(e => e.EstimatedDurationMinutes).HasColumnName("estimated_duration_minutes");
        entity.Property(e => e.RouteNote).HasColumnName("route_note");
        entity.Property(e => e.RoutePlanCode)
            .HasMaxLength(50)
            .HasColumnName("route_plan_code");
        entity.Property(e => e.TotalDistanceKm)
            .HasPrecision(10, 2)
            .HasColumnName("total_distance_km");
        entity.Property(e => e.TransportPlanId).HasColumnName("transport_plan_id");
        entity.Property(e => e.UpdatedAt)
            .IsConcurrencyToken()
            .HasColumnType("timestamp without time zone")
            .HasColumnName("updated_at");

        entity.HasOne(d => d.TransportPlan).WithMany(p => p.RoutePlans)
            .HasForeignKey(d => d.TransportPlanId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("RoutePlan_transport_plan_id_fkey");
    }
}
