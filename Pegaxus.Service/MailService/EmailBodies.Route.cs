namespace Pegaxus.Service.MailService;

public static partial class EmailBodies
{
    public sealed record RouteSegment(
        int SequenceNo,
        DateTime? PlannedDepartureAt,
        DateTime? PlannedArrivalAt,
        string? DriverCode,
        string? EscortCode,
        string? ProviderName);

    /// <summary>Tells an employee about a new, or replaced, personnel assignment on a route plan.</summary>
    public static string RouteAssignmentBody(
        string employeeName, string routePlanCode,
        bool isReplaced, string? replaceReason,
        string? actionUrl, IEnumerable<RouteSegment> segments)
    {
        var segmentsHtml = string.Concat(segments.Select(segment =>
        {
            var people = new List<string>();
            if (!string.IsNullOrWhiteSpace(segment.DriverCode))
                people.Add($"""Driver: <strong class="px-strong" style="color:#000000;">{Encode(segment.DriverCode)}</strong>""");
            if (!string.IsNullOrWhiteSpace(segment.EscortCode))
                people.Add($"""Escort: <strong class="px-strong" style="color:#000000;">{Encode(segment.EscortCode)}</strong>""");
            if (!string.IsNullOrWhiteSpace(segment.ProviderName))
                people.Add($"""Provider: <strong class="px-strong" style="color:#000000;">{Encode(segment.ProviderName)}</strong>""");
            var assignees = people.Count == 0 ? "&mdash;" : string.Join("<br>", people);
            return $"""<table class="px-line" role="presentation" width="100%" cellspacing="0" cellpadding="0" border="0" style="margin:0 0 12px;border:1px solid #EFEFEF;border-radius:14px;"><tr><td style="padding:16px 18px;"><div style="font-size:12px;line-height:1.4;font-weight:700;letter-spacing:.08em;text-transform:uppercase;color:#939B95;">Segment {segment.SequenceNo}</div><div style="margin-top:8px;font-size:15px;line-height:1.5;">Departs <strong class="px-strong" style="color:#000000;">{FmtDateTime(segment.PlannedDepartureAt)}</strong></div><div style="margin-top:2px;font-size:15px;line-height:1.5;">Arrives <strong class="px-strong" style="color:#000000;">{FmtDateTime(segment.PlannedArrivalAt)}</strong></div><div class="px-line" style="margin-top:10px;padding-top:10px;border-top:1px solid #EFEFEF;font-size:14px;line-height:1.6;">{assignees}</div></td></tr></table>""";
        }));
        var title = isReplaced ? "Route assignment updated" : "New route assignment";

        return Header(title, "Your route assignment details") +
            $"""
                  {Title(title, routePlanCode)}
                  {Greeting(employeeName)}
                  {Paragraph(isReplaced ? "Your assignment on the following route plan has been updated." : "You have been assigned to the following route plan.")}
                  {InfoPill(isReplaced ? "Replaced" : "Assigned")}
                  {Rows(Row("Route plan", Accent(routePlanCode)))}
                  {(isReplaced ? LabeledParagraph("Reason for change", replaceReason) : "")}
                  <p style="margin:0 0 12px;{BodyStyle}font-weight:500;">Route segments:</p>
                  {segmentsHtml}
                  <div style="height:12px;line-height:12px;font-size:12px;">&nbsp;</div>
                  {Button(actionUrl, "Open portal")}
            """ +
            Footer();
    }

    public enum RouteReviewKind
    {
        LegalReviewRequested, RevisionRequired, EmergencyApproval, ReturnApproval, ExtraDossierReady, ChangeApproved, ChangeRejected, ReturnActive
    }

    public static string RouteReviewTitle(RouteReviewKind kind) => kind switch
    {
        RouteReviewKind.LegalReviewRequested => "Route plan needs legal review",
        RouteReviewKind.RevisionRequired => "Route plan needs revision",
        RouteReviewKind.EmergencyApproval => "Emergency backup route needs approval",
        RouteReviewKind.ReturnApproval => "Return route needs your approval",
        RouteReviewKind.ExtraDossierReady => "Extra dossier ready for the return route",
        RouteReviewKind.ChangeApproved => "Route change approved",
        RouteReviewKind.ChangeRejected => "Route change rejected",
        _ => "Return route activated"
    };

    /// <summary>Staff email about a route plan: a review or decision is needed, or a route change was decided.</summary>
    public static string RouteReviewBody(
        string staffName, string routePlanCode, RouteReviewKind kind, string? detail, string? actionUrl)
    {
        var (message, tone, pill, noteLabel, button) = kind switch
        {
            RouteReviewKind.LegalReviewRequested => ("This route plan was submitted for legal review. Check that it is feasible, then confirm it or request changes.", Tone.Warning, "Action required", "Reason", "Review route plan"),
            RouteReviewKind.RevisionRequired => ("This route plan needs revision after legal review.", Tone.Warning, "Revision required", "Reviewer note", "Edit route plan"),
            RouteReviewKind.EmergencyApproval => ("A backup route is waiting for your decision.", Tone.Error, "Urgent", "Details", "Decide now"),
            RouteReviewKind.ReturnApproval => ("A return route plan was created and needs your approval before the transport can head back.", Tone.Error, "Urgent", "Details", "Decide now"),
            RouteReviewKind.ExtraDossierReady => ("A supplementary dossier was created for the return route. Add the necessary requirements and review the route plan.", Tone.Warning, "Action required", "Details", "Open route plan"),
            RouteReviewKind.ChangeApproved => ("The Logistics Manager approved this route change.", Tone.Success, "Approved", "Note", "View route plan"),
            RouteReviewKind.ChangeRejected => ("The Logistics Manager rejected this route change.", Tone.Error, "Rejected", "Note", "View route plan"),
            _ => ("This return route plan is now active.", Tone.Info, "Active", "Note", "View route plan")
        };
        var title = RouteReviewTitle(kind);
        return Notice(title, title, routePlanCode, staffName, Encode(message), tone, pill,
            [Row("Route plan", Accent(routePlanCode))], detail, noteLabel, actionUrl, button);
    }

    public enum RouteChangeKind { BackupActivated, ReturnActivated, RouteConfirmed, ReturnConfirmed, DriverBackup, DriverBeginReturn }

    public static string RouteChangedTitle(RouteChangeKind kind) => kind switch
    {
        RouteChangeKind.BackupActivated or RouteChangeKind.DriverBackup => "Backup route activated",
        RouteChangeKind.ReturnActivated => "Return route activated",
        RouteChangeKind.RouteConfirmed => "Route confirmed",
        RouteChangeKind.ReturnConfirmed => "Return route confirmed",
        _ => "Begin return journey"
    };

    /// <summary>A route change that affects the customer or the crew: backup or return activated, route confirmed, or a driver instruction.</summary>
    public static string RouteChangedBody(
        string recipientName, string routePlanCode, RouteChangeKind kind, string? note, string? actionUrl)
    {
        var (message, tone, pill, button) = kind switch
        {
            RouteChangeKind.BackupActivated => ("Your transport is now following a backup route because of an incident on the way.", Tone.Warning, "Route changed", "View booking"),
            RouteChangeKind.ReturnActivated => ("Your transport is heading back on a return route.", Tone.Warning, "Route changed", "View booking"),
            RouteChangeKind.RouteConfirmed => ("The route for your booking has been confirmed. Please upload the required legal documents.", Tone.Success, "Confirmed", "Open dossier"),
            RouteChangeKind.ReturnConfirmed => ("The return route has been confirmed. Please upload the additional documents it requires.", Tone.Warning, "Action required", "Open dossier"),
            RouteChangeKind.DriverBackup => ("Follow the backup route from the branch point. Check your updated segments.", Tone.Error, "Route changed", "View route"),
            _ => ("Start the return journey on this route plan.", Tone.Warning, "Action required", "View route")
        };
        var title = RouteChangedTitle(kind);
        return Notice(title, title, routePlanCode, recipientName, Encode(message), tone, pill,
            [Row("Route plan", Accent(routePlanCode))], note, "Note", actionUrl, button);
    }
}
