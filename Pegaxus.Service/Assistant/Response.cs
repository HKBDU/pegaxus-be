namespace Pegaxus.Service.Assistant;

public sealed record ChatResponse(string Reply, IReadOnlyList<AssistantReference> References)
{
    private const string OutOfScopeRefusal = "I can only help look up information about my transport bookings. Which booking would you like to see?";
    private static readonly HashSet<string> AllowedReferenceTypes = new(StringComparer.Ordinal)
    {
        "booking",
        "dossier",
        "tracking"
    };

    /// <summary>Builds API references only from validated server-side tool results.</summary>
    public static ChatResponse FromToolResults(string reply, IEnumerable<AssistantReferenceCandidate> candidates)
    {
        if (string.Equals(reply.Trim(), OutOfScopeRefusal, StringComparison.OrdinalIgnoreCase))
            return new ChatResponse(reply, []);

        var references = candidates
            .Where(candidate => AllowedReferenceTypes.Contains(candidate.Type)
                && Guid.TryParse(candidate.Id, out var id)
                && id != Guid.Empty
                && !string.IsNullOrWhiteSpace(candidate.Label))
            .Select(candidate => new AssistantReference(
                candidate.Type,
                Guid.Parse(candidate.Id),
                candidate.Label.Trim(),
                candidate.UpdatedAt,
                Guid.TryParse(candidate.BookingId, out var bookingId) && bookingId != Guid.Empty ? bookingId : null))
            .DistinctBy(reference => (reference.Type, reference.Id))
            .Take(3)
            .ToArray();

        return new ChatResponse(reply, references);
    }
}

/// <summary>Internal projection input produced by trusted domain tools; it has no URL field.</summary>
public sealed record AssistantReferenceCandidate(string Type, string Id, string Label, DateTimeOffset? UpdatedAt, string? BookingId = null);

public sealed record AssistantReference(string Type, Guid Id, string Label, DateTimeOffset? UpdatedAt, Guid? BookingId = null);
