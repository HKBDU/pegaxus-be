using System.Collections.Generic;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pegaxus.Repository.Entities;

namespace Pegaxus.Repository.Configurations;

public sealed class LocationConfiguration : IEntityTypeConfiguration<Location>
{
    public void Configure(EntityTypeBuilder<Location> entity)
    {
        entity.HasKey(e => e.LocationId).HasName("Location_pkey");

        entity.ToTable("Location", tb => tb.HasComment("Location: Địa điểm master trên bản đồ. Một địa điểm chỉ tạo một lần và có thể được nhiều Route tái sử dụng, ví dụ Thành phố Hồ Chí Minh."));

        entity.HasIndex(e => e.Country, "Location_country_idx");

        entity.HasIndex(e => e.IsDeleted, "Location_is_deleted_idx");

        entity.HasIndex(e => e.LocationName, "Location_location_name_idx");

        entity.Property(e => e.LocationId).HasColumnName("location_id");
        entity.Property(e => e.Address)
            .HasMaxLength(500)
            .HasColumnName("address");
        entity.Property(e => e.City)
            .HasMaxLength(100)
            .HasColumnName("city");
        entity.Property(e => e.Country)
            .HasMaxLength(100)
            .HasColumnName("country");
        entity.Property(e => e.CreatedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("created_at");
        entity.Property(e => e.DeletedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("deleted_at");
        entity.Property(e => e.DeletedByAccountId).HasColumnName("deleted_by_account_id");
        entity.Property(e => e.IsDeleted)
            .HasDefaultValue(false)
            .HasColumnName("is_deleted");
        entity.Property(e => e.Latitude)
            .HasPrecision(10, 4)
            .HasColumnName("latitude");
        entity.Property(e => e.LocationName)
            .HasMaxLength(200)
            .HasColumnName("location_name");
        entity.Property(e => e.LocationNote).HasColumnName("location_note");
        entity.Property(e => e.Longitude)
            .HasPrecision(10, 4)
            .HasColumnName("longitude");
        entity.Property(e => e.StateProvince)
            .HasMaxLength(100)
            .HasColumnName("state_province");
        entity.Property(e => e.UpdatedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("updated_at");

        entity.HasOne(d => d.DeletedByAccount).WithMany(p => p.Locations)
            .HasForeignKey(d => d.DeletedByAccountId)
            .HasConstraintName("Location_deleted_by_account_id_fkey");
    }
}
