namespace Pegaxus.Service.PaymentGatewayService;

public sealed class SePayOptions
{
    public string MerchantId { get; set; } = "";
    public string SecretKey { get; set; } = "";
    public string Environment { get; set; } = "sandbox";
    public string InvoiceDetailsUrlTemplate { get; set; } = "";
}
