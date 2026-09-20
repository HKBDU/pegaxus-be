using System;
using System.Collections.Generic;

namespace Pegaxus.Repository.Entities;

public partial class InvoiceItem
{
    public Guid InvoiceItemId { get; set; }

    public Guid InvoiceId { get; set; }

    public Guid? HorseId { get; set; }

    public Guid? EmergencyCostRequestId { get; set; }

    public string Description { get; set; } = null!;

    public decimal Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal LineAmount { get; set; }

    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual EmergencyCostRequest? EmergencyCostRequest { get; set; }

    public virtual Horse? Horse { get; set; }

    public virtual Invoice Invoice { get; set; } = null!;
}
