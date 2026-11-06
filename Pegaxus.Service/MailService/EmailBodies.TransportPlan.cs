namespace Pegaxus.Service.MailService;

public static partial class EmailBodies
{
    public enum TransportPlanMailKind { Assigned, PersonnelChanged, Updated }

    public static string TransportPlanTitle(TransportPlanMailKind kind) => kind switch
    {
        TransportPlanMailKind.Assigned => "Assigned to a transport plan",
        TransportPlanMailKind.PersonnelChanged => "Transport plan assignment updated",
        _ => "Transport plan updated"
    };

    /// <summary>Transport plan emails: a staff member was assigned, personnel changed, or the plan was updated.</summary>
    public static string TransportPlanBody(
        string recipientName, string planCode, TransportPlanMailKind kind, string? detail, string? actionUrl)
    {
        var (message, tone, pill, noteLabel) = kind switch
        {
            TransportPlanMailKind.Assigned => ("You have been assigned to this transport plan. Open it to review the booking and plan the next steps.", Tone.Success, "Assigned", "Your role"),
            TransportPlanMailKind.PersonnelChanged => ("A personnel assignment on this transport plan changed.", Tone.Warning, "Updated", "Details"),
            _ => ("This transport plan was updated.", Tone.Info, "Updated", "Reason")
        };
        var title = TransportPlanTitle(kind);
        return Notice(title, title, planCode, recipientName, Encode(message), tone, pill,
            [Row("Plan code", Accent(planCode))], detail, noteLabel, actionUrl, "View transport plan");
    }
}
