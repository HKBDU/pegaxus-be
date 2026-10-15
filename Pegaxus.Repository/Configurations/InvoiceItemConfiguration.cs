using System.Collections.Generic;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pegaxus.Repository.Entities;

namespace Pegaxus.Repository.Configurations;

public sealed class InvoiceItemConfiguration : IEntityTypeConfiguration<InvoiceItem>
{
    public void Configure(EntityTypeBuilder<InvoiceItem> entity)
    {
        entity.HasKey(e => e.InvoiceItemId).HasName("InvoiceItem_pkey");

        entity.ToTable("InvoiceItem");

        entity.HasIndex(e => e.EmergencyCostRequestId, "InvoiceItem_emergency_cost_request_id_key").IsUnique();

        entity.HasIndex(e => e.HorseId, "InvoiceItem_horse_id_idx");

        entity.HasIndex(e => e.InvoiceId, "InvoiceItem_invoice_id_idx");

        entity.Property(e => e.InvoiceItemId).HasColumnName("invoice_item_id");
        entity.Property(e => e.CreatedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("created_at");
        entity.Property(e => e.Description)
            .HasMaxLength(500)
            .HasColumnName("description");
        entity.Property(e => e.EmergencyCostRequestId).HasColumnName("emergency_cost_request_id");
        entity.Property(e => e.HorseId).HasColumnName("horse_id");
        entity.Property(e => e.InvoiceId).HasColumnName("invoice_id");
        entity.Property(e => e.LineAmount)
            .HasPrecision(18, 2)
            .HasColumnName("line_amount");
        entity.Property(e => e.Note).HasColumnName("note");
        entity.Property(e => e.Quantity)
            .HasPrecision(10, 2)
            .HasDefaultValueSql("1")
            .HasColumnName("quantity");
        entity.Property(e => e.UnitPrice)
            .HasPrecision(18, 2)
            .HasColumnName("unit_price");
        entity.Property(e => e.UpdatedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("updated_at");

        entity.HasOne(d => d.EmergencyCostRequest).WithOne(p => p.InvoiceItem)
            .HasForeignKey<InvoiceItem>(d => d.EmergencyCostRequestId)
            .HasConstraintName("InvoiceItem_emergency_cost_request_id_fkey");

        entity.HasOne(d => d.Horse).WithMany(p => p.InvoiceItems)
            .HasForeignKey(d => d.HorseId)
            .HasConstraintName("InvoiceItem_horse_id_fkey");

        entity.HasOne(d => d.Invoice).WithMany(p => p.InvoiceItems)
            .HasForeignKey(d => d.InvoiceId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("InvoiceItem_invoice_id_fkey");
    }
}
