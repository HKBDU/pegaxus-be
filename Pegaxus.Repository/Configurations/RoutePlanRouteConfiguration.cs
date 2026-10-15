using System.Collections.Generic;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pegaxus.Repository.Entities;

namespace Pegaxus.Repository.Configurations;

public sealed class RoutePlanRouteConfiguration : IEntityTypeConfiguration<RoutePlanRoute>
{
    public void Configure(EntityTypeBuilder<RoutePlanRoute> entity)
    {
        entity.HasKey(e => e.RoutePlanRouteId).HasName("RoutePlanRoute_pkey");

        entity.ToTable("RoutePlanRoute", tb => tb.HasComment("RoutePlanRoute: Bảng trung gian biểu diễn một Route được dùng trong một RoutePlan, gồm thứ tự đi, Driver phụ trách và thời gian dự kiến/thực tế của chính lần sử dụng Route đó."));

        entity.HasIndex(e => e.DriverEmployeeId, "RoutePlanRoute_driver_employee_id_idx");

        entity.HasIndex(e => e.EscortEmployeeId, "RoutePlanRoute_escort_employee_id_idx");

        entity.HasIndex(e => e.TransportProviderId, "RoutePlanRoute_transport_provider_id_idx");

        entity.HasIndex(e => e.RouteId, "RoutePlanRoute_route_id_idx");

        entity.HasIndex(e => e.RoutePlanId, "RoutePlanRoute_route_plan_id_idx");

        entity.HasIndex(e => new { e.RoutePlanId, e.SequenceNo }, "RoutePlanRoute_route_plan_id_sequence_no_idx").IsUnique();

        entity.Property(e => e.RoutePlanRouteId).HasColumnName("route_plan_route_id");
        entity.Property(e => e.ActualArrivalAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("actual_arrival_at");
        entity.Property(e => e.ActualDepartureAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("actual_departure_at");
        entity.Property(e => e.CreatedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("created_at");
        entity.Property(e => e.DriverEmployeeId).HasColumnName("driver_employee_id");
        entity.Property(e => e.EscortEmployeeId).HasColumnName("escort_employee_id");
        entity.Property(e => e.Note).HasColumnName("note");
        entity.Property(e => e.PlannedArrivalAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("planned_arrival_at");
        entity.Property(e => e.PlannedDepartureAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("planned_departure_at");
        entity.Property(e => e.RouteId).HasColumnName("route_id");
        entity.Property(e => e.RoutePlanId).HasColumnName("route_plan_id");
        entity.Property(e => e.TransportProviderId).HasColumnName("transport_provider_id");
        entity.Property(e => e.ServiceType).HasMaxLength(100).HasColumnName("service_type");
        entity.Property(e => e.ProviderReference).HasMaxLength(255).HasColumnName("provider_reference");
        entity.Property(e => e.OutsourcingCostAmount).HasPrecision(18, 2).HasColumnName("outsourcing_cost_amount");
        entity.Property(e => e.OutsourcingCurrency).HasMaxLength(3).HasColumnName("outsourcing_currency");
        entity.Property(e => e.SequenceNo).HasColumnName("sequence_no");
        entity.Property(e => e.UpdatedAt)
            .IsConcurrencyToken()
            .HasColumnType("timestamp without time zone")
            .HasColumnName("updated_at");

        entity.HasOne(d => d.DriverEmployee).WithMany(p => p.RoutePlanRoutes)
            .HasForeignKey(d => d.DriverEmployeeId)
            .HasConstraintName("RoutePlanRoute_driver_employee_id_fkey");

        entity.HasOne(d => d.EscortEmployee).WithMany(p => p.EscortRoutePlanRoutes)
            .HasForeignKey(d => d.EscortEmployeeId)
            .HasConstraintName("RoutePlanRoute_escort_employee_id_fkey");

        entity.HasOne(d => d.Route).WithMany(p => p.RoutePlanRoutes)
            .HasForeignKey(d => d.RouteId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("RoutePlanRoute_route_id_fkey");

        entity.HasOne(d => d.RoutePlan).WithMany(p => p.RoutePlanRoutes)
            .HasForeignKey(d => d.RoutePlanId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("RoutePlanRoute_route_plan_id_fkey");

        entity.HasOne(d => d.TransportProvider).WithMany(p => p.RoutePlanRoutes)
            .HasForeignKey(d => d.TransportProviderId)
            .HasConstraintName("RoutePlanRoute_transport_provider_id_fkey");
    }
}
