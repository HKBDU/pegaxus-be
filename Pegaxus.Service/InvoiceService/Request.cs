namespace Pegaxus.Service.InvoiceService;

public static class Request
{
    public sealed class SuggestionRequest
    {
        public string Type { get; set; } = string.Empty;
    }
}
