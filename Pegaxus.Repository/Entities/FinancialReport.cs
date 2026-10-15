using System.Collections.Generic;

namespace Pegaxus.Repository.Entities;

/// <summary>
/// FinancialReport: Báo cáo tài chính tổng hợp theo một Transport, 
/// được hệ thống sinh từ dữ liệu Invoice, InvoiceItem, Receipt và 
/// chi phí liên quan; không có trạng thái lưu nháp.
/// </summary>
public partial class FinancialReport
{
    public Guid FinancialReportId { get; set; }

    public string ReportCode { get; set; } = null!;

    public Guid TransportId { get; set; }

    public string Currency { get; set; } = null!;

    public decimal TotalInvoicedAmount { get; set; }

    public decimal TotalPaidAmount { get; set; }

    public decimal TotalEmergencyCost { get; set; }

    public decimal OutstandingAmount { get; set; }

    public string? SummaryNote { get; set; }

    public DateTime GeneratedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Transport Transport { get; set; } = null!;
}
