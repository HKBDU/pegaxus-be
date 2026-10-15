using System.Collections.Generic;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pegaxus.Repository.Entities;

namespace Pegaxus.Repository.Configurations;

public sealed class RoutePlanRouteVehicleConfiguration : IEntityTypeConfiguration<RoutePlanRouteVehicle>
{
    public void Configure(EntityTypeBuilder<RoutePlanRouteVehicle> entity)
    {
        entity.HasKey(e => new { e.RoutePlanRouteId, e.VehicleId }).HasName("RoutePlanRouteVehicle_pkey");

        entity.ToTable("RoutePlanRouteVehicle", tb => tb.HasComment("RoutePlanRouteVehicle: Bảng trung gian N-N gán Vehicle vào một chặng RoutePlanRoute cụ thể. Nhờ vậy một Transport có thể đi xe -> máy bay -> xe, và cùng một Vehicle có thể được tái sử dụng ở nhiều chuyến/chặng khác nhau."));

        entity.HasIndex(e => e.VehicleId, "RoutePlanRouteVehicle_vehicle_id_idx");

        entity.Property(e => e.RoutePlanRouteId).HasColumnName("route_plan_route_id");
        entity.Property(e => e.VehicleId).HasColumnName("vehicle_id");
        entity.Property(e => e.AssignedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("assigned_at");
        entity.Property(e => e.Note).HasColumnName("note");

        entity.HasOne(d => d.RoutePlanRoute).WithMany(p => p.RoutePlanRouteVehicles)
            .HasForeignKey(d => d.RoutePlanRouteId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("RoutePlanRouteVehicle_route_plan_route_id_fkey");

        entity.HasOne(d => d.Vehicle).WithMany(p => p.RoutePlanRouteVehicles)
            .HasForeignKey(d => d.VehicleId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("RoutePlanRouteVehicle_vehicle_id_fkey");
    }
}
