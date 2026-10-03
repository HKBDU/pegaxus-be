namespace Pegaxus.Service.MailService;

public interface IService
{
    public Task SendMail(MailContent mailContent, CancellationToken cancellationToken = default);
}

public class MailContent
{
    public required string To { get; set; }
    public required string Subject { get; set; }
    public required string Body { get; set; }
}
