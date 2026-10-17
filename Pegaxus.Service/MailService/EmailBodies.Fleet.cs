namespace Pegaxus.Service.MailService;

public static partial class EmailBodies
{
    /// <summary>A vehicle or transport provider changed status, for example it is no longer available.</summary>
    /// <param name="entityLabel">Fixed text such as "Vehicle" or "Transport provider".</param>
    public static string CatalogChangeBody(
        string staffName, string entityLabel, string code, string name, string outcome, string? reason, string? actionUrl) =>
        Notice($"{entityLabel} status changed", $"{entityLabel} {code} changed status", code, staffName,
            $"{Strong(name)} ({Encode(code)}) {Encode(outcome)}.", Tone.Warning, "Status changed",
            [Row(entityLabel, Accent(code))], reason, "Reason", actionUrl, $"View {entityLabel.ToLowerInvariant()}");
}
