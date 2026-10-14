using System.Collections.Generic;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pegaxus.Repository.Entities;

namespace Pegaxus.Repository.Configurations;

public sealed class ReceiptConfiguration : IEntityTypeConfiguration<Receipt>
{
    public void Configure(EntityTypeBuilder<Receipt> entity)
    {
        entity.HasKey(e => e.ReceiptId).HasName("Receipt_pkey");

        entity.ToTable("Receipt");

        entity.HasIndex(e => e.InvoiceId, "Receipt_invoice_id_idx");

        entity.HasIndex(e => e.PaidAt, "Receipt_paid_at_idx");

        entity.HasIndex(e => e.ReceiptCode, "Receipt_receipt_code_key").IsUnique();

        entity.HasIndex(e => e.TransactionReference, "Receipt_transaction_reference_idx");

        entity.Property(e => e.ReceiptId).HasColumnName("receipt_id");
        entity.Property(e => e.Amount)
            .HasPrecision(18, 2)
            .HasColumnName("amount");
        entity.Property(e => e.ConfirmedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("confirmed_at");
        entity.Property(e => e.CreatedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("created_at");
        entity.Property(e => e.Currency)
            .HasMaxLength(3)
            .HasColumnName("currency");
        entity.Property(e => e.InvoiceId).HasColumnName("invoice_id");
        entity.Property(e => e.Note).HasColumnName("note");
        entity.Property(e => e.PaidAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("paid_at");
        entity.Property(e => e.ReceiptCode)
            .HasMaxLength(50)
            .HasColumnName("receipt_code");
        entity.Property(e => e.TransactionReference)
            .HasMaxLength(150)
            .HasColumnName("transaction_reference");
        entity.Property(e => e.UpdatedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("updated_at");

        entity.HasOne(d => d.Invoice).WithMany(p => p.Receipts)
            .HasForeignKey(d => d.InvoiceId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("Receipt_invoice_id_fkey");
    }
}
