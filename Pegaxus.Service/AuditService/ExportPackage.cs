using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pegaxus.Repository.Enum;

using static Pegaxus.Service.Common.DbTime;
using static Pegaxus.Service.ReportService.ReportFiles;

namespace Pegaxus.Service.AuditService;

public sealed partial class Service
{
    private static readonly JsonSerializerOptions PackageJson = new(JsonSerializerDefaults.Web) { WriteIndented = true, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    private sealed record Package(Stream Content, string FileName, string ContentType);
    private sealed record ManifestEntry(string Path, long SizeBytes, string Sha256);
    private sealed record SkippedEntry(string Path, string Reason);

    /// <summary>What only the insurance package adds: the claims with their outcome and the incidents in full (never the internal notes).</summary>
    private sealed record InsuranceExtras(IReadOnlyList<object> Claims, IReadOnlyList<object> Incidents);

    private async Task<Package> BuildPackageAsync(ExportJob job, CancellationToken cancellationToken)
    {
        var transport = await db.Transports.AsNoTracking().Where(item => item.TransportId == job.TransportId)
            .Select(item => new TransportRow(item.TransportId, item.TransportCode, item.Status, item.BookingId, item.CreatedAt, item.ActualDepartureAt, item.ActualArrivalAt,
                item.CompletedAt, item.CancelledAt)).SingleAsync(cancellationToken);
        // The package is the full manager view of the transport, built with the requester's own rights.
        var actor = new Actor(job.RequestedByAccountId, true, false);
        var audit = await BuildAuditAsync(actor, transport, null, int.MaxValue, cancellationToken);
        var insurance = job.Mode == "INSURANCE";
        var extras = insurance ? await InsuranceExtrasAsync(job.TransportId, cancellationToken) : null;
        var stem = $"{job.TransportCode}-{job.Mode.ToLowerInvariant()}";
        return job.Format == "PDF"
            ? await BuildPdfAsync(job, audit, extras, stem, cancellationToken)
            : await BuildZipAsync(job, transport, audit, extras, stem, cancellationToken);
    }

    private static FileStream NewTempFile() => new(Path.Combine(Path.GetTempPath(), $"audit-{Guid.NewGuid():N}.tmp"), FileMode.Create, FileAccess.ReadWrite, FileShare.None, 81920,
        FileOptions.DeleteOnClose | FileOptions.Asynchronous);

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    /// <summary>A name that is safe inside a zip path: no separators or control characters.</summary>
    private static string Safe(string? name)
    {
        var cleaned = new string((name ?? "file").Select(c => char.IsControl(c) || "\\/:*?\"<>|".Contains(c) ? '_' : c).ToArray()).Trim().Trim('.');
        return cleaned.Length == 0 ? "file" : cleaned.Length > 120 ? cleaned[..120] : cleaned;
    }

    private async Task<byte[]> FetchAsync(string url, CancellationToken cancellationToken) =>
        (url.StartsWith("s3://", StringComparison.Ordinal) ? await storage.DownloadPreviewAsync(url, cancellationToken) : await media.DownloadPreviewAsync(url, cancellationToken)).Content;

    // ------------------------------------------------------------------ ZIP

    private sealed record SourceFile(string Path, string Url);

    private async Task<List<SourceFile>> SourceFilesAsync(Guid transportId, bool insurance, List<(Guid Id, string Code)> incidents, CancellationToken cancellationToken)
    {
        var files = new List<SourceFile>();

        // Every document's newest version: the approved ones for the standard package, all of them for insurance.
        var versions = await db.DocumentVersions.AsNoTracking().Where(item => item.ComplianceDocument.DossierRequirement.ComplianceDossier.TransportId == transportId)
            .Select(item => new
            {
                item.DocumentVersionId, item.ComplianceDocumentId, item.VersionNo, item.FileName, item.FileUrl, item.ComplianceDocument.DocumentCode, item.ComplianceDocument.Status,
                Dossier = item.ComplianceDocument.DossierRequirement.ComplianceDossier.DossierCode
            }).ToListAsync(cancellationToken);
        var newest = versions.GroupBy(item => item.ComplianceDocumentId).Select(group => group.OrderByDescending(item => item.VersionNo).First())
            .Where(item => insurance || item.Status == ComplianceDocumentStatus.APPROVED).OrderBy(item => item.Dossier).ThenBy(item => item.DocumentCode).ToList();
        var newestIds = newest.Select(item => item.DocumentVersionId).ToArray();
        var attachments = (await db.DocumentAttachments.AsNoTracking().Where(item => newestIds.Contains(item.DocumentVersionId)).OrderBy(item => item.SortOrder)
            .Select(item => new { item.DocumentVersionId, item.FileName, item.FileUrl }).ToListAsync(cancellationToken)).ToLookup(item => item.DocumentVersionId);
        foreach (var version in newest)
        {
            var folder = $"documents/{Safe(version.Dossier)}/{Safe(version.DocumentCode)}-v{version.VersionNo}";
            var parts = attachments[version.DocumentVersionId].ToList();
            if (parts.Count == 0) files.Add(new($"{folder}/01-{Safe(version.FileName)}", version.FileUrl));
            for (var index = 0; index < parts.Count; index++) files.Add(new($"{folder}/{index + 1:00}-{Safe(parts[index].FileName)}", parts[index].FileUrl));
        }

        var quarantine = await db.ProofAttachments.AsNoTracking().Where(item => item.QuarantineProof != null && item.QuarantineProof.ComplianceDossier.TransportId == transportId)
            .OrderBy(item => item.CreatedAt).ThenBy(item => item.SortOrder).Select(item => new { item.FileName, item.FileUrl }).ToListAsync(cancellationToken);
        for (var index = 0; index < quarantine.Count; index++) files.Add(new($"proofs/quarantine/{index + 1:00}-{Safe(quarantine[index].FileName)}", quarantine[index].FileUrl));
        var customs = await db.ProofAttachments.AsNoTracking().Where(item => item.CustomsClearanceProof != null && item.CustomsClearanceProof.ComplianceDossier.TransportId == transportId)
            .OrderBy(item => item.CreatedAt).ThenBy(item => item.SortOrder).Select(item => new { item.FileName, item.FileUrl }).ToListAsync(cancellationToken);
        for (var index = 0; index < customs.Count; index++) files.Add(new($"proofs/customs/{index + 1:00}-{Safe(customs[index].FileName)}", customs[index].FileUrl));

        if (insurance)
        {
            var incidentIds = incidents.Select(item => item.Id).ToArray();
            var photos = await db.IncidentPhotos.AsNoTracking().Where(item => incidentIds.Contains(item.IncidentReportId)).OrderBy(item => item.SortOrder).ThenBy(item => item.IncidentPhotoId)
                .Select(item => new { item.IncidentReportId, item.PhotoUrl }).ToListAsync(cancellationToken);
            foreach (var group in photos.GroupBy(item => item.IncidentReportId))
            {
                var code = incidents.First(item => item.Id == group.Key).Code;
                var number = 0;
                // The extension is added once the bytes are known (it comes from the content type).
                foreach (var photo in group) files.Add(new($"incidents/{Safe(code)}/photo-{++number:00}", photo.PhotoUrl));
            }
        }
        return files;
    }

    private async Task<Package> BuildZipAsync(ExportJob job, TransportRow transport, Response.AuditResponse audit, InsuranceExtras? extras, string stem, CancellationToken cancellationToken)
    {
        var file = NewTempFile();
        var manifest = new List<ManifestEntry>(); var skipped = new List<SkippedEntry>();
        var incidents = audit.Operations!.Incidents.Select(item => (item.IncidentReportId, item.IncidentCode)).ToList();
        try
        {
            using (var zip = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: true))
            {
                async Task AddAsync(string path, byte[] bytes, bool listed = true)
                {
                    var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
                    await using var target = entry.Open();
                    await target.WriteAsync(bytes, cancellationToken);
                    if (listed) manifest.Add(new(path, bytes.Length, Hash(bytes)));
                }

                await AddAsync("audit.json", JsonSerializer.SerializeToUtf8Bytes(audit, PackageJson));
                if (extras is not null)
                {
                    await AddAsync("claims.json", JsonSerializer.SerializeToUtf8Bytes(extras.Claims, PackageJson));
                    await AddAsync("incidents.json", JsonSerializer.SerializeToUtf8Bytes(extras.Incidents, PackageJson));
                }

                // The e-POD of every signed handover is generated with the requester's own rights.
                foreach (var handover in audit.Operations.Handovers.Where(item => item.Status == nameof(HandoverStatus.COMPLETED)))
                {
                    var path = $"handovers/{Safe(handover.HandoverCode)}-epod.pdf";
                    try { await AddAsync(path, (await handovers.GetEpodAsync(handover.HandoverRecordId, "pdf", job.RequestedByAccountId, cancellationToken)).Content); }
                    catch (Exception error) when (error is not OperationCanceledException)
                    {
                        logger.LogWarning(error, "The e-POD of handover {HandoverId} could not be added to the audit package", handover.HandoverRecordId);
                        skipped.Add(new(path, "The e-POD could not be generated"));
                    }
                }

                foreach (var source in await SourceFilesAsync(job.TransportId, extras is not null, incidents, cancellationToken))
                {
                    try
                    {
                        var bytes = await FetchAsync(source.Url, cancellationToken);
                        var path = source.Path.StartsWith("incidents/", StringComparison.Ordinal) ? source.Path + Extension(bytes) : source.Path;
                        await AddAsync(path, bytes);
                    }
                    catch (Exception error) when (error is not OperationCanceledException)
                    {
                        logger.LogWarning(error, "A file could not be added to the audit package: {Path}", source.Path);
                        skipped.Add(new(source.Path, "The file could not be read from storage"));
                    }
                }

                await AddAsync("manifest.json", JsonSerializer.SerializeToUtf8Bytes(new
                {
                    job.ExportId, transport.TransportCode, job.Mode, job.Format, GeneratedAt = clock.GetUtcNow(), Files = manifest, Skipped = skipped
                }, PackageJson), listed: false);
            }
            file.Position = 0;
            return new(file, $"{stem}.zip", "application/zip");
        }
        catch
        {
            await file.DisposeAsync();
            throw;
        }
    }

    private static string Extension(byte[] bytes) =>
        bytes.AsSpan().StartsWith(new byte[] { 0xff, 0xd8, 0xff }) ? ".jpg" : bytes.AsSpan().StartsWith(new byte[] { 0x89, 0x50, 0x4e, 0x47 }) ? ".png" : bytes.AsSpan().StartsWith("%PDF-"u8) ? ".pdf" : ".bin";

    private async Task<InsuranceExtras> InsuranceExtrasAsync(Guid transportId, CancellationToken cancellationToken)
    {
        var claims = await db.Claims.AsNoTracking().Where(item => item.HandoverRecord.TransportId == transportId).OrderBy(item => item.SubmittedAt).ThenBy(item => item.ClaimId)
            .Select(item => new
            {
                item.ClaimCode, item.ClaimType, item.Status, item.Title, item.Description, item.EvidenceNote, item.CustomerNote, item.RequestedCompensationAmount, item.Currency, item.SubmittedAt,
                item.ResolvedAt, item.CancelledAt, item.CancellationReason,
                Resolution = item.ClaimResolution == null ? null : new { Type = item.ClaimResolution.ResolutionType, item.ClaimResolution.ResolutionSummary, item.ClaimResolution.ApprovedCompensationAmount }
            }).ToListAsync(cancellationToken);
        var incidents = await db.IncidentReports.AsNoTracking().Where(item => item.TransportId == transportId).OrderBy(item => item.OccurredAt).ThenBy(item => item.IncidentCode)
            .Select(item => new
            {
                item.IncidentCode, item.Category, item.Severity, item.Status, item.Title, item.Description, item.ImmediateAction, item.ResolutionSummary, item.OccurredAt, item.ResolvedAt
            }).ToListAsync(cancellationToken);
        return new(claims.Cast<object>().ToList(), incidents.Cast<object>().ToList());
    }

    // ------------------------------------------------------------------ PDF

    private async Task<Package> BuildPdfAsync(ExportJob job, Response.AuditResponse audit, InsuranceExtras? extras, string stem, CancellationToken cancellationToken)
    {
        var exporter = await db.Employees.AsNoTracking().Where(item => item.AccountId == job.RequestedByAccountId).Select(item => item.FullName).SingleOrDefaultAsync(cancellationToken) ?? "Unknown";
        // The seal carries the hash of the data the pages were made from; a hash of the PDF itself cannot sit inside the PDF.
        var contentHash = Hash(JsonSerializer.SerializeToUtf8Bytes(new { audit, extras }, PackageJson));
        var generatedAt = clock.GetUtcNow();
        var tables = AuditTables(audit, extras);
        tables.Add(new("Seal", ["Field", "Value"],
        [
            ["Exported at", Show(generatedAt)], ["Exported by", exporter], ["Export id", job.ExportId], ["Mode", job.Mode], ["Content SHA-256", contentHash]
        ]));
        var bytes = Pdf(new($"Audit package {job.TransportCode}", $"{job.Mode} - generated {Show(generatedAt)}", tables));
        return new(new MemoryStream(bytes), $"{stem}.pdf", "application/pdf");
    }

    private static List<ReportTable> AuditTables(Response.AuditResponse audit, InsuranceExtras? extras)
    {
        object? Time(DateTimeOffset? value) => value is { } at ? Show(at) : null;
        var transport = audit.Transport;
        var tables = new List<ReportTable>
        {
            new("Transport", ["Field", "Value"],
            [
                ["Code", transport.TransportCode], ["Status", transport.Status], ["Departed", Time(transport.ActualDepartureAt)], ["Arrived", Time(transport.ActualArrivalAt)], ["Completed", Time(transport.CompletedAt)]
            ])
        };
        if (audit.Booking is { } booking)
        {
            tables.Add(new("Booking", ["Field", "Value"],
            [
                ["Code", booking.BookingCode], ["Status", booking.Status], ["Customer", booking.Customer.Name], ["Route", $"{booking.OriginCountry} to {booking.DestinationCountry}"]
            ]));
            tables.Add(new("Invoices", ["Invoice", "Type", "Status", "Total", "Currency"],
                booking.Invoices.Select(item => new object?[] { item.InvoiceNumber, item.Type, item.Status, item.TotalAmount, item.Currency }).ToList()));
            tables.Add(new("Claims", ["Claim", "Status"], booking.Claims.Select(item => new object?[] { item.ClaimCode, item.Status }).ToList()));
        }
        if (audit.Horses is { } horses)
        {
            tables.Add(new("Horses", ["Name", "Microchip", "Passport", "Weight (kg)", "Height (cm)"],
                horses.Select(item => new object?[] { item.Name, item.MicrochipNumber, item.PassportNumber, item.WeightKg, item.HeightCm }).ToList()));
            tables.Add(new("Health logs", ["Horse", "Checked at", "Type", "Status", "Temperature", "Heart rate", "Respiratory rate", "Veterinarian note"],
                horses.SelectMany(horse => horse.HealthLogs.Select(log => new object?[] { horse.Name, Show(log.CheckedAt), log.LogType, log.HealthStatus, log.TemperatureCelsius, log.HeartRateBpm,
                    log.RespiratoryRateBpm, log.VeterinarianNote })).ToList()));
        }
        if (audit.Routing is { } routing)
            tables.Add(new("Routing", ["Plan", "Leg", "Type", "From", "To", "Executed by", "Departed", "Arrived"],
                new[] { routing.Primary }.Concat(routing.Alternatives).OfType<Response.RoutePlanItem>().SelectMany(plan => plan.Segments.Select(leg => new object?[] { plan.RoutePlanCode, leg.SequenceNo, leg.RouteType, leg.From, leg.To,
                    leg.ExecutedBy, Time(leg.ActualDepartureAt), Time(leg.ActualArrivalAt) })).ToList()));
        if (audit.Compliance is { } compliance)
            tables.Add(new("Compliance documents", ["Dossier", "Requirement", "Document", "Status", "Number", "Authority", "Expires"],
                compliance.Dossiers.SelectMany(dossier => dossier.Requirements.Select(item => new object?[] { dossier.DossierCode, item.RequirementName, item.Document?.DocumentName, item.Document?.Status ?? item.Status,
                    item.Document?.DocumentNumber, item.Document?.IssuingAuthority, item.Document?.ExpiryDate?.ToString("yyyy-MM-dd") })).ToList()));
        if (audit.Operations is { } operations)
        {
            tables.Add(new("Handovers", ["Handover", "Type", "Status", "Completed"], operations.Handovers.Select(item => new object?[] { item.HandoverCode, item.HandoverType, item.Status, Time(item.CompletedAt) }).ToList()));
            tables.Add(new("Incidents", ["Incident", "Category", "Severity", "Status", "Occurred"],
                operations.Incidents.Select(item => new object?[] { item.IncidentCode, item.Category, item.Severity, item.Status, Show(item.OccurredAt) }).ToList()));
            tables.Add(new("Audit records", ["Action", "Target", "Summary", "Visibility", "At"],
                operations.AuditDossiers.Select(item => new object?[] { item.ActionType, item.TargetEntity, item.Summary, item.Visibility, Show(item.CreatedAt) }).ToList()));
        }
        if (audit.Finance is { } finance)
        {
            tables.Add(new("Payments", ["Invoice", "Receipt", "Status", "Method", "Amount", "Confirmed"],
                finance.Invoices.SelectMany(invoice => invoice.Receipts.Select(receipt => new object?[] { invoice.InvoiceNumber, receipt.ReceiptCode, receipt.Status, receipt.PaymentMethod, receipt.Amount,
                    Time(receipt.ConfirmedAt) })).ToList()));
            tables.Add(new("Financial report", ["Field", "Value"], finance.FinancialReport is { } report
                ? [["Code", report.ReportCode], ["Invoiced", report.TotalInvoicedAmount], ["Paid", report.TotalPaidAmount], ["Emergency cost", report.TotalEmergencyCost], ["Outstanding", report.OutstandingAmount]]
                : []));
            tables.Add(new("Emergency costs", ["Request", "Incident", "Category", "Amount"], finance.EmergencyCosts.Select(item => new object?[] { item.RequestCode, item.IncidentCode, item.Category, item.ApprovedAmount }).ToList()));
        }
        tables.Add(new("Timeline", ["At", "Source", "Event"], audit.Timeline.Select(item => new object?[] { Show(item.At), item.Source, item.Title }).ToList()));
        if (extras is not null)
        {
            tables.Add(new("Claims (full)", ["Detail"], extras.Claims.Select(item => new object?[] { JsonSerializer.Serialize(item) }).ToList()));
            tables.Add(new("Incidents (full)", ["Detail"], extras.Incidents.Select(item => new object?[] { JsonSerializer.Serialize(item) }).ToList()));
        }
        return tables;
    }
}
