using System.Text.Json;
using System.Text.Json.Serialization;
using Pegaxus.Service.Exceptions;

namespace Pegaxus.Service.Assistant.Tools;

public sealed class AssistantToolRegistry
{
    private static readonly string[] RequiredNames =
    [
        "customer_list_my_bookings",
        "customer_get_booking",
        "customer_get_booking_history",
        "customer_get_dossier",
        "customer_get_tracking"
    ];

    private readonly IReadOnlyDictionary<string, IAssistantTool> _byName;

    public AssistantToolRegistry(IEnumerable<IAssistantTool> tools)
    {
        var registered = tools.ToArray();
        if (registered.Any(tool => string.IsNullOrWhiteSpace(tool.Name)) ||
            registered.Select(tool => tool.Name).Distinct(StringComparer.Ordinal).Count() != registered.Length ||
            !RequiredNames.ToHashSet(StringComparer.Ordinal).SetEquals(registered.Select(tool => tool.Name)))
            throw new InvalidOperationException("Assistant registry must contain exactly the five Customer read-only tools.");

        _byName = registered.ToDictionary(tool => tool.Name, StringComparer.Ordinal);
        Tools = Array.AsReadOnly(RequiredNames.Select(name => _byName[name]).ToArray());
    }

    public IReadOnlyList<IAssistantTool> Tools { get; }

    public IReadOnlyList<AssistantToolDefinition> Definitions =>
        Array.AsReadOnly(Tools.Select(tool => new AssistantToolDefinition(tool.Name, tool.Description, tool.ParametersSchema)).ToArray());

    public bool TryGet(string name, out IAssistantTool? tool) => _byName.TryGetValue(name, out tool);

    public IAssistantTool GetRequired(string name) =>
        _byName.TryGetValue(name, out var tool)
            ? tool
            : throw new BadRequestException("The requested assistant tool is not allowed.", "TOOL_NOT_ALLOWED");
}

public sealed record AssistantToolDefinition(string Name, string Description, JsonElement Parameters);

public sealed record AssistantToolResult(string Name, object Data, IReadOnlyList<AssistantReferenceCandidate> References);

internal static class AssistantToolJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static JsonElement Schema(string json) => JsonDocument.Parse(json).RootElement.Clone();

    public static T Parse<T>(JsonElement arguments) where T : class
    {
        if (arguments.ValueKind != JsonValueKind.Object)
            throw new BadRequestException("Tool arguments must be a JSON object.", "VALIDATION_ERROR");

        try
        {
            return JsonSerializer.Deserialize<T>(arguments, Options)
                ?? throw new BadRequestException("Tool arguments are invalid.", "VALIDATION_ERROR");
        }
        catch (JsonException)
        {
            throw new BadRequestException("Tool arguments are invalid.", "VALIDATION_ERROR");
        }
    }

    public static void ValidateContext(AssistantToolContext context)
    {
        if (context.AccountId == Guid.Empty)
            throw new BadRequestException("Authenticated account context is required.", "VALIDATION_ERROR");
        context.CancellationToken.ThrowIfCancellationRequested();
    }
}
