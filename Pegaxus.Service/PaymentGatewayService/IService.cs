namespace Pegaxus.Service.PaymentGatewayService;

public interface IService
{
    Task<Checkout> CreateCheckoutAsync(Guid invoiceId, string receiptCode, decimal amount, string currency,
        CancellationToken cancellationToken = default);
}

public sealed record Checkout(string PaymentUrl, IReadOnlyDictionary<string, string> Fields);
