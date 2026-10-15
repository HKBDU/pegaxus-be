using System.Collections.Generic;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pegaxus.Repository.Entities;

namespace Pegaxus.Repository.Configurations;

public sealed class VehicleConfiguration : IEntityTypeConfiguration<Vehicle>
{
    public void Configure(EntityTypeBuilder<Vehicle> entity)
    {
        entity.HasKey(e => e.VehicleId).HasName("Vehicle_pkey");

        entity.ToTable("Vehicle", tb => tb.HasComment("Vehicle: Phương tiện vận chuyển master dùng chung cho ROAD, AIR hoặc SEA, ví dụ xe tải chở ngựa, máy bay hoặc tàu. Vehicle không thuộc cố định một TransportPlan; phương tiện được gán vào từng chặng RoutePlanRoute."));

        entity.HasIndex(e => e.IsDeleted, "Vehicle_is_deleted_idx");

        entity.HasIndex(e => e.LicensePlate, "Vehicle_license_plate_key").IsUnique().HasFilter("is_deleted = false");

        entity.HasIndex(e => e.VehicleCode, "Vehicle_vehicle_code_key").IsUnique().HasFilter("is_deleted = false");

        entity.Property(e => e.VehicleId).HasColumnName("vehicle_id");
        entity.Property(e => e.CapacityHorses).HasColumnName("capacity_horses");
        entity.Property(e => e.CreatedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("created_at");
        entity.Property(e => e.DeletedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("deleted_at");
        entity.Property(e => e.DeletedByAccountId).HasColumnName("deleted_by_account_id");
        entity.Property(e => e.InsuranceExpiryDate).HasColumnName("insurance_expiry_date");
        entity.Property(e => e.InsuranceNumber)
            .HasMaxLength(100)
            .HasColumnName("insurance_number");
        entity.Property(e => e.IsDeleted)
            .HasDefaultValue(false)
            .HasColumnName("is_deleted");
        entity.Property(e => e.LicensePlate)
            .HasMaxLength(50)
            .HasColumnName("license_plate");
        entity.Property(e => e.Manufacturer)
            .HasMaxLength(100)
            .HasColumnName("manufacturer");
        entity.Property(e => e.Model)
            .HasMaxLength(100)
            .HasColumnName("model");
        entity.Property(e => e.RegistrationExpiryDate).HasColumnName("registration_expiry_date");
        entity.Property(e => e.RegistrationNumber)
            .HasMaxLength(100)
            .HasColumnName("registration_number");
        entity.Property(e => e.SpecialEquipment).HasColumnName("special_equipment");
        entity.Property(e => e.UpdatedAt)
            .IsConcurrencyToken()
            .HasColumnType("timestamp without time zone")
            .HasColumnName("updated_at");
        entity.Property(e => e.VehicleCode)
            .HasMaxLength(50)
            .HasColumnName("vehicle_code");
        entity.Property(e => e.VehicleName)
            .HasMaxLength(200)
            .HasColumnName("vehicle_name");
        entity.Property(e => e.VehicleType)
            .HasMaxLength(100)
            .HasColumnName("vehicle_type");

        entity.HasOne(d => d.DeletedByAccount).WithMany(p => p.Vehicles)
            .HasForeignKey(d => d.DeletedByAccountId)
            .HasConstraintName("Vehicle_deleted_by_account_id_fkey");
    }
}
