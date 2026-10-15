namespace Pegaxus.Service.MailService;

/// <summary>Settings for the Brevo transactional email API. Bound from the "Brevo" configuration section.</summary>
public class BrevoOptions
{
    public const string SectionName = "Brevo";

    public string ApiUrl { get; set; } = "https://api.brevo.com/v3/smtp/email";
    public string ApiKey { get; set; } = string.Empty;
    public string SenderName { get; set; } = "Pegaxus";

    /// <summary>Must be a sender or domain verified in the Brevo account.</summary>
    public string SenderEmail { get; set; } = string.Empty;
}
