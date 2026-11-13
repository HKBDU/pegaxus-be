namespace Pegaxus.Service.Assistant;

/// <summary>Server-side Gemini settings for the Customer Assistant.</summary>
public sealed class AssistantOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string ApiUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta/interactions";
    public int TimeoutSeconds { get; set; } = 60;
    public int MaxOutputTokens { get; set; } = 1024;
    public int MaxToolCalls { get; set; } = 5;
}
