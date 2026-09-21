using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Pegaxus.API.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/webhooks/sepay")]
public sealed class SePayWebhookController : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SePayOptions _options;
    private readonly SePayWebhookDeduplicator _deduplicator;
    private readonly ILogger<SePayWebhookController> _logger;

    public SePayWebhookController(
        IOptions<SePayOptions> options,
        SePayWebhookDeduplicator deduplicator,
        ILogger<SePayWebhookController> logger)
    {
        _options = options.Value;
        _deduplicator = deduplicator;
        _logger = logger;
    }

    [HttpPost]
    [RequestSizeLimit(64 * 1024)]
    public async Task<IActionResult> Receive(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.WebhookSecret) || _options.AllowedAccountNumbers.Length == 0)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { success = false });

        var timestamp = Request.Headers["X-SePay-Timestamp"].ToString();
        var signature = Request.Headers["X-SePay-Signature"].ToString();

        if (!long.TryParse(timestamp, out var unixTimestamp))
            return Unauthorized(new { success = false });

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var timestampTolerance = Math.Clamp(_options.TimestampToleranceSeconds, 1, 300);
        if (unixTimestamp < now - timestampTolerance || unixTimestamp > now + timestampTolerance)
            return Unauthorized(new { success = false });

        await using var body = new MemoryStream();
        await Request.Body.CopyToAsync(body, cancellationToken);
        var rawBody = body.ToArray();

        if (!VerifySignature(timestamp, rawBody, signature, _options.WebhookSecret))
            return Unauthorized(new { success = false });

        SePayWebhookPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<SePayWebhookPayload>(rawBody, JsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Invalid SePay webhook JSON payload");
            return BadRequest(new { success = false });
        }

        if (payload is null
            || payload.Id <= 0
            || payload.TransferAmount <= 0
            || (payload.TransferType != "in" && payload.TransferType != "out")
            || string.IsNullOrWhiteSpace(payload.AccountNumber))
        {
            _logger.LogWarning("Invalid SePay webhook payload for transaction {TransactionId}", payload?.Id);
            return BadRequest(new { success = false });
        }

        if (_options.AllowedAccountNumbers.Length > 0
            && !_options.AllowedAccountNumbers.Contains(payload.AccountNumber, StringComparer.Ordinal))
            return BadRequest(new { success = false });

        if (!_deduplicator.TryAccept(payload.Id))
            return Ok(new { success = true });

        _logger.LogInformation(
            "Received SePay transaction {TransactionId}: {TransferType} {TransferAmount} VND to {AccountNumber}",
            payload.Id,
            payload.TransferType,
            payload.TransferAmount,
            payload.AccountNumber);

        return Ok(new { success = true });
    }

    private static bool VerifySignature(string timestamp, byte[] body, string signature, string secret)
    {
        const string prefix = "sha256=";
        if (!signature.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        byte[] providedSignature;
        try
        {
            providedSignature = Convert.FromHexString(signature[prefix.Length..]);
        }
        catch (FormatException)
        {
            return false;
        }

        var signedPrefix = Encoding.UTF8.GetBytes($"{timestamp}.");
        var signedPayload = new byte[signedPrefix.Length + body.Length];
        Buffer.BlockCopy(signedPrefix, 0, signedPayload, 0, signedPrefix.Length);
        Buffer.BlockCopy(body, 0, signedPayload, signedPrefix.Length, body.Length);

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return CryptographicOperations.FixedTimeEquals(hmac.ComputeHash(signedPayload), providedSignature);
    }
}

public sealed class SePayOptions
{
    public const string SectionName = "SePay";
    public string WebhookSecret { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string[] AllowedAccountNumbers { get; set; } = [];
    public int TimestampToleranceSeconds { get; set; } = 300;
}

public sealed class SePayWebhookDeduplicator
{
    // ponytail: process-local dedupe only; replace with a UNIQUE DB transaction ID when payment persistence lands.
    private readonly ConcurrentDictionary<long, byte> _transactionIds = new();

    public bool TryAccept(long transactionId) => _transactionIds.TryAdd(transactionId, 0);
}

public sealed record SePayWebhookPayload(
    long Id,
    string Gateway,
    string TransactionDate,
    string AccountNumber,
    string SubAccount,
    string? Code,
    string Content,
    string? Description,
    string TransferType,
    long TransferAmount,
    long Accumulated,
    string ReferenceCode);
