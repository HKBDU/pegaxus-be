namespace Pegaxus.Service.PaymentGatewayService;

public interface IPaymentGatewayService
{
    /// <param name="bookingId">Substituted for {bookingId} in the return URL template, which customer routes need.</param>
    Task<Checkout> CreateCheckoutAsync(Guid invoiceId, string receiptCode, decimal amount, string currency,
        Guid? bookingId = null, CancellationToken cancellationToken = default);
}

public sealed record Checkout(string PaymentUrl, IReadOnlyDictionary<string, string> Fields);
