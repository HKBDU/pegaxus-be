using System.Text.RegularExpressions;
using Pegaxus.Service.MailService;

using Mail = Pegaxus.Service.MailService.EmailBodies;

namespace Pegaxus.Service.Test;

public class EmailBodiesFlowTests
{
    private const string Evil = "<script>alert(1)</script>";
    private const string EncodedEvil = "&lt;script&gt;alert(1)&lt;/script&gt;";
    private const string Link = "https://app.pegaxus.example/bookings/1";

    private static IEnumerable<(string Name, string Html)> AllTemplates(string text = "Plain", string? link = Link) =>
    [
        ("booking-review", Mail.BookingNeedsReviewBody(text, text, "REQ-1", text, new DateOnly(2026, 12, 1), false, text, link)),
        ("booking-cancelled", Mail.BookingNeedsReviewBody(text, text, "REQ-1", text, null, true, text, link)),
        ("booking-changed-cancel-customer", Mail.BookingChangedBody(text, "REQ-1", text, true, text, false, link)),
        ("booking-changed-draft-staff", Mail.BookingChangedBody(text, "REQ-1", text, false, text, true, link)),
        ("dossier-opened", Mail.DossierUpdateBody(text, "CD-1", Mail.DossierUpdateKind.Opened, text, text, link)),
        ("dossier-added", Mail.DossierUpdateBody(text, "CD-1", Mail.DossierUpdateKind.RequirementAdded, text, text, link)),
        ("dossier-updated", Mail.DossierUpdateBody(text, "CD-1", Mail.DossierUpdateKind.RequirementUpdated, text, text, link)),
        ("dossier-waived", Mail.DossierUpdateBody(text, "CD-1", Mail.DossierUpdateKind.RequirementWaived, text, text, link)),
        ("dossier-removed", Mail.DossierUpdateBody(text, "CD-1", Mail.DossierUpdateKind.RequirementRemoved, text, text, link)),
        ("dossier-approved", Mail.DossierUpdateBody(text, "CD-1", Mail.DossierUpdateKind.DocumentApproved, text, text, link)),
        ("dossier-comment", Mail.DossierUpdateBody(text, "CD-1", Mail.DossierUpdateKind.RequestComment, text, text, link)),
        ("authority-submitted", Mail.AuthorityBody(text, "CD-1", text, Mail.AuthorityMailKind.Submitted, text, link)),
        ("authority-approved", Mail.AuthorityBody(text, "CD-1", text, Mail.AuthorityMailKind.Approved, text, link)),
        ("authority-rejected", Mail.AuthorityBody(text, "CD-1", text, Mail.AuthorityMailKind.Rejected, text, link)),
        ("customs-submitted", Mail.ClearanceBody(text, "CD-1", Mail.ClearanceKind.Customs, text, Mail.ClearanceOutcome.Submitted, text, link)),
        ("customs-held", Mail.ClearanceBody(text, "CD-1", Mail.ClearanceKind.Customs, text, Mail.ClearanceOutcome.Held, text, link)),
        ("customs-verified", Mail.ClearanceBody(text, "CD-1", Mail.ClearanceKind.Customs, text, Mail.ClearanceOutcome.Verified, text, link)),
        ("customs-rejected", Mail.ClearanceBody(text, "CD-1", Mail.ClearanceKind.Customs, text, Mail.ClearanceOutcome.Rejected, text, link)),
        ("bcp-failed", Mail.ClearanceBody(text, "CD-1", Mail.ClearanceKind.Bcp, text, Mail.ClearanceOutcome.Failed, text, link)),
        ("bcp-proof-rejected", Mail.ClearanceBody(text, "CD-1", Mail.ClearanceKind.Bcp, text, Mail.ClearanceOutcome.ProofRejected, text, link)),
        ("staff-action", Mail.StaffActionBody(text, "Document waiting for review", text, "CD-1", text, link)),
        ("route-legal", Mail.RouteReviewBody(text, "RP-1", Mail.RouteReviewKind.LegalReviewRequested, text, link)),
        ("route-revision", Mail.RouteReviewBody(text, "RP-1", Mail.RouteReviewKind.RevisionRequired, text, link)),
        ("route-emergency", Mail.RouteReviewBody(text, "RP-1", Mail.RouteReviewKind.EmergencyApproval, text, link)),
        ("route-return-approval", Mail.RouteReviewBody(text, "RP-1", Mail.RouteReviewKind.ReturnApproval, text, link)),
        ("route-extra-dossier", Mail.RouteReviewBody(text, "RP-1", Mail.RouteReviewKind.ExtraDossierReady, text, link)),
        ("route-approved", Mail.RouteReviewBody(text, "RP-1", Mail.RouteReviewKind.ChangeApproved, text, link)),
        ("route-rejected", Mail.RouteReviewBody(text, "RP-1", Mail.RouteReviewKind.ChangeRejected, text, link)),
        ("route-return-active", Mail.RouteReviewBody(text, "RP-1", Mail.RouteReviewKind.ReturnActive, text, link)),
        ("route-backup-customer", Mail.RouteChangedBody(text, "RP-1", Mail.RouteChangeKind.BackupActivated, text, link)),
        ("route-return-customer", Mail.RouteChangedBody(text, "RP-1", Mail.RouteChangeKind.ReturnActivated, text, link)),
        ("route-confirmed", Mail.RouteChangedBody(text, "RP-1", Mail.RouteChangeKind.RouteConfirmed, text, link)),
        ("route-return-confirmed", Mail.RouteChangedBody(text, "RP-1", Mail.RouteChangeKind.ReturnConfirmed, text, link)),
        ("route-driver-backup", Mail.RouteChangedBody(text, "RP-1", Mail.RouteChangeKind.DriverBackup, text, link)),
        ("route-driver-return", Mail.RouteChangedBody(text, "RP-1", Mail.RouteChangeKind.DriverBeginReturn, text, link)),
        ("plan-assigned", Mail.TransportPlanBody(text, "TP-1", Mail.TransportPlanMailKind.Assigned, text, link)),
        ("plan-personnel", Mail.TransportPlanBody(text, "TP-1", Mail.TransportPlanMailKind.PersonnelChanged, text, link)),
        ("plan-updated", Mail.TransportPlanBody(text, "TP-1", Mail.TransportPlanMailKind.Updated, text, link)),
        ("employee-welcome", Mail.EmployeeWelcomeBody(text, text, text, link)),
        ("catalog-change", Mail.CatalogChangeBody(text, "Vehicle", "VEH-1", text, "is no longer available", text, link)),
        ("invoice-overdue", Mail.InvoiceOverdueBody(text, "INV-1", "REQ-1", 1500000m, "VND", new DateOnly(2026, 11, 15), link)),
        ("transport-picked-up", Mail.TransportMilestoneBody(text, "REQ-1", "TR-1", false, new DateTime(2026, 12, 1, 6, 0, 0), link)),
        ("transport-delivered", Mail.TransportMilestoneBody(text, "REQ-1", "TR-1", true, null, link)),
        ("health-alert", Mail.HorseHealthAlertBody(text, "TR-1", text, link)),
        ("handover-scheduled", Mail.HandoverBody(text, "HO-1", false, text, link)),
        ("handover-cancelled", Mail.HandoverBody(text, "HO-1", true, text, link)),
    ];

    [Fact]
    public void EveryNewTemplate_IsAResponsiveBrandedDocument_WithLightAndDarkSupport()
    {
        foreach (var (name, html) in AllTemplates())
        {
            Assert.True(html.StartsWith("<!doctype html>", StringComparison.OrdinalIgnoreCase), name);
            Assert.Contains("name=\"viewport\"", html);
            Assert.Contains("prefers-color-scheme: dark", html);
            Assert.Contains("alt=\"Pegaxus\"", html);
            Assert.Contains($"{DateTime.UtcNow.Year} PEGAXUS LOGISTICS GROUP", html);
            Assert.Equal(Regex.Matches(html, "<table", RegexOptions.IgnoreCase).Count, Regex.Matches(html, "</table>", RegexOptions.IgnoreCase).Count);
            var markup = Regex.Replace(html, "<style>.*?</style>", "", RegexOptions.Singleline);
            Assert.DoesNotMatch(@"\{[A-Za-z]", markup);
            Assert.Contains($"href=\"{Link}\"", html);
        }
    }

    [Fact]
    public void EveryNewTemplate_EncodesUserSuppliedText()
    {
        foreach (var (name, html) in AllTemplates(Evil))
        {
            Assert.DoesNotContain(Evil, html);
            Assert.Contains(EncodedEvil, html);
        }
    }

    [Fact]
    public void EveryNewTemplate_HasADistinctTitle_PerKind()
    {
        var titles = AllTemplates().Select(item => Regex.Match(item.Html, "<title>(.*?)</title>").Groups[1].Value).ToList();
        Assert.All(titles, title => Assert.False(string.IsNullOrWhiteSpace(title)));
        Assert.Contains("Booking to review", titles);
        Assert.Contains("Document approved", titles);
        Assert.Contains("Emergency backup route needs approval", titles);
        Assert.Contains("Welcome to Pegaxus", titles);
    }

    [Fact]
    public void WithoutALink_TheButtonIsLeftOut()
    {
        foreach (var (name, html) in AllTemplates(link: null))
            Assert.DoesNotContain("<a href", html);
    }

    [Fact]
    public void EmployeeWelcome_NeverCarriesAPassword()
    {
        var html = Mail.EmployeeWelcomeBody("Ada", "Driver", "ada@example.com", Link);
        Assert.Contains("ada@example.com", html);
        Assert.Contains("Forgot password", html);
        Assert.DoesNotContain("pegaxusDemo", html);
    }

    [Fact]
    public void AnAppPath_RendersAPlaceholderButton_ThatResolvesAgainstTheFrontendOrigin()
    {
        var html = Mail.BookingNeedsReviewBody("Max", "Ada", "REQ-1", "FR to GB", null, false, null, "/admin/transport-requests/1?tab=a&b=c");

        Assert.Contains("{{PX_FRONTEND_URL}}" + "/admin/transport-requests/1?tab=a&amp;b=c", html);
        Assert.Contains("<!--px-button-->", html);

        var resolved = EmailLinks.Resolve(html, "https://app.pegaxus.example/");
        Assert.Contains("href=\"https://app.pegaxus.example/admin/transport-requests/1?tab=a&amp;b=c\"", resolved);
        Assert.DoesNotContain("{{PX_FRONTEND_URL}}", resolved);
        Assert.DoesNotContain("<!--px-button-->", resolved);
        Assert.DoesNotContain("<!--/px-button-->", resolved);
    }

    [Fact]
    public void LoopbackHttpFrontend_IsAcceptedForLocalDevelopment()
    {
        var html = Mail.HandoverBody("Ada", "HO-1", false, null, "/crew/handovers/1");
        Assert.Contains("href=\"http://localhost:5173/crew/handovers/1\"", EmailLinks.Resolve(html, "http://localhost:5173"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("http://app.pegaxus.example")]
    [InlineData("https://user:pw@app.pegaxus.example")]
    [InlineData("ftp://app.pegaxus.example")]
    public void WithoutAUsableFrontendOrigin_TheButtonIsRemoved_NotLinkedToADeadAddress(string? frontendUrl)
    {
        var html = Mail.HandoverBody("Ada", "HO-1", false, null, "/crew/handovers/1");

        var resolved = EmailLinks.Resolve(html, frontendUrl);

        Assert.DoesNotContain("<a href", resolved);
        Assert.DoesNotContain("PX_FRONTEND_URL", resolved);
        Assert.DoesNotContain("px-button", resolved);
        Assert.Contains("Handover scheduled", resolved);
    }

    [Theory]
    [InlineData("//evil.example/x")]
    [InlineData("/\\evil.example")]
    [InlineData("javascript:alert(1)")]
    [InlineData("http://evil.example")]
    [InlineData("relative/path")]
    public void UnsafeOrMalformedLinks_AreNeverRendered(string actionUrl)
    {
        var html = Mail.HandoverBody("Ada", "HO-1", false, null, actionUrl);
        Assert.DoesNotContain("<a href", html);
        Assert.DoesNotContain("PX_FRONTEND_URL", html);
    }

    [Fact]
    public void AnAbsoluteHttpsLink_IsKeptAsIs_AndNotTouchedByTheResolver()
    {
        var html = Mail.HandoverBody("Ada", "HO-1", false, null, Link);
        Assert.Equal(html, EmailLinks.Resolve(html, null));
    }
}
