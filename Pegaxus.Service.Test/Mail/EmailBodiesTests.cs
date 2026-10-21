using System.Text.RegularExpressions;

using Mail = Pegaxus.Service.MailService.EmailBodies;

namespace Pegaxus.Service.Test;

public class EmailBodiesTests
{
    private const string Evil = "<script>alert(1)</script>";
    private const string EncodedEvil = "&lt;script&gt;alert(1)&lt;/script&gt;";
    private const string Link = "https://app.pegaxus.example/bookings/1";

    private static IEnumerable<(string Name, string Html)> AllTemplates() =>
    [
        ("otp-verify", Mail.OtpBody(true, "Ada", "123456", 5)),
        ("otp-reset", Mail.OtpBody(false, "Ada", "123456", 5)),
        ("booking-approved", Mail.BookingApprovedBody("Ada", "REQ-1", "TR-1", new DateOnly(2026, 12, 1), Link)),
        ("booking-rejected", Mail.BookingRejectedBody("Ada", "REQ-1", "No capacity", Link)),
        ("delivered", Mail.TransportDeliveredBody("Ada", "TR-1", new DateTime(2026, 10, 16, 12, 0, 0), Link)),
        ("reupload", Mail.DocumentReuploadBody("DRQ-1", "Passport", "Storm", "EXPIRED", "Please retake", new DateOnly(2026, 12, 1), Link)),
        ("missing", Mail.MissingDocumentsBody("CDOS-1", "REQ-1", new DateOnly(2026, 12, 1), "Soon", Link,
            [new Mail.MissingItem("Passport", "Storm", null)])),
        ("route", Mail.RouteAssignmentBody("Ada", "RP-1", true, "Driver sick", Link,
            [new Mail.RouteSegment(1, new DateTime(2026, 11, 28, 6, 0, 0), new DateTime(2026, 11, 28, 10, 0, 0), "EMP-4", "EMP-5", null)])),
        ("incident", Mail.IncidentReportedBody("Ada", "INC-1", "HIGH", "Engine failure", "The engine stopped", Link)),
        ("invoice-issued", Mail.InvoiceIssuedBody("Ada", "INV-1", "DEPOSIT", "REQ-1", Mail.InvoiceMailKind.Invoice, 120m, "EUR", new DateOnly(2026, 11, 15), Link)),
        ("invoice-cancelled", Mail.InvoiceCancelledBody("Ada", "INV-1", "DEPOSIT", "Wrong amount", Link)),
        ("payment-confirmed", Mail.PaymentConfirmedBody("Ada", "INV-1", "RCP-1", 120m, "EUR", false, Link)),
        ("reconciliation", Mail.PaymentReconciliationBody("Max", "A payment was received", "RCP-1", "PENDING", "TX-1", 120m, "VND", "INV-1", "PAID", Link)),
    ];

    [Fact]
    public void EveryTemplate_IsAResponsiveBrandedDocument_WithLightAndDarkSupport()
    {
        foreach (var (name, html) in AllTemplates())
        {
            Assert.True(html.StartsWith("<!doctype html>", StringComparison.OrdinalIgnoreCase), name);
            Assert.Contains("name=\"viewport\"", html);
            Assert.Contains("prefers-color-scheme: dark", html);
            Assert.Contains("alt=\"Pegaxus\"", html);
            Assert.Contains($"{DateTime.UtcNow.Year} PEGAXUS LOGISTICS GROUP", html);
            Assert.Equal(Regex.Matches(html, "<table", RegexOptions.IgnoreCase).Count, Regex.Matches(html, "</table>", RegexOptions.IgnoreCase).Count);
            // No interpolation placeholder may leak into the markup (the stylesheet legitimately contains braces).
            var markup = Regex.Replace(html, "<style>.*?</style>", "", RegexOptions.Singleline);
            Assert.DoesNotMatch(@"\{[A-Za-z]", markup);
        }
    }

    [Fact]
    public void EveryTemplate_EncodesUserSuppliedText()
    {
        var html = new[]
        {
            Mail.OtpBody(true, Evil, "123456", 5),
            Mail.BookingApprovedBody(Evil, Evil, Evil, null, null),
            Mail.BookingRejectedBody(Evil, Evil, Evil, null),
            Mail.TransportDeliveredBody(Evil, Evil, new DateTime(2026, 10, 16, 12, 0, 0), null),
            Mail.DocumentReuploadBody(Evil, Evil, Evil, Evil, Evil, new DateOnly(2026, 12, 1), null),
            Mail.MissingDocumentsBody(Evil, Evil, new DateOnly(2026, 12, 1), Evil, null, [new Mail.MissingItem(Evil, Evil, null)]),
            Mail.RouteAssignmentBody(Evil, Evil, true, Evil, null, [new Mail.RouteSegment(1, null, null, Evil, Evil, Evil)]),
            Mail.InvoiceIssuedBody(Evil, Evil, Evil, Evil, Mail.InvoiceMailKind.Invoice, 1m, "EUR", null, null),
            Mail.InvoiceCancelledBody(Evil, Evil, Evil, Evil, null),
            Mail.PaymentConfirmedBody(Evil, Evil, Evil, 1m, "EUR", false, null),
            Mail.PaymentReconciliationBody(Evil, Evil, Evil, Evil, Evil, 1m, "EUR", Evil, Evil, null)
        };
        foreach (var body in html)
        {
            Assert.Contains(EncodedEvil, body);
            Assert.DoesNotContain("<script>", body);
        }
    }

    [Fact]
    public void Button_IsRenderedOnlyForAbsoluteHttpsUrls_OrAnAppPathTheMailServiceResolves()
    {
        Assert.Contains($"<a href=\"{Link}\"", Mail.BookingApprovedBody("Ada", "REQ-1", "TR-1", null, Link));
        Assert.Contains("<a href=\"{{PX_FRONTEND_URL}}/bookings/1\"", Mail.BookingApprovedBody("Ada", "REQ-1", "TR-1", null, "/bookings/1"));
        foreach (var unsafeUrl in new[] { null, "", "#", "//evil.example", "http://app.pegaxus.example/x", "javascript:alert(1)" })
        {
            var html = Mail.BookingApprovedBody("Ada", "REQ-1", "TR-1", null, unsafeUrl);
            Assert.DoesNotContain("<a ", html);
            Assert.DoesNotContain("View details", html);
        }
    }

    [Fact]
    public void Otp_KeepsTheCodeBetweenTagsSoItCanBeExtracted()
    {
        var html = Mail.OtpBody(true, "Ada", "004217", 5);
        Assert.Matches(@">004217<", html);
        Assert.Contains("5 minutes", html);
        Assert.Contains("Verify your email", html);
        Assert.Contains("Reset your password", Mail.OtpBody(false, "Ada", "004217", 5));
    }

    [Fact]
    public void Greeting_FallsBackToCustomer_WhenNameIsMissing()
    {
        Assert.Contains("Dear customer,", Mail.DocumentReuploadBody("DRQ-1", "Passport", null, "EXPIRED", null, new DateOnly(2026, 12, 1), null));
        Assert.Contains("Dear customer,", Mail.InvoiceCancelledBody("  ", "INV-1", "DEPOSIT", "x", null));
    }

    [Fact]
    public void Reupload_ShowsFriendlyIssue_AndOmitsEmptyOptionalRows()
    {
        var html = Mail.DocumentReuploadBody("DRQ-1", "Passport", null, "INVALID_IMAGE", null, new DateOnly(2026, 12, 1), null);
        Assert.Contains("Invalid or unreadable image", html);
        Assert.DoesNotContain("Horse:", html);
        Assert.DoesNotContain("Note:", html);
        Assert.Contains("01/12/2026", html);
        Assert.Contains("Horse:", Mail.DocumentReuploadBody("DRQ-1", "Passport", "Storm", "OTHER", "n", new DateOnly(2026, 12, 1), null));
    }

    [Fact]
    public void MissingDocuments_ListsEachItem_WithItsOwnOrTheGeneralDueDate()
    {
        var html = Mail.MissingDocumentsBody("CDOS-1", "REQ-1", new DateOnly(2026, 12, 1), null, null,
            [new Mail.MissingItem("Passport", "Storm", new DateOnly(2026, 11, 20)), new Mail.MissingItem("Vaccination", null, null)]);
        Assert.Contains("Passport", html); Assert.Contains("Horse: Storm", html); Assert.Contains("20/11/2026", html);
        Assert.Contains("Vaccination", html);
        Assert.DoesNotContain("Outstanding documents:", Mail.MissingDocumentsBody("CDOS-1", "REQ-1", new DateOnly(2026, 12, 1), null, null, []));
    }

    [Fact]
    public void RouteAssignment_ShowsSegmentsInUtc_AndAReasonOnlyWhenReplaced()
    {
        var segment = new Mail.RouteSegment(2, new DateTime(2026, 11, 28, 6, 5, 0), null, "EMP-4", null, "Acme Freight");
        var replaced = Mail.RouteAssignmentBody("Ada", "RP-1", true, "Driver sick", null, [segment]);
        Assert.Contains("Route assignment updated", replaced); Assert.Contains("Reason for change:", replaced);
        Assert.Contains("Segment 2", replaced); Assert.Contains("28/11/2026 06:05 UTC", replaced);
        Assert.Contains("EMP-4", replaced); Assert.Contains("Acme Freight", replaced);
        var assigned = Mail.RouteAssignmentBody("Ada", "RP-1", false, "ignored", null, [new Mail.RouteSegment(1, null, null, null, null, null)]);
        Assert.Contains("New route assignment", assigned); Assert.DoesNotContain("Reason for change:", assigned);
        Assert.Contains("&mdash;", assigned);
    }

    [Theory]
    [InlineData(Mail.InvoiceMailKind.Invoice, "Invoice issued", "Amount due", "Payment required", true)]
    [InlineData(Mail.InvoiceMailKind.Quotation, "Quotation issued", "Estimated total", "No payment required", false)]
    [InlineData(Mail.InvoiceMailKind.CreditNote, "Credit note issued", "Payout or credit amount", "Credit note", false)]
    public void InvoiceIssued_AdaptsToTheInvoiceKind(Mail.InvoiceMailKind kind, string title, string amountLabel, string pill, bool payable)
    {
        var html = Mail.InvoiceIssuedBody("Ada", "INV-1", "DEPOSIT", "REQ-1", kind, 1234.5m, "EUR", new DateOnly(2026, 11, 15), Link);
        Assert.Contains(title, html); Assert.Contains(amountLabel, html); Assert.Contains(pill, html);
        Assert.Contains("1,234.50 EUR", html);
        Assert.Equal(payable, html.Contains("Due date:") && html.Contains("15/11/2026"));
        Assert.Equal(payable, html.Contains("View and pay"));
    }

    [Fact]
    public void Money_UsesWholeNumbersForVnd_AndTwoDecimalsForOtherCurrencies()
    {
        Assert.Contains("120,000 VND", Mail.PaymentConfirmedBody("Ada", "INV-1", "RCP-1", 120000m, "VND", false, null));
        Assert.Contains("120.00 EUR", Mail.PaymentConfirmedBody("Ada", "INV-1", "RCP-1", 120m, "EUR", false, null));
    }

    [Fact]
    public void PaymentConfirmed_DistinguishesAPayoutFromAPayment()
    {
        var payout = Mail.PaymentConfirmedBody("Ada", "INV-1", "RCP-1", 120m, "EUR", true, null);
        Assert.Contains("Payout confirmed", payout); Assert.DoesNotContain("Payment confirmed", payout);
        var payment = Mail.PaymentConfirmedBody("Ada", "INV-1", "RCP-1", 120m, "EUR", false, null);
        Assert.Contains("Payment confirmed", payment); Assert.Contains("RCP-1", payment);
    }

    [Fact]
    public void UrgentIncident_EncodesUntrustedFieldsAndKeepsIncidentLink()
    {
        var body = Mail.IncidentReportedBody("<staff>", "<code>", "<severity>", "<title>", Evil, "/admin/incidents/test");
        Assert.DoesNotContain(Evil, body); Assert.Contains("&lt;title&gt;", body); Assert.Contains(EncodedEvil, body);
        Assert.Contains("/admin/incidents/test", body); Assert.Contains("&lt;staff&gt;", body);
    }

    [Fact]
    public void Reconciliation_CarriesEverythingAManagerNeeds()
    {
        var html = Mail.PaymentReconciliationBody("Max", "The gateway voided a payment that was already confirmed", "RCP-1", "CONFIRMED", "TX-9", 120000m, "VND", "INV-1", "PAID", null);
        foreach (var expected in new[] { "Max", "gateway voided", "RCP-1", "CONFIRMED", "TX-9", "120,000 VND", "INV-1", "PAID", "refund or reconcile" })
            Assert.Contains(expected, html);
    }
}
