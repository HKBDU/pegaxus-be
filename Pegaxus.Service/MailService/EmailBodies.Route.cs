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
}
