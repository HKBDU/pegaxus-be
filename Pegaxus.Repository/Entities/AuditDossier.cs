using System.Text.Json;
using Pegaxus.Repository.Enum;

namespace Pegaxus.Repository.Entities;

public partial class AuditDossier
{
    public Guid AuditDossierId { get; set; }

    public Guid ComplianceDossierId { get; set; }

    public string ActionType { get; set; } = null!;

    public string TargetEntity { get; set; } = null!;

    public Guid? TargetId { get; set; }

    public Guid ActorAccountId { get; set; }

    public string? Summary { get; set; }

    public JsonDocument? Details { get; set; }

    public AuditVisibility Visibility { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual ComplianceDossier ComplianceDossier { get; set; } = null!;

    public virtual Account ActorAccount { get; set; } = null!;
}
