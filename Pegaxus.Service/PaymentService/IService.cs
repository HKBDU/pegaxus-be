namespace Pegaxus.Service.PaymentService;

public interface IService
{
    Task HandleWebhookAsync(Request.WebhookRequest request, string? secretKey, CancellationToken cancellationToken = default);
}
