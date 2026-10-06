using System;
using System.Collections.Generic;
using Pegaxus.Repository.Enum;

namespace Pegaxus.Repository.Entities;

/// <summary>
/// Receipt: Bản ghi một lần thanh toán cho Invoice, 
/// lưu phương thức, trạng thái, số tiền, mã giao dịch và thời điểm xác nhận.
/// </summary>
public partial class Receipt
{
    public Guid ReceiptId { get; set; }

    public string ReceiptCode { get; set; } = null!;

    public Guid InvoiceId { get; set; }

    public PaymentMethod PaymentMethod { get; set; }

    public ReceiptStatus Status { get; set; } = ReceiptStatus.PENDING;

    public decimal Amount { get; set; }

    public string Currency { get; set; } = null!;

    public string? TransactionReference { get; set; }

    public DateTime? PaidAt { get; set; }

    public DateTime? ConfirmedAt { get; set; }

    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Invoice Invoice { get; set; } = null!;
}
