namespace Pegaxus.Service.Assistant;

/// <summary>Customer chat input. Identity and model configuration come from the server.</summary>
public sealed class ChatRequest
{
    public IReadOnlyList<ChatMessageRequest> Messages { get; init; } = [];
    public string? BookingId { get; init; }
}

public sealed class ChatMessageRequest
{
    public string Role { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
}
