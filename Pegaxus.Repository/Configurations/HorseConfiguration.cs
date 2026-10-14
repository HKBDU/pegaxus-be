using System.Collections.Generic;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pegaxus.Repository.Entities;

namespace Pegaxus.Repository.Configurations;

public sealed class HorseConfiguration : IEntityTypeConfiguration<Horse>
{
    public void Configure(EntityTypeBuilder<Horse> entity)
    {
        entity.HasKey(e => e.HorseId).HasName("Horse_pkey");

        entity.ToTable("Horse");

        entity.HasIndex(e => e.CustomerId, "Horse_customer_id_idx");

        entity.HasIndex(e => e.HorseName, "Horse_horse_name_idx");

        entity.HasIndex(e => e.IsDeleted, "Horse_is_deleted_idx");

        entity.HasIndex(e => e.MicrochipNumber, "Horse_microchip_number_key").IsUnique();

        entity.HasIndex(e => e.PassportNumber, "Horse_passport_number_key").IsUnique();

        entity.Property(e => e.HorseId).HasColumnName("horse_id");
        entity.Property(e => e.Breed)
            .HasMaxLength(100)
            .HasColumnName("breed");
        entity.Property(e => e.Color)
            .HasMaxLength(100)
            .HasColumnName("color");
        entity.Property(e => e.CountryOfOrigin)
            .HasMaxLength(100)
            .HasColumnName("country_of_origin");
        entity.Property(e => e.CreatedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("created_at");
        entity.Property(e => e.CustomerId).HasColumnName("customer_id");
        entity.Property(e => e.DateOfBirth).HasColumnName("date_of_birth");
        entity.Property(e => e.DeletedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("deleted_at");
        entity.Property(e => e.DeletedByAccountId).HasColumnName("deleted_by_account_id");
        entity.Property(e => e.HeightCm)
            .HasPrecision(5, 2)
            .HasColumnName("height_cm");
        entity.Property(e => e.HorseName)
            .HasMaxLength(150)
            .HasColumnName("horse_name");
        entity.Property(e => e.IdentificationNotes).HasColumnName("identification_notes");
        entity.Property(e => e.IsDeleted)
            .HasDefaultValue(false)
            .HasColumnName("is_deleted");
        entity.Property(e => e.MicrochipNumber)
            .HasMaxLength(100)
            .HasColumnName("microchip_number");
        entity.Property(e => e.PassportNumber)
            .HasMaxLength(100)
            .HasColumnName("passport_number");
        entity.Property(e => e.UpdatedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("updated_at");
        entity.Property(e => e.WeightKg)
            .HasPrecision(6, 2)
            .HasColumnName("weight_kg");

        entity.HasOne(d => d.Customer).WithMany(p => p.Horses)
            .HasForeignKey(d => d.CustomerId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("Horse_customer_id_fkey");

        entity.HasOne(d => d.DeletedByAccount).WithMany(p => p.Horses)
            .HasForeignKey(d => d.DeletedByAccountId)
            .HasConstraintName("Horse_deleted_by_account_id_fkey");
    }
}
