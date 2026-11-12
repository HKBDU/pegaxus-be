using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Pegaxus.Service.Assistant;
using Pegaxus.Service.Assistant.Gemini;
using Pegaxus.Service.Assistant.Tools;

namespace Pegaxus.Service.Test.Assistant;

public class AssistantServiceTests
{
    [Fact]
    public async Task ChatAsync_PreservesModelStepsAndBuildsReferenceFromToolResult()
    {
        var bookingId = Guid.NewGuid();
        var gemini = new QueueGemini(
            new GeminiInteractionResult(new JsonArray(
                new JsonObject { ["type"] = "thought", ["signature"] = "preserve-this-step" },
                new JsonObject { ["type"] = "function_call", ["id"] = "call-1", ["name"] = "customer_list_my_bookings", ["arguments"] = new JsonObject() }), string.Empty),
            new GeminiInteractionResult(new JsonArray(new JsonObject
            {
                ["type"] = "model_output",
                ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = "Your booking is approved." })
            }), "Your booking is approved."));
        var tools = RequiredTools(new FakeTool("customer_list_my_bookings", bookingId));
        var service = new AssistantService(gemini, new AssistantToolRegistry(tools), Options.Create(new AssistantOptions()));

        var response = await service.ChatAsync(Guid.NewGuid(), new ChatRequest
        {
            Messages = [new ChatMessageRequest { Role = "user", Content = "What is my booking status?" }]
        }, CancellationToken.None);

        Assert.Equal("Your booking is approved.", response.Reply);
        Assert.Equal(bookingId, Assert.Single(response.References).Id);
        Assert.Equal("preserve-this-step", gemini.Inputs[1][1]!["signature"]!.GetValue<string>());
        Assert.Equal("function_result", gemini.Inputs[1][3]!["type"]!.GetValue<string>());
    }

    [Fact]
    public async Task ChatAsync_ExecutesAtMostFiveToolCalls()
    {
        var tool = new FakeTool("customer_list_my_bookings", Guid.NewGuid());
        var gemini = new QueueGemini(Enumerable.Range(1, 6).Select(index => new GeminiInteractionResult(
            new JsonArray(new JsonObject
            {
                ["type"] = "function_call", ["id"] = $"call-{index}", ["name"] = "customer_list_my_bookings", ["arguments"] = new JsonObject()
            }), string.Empty)).ToArray());
        var service = new AssistantService(gemini, new AssistantToolRegistry(RequiredTools(tool)), Options.Create(new AssistantOptions()));

        await Assert.ThrowsAsync<GeminiUnavailableException>(() => service.ChatAsync(Guid.NewGuid(), new ChatRequest
        {
            Messages = [new ChatMessageRequest { Role = "user", Content = "List my bookings" }]
        }, CancellationToken.None));

        Assert.Equal(5, tool.Calls);
        Assert.Equal(6, gemini.Inputs.Count);
    }

    private static IAssistantTool[] RequiredTools(IAssistantTool active) => [
        active,
        new FakeTool("customer_get_booking", Guid.NewGuid()),
        new FakeTool("customer_get_booking_history", Guid.NewGuid()),
        new FakeTool("customer_get_dossier", Guid.NewGuid()),
        new FakeTool("customer_get_tracking", Guid.NewGuid())
    ];

    private sealed class FakeTool(string name, Guid bookingId) : IAssistantTool
    {
        public int Calls { get; private set; }
        public string Name => name;
        public string Description => "test";
        public JsonElement ParametersSchema => JsonDocument.Parse("{\"type\":\"object\"}").RootElement.Clone();

        public Task<AssistantToolResult> RunAsync(JsonElement arguments, AssistantToolContext context)
        {
            Calls++;
            return Task.FromResult(new AssistantToolResult(Name, new { status = "APPROVED" },
                [new AssistantReferenceCandidate("booking", bookingId.ToString(), "Booking", DateTimeOffset.UtcNow)]));
        }
    }

    private sealed class QueueGemini(params GeminiInteractionResult[] responses) : IGeminiClient
    {
        private readonly Queue<GeminiInteractionResult> _responses = new(responses);
        public List<JsonArray> Inputs { get; } = [];

        public Task<GeminiInteractionResult> CreateAsync(JsonArray input, IReadOnlyList<AssistantToolDefinition> tools, CancellationToken cancellationToken)
        {
            Inputs.Add((JsonArray)input.DeepClone());
            return Task.FromResult(_responses.Dequeue());
        }
    }
}
