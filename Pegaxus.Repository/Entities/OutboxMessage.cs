namespace Pegaxus.Repository.Entities;

public sealed class OutboxMessage
{
    public Guid OutboxMessageId { get; set; }

    public string MessageType { get; set; } = null!;

    public string Payload { get; set; } = null!;

    public string Status { get; set; } = "PENDING";

    public int Attempts { get; set; }

    public DateTime AvailableAt { get; set; }

    public DateTime? LockedAt { get; set; }

    public DateTime? ProcessedAt { get; set; }

    public string? LastError { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
