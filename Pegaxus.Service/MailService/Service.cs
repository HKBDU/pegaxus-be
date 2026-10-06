using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using MimeKit;

namespace Pegaxus.Service.MailService;

public class Service : IService
{
    private readonly MailOptions _mailOptions = new();

    public Service(IConfiguration configuration)
    {
        configuration.GetSection(nameof(MailOptions)).Bind(_mailOptions);
    }
    
    public async Task SendMail(MailContent mailContent, CancellationToken cancellationToken = default)
    {
        MimeMessage email = new();
        email.Sender = new MailboxAddress(_mailOptions?.DisplayName, _mailOptions!.Mail);
        email.From.Add(new MailboxAddress(_mailOptions?.DisplayName, _mailOptions!.Mail));
        email.To.Add(MailboxAddress.Parse(mailContent.To));
        email.Subject = mailContent.Subject;


        BodyBuilder builder = new();
        builder.HtmlBody = mailContent.Body;
        email.Body = builder.ToMessageBody();
        
        using SmtpClient smtp = new();

        await smtp.ConnectAsync(_mailOptions!.Host, _mailOptions.Port,
            SecureSocketOptions.StartTls, cancellationToken);
        await smtp.AuthenticateAsync(_mailOptions.Mail, _mailOptions.Password, cancellationToken);
        await smtp.SendAsync(email, cancellationToken);
        await smtp.DisconnectAsync(true, cancellationToken);
    }
}
