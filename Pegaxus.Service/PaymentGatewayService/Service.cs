using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Pegaxus.Service.Exceptions;

namespace Pegaxus.Service.PaymentGatewayService;

public sealed class Service(IOptions<SePayOptions> options) : IPaymentGatewayService
{
    public Task<Checkout> CreateCheckoutAsync(Guid invoiceId, string receiptCode, decimal amount, string currency,
        Guid? bookingId = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (currency != "VND")
            throw new UnprocessableEntityException("SePay bank transfers support VND only", "UNSUPPORTED_PAYMENT_CURRENCY");
        if (amount <= 0 || amount != decimal.Truncate(amount) || amount > long.MaxValue)
            throw new UnprocessableEntityException("SePay requires a positive whole VND amount", "UNSUPPORTED_PAYMENT_AMOUNT");
        if (!Regex.IsMatch(receiptCode, @"\ARCP-[0-9]{8}-[0-9]{4}\z"))
            throw new InvalidDataException("Invalid receipt code");
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.MerchantId) || string.IsNullOrWhiteSpace(settings.SecretKey))
            throw new Pegaxus.Service.Exceptions.Exceptions("Payment gateway is not configured", 503, "PAYMENT_GATEWAY_NOT_CONFIGURED");
        var endpoint = settings.Environment switch
        {
            "sandbox" => "https://pay-sandbox.sepay.vn/v1/checkout/init",
            "production" => "https://pay.sepay.vn/v1/checkout/init",
            _ => throw new InvalidOperationException("SePay environment must be sandbox or production")
        };
        // Customers open invoices at /bookings/{bookingId}/invoices/{invoiceId}, so the template may use both placeholders.
        if (settings.InvoiceDetailsUrlTemplate.Contains("{bookingId}", StringComparison.Ordinal) && bookingId is null)
            throw new InvalidOperationException("SePay invoice details URL uses {bookingId} but no booking was supplied");
        var callback = settings.InvoiceDetailsUrlTemplate
            .Replace("{invoiceId}", invoiceId.ToString(), StringComparison.Ordinal)
            .Replace("{bookingId}", bookingId?.ToString() ?? "", StringComparison.Ordinal);
        if (!settings.InvoiceDetailsUrlTemplate.Contains("{invoiceId}", StringComparison.Ordinal) ||
            !Uri.TryCreate(callback, UriKind.Absolute, out var uri) || uri.UserInfo.Length != 0 ||
            uri.Scheme != Uri.UriSchemeHttps && !(settings.Environment == "sandbox" && uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))
            throw new InvalidOperationException("SePay invoice details URL must contain {invoiceId} and use HTTPS (local HTTP is allowed in sandbox)");

        // SePay requires a signed browser POST; preserve field order when building and submitting the form.
        var fields = new Dictionary<string, string>
        {
            ["merchant"] = settings.MerchantId,
            ["operation"] = "PURCHASE",
            ["payment_method"] = "BANK_TRANSFER",
            ["order_invoice_number"] = receiptCode,
            ["order_amount"] = amount.ToString("0", CultureInfo.InvariantCulture),
            ["currency"] = currency,
            ["order_description"] = $"Invoice payment {receiptCode}",
            ["success_url"] = callback,
            ["error_url"] = callback,
            ["cancel_url"] = callback
        };
        var signed = Encoding.UTF8.GetBytes(string.Join(",", fields.Select(x => $"{x.Key}={x.Value}")));
        fields["signature"] = Convert.ToBase64String(HMACSHA256.HashData(Encoding.UTF8.GetBytes(settings.SecretKey), signed));
        return Task.FromResult(new Checkout(endpoint, fields));
    }
}
