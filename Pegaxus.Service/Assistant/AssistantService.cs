using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Pegaxus.Service.Assistant.Gemini;
using Pegaxus.Service.Assistant.Tools;
using Pegaxus.Service.Exceptions;

namespace Pegaxus.Service.Assistant;

public sealed class AssistantService(
    IGeminiClient gemini,
    AssistantToolRegistry tools,
    Microsoft.Extensions.Options.IOptions<AssistantOptions> options) : IAssistantService
{
    private readonly AssistantOptions _options = options.Value;

    public async Task<ChatResponse> ChatAsync(Guid accountId, ChatRequest request, CancellationToken cancellationToken)
    {
        if (accountId == Guid.Empty) throw new UnauthorizedAccessException("Authenticated account is required.");
        var input = new JsonArray();
        foreach (var message in request.Messages)
        {
            input.Add(new JsonObject
            {
                ["type"] = message.Role == "user" ? "user_input" : "model_output",
                ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = message.Content.Trim() })
            });
        }
        if (request.BookingId is not null)
            input.Add(new JsonObject { ["type"] = "user_input", ["content"] = new JsonArray(new JsonObject
                { ["type"] = "text", ["text"] = $"The customer has selected booking context {request.BookingId}. Verify ownership through tools before using it." }) });

        var references = new List<AssistantReferenceCandidate>();
        var totalToolCalls = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await gemini.CreateAsync(input, tools.Definitions, cancellationToken);
            foreach (var step in result.Steps) input.Add(step?.DeepClone());
            var calls = result.Steps.OfType<JsonObject>()
                .Where(step => step["type"]?.GetValue<string>() == "function_call").ToArray();
            if (calls.Length == 0)
                return ChatResponse.FromToolResults(string.IsNullOrWhiteSpace(result.OutputText)
                    ? "I could not find enough information to answer that."
                    : result.OutputText, references);

            if (totalToolCalls + calls.Length > _options.MaxToolCalls)
                throw new GeminiUnavailableException("The assistant reached its tool call limit. Please try a narrower question.", StatusCodes.Status502BadGateway);

            foreach (var call in calls)
            {
                totalToolCalls++;
                var name = call["name"]?.GetValue<string>() ?? string.Empty;
                var callId = call["id"]?.GetValue<string>() ?? call["call_id"]?.GetValue<string>() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(callId) || call["arguments"] is not JsonObject args)
                    throw new GeminiUnavailableException("The assistant returned an invalid tool request.", StatusCodes.Status502BadGateway);
                var tool = tools.GetRequired(name);
                using var arguments = JsonDocument.Parse(args.ToJsonString());
                var toolResult = await tool.RunAsync(arguments.RootElement, new AssistantToolContext(accountId, cancellationToken));
                references.AddRange(toolResult.References);
                input.Add(new JsonObject
                {
                    ["type"] = "function_result",
                    ["name"] = name,
                    ["call_id"] = callId,
                    ["result"] = new JsonArray(new JsonObject
                    {
                        ["type"] = "text",
                        ["text"] = JsonSerializer.Serialize(toolResult.Data)
                    })
                });
            }
        }
    }
}
