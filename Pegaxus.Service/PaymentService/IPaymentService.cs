namespace Pegaxus.Service.PaymentService;

public interface IPaymentService
{
    Task HandleWebhookAsync(Request.WebhookRequest request, string? secretKey, CancellationToken cancellationToken = default);
}
