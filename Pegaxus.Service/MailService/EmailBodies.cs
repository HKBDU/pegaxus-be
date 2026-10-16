using System.Globalization;
using System.Net;

namespace Pegaxus.Service.MailService;

/// <summary>
/// Pegaxus branded HTML email bodies. Shared layout and helpers live here; each template group sits in its own
/// partial file (<c>EmailBodies.Account.cs</c>, <c>EmailBodies.Booking.cs</c>, ...).
/// Every template supports light and dark mode and is responsive down to 480 px.
/// All caller supplied text is HTML-encoded by the template, so callers pass plain text.
/// </summary>
public static partial class EmailBodies
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

    private const string BodyStyle = "font-size:15.5px;line-height:1.65;";

    // ── layout ───────────────────────────────────────────────────────────────

    private static string Header(string title, string preheader) => $"""
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <meta name="color-scheme" content="light dark">
          <meta name="supported-color-schemes" content="light dark">
          <title>{Encode(title)}</title>
          <link href="https://fonts.googleapis.com/css2?family=Bebas+Neue&amp;family=Space+Grotesk:wght@400;500;700&amp;display=swap" rel="stylesheet">
          <style>{Css}</style>
        </head>
        <body class="px-page" style="margin:0;padding:0;background:#F4F4F4;font-family:'Space Grotesk',-apple-system,BlinkMacSystemFont,'Segoe UI',Arial,Helvetica,sans-serif;">
          <div style="display:none;max-height:0;overflow:hidden;mso-hide:all;">{Encode(preheader)}</div>
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

    // ── building blocks ──────────────────────────────────────────────────────

    private static string Encode(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    /// <summary>Accent coloured italic text used for codes and dates, for example a booking code.</summary>
    private static string Accent(string? value) =>
        $"""<span style="color:#F39200;font-style:italic;white-space:nowrap;">{Encode(value)}</span>""";

    private static string Title(string text, string? code = null) =>
        $"""<h1 class="px-title" style="margin:0 0 24px;font-size:29px;line-height:1.25;font-weight:700;letter-spacing:-.01em;">{text}{(code is null ? "" : $"&mdash;{Accent(code)}")}</h1>""";

    private static string Greeting(string? name) => string.IsNullOrWhiteSpace(name)
        ? $"""<p style="margin:0 0 20px;{BodyStyle}">Dear customer,</p>"""
        : $"""<p style="margin:0 0 20px;{BodyStyle}">Dear <span style="color:#F39200;font-style:italic;font-weight:500;">{Encode(name)}</span>,</p>""";

    private static string Paragraph(string html) => $"""<p style="margin:0 0 24px;{BodyStyle}">{html}</p>""";

    private static string LabeledParagraph(string label, string? text) => string.IsNullOrWhiteSpace(text)
        ? ""
        : Paragraph($"""<strong class="px-strong" style="color:#000000;">{Encode(label)}:</strong> {Encode(text)}""");

    private static string Muted(string text) =>
        $"""<p class="px-sub" style="margin:0px 0 0;font-size:13.5px;line-height:1.6;color:#6B726D;">{Encode(text)}</p>""";

    private static string Pill(string cssClass, string background, string color, string text) =>
        $"""<div style="margin:0 0 24px;"><span class="{cssClass}" style="display:inline-block;padding:7px 14px;border-radius:999px;background:{background};color:{color};font-size:12px;line-height:1;font-weight:700;letter-spacing:.08em;text-transform:uppercase;">{Encode(text)}</span></div>""";

    private static string SuccessPill(string text) => Pill("px-pill-success", "#E6F4EA", "#17692F", text);
    private static string WarningPill(string text) => Pill("px-pill-warning", "#FFF1DB", "#A25A00", text);
    private static string ErrorPill(string text) => Pill("px-pill-error", "#FDE8E6", "#B3261E", text);
    private static string InfoPill(string text) => Pill("px-pill-info", "#181818", "#FFFFFF", text);

    /// <summary>One bullet row. <paramref name="valueHtml"/> is already encoded HTML.</summary>
    private static string Row(string label, string valueHtml) =>
        $"""<tr><td class="px-bullet" width="18" valign="top" style="padding:0 0 10px 4px;font-size:15.5px;line-height:1.6;color:#000000;">&bull;</td><td valign="top" style="padding:0 0 10px;font-size:15.5px;line-height:1.6;"><strong class="px-strong" style="color:#000000;">{Encode(label)}:</strong> {valueHtml}</td></tr>""";

    private static string Rows(params string?[] rows) =>
        $"""<table role="presentation" cellspacing="0" cellpadding="0" border="0" style="margin:0 0 14px;">{string.Concat(rows)}</table>""";

    /// <summary>
    /// Call to action button. Only absolute https URLs are rendered; without one the button is left out
    /// instead of linking to a dead "#".
    /// </summary>
    private static string Button(string? actionUrl, string label)
    {
        if (!Uri.TryCreate(actionUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return "";
        return $"""<table role="presentation" cellspacing="0" cellpadding="0" border="0" style="margin:0 0 32px;"><tr><td bgcolor="#F39200" style="border-radius:999px;"><a href="{Encode(uri.AbsoluteUri)}" target="_blank" style="display:inline-block;padding:14px 28px;border-radius:999px;font-family:'Space Grotesk',-apple-system,BlinkMacSystemFont,'Segoe UI',Arial,Helvetica,sans-serif;font-size:14px;line-height:1.2;font-weight:700;letter-spacing:.08em;text-transform:uppercase;color:#181818;text-decoration:none;">{Encode(label)}</a></td></tr></table>""";
    }

    private static string FmtDate(DateOnly? value) =>
        value.HasValue ? value.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) : "—";

    /// <summary>Timestamps are stored in UTC, so the label keeps recipients in other time zones from guessing.</summary>
    private static string FmtDateTime(DateTime? value) =>
        value.HasValue ? value.Value.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture) + " UTC" : "—";

    /// <summary>VND has no minor unit, every other currency keeps two decimals.</summary>
    private static string FmtMoney(decimal amount, string currency) =>
        $"{amount.ToString(string.Equals(currency, "VND", StringComparison.OrdinalIgnoreCase) ? "#,##0" : "#,##0.00", CultureInfo.InvariantCulture)} {currency}";
}
