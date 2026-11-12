namespace Pegaxus.Service.Assistant;

public sealed record ChatResponse(string Reply, IReadOnlyList<AssistantReference> References)
{
    private static readonly HashSet<string> AllowedReferenceTypes = new(StringComparer.Ordinal)
    {
        "booking",
        "dossier",
        "tracking"
    };

    /// <summary>Builds API references only from validated server-side tool results.</summary>
    public static ChatResponse FromToolResults(string reply, IEnumerable<AssistantReferenceCandidate> candidates)
    {
        var references = candidates
            .Where(candidate => AllowedReferenceTypes.Contains(candidate.Type)
                && Guid.TryParse(candidate.Id, out var id)
                && id != Guid.Empty
                && !string.IsNullOrWhiteSpace(candidate.Label))
            .Select(candidate => new AssistantReference(
                candidate.Type,
                Guid.Parse(candidate.Id),
                candidate.Label.Trim(),
                candidate.UpdatedAt))
            .DistinctBy(reference => (reference.Type, reference.Id))
            .Take(3)
            .ToArray();

        return new ChatResponse(reply, references);
    }
}

/// <summary>Internal projection input produced by trusted domain tools; it has no URL field.</summary>
public sealed record AssistantReferenceCandidate(string Type, string Id, string Label, DateTimeOffset? UpdatedAt);

public sealed record AssistantReference(string Type, Guid Id, string Label, DateTimeOffset? UpdatedAt);
