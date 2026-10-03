namespace Pegaxus.Repository.Entities;

public class DocumentRequestComment
{
    public Guid DocumentRequestCommentId { get; set; }
    public Guid DocumentRequestId { get; set; }
    public Guid AuthorAccountId { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DocumentRequest DocumentRequest { get; set; } = null!;
    public Account AuthorAccount { get; set; } = null!;
}
