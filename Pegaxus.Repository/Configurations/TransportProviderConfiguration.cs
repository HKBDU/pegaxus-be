using System.Collections.Generic;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pegaxus.Repository.Entities;

namespace Pegaxus.Repository.Configurations;

public sealed class TransportProviderConfiguration : IEntityTypeConfiguration<TransportProvider>
{
    public void Configure(EntityTypeBuilder<TransportProvider> entity)
    {
        entity.HasKey(e => e.TransportProviderId).HasName("TransportProvider_pkey");

        entity.ToTable("TransportProvider");

        entity.HasIndex(e => e.IsDeleted, "TransportProvider_is_deleted_idx");

        entity.HasIndex(e => e.ProviderCode, "TransportProvider_provider_code_key").HasFilter("is_deleted = false").IsUnique();

        entity.HasIndex(e => e.TaxCode, "TransportProvider_tax_code_key").HasFilter("is_deleted = false").IsUnique();

        entity.HasIndex(e => e.ProviderName, "TransportProvider_provider_name_idx");

        entity.Property(e => e.TransportProviderId).HasColumnName("transport_provider_id");
        entity.Property(e => e.Address)
            .HasMaxLength(500)
            .HasColumnName("address");
        entity.Property(e => e.ContactName)
            .HasMaxLength(150)
            .HasColumnName("contact_name");
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
        entity.Property(e => e.Email)
            .HasMaxLength(255)
            .HasColumnName("email");
        entity.Property(e => e.IsDeleted)
            .HasDefaultValue(false)
            .HasColumnName("is_deleted");
        entity.Property(e => e.Note).HasColumnName("note");
        entity.Property(e => e.Phone)
            .HasMaxLength(30)
            .HasColumnName("phone");
        entity.Property(e => e.ProviderCode)
            .HasMaxLength(50)
            .HasColumnName("provider_code");
        entity.Property(e => e.ProviderName)
            .HasMaxLength(200)
            .HasColumnName("provider_name");
        entity.Property(e => e.SupportedModes)
            .HasMaxLength(100)
            .HasColumnName("supported_modes");
        entity.Property(e => e.TaxCode)
            .HasMaxLength(100)
            .HasColumnName("tax_code");
        entity.Property(e => e.UpdatedAt)
            .IsConcurrencyToken()
            .HasColumnType("timestamp without time zone")
            .HasColumnName("updated_at");

        entity.HasOne(d => d.DeletedByAccount).WithMany(p => p.TransportProviders)
            .HasForeignKey(d => d.DeletedByAccountId)
            .HasConstraintName("TransportProvider_deleted_by_account_id_fkey");
    }
}
