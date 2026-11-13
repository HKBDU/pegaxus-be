using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Pegaxus.Service.Assistant.Gemini;
using Pegaxus.Service.Assistant.Tools;
using Pegaxus.Service.Exceptions;

namespace Pegaxus.Service.Assistant;

public sealed class AssistantService(
    IGeminiClient gemini,
    AssistantToolRegistry tools,
    Microsoft.Extensions.Options.IOptions<AssistantOptions> options,
    ILogger<AssistantService> logger) : IAssistantService
{
    private const string OutOfScopeReply = "I can only help look up information about my transport bookings. Which booking would you like to see?";
    private const string NoAnswerReply = "I could not find enough information to answer that.";
    private const string ToolLimitMessage = "The tool call limit for this question is reached. Answer now with the data you already have, and say what is missing.";
    private readonly AssistantOptions _options = options.Value;

    public async Task<ChatResponse> ChatAsync(Guid accountId, ChatRequest request, CancellationToken cancellationToken)
    {
        if (accountId == Guid.Empty) throw new UnauthorizedAccessException("Authenticated account is required.");
        var latestUserMessage = request.Messages.LastOrDefault(message => message.Role == "user")?.Content.Trim();
        if (string.IsNullOrWhiteSpace(latestUserMessage))
            return new ChatResponse(OutOfScopeReply, []);

        var total = Stopwatch.StartNew();
        var context = new AssistantToolContext(accountId, cancellationToken);
        var references = new List<AssistantReferenceCandidate>();
        var input = new JsonArray();
        foreach (var message in request.Messages)
        {
            input.Add(new JsonObject
            {
                ["type"] = message.Role == "user" ? "user_input" : "model_output",
                ["content"] = Text(message.Content.Trim())
            });
        }

        // The scope check runs beside the first answer instead of before it: an in-scope question no longer waits for two
        // provider round trips in a row, and an out-of-scope one discards the answer unread.
        var scope = TimedAsync("scope", () => gemini.IsInScopeAsync(latestUserMessage, cancellationToken));
        if (request.BookingId is not null)
            input.Add(await SelectedBookingAsync(request.BookingId, context, references));

        using var firstCall = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var first = TimedAsync("answer-1", () => gemini.CreateAsync(input, tools.Definitions, firstCall.Token));
        bool inScope;
        try
        {
            inScope = await scope;
        }
        catch
        {
            await DiscardAsync(first, firstCall);
            throw;
        }
        if (!inScope)
        {
            await DiscardAsync(first, firstCall);
            return new ChatResponse(OutOfScopeReply, []);
        }

        var result = await first;
        var totalToolCalls = 0;
        for (var round = 2; ; round++)
        {
            foreach (var step in result.Steps) input.Add(step?.DeepClone());
            var calls = result.Steps.OfType<JsonObject>()
                .Where(step => step["type"]?.GetValue<string>() == "function_call").ToArray();
            if (calls.Length == 0)
            {
                logger.LogInformation("Assistant answered in {ElapsedMs} ms after {Rounds} model rounds and {ToolCalls} tool calls",
                    total.ElapsedMilliseconds, round - 1, totalToolCalls);
                return ChatResponse.FromToolResults(string.IsNullOrWhiteSpace(result.OutputText) ? NoAnswerReply : result.OutputText, references);
            }

            var limitReached = totalToolCalls + calls.Length > _options.MaxToolCalls;
            foreach (var call in calls)
            {
                var name = call["name"]?.GetValue<string>() ?? string.Empty;
                var callId = call["id"]?.GetValue<string>() ?? call["call_id"]?.GetValue<string>() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(callId) || call["arguments"] is not JsonObject args)
                    throw new GeminiUnavailableException("The assistant returned an invalid tool request.", Microsoft.AspNetCore.Http.StatusCodes.Status502BadGateway);
                object data;
                if (limitReached)
                    data = new { error = ToolLimitMessage };
                else
                {
                    totalToolCalls++;
                    data = await RunToolAsync(name, args, context, references);
                }
                input.Add(new JsonObject
                {
                    ["type"] = "function_result",
                    ["name"] = name,
                    ["call_id"] = callId,
                    ["result"] = Text(JsonSerializer.Serialize(data))
                });
            }

            // Past the limit the model gets no tools for one last round, so the customer still gets an answer from what was found.
            result = await TimedAsync($"answer-{round}", () => gemini.CreateAsync(input, limitReached ? [] : tools.Definitions, cancellationToken));
            if (limitReached)
            {
                logger.LogInformation("Assistant hit the tool call limit and answered in {ElapsedMs} ms", total.ElapsedMilliseconds);
                return ChatResponse.FromToolResults(string.IsNullOrWhiteSpace(result.OutputText) ? NoAnswerReply : result.OutputText, references);
            }
        }
    }

    /// <summary>
    /// Runs one tool. A domain refusal (not found, invalid argument) goes back to the model as data so it can tell the customer,
    /// instead of failing the whole chat.
    /// </summary>
    private async Task<object> RunToolAsync(string name, JsonObject args, AssistantToolContext context, List<AssistantReferenceCandidate> references)
    {
        try
        {
            var tool = tools.GetRequired(name);
            using var arguments = JsonDocument.Parse(args.ToJsonString());
            var result = await tool.RunAsync(arguments.RootElement, context);
            references.AddRange(result.References);
            return result.Data;
        }
        catch (Exceptions.Exceptions exception) when (exception is not GeminiUnavailableException)
        {
            logger.LogInformation("Assistant tool {Tool} refused: {ErrorCode}", name, exception.ErrorCode);
            return new { error = exception.Message };
        }
    }

    /// <summary>The booking the customer has open is read up front, which saves the model a tool round.</summary>
    private async Task<JsonObject> SelectedBookingAsync(string bookingId, AssistantToolContext context, List<AssistantReferenceCandidate> references)
    {
        var data = await RunToolAsync("customer_get_booking", new JsonObject { ["bookingId"] = bookingId }, context, references);
        return new JsonObject
        {
            ["type"] = "user_input",
            ["content"] = Text($"The customer has this booking open (result of customer_get_booking): <data>{JsonSerializer.Serialize(data)}</data>")
        };
    }

    private async Task<T> TimedAsync<T>(string step, Func<Task<T>> call)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            return await call();
        }
        finally
        {
            logger.LogInformation("Assistant model call {Step} took {ElapsedMs} ms", step, watch.ElapsedMilliseconds);
        }
    }

    private static async Task DiscardAsync(Task task, CancellationTokenSource source)
    {
        source.Cancel();
        // The answer is not used; its cancellation or failure must not surface (or go unobserved).
        try { await task; } catch { }
    }

    private static JsonArray Text(string text) => new(new JsonObject { ["type"] = "text", ["text"] = text });
}
