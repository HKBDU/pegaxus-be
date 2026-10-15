using System.Text.Json;

namespace Pegaxus.Repository.Entities;

public partial class CatalogAuditLog
{
    public Guid CatalogAuditLogId { get; set; }

    public string EntityType { get; set; } = null!;

    public Guid EntityId { get; set; }

    public string ActionType { get; set; } = null!;

    public Guid ActorAccountId { get; set; }

    public string? Summary { get; set; }

    public JsonDocument BeforeData { get; set; } = null!;

    public JsonDocument AfterData { get; set; } = null!;

    public JsonDocument? ReferenceCountSnapshot { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Account ActorAccount { get; set; } = null!;
}
