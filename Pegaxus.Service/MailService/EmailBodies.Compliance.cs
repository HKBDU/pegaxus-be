namespace Pegaxus.Service.MailService;

public static partial class EmailBodies
{
    public sealed record MissingItem(string RequirementName, string? HorseName, DateOnly? DueDate);

    /// <summary>A reviewed document was rejected and the customer must upload a new version.</summary>
    public static string DocumentReuploadBody(
        string requestCode, string requirementName, string? horseName,
        string errorCategory, string? note, DateOnly revisionDeadline, string? actionUrl)
    {
        var issue = errorCategory switch
        {
            "INVALID_IMAGE" => "Invalid or unreadable image",
            "EXPIRED" => "Document expired",
            _ => errorCategory
        };

        return Header("Document re-upload required", "Please re-upload a document for your request") +
            $"""
                  {Title("Re-upload required", requestCode)}
                  {Greeting(null)}
                  {Paragraph("One of your documents could not be accepted. Please upload a new version before the deadline.")}
                  {WarningPill("Action required")}
                  {Rows(
                      Row("Document", Encode(requirementName)),
                      string.IsNullOrWhiteSpace(horseName) ? null : Row("Horse", Encode(horseName)),
                      Row("Issue", Encode(issue)),
                      Row("Request code", Accent(requestCode)))}
                  {LabeledParagraph("Note", note)}
                  {Paragraph($"""<strong class="px-strong" style="color:#000000;">Deadline:</strong> Please re-upload by <strong class="px-strong" style="color:#000000;">{FmtDate(revisionDeadline)}</strong>.""")}
                  {Button(actionUrl, "Upload")}
            """ +
            Footer();
    }

    /// <summary>Reminder that some dossier requirements still have no uploaded document.</summary>
    public static string MissingDocumentsBody(
        string dossierCode, string bookingCode, DateOnly generalDueDate,
        string? note, string? actionUrl, IEnumerable<MissingItem> missingItems)
    {
        var items = missingItems.ToList();
        var itemRows = string.Concat(items.Select(item =>
        {
            var horse = string.IsNullOrWhiteSpace(item.HorseName) ? "" : $"Horse: {Encode(item.HorseName)} &middot; ";
            return $"""<tr><td class="px-line" style="padding:14px 0;border-bottom:1px solid #EFEFEF;"><div class="px-strong" style="font-size:15px;line-height:1.4;font-weight:700;color:#000000;">{Encode(item.RequirementName)}</div><div class="px-sub" style="margin-top:4px;font-size:13.5px;line-height:1.5;color:#6B726D;">{horse}Due {Accent(FmtDate(item.DueDate ?? generalDueDate))}</div></td></tr>""";
        }));
        var itemsBlock = items.Count == 0 ? "" :
            $"""<p style="margin:0 0 12px;{BodyStyle}font-weight:500;">Outstanding documents:</p><table role="presentation" width="100%" cellspacing="0" cellpadding="0" border="0" style="margin:0 0 24px;">{itemRows}</table>""";

        return Header("Missing documents reminder", "Some of your documents are still missing") +
            $"""
                  {Title("Missing documents", dossierCode)}
                  {Greeting(null)}
                  {Paragraph($"""Some documents for booking <strong class="px-strong" style="color:#000000;">{Encode(bookingCode)}</strong> have not been uploaded yet. Please submit them before the due date.""")}
                  {WarningPill("Action required")}
                  {Rows(
                      Row("Dossier code", Accent(dossierCode)),
                      Row("Booking code", Encode(bookingCode)),
                      Row("Due date", Accent(FmtDate(generalDueDate))))}
                  {itemsBlock}
                  {LabeledParagraph("Note", note)}
                  {Button(actionUrl, "Upload")}
            """ +
            Footer();
    }

    public enum DossierUpdateKind { Opened, RequirementAdded, RequirementUpdated, RequirementWaived, RequirementRemoved, DocumentApproved, RequestComment }

    public static string DossierUpdateTitle(DossierUpdateKind kind) => kind switch
    {
        DossierUpdateKind.Opened => "Compliance dossier opened",
        DossierUpdateKind.RequirementAdded => "New document required",
        DossierUpdateKind.RequirementUpdated => "Document requirement updated",
        DossierUpdateKind.RequirementWaived => "Document requirement waived",
        DossierUpdateKind.RequirementRemoved => "Document requirement removed",
        DossierUpdateKind.DocumentApproved => "Document approved",
        _ => "New comment on a document request"
    };

    /// <summary>Customer update about a compliance dossier: opened, a requirement changed, a document approved or a comment added.</summary>
    /// <param name="subject">The requirement or document name the update is about.</param>
    public static string DossierUpdateBody(
        string customerName, string dossierCode, DossierUpdateKind kind, string? subject, string? detail, string? actionUrl)
    {
        var (message, tone, pill, noteLabel) = kind switch
        {
            DossierUpdateKind.Opened => ("Your compliance dossier is ready. Open it to see the documents you need to upload.", Tone.Info, "Opened", "Note"),
            DossierUpdateKind.RequirementAdded => ($"A new document is required for your dossier: {Strong(subject)}. Please upload it.", Tone.Warning, "Action required", "Details"),
            DossierUpdateKind.RequirementUpdated => ($"The requirement {Strong(subject)} was updated. Please check its details and instructions.", Tone.Info, "Updated", "Details"),
            DossierUpdateKind.RequirementWaived => ($"{Strong(subject)} is no longer required. No action is needed from you.", Tone.Success, "Waived", "Reason"),
            DossierUpdateKind.RequirementRemoved => ($"{Strong(subject)} was removed from your dossier. No action is needed from you.", Tone.Info, "Removed", "Reason"),
            DossierUpdateKind.DocumentApproved => ($"{Strong(subject)} was approved.", Tone.Success, "Approved", "Note"),
            _ => ("A new comment was added to one of your document requests.", Tone.Info, "New comment", "Comment")
        };
        var title = DossierUpdateTitle(kind);
        return Notice(title, title, dossierCode, customerName, message, tone, pill,
            [Row("Dossier code", Accent(dossierCode))], detail, noteLabel, actionUrl, "Open dossier");
    }

    public enum AuthorityMailKind { Submitted, Approved, Rejected }

    public static string AuthorityTitle(AuthorityMailKind kind) => kind switch
    {
        AuthorityMailKind.Submitted => "Dossier submitted to authorities",
        AuthorityMailKind.Approved => "Dossier approved by authorities",
        _ => "Dossier rejected by an authority"
    };

    /// <summary>A dossier was submitted to an authority, or the authority approved or rejected it.</summary>
    public static string AuthorityBody(
        string recipientName, string dossierCode, string authorityName, AuthorityMailKind kind, string? reason, string? actionUrl)
    {
        var (message, tone, pill) = kind switch
        {
            AuthorityMailKind.Submitted => ($"Compliance dossier {Strong(dossierCode)} was submitted to {Strong(authorityName)}. You will be notified when the authority decides.", Tone.Info, "Submitted"),
            AuthorityMailKind.Approved => ($"{Strong(authorityName)} approved the compliance dossier. All required authority approvals are now in place.", Tone.Success, "Approved"),
            _ => ($"{Strong(authorityName)} rejected a required submission. The dossier needs action before departure.", Tone.Error, "Action required")
        };
        var title = AuthorityTitle(kind);
        return Notice(title, title, dossierCode, recipientName, message, tone, pill,
            [Row("Dossier code", Accent(dossierCode)), Row("Authority", Encode(authorityName))], reason, "Reason", actionUrl, "Open dossier");
    }

    public enum ClearanceKind { Customs, Bcp }

    public enum ClearanceOutcome { Submitted, Held, Verified, Rejected, Failed, ProofRejected }

    private static string ClearanceLabel(ClearanceKind kind) => kind == ClearanceKind.Customs ? "Customs clearance" : "BCP quarantine";

    public static string ClearanceTitle(ClearanceKind kind, ClearanceOutcome outcome) => outcome switch
    {
        ClearanceOutcome.Submitted => $"{ClearanceLabel(kind)} proof submitted",
        ClearanceOutcome.Held => $"{ClearanceLabel(kind)} is holding the transport",
        ClearanceOutcome.Verified => $"{ClearanceLabel(kind)} verified",
        ClearanceOutcome.Rejected => $"{ClearanceLabel(kind)} rejected",
        ClearanceOutcome.Failed => $"{ClearanceLabel(kind)} inspection failed",
        _ => $"{ClearanceLabel(kind)} proof rejected"
    };

    /// <summary>Customs clearance or BCP quarantine update for a dossier.</summary>
    /// <param name="place">The customs port or the border control post.</param>
    public static string ClearanceBody(
        string recipientName, string dossierCode, ClearanceKind kind, string place, ClearanceOutcome outcome, string? detail, string? actionUrl)
    {
        var label = ClearanceLabel(kind).ToLowerInvariant();
        var (message, tone, pill, noteLabel) = outcome switch
        {
            ClearanceOutcome.Submitted => ($"A {label} proof for this dossier ({Strong(place)}) is waiting for your verification.", Tone.Warning, "Action required", "Note"),
            ClearanceOutcome.Held => ($"{Strong(place)} is holding the transport for additional inspection. Review the proof and plan the next steps.", Tone.Error, "Held", "Note"),
            ClearanceOutcome.Verified => ($"{Strong(place)} finished the {label} check and the result was verified.", Tone.Success, "Verified", "Note"),
            ClearanceOutcome.Rejected => ($"{Strong(place)} rejected the {label}. Immediate action is required.", Tone.Error, "Rejected", "Reason"),
            ClearanceOutcome.Failed => ($"{Strong(place)} failed the inspection. Immediate action is required.", Tone.Error, "Failed", "Note"),
            _ => ($"Your {label} proof ({Strong(place)}) was rejected. Please submit a new proof.", Tone.Warning, "Action required", "Reason")
        };
        var title = ClearanceTitle(kind, outcome);
        return Notice(title, title, dossierCode, recipientName, message, tone, pill,
            [Row("Dossier code", Accent(dossierCode)), Row(kind == ClearanceKind.Customs ? "Customs port" : "Border control post", Encode(place))],
            detail, noteLabel, actionUrl, "Open dossier");
    }

    /// <summary>Generic staff alert for a task that needs action, for example a document waiting for review.</summary>
    public static string StaffActionBody(
        string staffName, string title, string summary, string? code, string? detail, string? actionUrl) =>
        Notice(title, summary, code, staffName, Encode(summary), Tone.Warning, "Action required", [], detail, "Details", actionUrl, "Open");
}
