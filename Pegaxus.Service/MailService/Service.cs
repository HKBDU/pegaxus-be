using System.Net.Http.Json;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;

namespace Pegaxus.Service.MailService;

/// <summary>Sends transactional email through the Brevo HTTP API (POST /v3/smtp/email).</summary>
public class Service : IMailService
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);
    private const int MaxErrorLength = 500;

    private readonly BrevoOptions _options = new();
    private readonly string? _frontendUrl;
    private readonly IHttpClientFactory _httpClientFactory;

    public Service(IConfiguration configuration, IHttpClientFactory httpClientFactory)
    {
        configuration.GetSection(BrevoOptions.SectionName).Bind(_options);
        _frontendUrl = configuration["Cors:FrontendUrl"];
        _httpClientFactory = httpClientFactory;
    }

    public async Task SendMail(MailContent mailContent, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey) || string.IsNullOrWhiteSpace(_options.SenderEmail) ||
            !Uri.TryCreate(_options.ApiUrl, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException(
                "Brevo email API is not configured: Brevo:ApiUrl (https), Brevo:ApiKey and Brevo:SenderEmail are required.");

        // A malformed recipient fails here instead of spending an API call that Brevo would reject with 400.
        var recipient = new MailAddress(mailContent.To).Address;

        var sender = new Dictionary<string, string> { ["email"] = _options.SenderEmail };
        if (!string.IsNullOrWhiteSpace(_options.SenderName))
            sender["name"] = _options.SenderName;

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(new
            {
                sender,
                to = new[] { new { email = recipient } },
                subject = mailContent.Subject,
                htmlContent = EmailLinks.Resolve(mailContent.Body, _frontendUrl)
            })
        };
        request.Headers.Add("api-key", _options.ApiKey);
        request.Headers.Accept.ParseAdd("application/json");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        try
        {
            using var response = await _httpClientFactory.CreateClient().SendAsync(request, timeout.Token);
            if (response.IsSuccessStatusCode)
                return;

            // The body is Brevo's { code, message } error object. The api-key header is never echoed back.
            var detail = await response.Content.ReadAsStringAsync(timeout.Token);
            if (detail.Length > MaxErrorLength)
                detail = detail[..MaxErrorLength];
            throw new HttpRequestException(
                $"Brevo rejected the email with HTTP {(int)response.StatusCode}: {detail}", null, response.StatusCode);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Brevo did not answer within {RequestTimeout.TotalSeconds:0} seconds.");
        }
    }
}
