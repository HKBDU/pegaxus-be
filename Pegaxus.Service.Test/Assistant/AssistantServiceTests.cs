using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pegaxus.Service.Assistant;
using Pegaxus.Service.Assistant.Gemini;
using Pegaxus.Service.Assistant.Tools;
using Pegaxus.Service.Exceptions;

namespace Pegaxus.Service.Test.Assistant;

public class AssistantServiceTests
{
    [Fact]
    public async Task ChatAsync_PreservesModelStepsAndBuildsReferenceFromToolResult()
    {
        var bookingId = Guid.NewGuid();
        var gemini = new QueueGemini(true,
            new GeminiInteractionResult(new JsonArray(
                new JsonObject { ["type"] = "thought", ["signature"] = "preserve-this-step" },
                Call("call-1", "customer_list_my_bookings")), string.Empty),
            Answer("Your booking is approved."));
        var service = Create(gemini, new FakeTool("customer_list_my_bookings", bookingId));

        var response = await service.ChatAsync(Guid.NewGuid(), Ask("What is my booking status?"), CancellationToken.None);

        Assert.Equal("Your booking is approved.", response.Reply);
        Assert.Equal(bookingId, Assert.Single(response.References).Id);
        Assert.Equal("preserve-this-step", gemini.Inputs[1][1]!["signature"]!.GetValue<string>());
        Assert.Equal("function_result", gemini.Inputs[1][3]!["type"]!.GetValue<string>());
    }

    [Fact]
    public async Task ChatAsync_PastTheToolCallLimit_AnswersWithoutToolsFromWhatWasFound()
    {
        var tool = new FakeTool("customer_list_my_bookings", Guid.NewGuid());
        var gemini = new QueueGemini(true, Enumerable.Range(1, 6)
            .Select(index => new GeminiInteractionResult(new JsonArray(Call($"call-{index}", "customer_list_my_bookings")), string.Empty))
            .Append(Answer("Here is what I found so far."))
            .ToArray());
        var service = Create(gemini, tool);

        var response = await service.ChatAsync(Guid.NewGuid(), Ask("List my bookings"), CancellationToken.None);

        Assert.Equal("Here is what I found so far.", response.Reply);
        Assert.Equal(5, tool.Calls);
        Assert.Equal(7, gemini.Inputs.Count);
        Assert.Empty(gemini.Tools[^1]);
        Assert.Contains("limit", gemini.Inputs[^1][^1]!["result"]![0]!["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task ChatAsync_OutOfScope_RefusesAndDropsTheAnswerStartedAlongside()
    {
        var gemini = new QueueGemini(false, Answer("Here is a pasta recipe."));
        var service = Create(gemini, new FakeTool("customer_list_my_bookings", Guid.NewGuid()));

        var response = await service.ChatAsync(Guid.NewGuid(), Ask("How do I cook pasta?"), CancellationToken.None);

        Assert.StartsWith("I can only help look up information", response.Reply);
        Assert.Empty(response.References);
        // The scope check no longer delays the first answer: both were sent.
        Assert.Single(gemini.Inputs);
    }

    [Fact]
    public async Task ChatAsync_AToolRefusal_GoesBackToTheModel_InsteadOfFailingTheChat()
    {
        var gemini = new QueueGemini(true,
            new GeminiInteractionResult(new JsonArray(Call("call-1", "customer_get_booking")), string.Empty),
            Answer("I couldn't find this booking in your account."));
        var failing = new FakeTool("customer_get_booking", Guid.NewGuid(), new NotFoundException("Booking not found", "BOOKING_NOT_FOUND"));
        var service = Create(gemini, new FakeTool("customer_list_my_bookings", Guid.NewGuid()), failing);

        var response = await service.ChatAsync(Guid.NewGuid(), Ask("Where is REQ-404?"), CancellationToken.None);

        Assert.Equal("I couldn't find this booking in your account.", response.Reply);
        var result = gemini.Inputs[1][^1]!["result"]![0]!["text"]!.GetValue<string>();
        Assert.Equal("Booking not found", JsonDocument.Parse(result).RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task ChatAsync_TheOpenBooking_IsReadUpFrontAndGivenToTheFirstModelCall()
    {
        var bookingId = Guid.NewGuid();
        var detail = new FakeTool("customer_get_booking", bookingId);
        var gemini = new QueueGemini(true, Answer("It is approved."));
        var service = Create(gemini, new FakeTool("customer_list_my_bookings", Guid.NewGuid()), detail);

        var request = new ChatRequest { Messages = Ask("Is it approved?").Messages, BookingId = bookingId.ToString() };
        var response = await service.ChatAsync(Guid.NewGuid(), request, CancellationToken.None);

        Assert.Equal(1, detail.Calls);
        Assert.Equal(bookingId.ToString(), detail.LastArguments!.Value.GetProperty("bookingId").GetString());
        Assert.Single(gemini.Inputs);
        Assert.Contains("APPROVED", gemini.Inputs[0][^1]!["content"]![0]!["text"]!.GetValue<string>());
        Assert.Equal(bookingId, Assert.Single(response.References).Id);
    }

    private static AssistantService Create(QueueGemini gemini, IAssistantTool active, IAssistantTool? detail = null)
    {
        IAssistantTool[] tools = [
            active,
            detail ?? new FakeTool("customer_get_booking", Guid.NewGuid()),
            new FakeTool("customer_get_booking_history", Guid.NewGuid()),
            new FakeTool("customer_get_dossier", Guid.NewGuid()),
            new FakeTool("customer_get_tracking", Guid.NewGuid())
        ];
        return new AssistantService(gemini, new AssistantToolRegistry(tools.DistinctBy(tool => tool.Name)),
            Options.Create(new AssistantOptions()), NullLogger<AssistantService>.Instance);
    }

    private static ChatRequest Ask(string question) => new() { Messages = [new ChatMessageRequest { Role = "user", Content = question }] };

    private static JsonObject Call(string id, string name) =>
        new() { ["type"] = "function_call", ["id"] = id, ["name"] = name, ["arguments"] = new JsonObject() };

    private static GeminiInteractionResult Answer(string text) => new(new JsonArray(new JsonObject
    {
        ["type"] = "model_output",
        ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text })
    }), text);

    private sealed class FakeTool(string name, Guid bookingId, Exception? failure = null) : IAssistantTool
    {
        public int Calls { get; private set; }
        public JsonElement? LastArguments { get; private set; }
        public string Name => name;
        public string Description => "test";
        public JsonElement ParametersSchema => JsonDocument.Parse("{\"type\":\"object\"}").RootElement.Clone();

        public Task<AssistantToolResult> RunAsync(JsonElement arguments, AssistantToolContext context)
        {
            Calls++;
            LastArguments = arguments.Clone();
            if (failure is not null) throw failure;
            return Task.FromResult(new AssistantToolResult(Name, new { status = "APPROVED" },
                [new AssistantReferenceCandidate("booking", bookingId.ToString(), "Booking", DateTimeOffset.UtcNow)]));
        }
    }

    private sealed class QueueGemini(bool inScope, params GeminiInteractionResult[] responses) : IGeminiClient
    {
        private readonly Queue<GeminiInteractionResult> _responses = new(responses);
        public List<JsonArray> Inputs { get; } = [];
        public List<IReadOnlyList<AssistantToolDefinition>> Tools { get; } = [];

        public Task<bool> IsInScopeAsync(string message, CancellationToken cancellationToken) => Task.FromResult(inScope);

        public Task<GeminiInteractionResult> CreateAsync(JsonArray input, IReadOnlyList<AssistantToolDefinition> tools, CancellationToken cancellationToken)
        {
            Inputs.Add((JsonArray)input.DeepClone());
            Tools.Add(tools);
            return Task.FromResult(_responses.Dequeue());
        }
    }
}
