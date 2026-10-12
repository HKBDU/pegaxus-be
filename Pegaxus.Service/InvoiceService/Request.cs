namespace Pegaxus.Service.InvoiceService;

public static class Request
{
    public sealed class CancelRequest
    {
        public string CancellationReason { get; set; } = "";
    }
}
