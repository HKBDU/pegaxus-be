using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Pegaxus.Service.Exceptions;

using SePay = Pegaxus.Service.PaymentGatewayService;

namespace Pegaxus.Service.Test;

public class SePayCheckoutTests
{
    internal static readonly Guid InvoiceId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    internal static SePay.SePayOptions Settings() => new()
    {
        MerchantId = "SP-TEST-example", SecretKey = "sepay-test-secret", Environment = "sandbox",
        InvoiceDetailsUrlTemplate = "https://customer.example/invoices/{invoiceId}"
    };

    [Theory]
    [InlineData("sandbox", "https://pay-sandbox.sepay.vn/v1/checkout/init")]
    [InlineData("production", "https://pay.sepay.vn/v1/checkout/init")]
    public async Task Checkout_OrderedFieldsAndSignature_MatchSePaySdk_WithInvariantAmount(string environment, string endpoint)
    {
        var settings = Settings(); settings.Environment = environment;
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var checkout = await new SePay.Service(Options.Create(settings)).CreateCheckoutAsync(InvoiceId, "RCP-20261110-0001", 151800000m, "VND");
            Assert.Equal(endpoint, checkout.PaymentUrl);
            Assert.Equal(new[] { "merchant", "operation", "payment_method", "order_invoice_number", "order_amount", "currency",
                "order_description", "success_url", "error_url", "cancel_url", "signature" }, checkout.Fields.Keys);
            Assert.Equal("151800000", checkout.Fields["order_amount"]); Assert.Equal("VND", checkout.Fields["currency"]);
            Assert.Equal("BANK_TRANSFER", checkout.Fields["payment_method"]); Assert.Equal("PURCHASE", checkout.Fields["operation"]);
            Assert.Equal("RCP-20261110-0001", checkout.Fields["order_invoice_number"]);
            foreach (var field in new[] { "success_url", "error_url", "cancel_url" })
                Assert.Equal($"https://customer.example/invoices/{InvoiceId}", checkout.Fields[field]);
            // Golden value produced with the official Node SDK signing algorithm, not this C# implementation.
            Assert.Equal("tOsaalrcGQ/sP+W5znQu0VW1Rvn0uoZBMduF/Xpk0qw=", checkout.Fields["signature"]);
            Assert.DoesNotContain(settings.SecretKey, JsonSerializer.Serialize(checkout));
        }
        finally { CultureInfo.CurrentCulture = culture; }
    }

    [Fact]
    public async Task BookingPlaceholder_IsReplaced_AndRequiresTheBooking()
    {
        var settings = Settings();
        settings.InvoiceDetailsUrlTemplate = "https://customer.example/bookings/{bookingId}/invoices/{invoiceId}";
        var bookingId = Guid.Parse("66666666-7777-8888-9999-000000000000");
        var service = new SePay.Service(Options.Create(settings));
        var checkout = await service.CreateCheckoutAsync(InvoiceId, "RCP-20261110-0001", 100000m, "VND", bookingId);
        foreach (var field in new[] { "success_url", "error_url", "cancel_url" })
            Assert.Equal($"https://customer.example/bookings/{bookingId}/invoices/{InvoiceId}", checkout.Fields[field]);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateCheckoutAsync(InvoiceId, "RCP-20261110-0001", 100000m, "VND"));
    }

    [Theory]
    [InlineData("EUR")]
    [InlineData("USD")]
    public async Task UnsupportedCurrency_IsRejectedWithoutConversion(string currency)
    {
        var error = await Assert.ThrowsAsync<UnprocessableEntityException>(() => new SePay.Service(Options.Create(Settings()))
            .CreateCheckoutAsync(InvoiceId, "RCP-20261110-0001", 151800000m, currency));
        Assert.Equal("UNSUPPORTED_PAYMENT_CURRENCY", error.ErrorCode); Assert.Equal(422, error.StatusCode);
    }

    [Theory]
    [InlineData("123.45")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("9223372036854775808")]
    public async Task InvalidWholeDongAmount_IsRejectedWithoutRounding(string amount)
    {
        var error = await Assert.ThrowsAsync<UnprocessableEntityException>(() => new SePay.Service(Options.Create(Settings()))
            .CreateCheckoutAsync(InvoiceId, "RCP-20261110-0001", decimal.Parse(amount, CultureInfo.InvariantCulture), "VND"));
        Assert.Equal("UNSUPPORTED_PAYMENT_AMOUNT", error.ErrorCode);
    }

    [Theory]
    [InlineData("RCP-20261110-0001,currency=EUR")]
    [InlineData("RCP-20261110-0001\n")]
    public async Task ReceiptCode_CannotInjectSignedFields(string code) =>
        await Assert.ThrowsAsync<InvalidDataException>(() => new SePay.Service(Options.Create(Settings()))
            .CreateCheckoutAsync(InvoiceId, code, 100000m, "VND"));

    [Fact]
    public async Task InvalidConfiguration_IsRejected_OnlyLocalSandboxAllowsHttp()
    {
        var invalid = new[]
        {
            new SePay.SePayOptions(),
            new SePay.SePayOptions { MerchantId = "merchant", SecretKey = "secret", Environment = "unknown" },
            new SePay.SePayOptions { MerchantId = "merchant", SecretKey = "secret", InvoiceDetailsUrlTemplate = "https://customer.example/invoices" },
            new SePay.SePayOptions { MerchantId = "merchant", SecretKey = "secret", Environment = "production", InvoiceDetailsUrlTemplate = "http://localhost/invoices/{invoiceId}" },
            new SePay.SePayOptions { MerchantId = "merchant", SecretKey = "secret", InvoiceDetailsUrlTemplate = "http://customer.example/invoices/{invoiceId}" },
            new SePay.SePayOptions { MerchantId = "merchant", SecretKey = "secret", InvoiceDetailsUrlTemplate = "https://user:password@customer.example/invoices/{invoiceId}" }
        };
        foreach (var settings in invalid)
            await Assert.ThrowsAsync<InvalidOperationException>(() => new SePay.Service(Options.Create(settings))
                .CreateCheckoutAsync(InvoiceId, "RCP-20261110-0001", 100000m, "VND"));
        var local = Settings(); local.InvoiceDetailsUrlTemplate = "http://localhost:3000/invoices/{invoiceId}";
        Assert.StartsWith("http://localhost:3000/", (await new SePay.Service(Options.Create(local))
            .CreateCheckoutAsync(InvoiceId, "RCP-20261110-0001", 100000m, "VND")).Fields["success_url"]);
    }
}
