namespace Pegaxus.Service.Assistant.Tools;

public readonly record struct AssistantToolContext(Guid AccountId, CancellationToken CancellationToken);
