namespace Pegaxus.Repository.Entities;

public class MissingDocumentReminder
{
    public Guid MissingDocumentReminderId { get; set; }
    public Guid ComplianceDossierId { get; set; }
    public Guid RecipientAccountId { get; set; }
    public Guid[] RequirementIds { get; set; } = [];
    public DateOnly DueDate { get; set; }
    public string? Note { get; set; }
    public DateOnly? AutomaticDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastAttemptAt { get; set; }
    public DateTime? EmailSentAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public int EmailAttempts { get; set; }
    public ComplianceDossier ComplianceDossier { get; set; } = null!;
    public Account RecipientAccount { get; set; } = null!;
}
