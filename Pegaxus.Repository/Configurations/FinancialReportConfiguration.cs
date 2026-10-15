using System.Collections.Generic;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pegaxus.Repository.Entities;

namespace Pegaxus.Repository.Configurations;

public sealed class FinancialReportConfiguration : IEntityTypeConfiguration<FinancialReport>
{
    public void Configure(EntityTypeBuilder<FinancialReport> entity)
    {
        entity.HasKey(e => e.FinancialReportId).HasName("FinancialReport_pkey");

        entity.ToTable("FinancialReport", tb => tb.HasComment("FinancialReport: Báo cáo tài chính tổng hợp theo một Transport, được hệ thống sinh từ dữ liệu Invoice, InvoiceItem, Receipt và chi phí liên quan; không có trạng thái lưu nháp."));

        entity.HasIndex(e => e.ReportCode, "FinancialReport_report_code_key").IsUnique();

        entity.HasIndex(e => e.TransportId, "FinancialReport_transport_id_key").IsUnique();

        entity.Property(e => e.FinancialReportId).HasColumnName("financial_report_id");
        entity.Property(e => e.CreatedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("created_at");
        entity.Property(e => e.Currency)
            .HasMaxLength(3)
            .HasColumnName("currency");
        entity.Property(e => e.GeneratedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("generated_at");
        entity.Property(e => e.OutstandingAmount)
            .HasPrecision(18, 2)
            .HasColumnName("outstanding_amount");
        entity.Property(e => e.ReportCode)
            .HasMaxLength(50)
            .HasColumnName("report_code");
        entity.Property(e => e.SummaryNote).HasColumnName("summary_note");
        entity.Property(e => e.TotalEmergencyCost)
            .HasPrecision(18, 2)
            .HasColumnName("total_emergency_cost");
        entity.Property(e => e.TotalInvoicedAmount)
            .HasPrecision(18, 2)
            .HasColumnName("total_invoiced_amount");
        entity.Property(e => e.TotalPaidAmount)
            .HasPrecision(18, 2)
            .HasColumnName("total_paid_amount");
        entity.Property(e => e.TransportId).HasColumnName("transport_id");
        entity.Property(e => e.UpdatedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("updated_at");

        entity.HasOne(d => d.Transport).WithOne(p => p.FinancialReport)
            .HasForeignKey<FinancialReport>(d => d.TransportId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("FinancialReport_transport_id_fkey");
    }
}
