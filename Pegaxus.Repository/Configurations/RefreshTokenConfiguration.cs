using System.Collections.Generic;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pegaxus.Repository.Entities;

namespace Pegaxus.Repository.Configurations;

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> entity)
    {
        entity.HasKey(e => e.RefreshTokenId).HasName("RefreshToken_pkey");

        entity.ToTable("RefreshToken", tb => tb.HasComment("RefreshToken: Token dùng để cấp lại Access Token cho một Account. Một Account có thể có nhiều RefreshToken cho nhiều thiết bị/phiên đăng nhập. Database chỉ lưu token_hash, không lưu raw token."));

        entity.HasIndex(e => e.AccountId, "RefreshToken_account_id_idx");

        entity.HasIndex(e => e.ExpiresAt, "RefreshToken_expires_at_idx");

        entity.HasIndex(e => e.RevokedAt, "RefreshToken_revoked_at_idx");

        entity.HasIndex(e => e.TokenHash, "RefreshToken_token_hash_key").IsUnique();

        entity.Property(e => e.RefreshTokenId).HasColumnName("refresh_token_id");
        entity.Property(e => e.AccountId).HasColumnName("account_id");
        entity.Property(e => e.CreatedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("created_at");
        entity.Property(e => e.CreatedByIp)
            .HasMaxLength(64)
            .HasColumnName("created_by_ip");
        entity.Property(e => e.DeviceInfo)
            .HasMaxLength(500)
            .HasColumnName("device_info");
        entity.Property(e => e.ExpiresAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("expires_at");
        entity.Property(e => e.ReplacedByTokenId).HasColumnName("replaced_by_token_id");
        entity.Property(e => e.RevokedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("revoked_at");
        entity.Property(e => e.RevokedByIp)
            .HasMaxLength(64)
            .HasColumnName("revoked_by_ip");
        entity.Property(e => e.TokenHash)
            .HasMaxLength(255)
            .HasColumnName("token_hash");

        entity.HasOne(d => d.Account).WithMany(p => p.RefreshTokens)
            .HasForeignKey(d => d.AccountId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("RefreshToken_account_id_fkey");

        entity.HasOne(d => d.ReplacedByToken).WithMany(p => p.InverseReplacedByToken)
            .HasForeignKey(d => d.ReplacedByTokenId)
            .HasConstraintName("RefreshToken_replaced_by_token_id_fkey");
    }
}
