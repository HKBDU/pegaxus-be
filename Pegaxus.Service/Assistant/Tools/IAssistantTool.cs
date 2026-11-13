using System.Text.Json;

namespace Pegaxus.Service.Assistant.Tools;

public interface IAssistantTool
{
    string Name { get; }
    string Description { get; }
    JsonElement ParametersSchema { get; }
    Task<AssistantToolResult> RunAsync(JsonElement arguments, AssistantToolContext context);
}
