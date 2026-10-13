using System.Collections.Generic;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pegaxus.Repository.Entities;

namespace Pegaxus.Repository.Configurations;

public sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> entity)
    {
        entity.HasKey(e => e.CustomerId).HasName("Customer_pkey");

        entity.ToTable("Customer");

        entity.HasIndex(e => e.AccountId, "Customer_account_id_key").IsUnique();

        entity.HasIndex(e => e.CustomerCode, "Customer_customer_code_key").IsUnique();

        entity.HasIndex(e => e.IsDeleted, "Customer_is_deleted_idx");

        entity.HasIndex(e => e.Phone, "Customer_phone_idx");

        entity.Property(e => e.CustomerId).HasColumnName("customer_id");
        entity.Property(e => e.AccountId).HasColumnName("account_id");
        entity.Property(e => e.CustomerCode)
            .HasMaxLength(20)
            .HasColumnName("customer_code");
        entity.Property(e => e.Address)
            .HasMaxLength(500)
            .HasColumnName("address");
        entity.Property(e => e.Country)
            .HasMaxLength(100)
            .HasColumnName("country");
        entity.Property(e => e.CreatedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("created_at");
        entity.Property(e => e.DateOfBirth).HasColumnName("date_of_birth");
        entity.Property(e => e.DeletedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("deleted_at");
        entity.Property(e => e.DeletedByAccountId).HasColumnName("deleted_by_account_id");
        entity.Property(e => e.FullName)
            .HasMaxLength(150)
            .HasColumnName("full_name");
        entity.Property(e => e.IdentityNumber)
            .HasMaxLength(100)
            .HasColumnName("identity_number");
        entity.Property(e => e.IsDeleted)
            .HasDefaultValue(false)
            .HasColumnName("is_deleted");
        entity.Property(e => e.Phone)
            .HasMaxLength(30)
            .HasColumnName("phone");
        entity.Property(e => e.UpdatedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("updated_at");

        entity.HasOne(d => d.Account).WithOne(p => p.CustomerAccount)
            .HasForeignKey<Customer>(d => d.AccountId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("Customer_account_id_fkey");

        entity.HasOne(d => d.DeletedByAccount).WithMany(p => p.CustomerDeletedByAccounts)
            .HasForeignKey(d => d.DeletedByAccountId)
            .HasConstraintName("Customer_deleted_by_account_id_fkey");
    }
}
