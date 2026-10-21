namespace Pegaxus.Service.MailService;

public static partial class EmailBodies
{
    /// <summary>Urgent incident alert for the Logistics Manager.</summary>
    public static string IncidentReportedBody(string staffName, string incidentCode, string severity, string title, string description, string? actionUrl) =>
        Notice("Urgent transport incident", "An incident needs urgent attention", incidentCode, staffName,
            Encode("An incident has been reported. Review it and coordinate the response."), Tone.Error, "Urgent",
            [Row("Incident", Accent(incidentCode)), Row("Severity", Encode(severity)), Row("Title", Encode(title))],
            description, "Details", actionUrl, "Review incident");
} 
