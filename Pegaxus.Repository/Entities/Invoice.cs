using System.Collections.Generic;
using Pegaxus.Repository.Enum;

namespace Pegaxus.Repository.Entities;

/// <summary>
/// Invoice: Hóa đơn của một Booking do Employee lập, 
/// tổng hợp các InvoiceItem, số tiền, thời hạn thanh toán, lịch sử trạng thái và 
/// các Receipt liên quan.
/// </summary>
public partial class Invoice
{
    public Guid InvoiceId { get; set; }

    public string InvoiceCode { get; set; } = null!;

    public Guid BookingId { get; set; }

    public Guid EmployeeId { get; set; }

    public InvoiceType Type { get; set; } = InvoiceType.QUOTATION;

    public InvoiceStatus Status { get; set; } = InvoiceStatus.DRAFT;

    public string Currency { get; set; } = null!;

    public decimal Subtotal { get; set; }

    public decimal DiscountAmount { get; set; }

    public decimal TaxAmount { get; set; }

    public decimal TotalAmount { get; set; }

    public DateTime? IssuedAt { get; set; }

    public DateOnly? DueDate { get; set; }

    public DateTime? PaidAt { get; set; }

    public DateTime? CancelledAt { get; set; }

    public string? CancellationReason { get; set; }

    public string? BillingNote { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Booking Booking { get; set; } = null!;

    public virtual Employee Employee { get; set; } = null!;

    public virtual ICollection<InvoiceItem> InvoiceItems { get; set; } = new List<InvoiceItem>();

    public virtual ICollection<InvoiceStatusHistory> InvoiceStatusHistories { get; set; } = new List<InvoiceStatusHistory>();

    public virtual ICollection<Receipt> Receipts { get; set; } = new List<Receipt>();
}
