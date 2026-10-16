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
}
