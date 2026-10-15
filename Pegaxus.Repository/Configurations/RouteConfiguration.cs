using System.Collections.Generic;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pegaxus.Repository.Entities;

namespace Pegaxus.Repository.Configurations;

public sealed class RouteConfiguration : IEntityTypeConfiguration<Route>
{
    public void Configure(EntityTypeBuilder<Route> entity)
    {
        entity.HasKey(e => e.RouteId).HasName("Route_pkey");

        entity.ToTable("Route", tb => tb.HasComment("Route: Một tuyến đường có hướng nối từ một Location bắt đầu đến một Location kết thúc. Route có thể được tái sử dụng trong nhiều RoutePlan."));

        entity.HasIndex(e => e.FromLocationId, "Route_from_location_id_idx");

        entity.HasIndex(e => e.IsDeleted, "Route_is_deleted_idx");

        entity.HasIndex(e => e.RouteCode, "Route_route_code_key").IsUnique();

        entity.HasIndex(e => e.ToLocationId, "Route_to_location_id_idx");

        entity.Property(e => e.RouteId).HasColumnName("route_id");
        entity.Property(e => e.CreatedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("created_at");
        entity.Property(e => e.DeletedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("deleted_at");
        entity.Property(e => e.DeletedByAccountId).HasColumnName("deleted_by_account_id");
        entity.Property(e => e.DistanceKm)
            .HasPrecision(10, 2)
            .HasColumnName("distance_km");
        entity.Property(e => e.EstimatedDurationMinutes).HasColumnName("estimated_duration_minutes");
        entity.Property(e => e.FromLocationId).HasColumnName("from_location_id");
        entity.Property(e => e.IsDeleted)
            .HasDefaultValue(false)
            .HasColumnName("is_deleted");
        entity.Property(e => e.RouteCode)
            .HasMaxLength(50)
            .HasColumnName("route_code");
        entity.Property(e => e.RouteName)
            .HasMaxLength(200)
            .HasColumnName("route_name");
        entity.Property(e => e.RouteNote).HasColumnName("route_note");
        entity.Property(e => e.ToLocationId).HasColumnName("to_location_id");
        entity.Property(e => e.UpdatedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("updated_at");

        entity.HasOne(d => d.DeletedByAccount).WithMany(p => p.Routes)
            .HasForeignKey(d => d.DeletedByAccountId)
            .HasConstraintName("Route_deleted_by_account_id_fkey");

        entity.HasOne(d => d.FromLocation).WithMany(p => p.RouteFromLocations)
            .HasForeignKey(d => d.FromLocationId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("Route_from_location_id_fkey");

        entity.HasOne(d => d.ToLocation).WithMany(p => p.RouteToLocations)
            .HasForeignKey(d => d.ToLocationId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("Route_to_location_id_fkey");
    }
}
