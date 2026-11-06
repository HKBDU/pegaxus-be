using System.Collections.Generic;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pegaxus.Repository.Entities;

namespace Pegaxus.Repository.Configurations;

public sealed class VehicleIncidentDetailConfiguration : IEntityTypeConfiguration<VehicleIncidentDetail>
{
    public void Configure(EntityTypeBuilder<VehicleIncidentDetail> entity)
    {
        entity.HasKey(e => e.VehicleIncidentDetailId).HasName("VehicleIncidentDetail_pkey");

        entity.ToTable("VehicleIncidentDetail");

        entity.HasIndex(e => e.IncidentReportId, "VehicleIncidentDetail_incident_report_id_key").IsUnique();

        entity.HasIndex(e => e.VehicleId, "VehicleIncidentDetail_vehicle_id_idx");

        entity.Property(e => e.VehicleIncidentDetailId).HasColumnName("vehicle_incident_detail_id");
        entity.Property(e => e.CreatedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("created_at");
        entity.Property(e => e.DamageDescription).HasColumnName("damage_description");
        entity.Property(e => e.EstimatedRepairCost)
            .HasPrecision(18, 2)
            .HasColumnName("estimated_repair_cost");
        entity.Property(e => e.EstimatedRepairHours)
            .HasPrecision(4, 1)
            .HasColumnName("estimated_repair_hours");
        entity.Property(e => e.DriverSafetyStatus).HasColumnName("driver_safety_status");
        entity.Property(e => e.CargoConditionNote).HasColumnName("cargo_condition_note");
        entity.Property(e => e.IncidentReportId).HasColumnName("incident_report_id");
        entity.Property(e => e.RepairNote).HasColumnName("repair_note");
        entity.Property(e => e.RepairRequired)
            .HasDefaultValue(false)
            .HasColumnName("repair_required");
        entity.Property(e => e.ReplacementVehicleRequired)
            .HasDefaultValue(false)
            .HasColumnName("replacement_vehicle_required");
        entity.Property(e => e.TowingRequired)
            .HasDefaultValue(false)
            .HasColumnName("towing_required");
        entity.Property(e => e.UpdatedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("updated_at");
        entity.Property(e => e.VehicleCondition).HasColumnName("vehicle_condition");
        entity.Property(e => e.VehicleId).HasColumnName("vehicle_id");
        entity.Property(e => e.VehicleOperable)
            .HasDefaultValue(true)
            .HasColumnName("vehicle_operable");

        entity.HasOne(d => d.IncidentReport).WithOne(p => p.VehicleIncidentDetail)
            .HasForeignKey<VehicleIncidentDetail>(d => d.IncidentReportId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("VehicleIncidentDetail_incident_report_id_fkey");

        entity.HasOne(d => d.Vehicle).WithMany(p => p.VehicleIncidentDetails)
            .HasForeignKey(d => d.VehicleId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("VehicleIncidentDetail_vehicle_id_fkey");
    }
}
