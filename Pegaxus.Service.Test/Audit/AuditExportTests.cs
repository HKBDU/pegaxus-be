using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.OutboxService;

using Audit = Pegaxus.Service.AuditService;
using static Pegaxus.Service.Test.AuditSupport;

namespace Pegaxus.Service.Test;

public class AuditExportTests
{
    private static Audit.Request.ExportRequest Ask(string format = "ZIP", string mode = "STANDARD") => new() { Format = format, Mode = mode };

    private static async Task<Guid> NextQueuedAsync(Audit.AuditExportQueue queue)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await foreach (var id in queue.ReadAllAsync(timeout.Token)) return id;
        throw new InvalidOperationException("Nothing was queued");
    }

    private static Dictionary<string, byte[]> Entries(byte[] zip)
    {
        using var archive = new ZipArchive(new MemoryStream(zip));
        return archive.Entries.ToDictionary(entry => entry.FullName, entry =>
        {
            using var stream = entry.Open(); using var copy = new MemoryStream(); stream.CopyTo(copy); return copy.ToArray();
        });
    }

    private static string Text(byte[] bytes) => Encoding.UTF8.GetString(bytes);

    /// <summary>Asks for a package, lets the worker's step run and returns the finished status with the stored bytes.</summary>
    private static async Task<(Audit.Response.ExportStatus Status, byte[] Bytes)> BuildAsync(Fixture f, string format = "ZIP", string mode = "STANDARD")
    {
        var queued = await f.Service.RequestExportAsync(f.ManagerId, f.Transport.TransportId, Ask(format, mode));
        await f.Service.ProcessExportAsync(queued.ExportId);
        var status = await f.Service.GetExportAsync(f.ManagerId, queued.ExportId);
        return (status, f.Files.Exports.Values.Last());
    }

    // ---------------------------------------------------------------- AU3

    [Fact]
    public async Task Request_QueuesAJobAndTheStatusShowsItWaiting()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);

        var queued = await f.Service.RequestExportAsync(f.ManagerId, f.Transport.TransportId, Ask("zip", "insurance"));
        var status = await f.Service.GetExportAsync(f.ManagerId, queued.ExportId);

        Assert.Equal((f.Transport.TransportId, "ZIP", "INSURANCE", "QUEUED"), (queued.TransportId, queued.Format, queued.Mode, queued.Status));
        Assert.Equal(f.Clock.GetUtcNow(), queued.RequestedAt); Assert.Equal(queued.ExportId, await NextQueuedAsync(f.Queue));
        Assert.Equal(("QUEUED", f.Transport.TransportCode), (status.Status, status.TransportCode));
        Assert.All(new object?[] { status.Delivery, status.DownloadUrl, status.DownloadUrlExpiresAt, status.FileName, status.SizeBytes, status.Sha256, status.ErrorMessage, status.CompletedAt }, Assert.Null);
    }

    [Fact]
    public async Task Request_AskingAgainWhileItIsBeingBuiltReturnsTheSameJob()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        var (_, otherManager) = await TestSupport.AddLogisticsManagerAsync(db);

        var first = await f.Service.RequestExportAsync(f.ManagerId, f.Transport.TransportId, Ask());
        var again = await f.Service.RequestExportAsync(f.ManagerId, f.Transport.TransportId, Ask("ZIP", "standard"));
        var otherMode = await f.Service.RequestExportAsync(f.ManagerId, f.Transport.TransportId, Ask("ZIP", "INSURANCE"));
        var otherFormat = await f.Service.RequestExportAsync(f.ManagerId, f.Transport.TransportId, Ask("PDF"));
        var otherUser = await f.Service.RequestExportAsync(otherManager.AccountId, f.Transport.TransportId, Ask());

        Assert.Equal(first.ExportId, again.ExportId);
        Assert.Equal(4, new[] { first.ExportId, otherMode.ExportId, otherFormat.ExportId, otherUser.ExportId }.Distinct().Count());
        // Once the first one is finished, asking again starts a new package.
        await f.Service.ProcessExportAsync(first.ExportId);
        Assert.NotEqual(first.ExportId, (await f.Service.RequestExportAsync(f.ManagerId, f.Transport.TransportId, Ask())).ExportId);
    }

    [Fact]
    public async Task Request_RefusesWhatItCannotExport()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        var onTheRoad = f.R.AddTransport("2026-10-25 10:00", status: TransportStatus.IN_TRANSIT); await db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<FieldValidationException>(() => f.Service.RequestExportAsync(f.ManagerId, f.Transport.TransportId, Ask("DOCX", "FULL")));
        Assert.Equal(new[] { "format", "mode" }, error.Errors.Select(item => item.Field).Order());
        Assert.Equal("INVALID_TRANSPORT_STATUS", (await Assert.ThrowsAsync<ConflictException>(() => f.Service.RequestExportAsync(f.ManagerId, onTheRoad.TransportId, Ask()))).ErrorCode);
        Assert.Equal("TRANSPORT_NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(() => f.Service.RequestExportAsync(f.ManagerId, Guid.NewGuid(), Ask()))).ErrorCode);
        await Assert.ThrowsAsync<ForbiddenException>(() => f.Service.RequestExportAsync(f.CustomerId, f.Transport.TransportId, Ask()));
        await Assert.ThrowsAsync<ForbiddenException>(() => f.Service.RequestExportAsync(f.Driver.AccountId, f.Transport.TransportId, Ask()));
    }

    // ---------------------------------------------------------------- the package

    [Fact]
    public async Task Zip_TheStandardPackageHoldsTheApprovedFilesTheProofsTheEpodsAndAManifest()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);

        var (status, bytes) = await BuildAsync(f);

        Assert.Equal(("READY", "DIRECT", $"{f.Transport.TransportCode}-standard.zip"), (status.Status, status.Delivery, status.FileName));
        Assert.Equal(bytes.Length, status.SizeBytes); Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), status.Sha256);
        Assert.Equal(f.Clock.GetUtcNow(), status.CompletedAt); Assert.Null(status.ErrorMessage);
        Assert.Equal(f.Clock.GetUtcNow().AddHours(24), status.DownloadUrlExpiresAt);
        Assert.StartsWith($"https://signed.example.com/{status.FileName}", status.DownloadUrl);

        var entries = Entries(bytes);
        Assert.Equal(new[]
        {
            "audit.json", "documents/DOS-MAIN/CD-EHC-v2/01-page-1.pdf", "documents/DOS-MAIN/CD-EHC-v2/02-page-2.pdf", "handovers/HO-1-epod.pdf", "handovers/HO-2-epod.pdf",
            "handovers/HO-3-epod.pdf", "manifest.json", "proofs/quarantine/01-bcp.pdf"
        }, entries.Keys.Order());
        Assert.Equal("%PDF-ehc-page-1", Text(entries["documents/DOS-MAIN/CD-EHC-v2/01-page-1.pdf"]));

        using var audit = JsonDocument.Parse(entries["audit.json"]);
        Assert.Equal("FULL", audit.RootElement.GetProperty("viewMode").GetString()); Assert.True(audit.RootElement.TryGetProperty("finance", out _));

        using var manifest = JsonDocument.Parse(entries["manifest.json"]);
        var listed = manifest.RootElement.GetProperty("files").EnumerateArray().ToDictionary(item => item.GetProperty("path").GetString()!, item => item);
        Assert.Equal(entries.Keys.Where(path => path != "manifest.json").Order(), listed.Keys.Order());
        foreach (var (path, item) in listed)
        {
            Assert.Equal(entries[path].Length, item.GetProperty("sizeBytes").GetInt64());
            Assert.Equal(Convert.ToHexString(SHA256.HashData(entries[path])).ToLowerInvariant(), item.GetProperty("sha256").GetString());
        }
        // The customs file is missing from storage: it is reported, and the package is still made.
        Assert.Equal("proofs/customs/01-customs.pdf", Assert.Single(manifest.RootElement.GetProperty("skipped").EnumerateArray()).GetProperty("path").GetString());
    }

    [Fact]
    public async Task Zip_TheInsurancePackageAddsEveryDocumentTheClaimsTheIncidentsAndTheirPhotos()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        var incident = await db.IncidentReports.SingleAsync(item => item.TransportId == f.Transport.TransportId && item.Severity == IncidentSeverity.HIGH);
        db.IncidentPhotos.Add(new IncidentPhoto { IncidentPhotoId = Guid.NewGuid(), IncidentReportId = incident.IncidentReportId, PhotoUrl = "https://res.cloudinary.com/test/image/authenticated/crash", SortOrder = 0, CreatedAt = D("2026-10-12 11:00") });
        await db.SaveChangesAsync();
        f.Files.Files["https://res.cloudinary.com/test/image/authenticated/crash"] = [0x89, 0x50, 0x4e, 0x47, 0, 0];

        var (status, bytes) = await BuildAsync(f, mode: "INSURANCE");

        Assert.Equal($"{f.Transport.TransportCode}-insurance.zip", status.FileName);
        var entries = Entries(bytes);
        Assert.Contains("documents/DOS-MAIN/CD-COG-v1/01-CD-COG-1.pdf", entries.Keys); // the pending certificate too
        Assert.Contains($"incidents/{incident.IncidentCode}/photo-01.png", entries.Keys);
        var claims = Text(entries["claims.json"]);
        Assert.Contains("CLM-LIVE", claims); Assert.Contains("Photos sent by email", claims); Assert.DoesNotContain("internalNote", claims, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Incident", Text(entries["incidents.json"]));
    }

    [Fact]
    public async Task Zip_AnEpodThatCannotBeMadeIsSkippedNotFatal()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        f.EpodMaker.Missing.Add(f.BorderId);

        var (status, bytes) = await BuildAsync(f);

        Assert.Equal("READY", status.Status);
        var entries = Entries(bytes);
        Assert.DoesNotContain("handovers/HO-2-epod.pdf", entries.Keys); Assert.Contains("handovers/HO-3-epod.pdf", entries.Keys);
        using var manifest = JsonDocument.Parse(entries["manifest.json"]);
        Assert.Contains(manifest.RootElement.GetProperty("skipped").EnumerateArray(), item => item.GetProperty("path").GetString() == "handovers/HO-2-epod.pdf");
    }

    [Fact]
    public async Task Pdf_IsOneSealedSummaryWithTheHashOfItsContent()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);

        var (status, bytes) = await BuildAsync(f, "PDF", "INSURANCE");

        Assert.Equal(("READY", $"{f.Transport.TransportCode}-insurance.pdf"), (status.Status, status.FileName));
        Assert.Equal("%PDF", Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), status.Sha256);
        Assert.Equal(bytes.Length, status.SizeBytes);
    }

    // ---------------------------------------------------------------- AU4

    [Fact]
    public async Task Large_APackageOver500MbIsMailedNotOfferedOnScreen()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        f.Files.ReportedSize = 600L * 1024 * 1024;

        var (status, _) = await BuildAsync(f);

        Assert.Equal(("READY", "EMAIL", null, 600L * 1024 * 1024), (status.Status, status.Delivery, status.DownloadUrl, status.SizeBytes));
        Assert.NotNull(status.Sha256);
        var email = OutboxQueue.Deserialize<EmailPayload>(Assert.Single(await db.OutboxMessages.ToListAsync()).Payload);
        Assert.Equal(f.R.ManagerAccount.Email, email.To); Assert.Equal($"Audit package ready — {f.Transport.TransportCode}", email.Subject);
        Assert.Contains("https://signed.example.com/", email.Body); Assert.Contains("600 MB", email.Body);
        var notification = Assert.Single(await db.Notifications.ToListAsync());
        Assert.Equal(("Audit package ready", NotificationType.SYSTEM, f.Transport.TransportId, $"/admin/transport-archive/{f.Transport.TransportId}"),
            (notification.Title, notification.NotificationType, notification.TransportId, notification.ActionUrl));
        Assert.Equal(f.ManagerId, (await db.AccountNotifications.SingleAsync()).AccountId);
    }

    [Fact]
    public async Task Small_ADirectPackageSendsNoMailAndNoNotification()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);

        await BuildAsync(f);

        Assert.Empty(db.OutboxMessages); Assert.Empty(db.Notifications);
    }

    [Fact]
    public async Task Failure_AStorageErrorEndsTheJobAsFailedWithoutLeakingTheCause()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        f.Files.FailUpload = true;

        var queued = await f.Service.RequestExportAsync(f.ManagerId, f.Transport.TransportId, Ask());
        await f.Service.ProcessExportAsync(queued.ExportId);
        var status = await f.Service.GetExportAsync(f.ManagerId, queued.ExportId);

        Assert.Equal("FAILED", status.Status); Assert.Equal("The audit package could not be built. Please try again.", status.ErrorMessage);
        Assert.DoesNotContain("bucket", status.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
        Assert.All(new object?[] { status.DownloadUrl, status.SizeBytes, status.Sha256, status.CompletedAt }, Assert.Null);
        // A failed job is not "being built", so the next request starts over.
        f.Files.FailUpload = false;
        Assert.NotEqual(queued.ExportId, (await f.Service.RequestExportAsync(f.ManagerId, f.Transport.TransportId, Ask())).ExportId);
    }

    [Fact]
    public async Task Stuck_AJobNobodyPickedUpIsReportedFailedAndCanBeAskedAgain()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        var queued = await f.Service.RequestExportAsync(f.ManagerId, f.Transport.TransportId, Ask());

        f.Clock.Value = f.Clock.Value.AddMinutes(16);
        var status = await f.Service.GetExportAsync(f.ManagerId, queued.ExportId);

        Assert.Equal(("FAILED", "The export was interrupted. Please request it again."), (status.Status, status.ErrorMessage));
        Assert.NotEqual(queued.ExportId, (await f.Service.RequestExportAsync(f.ManagerId, f.Transport.TransportId, Ask())).ExportId);
    }

    [Fact]
    public async Task Process_OnlyRunsAQueuedJobOnce()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        var queued = await f.Service.RequestExportAsync(f.ManagerId, f.Transport.TransportId, Ask());

        await f.Service.ProcessExportAsync(queued.ExportId);
        await f.Service.ProcessExportAsync(queued.ExportId);
        await f.Service.ProcessExportAsync(Guid.NewGuid());

        Assert.Single(f.Files.Exports);
    }

    [Fact]
    public async Task Status_AFreshLinkEveryTimeUntilThePackageExpires()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        var queued = await f.Service.RequestExportAsync(f.ManagerId, f.Transport.TransportId, Ask());
        await f.Service.ProcessExportAsync(queued.ExportId);

        f.Clock.Value = f.Clock.Value.AddHours(23);
        var late = await f.Service.GetExportAsync(f.ManagerId, queued.ExportId);
        f.Clock.Value = f.Clock.Value.AddHours(2);
        var gone = await f.Service.GetExportAsync(f.ManagerId, queued.ExportId);

        Assert.Equal("READY", late.Status); Assert.Equal(new DateTimeOffset(2026, 11, 2, 2, 0, 0, TimeSpan.Zero), late.DownloadUrlExpiresAt);
        Assert.Equal("EXPIRED", gone.Status); Assert.Null(gone.DownloadUrl); Assert.Null(gone.FileName);
    }

    [Fact]
    public async Task Status_AnyManagerCanReadItAndOthersCannot()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        var (otherManager, _) = await TestSupport.AddLogisticsManagerAsync(db);
        var queued = await f.Service.RequestExportAsync(f.ManagerId, f.Transport.TransportId, Ask());

        Assert.Equal(queued.ExportId, (await f.Service.GetExportAsync(otherManager.AccountId, queued.ExportId)).ExportId);
        await Assert.ThrowsAsync<ForbiddenException>(() => f.Service.GetExportAsync(f.CustomerId, queued.ExportId));
        await Assert.ThrowsAsync<ForbiddenException>(() => f.Service.GetExportAsync(f.Driver.AccountId, queued.ExportId));
        var missing = await Assert.ThrowsAsync<NotFoundException>(() => f.Service.GetExportAsync(f.ManagerId, Guid.NewGuid()));
        Assert.Equal(("EXPORT_NOT_FOUND", "Export job not found or expired"), (missing.ErrorCode, missing.Message));
    }
}
