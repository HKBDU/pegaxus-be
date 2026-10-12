using System.Globalization;
using System.Net;

namespace Pegaxus.Service;

/// <summary>
/// Centralised Pegaxus branded email bodies.
/// All templates support light/dark mode and are responsive down to 480 px.
/// </summary>
public static class EmailBodies
{
    private const string LogoUrl = "https://res.cloudinary.com/lfhcfjes/image/upload/v1791813419/pegaxus-logo.png";

    private const string Css = """
        :root { color-scheme: light dark; supported-color-schemes: light dark; }
        a { text-decoration: none; }
        @media (max-width:480px) {
          .px-outer { padding: 28px 12px !important; }
          .px-card { padding: 32px !important; }
          .px-title { font-size: 26px !important; }
          .px-tag { font-size: 20px !important; letter-spacing: .04em !important; }
          .px-logo { width: 125px !important; height: 40px !important; }
        }
        @media (min-width:640px) {
          .px-tag { font-size: 26px !important; }
        }
        @media (prefers-color-scheme: dark) {
          .px-page { background-color:#121414!important; }
          .px-card { background-color:#1A1C1C!important;border-color:#1F2222!important;color:#F5F5F5!important;box-shadow:none!important; }
          .px-strong { color:#FFFFFF!important; }
          .px-bullet { color:#F39200!important; }
          .px-sub { color:#A3AAA5!important; }
          .px-tag { color:#FFFFFF!important; }
          .px-line { border-color:#2A2D2D!important; }
          .px-otp { background-color:#2A2214!important;color:#FFFFFF!important; }
          .px-dark-only { display:table-row!important; }
          .px-pill-success { background-color:#16301F!important;color:#7DD99A!important; }
          .px-pill-warning { background-color:#3A2A0F!important;color:#FFB74D!important; }
          .px-pill-error { background-color:#3A1A18!important;color:#FF8A80!important; }
          .px-pill-info { background-color:#FFFFFF!important;color:#181818!important; }
        }
        [data-ogsc] .px-page { background-color:#121414!important; }
        [data-ogsc] .px-card { background-color:#1A1C1C!important;border-color:#1F2222!important;color:#F5F5F5!important;box-shadow:none!important; }
        [data-ogsc] .px-strong { color:#FFFFFF!important; }
        [data-ogsc] .px-bullet { color:#F39200!important; }
        [data-ogsc] .px-sub { color:#A3AAA5!important; }
        [data-ogsc] .px-tag { color:#FFFFFF!important; }
        [data-ogsc] .px-line { border-color:#2A2D2D!important; }
        [data-ogsc] .px-otp { background-color:#2A2214!important;color:#FFFFFF!important; }
        [data-ogsc] .px-dark-only { display:table-row!important; }
        [data-ogsc] .px-pill-success { background-color:#16301F!important;color:#7DD99A!important; }
        [data-ogsc] .px-pill-warning { background-color:#3A2A0F!important;color:#FFB74D!important; }
        [data-ogsc] .px-pill-error { background-color:#3A1A18!important;color:#FF8A80!important; }
        [data-ogsc] .px-pill-info { background-color:#FFFFFF!important;color:#181818!important; }
        """;

    // ── shared fragments ─────────────────────────────────────────────────────

    private static string Header(string title, string preheader) => $"""
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <meta name="color-scheme" content="light dark">
          <meta name="supported-color-schemes" content="light dark">
          <title>{WebUtility.HtmlEncode(title)}</title>
          <link href="https://fonts.googleapis.com/css2?family=Bebas+Neue&amp;family=Space+Grotesk:wght@400;500;700&amp;display=swap" rel="stylesheet">
          <style>{Css}</style>
        </head>
        <body class="px-page" style="margin:0;padding:0;background:#F4F4F4;font-family:'Space Grotesk',-apple-system,BlinkMacSystemFont,'Segoe UI',Arial,Helvetica,sans-serif;">
          <div style="display:none;max-height:0;overflow:hidden;mso-hide:all;">{WebUtility.HtmlEncode(preheader)}</div>
          <table class="px-page" role="presentation" width="100%" cellspacing="0" cellpadding="0" border="0" style="background:#F4F4F4;">
            <tr>
              <td class="px-outer" align="center" style="padding:48px 16px;">
                <table role="presentation" width="100%" cellspacing="0" cellpadding="0" border="0" style="max-width:620px;">
                  <tr><td align="center" style="padding-bottom:12px;background:transparent;">
                    <img class="px-logo" src="{LogoUrl}" width="138" height="44" alt="Pegaxus" style="display:block;border:0;width:138px;height:44px;background:transparent;">
                  </td></tr>
                  <tr class="px-dark-only" style="display:none;mso-hide:all;"><td align="center" style="padding-bottom:12px;"><div style="width:48px;height:1px;line-height:1px;font-size:1px;background:#F39200;">&nbsp;</div></td></tr>
                  <tr><td class="px-tag" align="center" style="padding-bottom:28px;font-family:'Bebas Neue',Impact,'Arial Narrow',Arial,sans-serif;font-size:24px;line-height:1.1;letter-spacing:.06em;text-transform:uppercase;color:#181818;">Relentless Pursuit <span style="color:#F39200;">Beyond Boundaries</span></td></tr>
                  <tr>
                    <td class="px-card" style="background:#FFFFFF;border:1px solid #EFEFEF;border-radius:28px;padding:48px;color:#181818;font-family:'Space Grotesk',-apple-system,BlinkMacSystemFont,'Segoe UI',Arial,Helvetica,sans-serif;box-shadow:0 10px 35px -8px rgba(0,0,0,.07),0 2px 8px rgba(0,0,0,.03);">
        """;

    private static string Footer() => $"""
                    </td>
                  </tr>
                  <tr><td align="center" style="padding-top:28px;font-size:11.5px;line-height:1.5;letter-spacing:.08em;font-weight:500;text-transform:uppercase;color:#939B95;">&copy; {DateTime.UtcNow.Year} PEGAXUS LOGISTICS GROUP. ALL RIGHTS RESERVED.</td></tr>
                </table>
              </td>
            </tr>
          </table>
        </body>
        </html>
        """;

    private static string SafeUrl(string? actionUrl)
    {
        if (Uri.TryCreate(actionUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
            return WebUtility.HtmlEncode(uri.AbsoluteUri);
        return "#";
    }

    private static string FmtDate(DateOnly? value) =>
        value.HasValue ? value.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) : "—";

    private static string FmtDateTime(DateTime? value) =>
        value.HasValue ? value.Value.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture) : "—";

    private static string PrimaryButton(string url, string label) =>
        $"""<table role="presentation" cellspacing="0" cellpadding="0" border="0" style="margin:0 0 32px;"><tr><td bgcolor="#F39200" style="border-radius:999px;"><a href="{url}" target="_blank" style="display:inline-block;padding:14px 28px;border-radius:999px;font-family:'Space Grotesk',-apple-system,BlinkMacSystemFont,'Segoe UI',Arial,Helvetica,sans-serif;font-size:14px;line-height:1.2;font-weight:700;letter-spacing:.08em;text-transform:uppercase;color:#181818;text-decoration:none;">{label}</a></td></tr></table>""";

    // OTP

    public static string OtpBody(bool isVerification, string fullName, string otp, int expiresInMinutes) =>
        Header(
            isVerification ? "Verify your Pegaxus account" : "Reset your Pegaxus password",
            isVerification ? "Your Pegaxus verification code" : "Your Pegaxus password reset code") +
        $"""
              <h1 class="px-title" style="margin:0 0 24px;font-size:29px;line-height:1.25;font-weight:700;letter-spacing:-.01em;">{(isVerification ? "Verify your email" : "Reset your password")}</h1>
              <p style="margin:0 0 20px;font-size:15.5px;line-height:1.65;">Dear <span style="color:#F39200;font-style:italic;font-weight:500;">{WebUtility.HtmlEncode(fullName)}</span>,</p>
              <p style="margin:0 0 24px;font-size:15.5px;line-height:1.65;">{(isVerification ? "Use this code to verify your Pegaxus account." : "Use this code to reset your Pegaxus password.")}</p>
              <div class="px-otp" style="margin:0 0 20px;padding:20px 12px 20px 15px;text-align:center;background:#FFF6E5;border:1px solid #F39200;border-radius:16px;font-size:36px;line-height:1.2;font-weight:700;letter-spacing:10px;color:#181818;white-space:nowrap;">{WebUtility.HtmlEncode(otp)}</div>
              <p style="margin:0 0 24px;font-size:15.5px;line-height:1.65;">This code expires in <strong class="px-strong" style="color:#000000;">{expiresInMinutes} minutes</strong>.</p>
              <p style="margin:0 0 24px;font-size:15.5px;line-height:1.65;"><strong class="px-strong" style="color:#000000;">Security notice:</strong> Never share this code with anyone. Pegaxus will never ask you for it.</p>
              <p class="px-sub" style="margin:0px 0 0;font-size:13.5px;line-height:1.6;color:#6B726D;">{(isVerification ? "If you did not create a Pegaxus account, you can safely ignore this email." : "If you did not request this, you can safely ignore this email.")}</p>
        """ +
        Footer();

    // Booking approved

    public static string BookingApprovedBody(
        string customerName, string bookingCode, string transportCode,
        DateOnly? departureDate, string? actionUrl) =>
        Header("Booking approved", "Your Pegaxus booking has been approved") +
        $"""
              <h1 class="px-title" style="margin:0 0 24px;font-size:29px;line-height:1.25;font-weight:700;letter-spacing:-.01em;">Booking approved&mdash;<span style="color:#F39200;font-style:italic;white-space:nowrap;">{WebUtility.HtmlEncode(bookingCode)}</span></h1>
              <p style="margin:0 0 20px;font-size:15.5px;line-height:1.65;">Dear <span style="color:#F39200;font-style:italic;font-weight:500;">{WebUtility.HtmlEncode(customerName)}</span>,</p>
              <p style="margin:0 0 24px;font-size:15.5px;line-height:1.65;">Good news! Your booking has been approved and a transport trip has been created.</p>
              <div style="margin:0 0 24px;"><span class="px-pill-success" style="display:inline-block;padding:7px 14px;border-radius:999px;background:#E6F4EA;color:#17692F;font-size:12px;line-height:1;font-weight:700;letter-spacing:.08em;text-transform:uppercase;">Approved</span></div>
              <table role="presentation" cellspacing="0" cellpadding="0" border="0" style="margin:0 0 14px;">
              <tr><td class="px-bullet" width="18" valign="top" style="padding:0 0 10px 4px;font-size:15.5px;line-height:1.6;color:#000000;">&bull;</td><td valign="top" style="padding:0 0 10px;font-size:15.5px;line-height:1.6;"><strong class="px-strong" style="color:#000000;">Booking code:</strong> <span style="color:#F39200;font-style:italic;white-space:nowrap;">{WebUtility.HtmlEncode(bookingCode)}</span></td></tr>
              <tr><td class="px-bullet" width="18" valign="top" style="padding:0 0 10px 4px;font-size:15.5px;line-height:1.6;color:#000000;">&bull;</td><td valign="top" style="padding:0 0 10px;font-size:15.5px;line-height:1.6;"><strong class="px-strong" style="color:#000000;">Transport code:</strong> {WebUtility.HtmlEncode(transportCode)}</td></tr>
              <tr><td class="px-bullet" width="18" valign="top" style="padding:0 0 10px 4px;font-size:15.5px;line-height:1.6;color:#000000;">&bull;</td><td valign="top" style="padding:0 0 10px;font-size:15.5px;line-height:1.6;"><strong class="px-strong" style="color:#000000;">Planned departure:</strong> <span style="color:#F39200;font-style:italic;white-space:nowrap;">{FmtDate(departureDate)}</span></td></tr>
              </table>
              {PrimaryButton(SafeUrl(actionUrl), "View details")}
        """ +
        Footer();

    // Booking rejected
    public static string BookingRejectedBody(
        string customerName, string bookingCode, string reason, string? actionUrl) =>
        Header("Booking rejected", "Your Pegaxus booking could not be approved") +
        $"""
              <h1 class="px-title" style="margin:0 0 24px;font-size:29px;line-height:1.25;font-weight:700;letter-spacing:-.01em;">Booking rejected&mdash;<span style="color:#F39200;font-style:italic;white-space:nowrap;">{WebUtility.HtmlEncode(bookingCode)}</span></h1>
              <p style="margin:0 0 20px;font-size:15.5px;line-height:1.65;">Dear <span style="color:#F39200;font-style:italic;font-weight:500;">{WebUtility.HtmlEncode(customerName)}</span>,</p>
              <p style="margin:0 0 24px;font-size:15.5px;line-height:1.65;">Unfortunately, we were unable to approve your booking request.</p>
              <div style="margin:0 0 24px;"><span class="px-pill-error" style="display:inline-block;padding:7px 14px;border-radius:999px;background:#FDE8E6;color:#B3261E;font-size:12px;line-height:1;font-weight:700;letter-spacing:.08em;text-transform:uppercase;">Rejected</span></div>
              <table role="presentation" cellspacing="0" cellpadding="0" border="0" style="margin:0 0 14px;">
              <tr><td class="px-bullet" width="18" valign="top" style="padding:0 0 10px 4px;font-size:15.5px;line-height:1.6;color:#000000;">&bull;</td><td valign="top" style="padding:0 0 10px;font-size:15.5px;line-height:1.6;"><strong class="px-strong" style="color:#000000;">Booking code:</strong> <span style="color:#F39200;font-style:italic;white-space:nowrap;">{WebUtility.HtmlEncode(bookingCode)}</span></td></tr>
              </table>
              <p style="margin:0 0 24px;font-size:15.5px;line-height:1.65;"><strong class="px-strong" style="color:#000000;">Reason:</strong> {WebUtility.HtmlEncode(reason)}</p>
              {PrimaryButton(SafeUrl(actionUrl), "Contact support")}
              <p class="px-sub" style="margin:0px 0 0;font-size:13.5px;line-height:1.6;color:#6B726D;">If you believe this is a mistake, please contact our support team.</p>
        """ +
        Footer();

    // Document re-upload required

    public static string DocumentReuploadBody(
        string requestCode, string requirementName, string? horseName,
        string errorCategory, string? note, DateOnly revisionDeadline, string? actionUrl)
    {
        var errorText = errorCategory switch
        {
            "INVALID_IMAGE" => "Invalid or unreadable image",
            "EXPIRED"       => "Document expired",
            _               => WebUtility.HtmlEncode(errorCategory)
        };
        var horseRow = string.IsNullOrWhiteSpace(horseName) ? "" :
            $"""<tr><td class="px-bullet" width="18" valign="top" style="padding:0 0 10px 4px;font-size:15.5px;line-height:1.6;color:#000000;">&bull;</td><td valign="top" style="padding:0 0 10px;font-size:15.5px;line-height:1.6;"><strong class="px-strong" style="color:#000000;">Horse:</strong> {WebUtility.HtmlEncode(horseName)}</td></tr>""";
        var noteBlock = string.IsNullOrWhiteSpace(note) ? "" :
            $"""<p style="margin:0 0 24px;font-size:15.5px;line-height:1.65;"><strong class="px-strong" style="color:#000000;">Note:</strong> {WebUtility.HtmlEncode(note)}</p>""";

        return Header("Document re-upload required", "Please re-upload a document for your request") +
            $"""
                  <h1 class="px-title" style="margin:0 0 24px;font-size:29px;line-height:1.25;font-weight:700;letter-spacing:-.01em;">Re-upload required&mdash;<span style="color:#F39200;font-style:italic;white-space:nowrap;">{WebUtility.HtmlEncode(requestCode)}</span></h1>
                  <p style="margin:0 0 20px;font-size:15.5px;line-height:1.65;">Dear customer,</p>
                  <p style="margin:0 0 24px;font-size:15.5px;line-height:1.65;">One of your documents could not be accepted. Please upload a new version before the deadline.</p>
                  <div style="margin:0 0 24px;"><span class="px-pill-warning" style="display:inline-block;padding:7px 14px;border-radius:999px;background:#FFF1DB;color:#A25A00;font-size:12px;line-height:1;font-weight:700;letter-spacing:.08em;text-transform:uppercase;">Action required</span></div>
                  <table role="presentation" cellspacing="0" cellpadding="0" border="0" style="margin:0 0 14px;">
                  <tr><td class="px-bullet" width="18" valign="top" style="padding:0 0 10px 4px;font-size:15.5px;line-height:1.6;color:#000000;">&bull;</td><td valign="top" style="padding:0 0 10px;font-size:15.5px;line-height:1.6;"><strong class="px-strong" style="color:#000000;">Document:</strong> {WebUtility.HtmlEncode(requirementName)}</td></tr>
                  {horseRow}
                  <tr><td class="px-bullet" width="18" valign="top" style="padding:0 0 10px 4px;font-size:15.5px;line-height:1.6;color:#000000;">&bull;</td><td valign="top" style="padding:0 0 10px;font-size:15.5px;line-height:1.6;"><strong class="px-strong" style="color:#000000;">Issue:</strong> {errorText}</td></tr>
                  <tr><td class="px-bullet" width="18" valign="top" style="padding:0 0 10px 4px;font-size:15.5px;line-height:1.6;color:#000000;">&bull;</td><td valign="top" style="padding:0 0 10px;font-size:15.5px;line-height:1.6;"><strong class="px-strong" style="color:#000000;">Request code:</strong> <span style="color:#F39200;font-style:italic;white-space:nowrap;">{WebUtility.HtmlEncode(requestCode)}</span></td></tr>
                  </table>
                  {noteBlock}
                  <p style="margin:0 0 24px;font-size:15.5px;line-height:1.65;"><strong class="px-strong" style="color:#000000;">Deadline:</strong> Please re-upload by <strong class="px-strong" style="color:#000000;">{FmtDate(revisionDeadline)}</strong>.</p>
                  {PrimaryButton(SafeUrl(actionUrl), "Upload")}
            """ +
            Footer();
    }

    // Missing documents reminder

    public sealed record MissingItem(string RequirementName, string? HorseName, DateOnly? DueDate);

    public static string MissingDocumentsBody(
        string dossierCode, string bookingCode, DateOnly generalDueDate,
        string? note, string? actionUrl, IEnumerable<MissingItem> missingItems)
    {
        var itemsList = missingItems.ToList();
        var rows = string.Concat(itemsList.Select(i =>
        {
            var horse = string.IsNullOrWhiteSpace(i.HorseName) ? "" : $"Horse: {WebUtility.HtmlEncode(i.HorseName)} &middot; ";
            var due   = FmtDate(i.DueDate ?? generalDueDate);
            return $"""<tr><td class="px-line" style="padding:14px 0;border-bottom:1px solid #EFEFEF;"><div class="px-strong" style="font-size:15px;line-height:1.4;font-weight:700;color:#000000;">{WebUtility.HtmlEncode(i.RequirementName)}</div><div class="px-sub" style="margin-top:4px;font-size:13.5px;line-height:1.5;color:#6B726D;">{horse}Due <span style="color:#F39200;font-style:italic;white-space:nowrap;">{due}</span></div></td></tr>""";
        }));
        var itemsBlock = itemsList.Count > 0
            ? $"""<p style="margin:0 0 12px;font-size:15.5px;line-height:1.65;font-weight:500;">Outstanding documents:</p><table role="presentation" width="100%" cellspacing="0" cellpadding="0" border="0" style="margin:0 0 24px;">{rows}</table>"""
            : "";
        var noteBlock = string.IsNullOrWhiteSpace(note) ? "" :
            $"""<p style="margin:0 0 24px;font-size:15.5px;line-height:1.65;"><strong class="px-strong" style="color:#000000;">Note:</strong> {WebUtility.HtmlEncode(note)}</p>""";

        return Header("Missing documents reminder", "Some of your documents are still missing") +
            $"""
                  <h1 class="px-title" style="margin:0 0 24px;font-size:29px;line-height:1.25;font-weight:700;letter-spacing:-.01em;">Missing documents&mdash;<span style="color:#F39200;font-style:italic;white-space:nowrap;">{WebUtility.HtmlEncode(dossierCode)}</span></h1>
                  <p style="margin:0 0 20px;font-size:15.5px;line-height:1.65;">Dear customer,</p>
                  <p style="margin:0 0 24px;font-size:15.5px;line-height:1.65;">Some documents for booking <strong class="px-strong" style="color:#000000;">{WebUtility.HtmlEncode(bookingCode)}</strong> have not been uploaded yet. Please submit them before the due date.</p>
                  <div style="margin:0 0 24px;"><span class="px-pill-warning" style="display:inline-block;padding:7px 14px;border-radius:999px;background:#FFF1DB;color:#A25A00;font-size:12px;line-height:1;font-weight:700;letter-spacing:.08em;text-transform:uppercase;">Action required</span></div>
                  <table role="presentation" cellspacing="0" cellpadding="0" border="0" style="margin:0 0 14px;">
                  <tr><td class="px-bullet" width="18" valign="top" style="padding:0 0 10px 4px;font-size:15.5px;line-height:1.6;color:#000000;">&bull;</td><td valign="top" style="padding:0 0 10px;font-size:15.5px;line-height:1.6;"><strong class="px-strong" style="color:#000000;">Dossier code:</strong> <span style="color:#F39200;font-style:italic;white-space:nowrap;">{WebUtility.HtmlEncode(dossierCode)}</span></td></tr>
                  <tr><td class="px-bullet" width="18" valign="top" style="padding:0 0 10px 4px;font-size:15.5px;line-height:1.6;color:#000000;">&bull;</td><td valign="top" style="padding:0 0 10px;font-size:15.5px;line-height:1.6;"><strong class="px-strong" style="color:#000000;">Booking code:</strong> {WebUtility.HtmlEncode(bookingCode)}</td></tr>
                  <tr><td class="px-bullet" width="18" valign="top" style="padding:0 0 10px 4px;font-size:15.5px;line-height:1.6;color:#000000;">&bull;</td><td valign="top" style="padding:0 0 10px;font-size:15.5px;line-height:1.6;"><strong class="px-strong" style="color:#000000;">Due date:</strong> <span style="color:#F39200;font-style:italic;white-space:nowrap;">{FmtDate(generalDueDate)}</span></td></tr>
                  </table>
                  {itemsBlock}
                  {noteBlock}
                  {PrimaryButton(SafeUrl(actionUrl), "Upload")}
            """ +
            Footer();
    }

    // Route assigned / replaced

    public sealed record RouteSegment(
        int SequenceNo,
        DateTime? PlannedDepartureAt,
        DateTime? PlannedArrivalAt,
        string? DriverCode,
        string? EscortCode,
        string? ProviderName);

    /// <summary>Notifies an employee of a new or updated route assignment.</summary>
    public static string RouteAssignmentBody(
        string employeeName, string routePlanCode,
        bool isReplaced, string? replaceReason,
        string? actionUrl, IEnumerable<RouteSegment> segments)
    {
        var segmentsHtml = string.Concat(segments.Select(s =>
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(s.DriverCode))   parts.Add($"""Driver: <strong class="px-strong" style="color:#000000;">{WebUtility.HtmlEncode(s.DriverCode)}</strong>""");
            if (!string.IsNullOrWhiteSpace(s.EscortCode))   parts.Add($"""Escort: <strong class="px-strong" style="color:#000000;">{WebUtility.HtmlEncode(s.EscortCode)}</strong>""");
            if (!string.IsNullOrWhiteSpace(s.ProviderName)) parts.Add($"""Provider: <strong class="px-strong" style="color:#000000;">{WebUtility.HtmlEncode(s.ProviderName)}</strong>""");
            var assignees = parts.Count == 0 ? "&mdash;" : string.Join("<br>", parts);
            return $"""<table class="px-line" role="presentation" width="100%" cellspacing="0" cellpadding="0" border="0" style="margin:0 0 12px;border:1px solid #EFEFEF;border-radius:14px;"><tr><td style="padding:16px 18px;"><div style="font-size:12px;line-height:1.4;font-weight:700;letter-spacing:.08em;text-transform:uppercase;color:#939B95;">Segment {s.SequenceNo}</div><div style="margin-top:8px;font-size:15px;line-height:1.5;">Departs <strong class="px-strong" style="color:#000000;">{FmtDateTime(s.PlannedDepartureAt)}</strong></div><div style="margin-top:2px;font-size:15px;line-height:1.5;">Arrives <strong class="px-strong" style="color:#000000;">{FmtDateTime(s.PlannedArrivalAt)}</strong></div><div class="px-line" style="margin-top:10px;padding-top:10px;border-top:1px solid #EFEFEF;font-size:14px;line-height:1.6;">{assignees}</div></td></tr></table>""";
        }));
        var reasonBlock = isReplaced && !string.IsNullOrWhiteSpace(replaceReason)
            ? $"""<p style="margin:0 0 24px;font-size:15.5px;line-height:1.65;"><strong class="px-strong" style="color:#000000;">Reason for change:</strong> {WebUtility.HtmlEncode(replaceReason)}</p>"""
            : "";
        var titleText = isReplaced ? "Route assignment updated" : "New route assignment";

        return Header(titleText, "Your route assignment details") +
            $"""
                  <h1 class="px-title" style="margin:0 0 24px;font-size:29px;line-height:1.25;font-weight:700;letter-spacing:-.01em;">{titleText}&mdash;<span style="color:#F39200;font-style:italic;white-space:nowrap;">{WebUtility.HtmlEncode(routePlanCode)}</span></h1>
                  <p style="margin:0 0 20px;font-size:15.5px;line-height:1.65;">Dear <span style="color:#F39200;font-style:italic;font-weight:500;">{WebUtility.HtmlEncode(employeeName)}</span>,</p>
                  <p style="margin:0 0 24px;font-size:15.5px;line-height:1.65;">{(isReplaced ? "Your assignment on the following route plan has been updated." : "You have been assigned to the following route plan.")}</p>
                  <div style="margin:0 0 24px;"><span class="px-pill-info" style="display:inline-block;padding:7px 14px;border-radius:999px;background:#181818;color:#FFFFFF;font-size:12px;line-height:1;font-weight:700;letter-spacing:.08em;text-transform:uppercase;">{(isReplaced ? "Replaced" : "Assigned")}</span></div>
                  <table role="presentation" cellspacing="0" cellpadding="0" border="0" style="margin:0 0 14px;">
                  <tr><td class="px-bullet" width="18" valign="top" style="padding:0 0 10px 4px;font-size:15.5px;line-height:1.6;color:#000000;">&bull;</td><td valign="top" style="padding:0 0 10px;font-size:15.5px;line-height:1.6;"><strong class="px-strong" style="color:#000000;">Route plan:</strong> <span style="color:#F39200;font-style:italic;white-space:nowrap;">{WebUtility.HtmlEncode(routePlanCode)}</span></td></tr>
                  </table>
                  {reasonBlock}
                  <p style="margin:0 0 12px;font-size:15.5px;line-height:1.65;font-weight:500;">Route segments:</p>
                  {segmentsHtml}
                  <div style="height:12px;line-height:12px;font-size:12px;">&nbsp;</div>
                  {PrimaryButton(SafeUrl(actionUrl), "Open portal")}
            """ +
            Footer();
    }
}
