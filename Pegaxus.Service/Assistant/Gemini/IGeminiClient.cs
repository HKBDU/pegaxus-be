using System.Text.Json.Nodes;
using Pegaxus.Service.Assistant.Tools;

namespace Pegaxus.Service.Assistant.Gemini;

public interface IGeminiClient
{
    Task<bool> IsInScopeAsync(string message, CancellationToken cancellationToken);

    Task<GeminiInteractionResult> CreateAsync(
        JsonArray input,
        IReadOnlyList<AssistantToolDefinition> tools,
        CancellationToken cancellationToken);
}

public sealed record GeminiInteractionResult(JsonArray Steps, string OutputText);
