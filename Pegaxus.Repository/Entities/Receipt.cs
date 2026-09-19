using System;
using System.Collections.Generic;

namespace Pegaxus.Repository.Entities;

public partial class Receipt
{
    public long ReceiptId { get; set; }

    public string ReceiptCode { get; set; } = null!;

    public long InvoiceId { get; set; }

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
