using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pegaxus.Repository.Entities;

namespace Pegaxus.Repository.Configurations;

public sealed class PriceListConfiguration : IEntityTypeConfiguration<PriceList>
{
    public void Configure(EntityTypeBuilder<PriceList> entity)
    {
        entity.HasKey(e => e.PriceListId).HasName("PriceList_pkey");
        entity.ToTable("PriceList", table =>
        {
            table.HasCheckConstraint("PriceList_tax_rate_check", "tax_rate >= 0 AND tax_rate <= 100");
            table.HasCheckConstraint("PriceList_deposit_rate_check", "deposit_rate >= 0 AND deposit_rate <= 100");
        });
        // The database itself refuses a second active list (BR-010).
        entity.HasIndex(e => e.IsActive, "PriceList_single_active_idx").IsUnique().HasFilter("is_active = true");
        entity.HasIndex(e => e.CreatedAt, "PriceList_created_at_idx");
        entity.Property(e => e.PriceListId).HasColumnName("price_list_id");
        entity.Property(e => e.Name).HasMaxLength(200).HasColumnName("name");
        entity.Property(e => e.Currency).HasMaxLength(3).IsFixedLength().HasDefaultValueSql("'VND'").HasColumnName("currency");
        entity.Property(e => e.TaxRate).HasPrecision(5, 2).HasColumnName("tax_rate");
        entity.Property(e => e.DepositRate).HasPrecision(5, 2).HasColumnName("deposit_rate");
        entity.Property(e => e.IsActive).HasColumnName("is_active");
        entity.Property(e => e.CreatedAt).HasColumnType("timestamp without time zone").HasColumnName("created_at");
        entity.Property(e => e.UpdatedAt).HasColumnType("timestamp without time zone").HasColumnName("updated_at");
        entity.Property(e => e.CreatedByAccountId).HasColumnName("created_by_account_id");
        entity.Property(e => e.UpdatedByAccountId).HasColumnName("updated_by_account_id");
        entity.HasOne<Account>().WithMany()
            .HasForeignKey(e => e.CreatedByAccountId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("PriceList_created_by_account_id_fkey");
        entity.HasOne<Account>().WithMany()
            .HasForeignKey(e => e.UpdatedByAccountId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("PriceList_updated_by_account_id_fkey");
    }
}

public sealed class PriceListItemConfiguration : IEntityTypeConfiguration<PriceListItem>
{
    public void Configure(EntityTypeBuilder<PriceListItem> entity)
    {
        entity.HasKey(e => e.PriceListItemId).HasName("PriceListItem_pkey");
        entity.ToTable("PriceListItem", table =>
        {
            table.HasCheckConstraint("PriceListItem_unit_price_check", "unit_price > 0");
            table.HasCheckConstraint("PriceListItem_vehicle_tier_check", "vehicle_tier IS NULL OR vehicle_tier IN ('S', 'M', 'L')");
        });
        entity.HasIndex(e => new { e.PriceListId, e.ItemCode }, "PriceListItem_price_list_id_item_code_key").IsUnique();
        entity.Property(e => e.PriceListItemId).HasColumnName("price_list_item_id");
        entity.Property(e => e.PriceListId).HasColumnName("price_list_id");
        entity.Property(e => e.ItemCode).HasMaxLength(50).HasColumnName("item_code");
        entity.Property(e => e.ItemName).HasMaxLength(200).HasColumnName("item_name");
        entity.Property(e => e.Unit).HasMaxLength(50).HasColumnName("unit");
        entity.Property(e => e.UnitPrice).HasPrecision(18, 2).HasColumnName("unit_price");
        entity.Property(e => e.VehicleTier).HasMaxLength(10).HasColumnName("vehicle_tier");
        entity.Property(e => e.SortOrder).HasColumnName("sort_order");
        entity.Property(e => e.CreatedAt).HasColumnType("timestamp without time zone").HasColumnName("created_at");
        entity.Property(e => e.UpdatedAt).HasColumnType("timestamp without time zone").HasColumnName("updated_at");
        entity.HasOne(e => e.PriceList).WithMany(p => p.PriceListItems)
            .HasForeignKey(e => e.PriceListId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("PriceListItem_price_list_id_fkey");
    }
}
