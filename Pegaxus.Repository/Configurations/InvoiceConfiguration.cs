using System.Collections.Generic;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pegaxus.Repository.Entities;

namespace Pegaxus.Repository.Configurations;

public sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> entity)
    {
        entity.HasKey(e => e.InvoiceId).HasName("Invoice_pkey");

        entity.ToTable("Invoice");

        entity.HasIndex(e => e.BookingId, "Invoice_booking_id_idx");

        entity.HasIndex(e => e.DueDate, "Invoice_due_date_idx");

        entity.HasIndex(e => e.EmployeeId, "Invoice_employee_id_idx");

        entity.HasIndex(e => e.InvoiceCode, "Invoice_invoice_code_key").IsUnique();

        entity.Property(e => e.InvoiceId).HasColumnName("invoice_id");
        entity.Property(e => e.BillingNote).HasColumnName("billing_note");
        entity.Property(e => e.BookingId).HasColumnName("booking_id");
        entity.Property(e => e.CancellationReason).HasColumnName("cancellation_reason");
        entity.Property(e => e.CancelledAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("cancelled_at");
        entity.Property(e => e.CreatedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("created_at");
        entity.Property(e => e.Currency)
            .HasMaxLength(3)
            .HasColumnName("currency");
        entity.Property(e => e.DiscountAmount)
            .HasPrecision(18, 2)
            .HasColumnName("discount_amount");
        entity.Property(e => e.DueDate).HasColumnName("due_date");
        entity.Property(e => e.EmployeeId).HasColumnName("employee_id");
        entity.Property(e => e.InvoiceCode)
            .HasMaxLength(50)
            .HasColumnName("invoice_code");
        entity.Property(e => e.Type)
            .HasColumnName("type")
            .HasColumnType("invoice_type");
        entity.Property(e => e.IssuedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("issued_at");
        entity.Property(e => e.PaidAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("paid_at");
        entity.Property(e => e.Subtotal)
            .HasPrecision(18, 2)
            .HasColumnName("subtotal");
        entity.Property(e => e.TaxAmount)
            .HasPrecision(18, 2)
            .HasColumnName("tax_amount");
        entity.Property(e => e.TotalAmount)
            .HasPrecision(18, 2)
            .HasColumnName("total_amount");
        entity.Property(e => e.UpdatedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("updated_at");

        entity.HasOne(d => d.Booking).WithMany(p => p.Invoices)
            .HasForeignKey(d => d.BookingId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("Invoice_booking_id_fkey");

        entity.HasOne(d => d.Employee).WithMany(p => p.Invoices)
            .HasForeignKey(d => d.EmployeeId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("Invoice_employee_id_fkey");
    }
}
