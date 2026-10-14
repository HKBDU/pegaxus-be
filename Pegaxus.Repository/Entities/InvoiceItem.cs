using System.Collections.Generic;
using Pegaxus.Repository.Enum;

namespace Pegaxus.Repository.Entities;

/// <summary>
/// InvoiceItem: Một dòng chi phí trong Invoice, 
/// có thể gắn với Horse hoặc EmergencyCostRequest và 
/// lưu loại phí, số lượng, đơn giá, thành tiền.
/// </summary>
public partial class InvoiceItem
{
    public Guid InvoiceItemId { get; set; }

    public Guid InvoiceId { get; set; }

    public Guid? HorseId { get; set; }

    public Guid? EmergencyCostRequestId { get; set; }

    public InvoiceItemType ItemType { get; set; }

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
