using System;
using System.Collections.Generic;

namespace Pegaxus.Repository.Entities;

public partial class TransportProvider
{
    public long TransportProviderId { get; set; }

    public string ProviderCode { get; set; } = null!;

    public string ProviderName { get; set; } = null!;

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

    public long? DeletedByAccountId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Account? DeletedByAccount { get; set; }

    public virtual ICollection<TransportPlanProvider> TransportPlanProviders { get; set; } = new List<TransportPlanProvider>();
}
