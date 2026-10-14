using System.Collections.Generic;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pegaxus.Repository.Entities;

namespace Pegaxus.Repository.Configurations;

public sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> entity)
    {
        entity.HasKey(e => e.AccountId).HasName("Account_pkey");

        entity.ToTable("Account");

        entity.HasIndex(e => e.Email, "Account_email_key").IsUnique();

        entity.HasIndex(e => e.IsDeleted, "Account_is_deleted_idx");

        entity.Property(e => e.AccountId).HasColumnName("account_id");
        entity.Property(e => e.AvatarUrl)
            .HasMaxLength(500)
            .HasColumnName("avatar_url");
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
        entity.Property(e => e.EmailVerifiedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("email_verified_at");
        entity.Property(e => e.IsDeleted)
            .HasDefaultValue(false)
            .HasColumnName("is_deleted");
        entity.Property(e => e.LastLoginAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("last_login_at");
        entity.Property(e => e.PasswordHash)
            .HasMaxLength(255)
            .HasColumnName("password_hash");
        entity.Property(e => e.UpdatedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("updated_at");

        entity.HasOne(d => d.DeletedByAccount).WithMany(p => p.InverseDeletedByAccount)
            .HasForeignKey(d => d.DeletedByAccountId)
            .HasConstraintName("Account_deleted_by_account_id_fkey");
    }
}
