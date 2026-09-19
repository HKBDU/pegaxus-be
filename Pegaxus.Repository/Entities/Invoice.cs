using System;
using System.Collections.Generic;

namespace Pegaxus.Repository.Entities;

public partial class Invoice
{
    public long InvoiceId { get; set; }

    public string InvoiceCode { get; set; } = null!;

    public long BookingId { get; set; }

    public long EmployeeId { get; set; }

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
