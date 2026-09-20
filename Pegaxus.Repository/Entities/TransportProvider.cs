using System;
using System.Collections.Generic;
using Pegaxus.Repository.Enum;

namespace Pegaxus.Repository.Entities;

/// <summary>
/// TransportProvider: Danh mục nhà cung cấp dịch vụ vận chuyển bên ngoài, 
/// lưu thông tin liên hệ, phương thức hỗ trợ và trạng thái hoạt động.
/// </summary>
public partial class TransportProvider
{
    public Guid TransportProviderId { get; set; }

    public string ProviderCode { get; set; } = null!;

    public string ProviderName { get; set; } = null!;

    public ProviderStatus Status { get; set; } = ProviderStatus.ACTIVE;

    public string? ContactName { get; set; }

    public string? Phone { get; set; }

    public string? Email { get; set; }

    public string? Address { get; set; }

    public string? Country { get; set; }

    public string? TaxCode { get; set; }

    public string? SupportedModes { get; set; }

    public string? Note { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime? DeletedAt { get; set; }

    public Guid? DeletedByAccountId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Account? DeletedByAccount { get; set; }

    public virtual ICollection<TransportPlanProvider> TransportPlanProviders { get; set; } = new List<TransportPlanProvider>();
}
