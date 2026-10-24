using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Common;

using static Pegaxus.Service.Common.DbTime;

namespace Pegaxus.Service.AuditService;

public sealed partial class Service
{
    // ------------------------------------------------------------------ compliance

    private async Task<Response.ComplianceBlock> ComplianceBlockAsync(Guid transportId, Actor actor, CancellationToken cancellationToken)
    {
        var dossiers = await db.ComplianceDossiers.AsNoTracking().Where(item => item.TransportId == transportId)
            .OrderBy(item => item.DossierType).ThenBy(item => item.CreatedAt).ThenBy(item => item.ComplianceDossierId)
            .Select(item => new { item.ComplianceDossierId, item.DossierCode, item.DossierType, item.Status, item.OpenedAt, item.ApprovedAt }).ToListAsync(cancellationToken);
        var dossierIds = dossiers.Select(item => item.ComplianceDossierId).ToArray();
        var requirements = await db.DossierRequirements.AsNoTracking().Where(item => dossierIds.Contains(item.ComplianceDossierId) && !item.IsDeleted)
            .OrderBy(item => item.CreatedAt).ThenBy(item => item.DossierRequirementId)
            .Select(item => new { item.DossierRequirementId, item.ComplianceDossierId, item.ComplianceRequirement.RequirementName, item.HorseId, item.Status, item.IsMandatory })
            .ToListAsync(cancellationToken);
        var requirementIds = requirements.Select(item => item.DossierRequirementId).ToArray();
        var documents = db.ComplianceDocuments.AsNoTracking().Where(item => requirementIds.Contains(item.DossierRequirementId));
        // A customer sees the approved documents only.
        if (!actor.IsManager) documents = documents.Where(item => item.Status == ComplianceDocumentStatus.APPROVED);
        var documentRows = await documents.Select(item => new
        {
            item.ComplianceDocumentId, item.DossierRequirementId, item.DocumentCode, item.DocumentName, item.Status, item.DocumentNumber, item.IssuingAuthority, item.IssuedDate, item.ExpiryDate
        }).ToListAsync(cancellationToken);
        var documentIds = documentRows.Select(item => item.ComplianceDocumentId).ToArray();
        var versions = (await db.DocumentVersions.AsNoTracking().Where(item => documentIds.Contains(item.ComplianceDocumentId)).OrderBy(item => item.VersionNo)
                .Select(item => new { item.DocumentVersionId, item.ComplianceDocumentId, item.VersionNo, item.FileName, item.UploadedAt }).ToListAsync(cancellationToken))
            .ToLookup(item => item.ComplianceDocumentId);
        var versionIds = versions.SelectMany(group => group).Select(item => item.DocumentVersionId).ToArray();
        var reviews = actor.IsManager
            ? (await db.DocumentReviews.AsNoTracking().Where(item => versionIds.Contains(item.DocumentVersionId)).OrderBy(item => item.ReviewedAt)
                .Select(item => new { item.DocumentVersionId, item.Result, item.ReviewComment, item.ReviewedAt }).ToListAsync(cancellationToken)).ToLookup(item => item.DocumentVersionId)
            : null;

        // The veterinarian's notes live on the quarantine proof and the customs facts on the clearance proof; both are internal.
        var quarantine = actor.IsManager
            ? (await db.QuarantineProofs.AsNoTracking().Where(item => dossierIds.Contains(item.ComplianceDossierId)).OrderBy(item => item.CreatedAt)
                .Select(item => new { item.ComplianceDossierId, item.QuarantineProofId, item.Status, item.BcpName, item.InspectionAt, item.VerifiedAt, item.OfficialVeterinarianName, item.Conditions, item.VerificationNote })
                .ToListAsync(cancellationToken)).GroupBy(item => item.ComplianceDossierId).ToDictionary(group => group.Key, group => group.Last())
            : null;
        var customs = actor.IsManager
            ? (await db.CustomsClearanceProofs.AsNoTracking().Where(item => dossierIds.Contains(item.ComplianceDossierId)).OrderBy(item => item.CreatedAt)
                .Select(item => new { item.ComplianceDossierId, item.CustomsClearanceProofId, item.Status, item.CustomsPortName, item.MrnNumber, item.ClearanceAt, item.VerifiedAt, item.VerificationNote })
                .ToListAsync(cancellationToken)).GroupBy(item => item.ComplianceDossierId).ToDictionary(group => group.Key, group => group.Last())
            : null;

        Response.DocumentItem? Document(Guid requirementId)
        {
            var row = documentRows.FirstOrDefault(item => item.DossierRequirementId == requirementId);
            if (row is null) return null;
            var all = versions[row.ComplianceDocumentId].ToList();
            // A customer only needs to know the current version exists, not how it was reviewed.
            var shown = actor.IsManager ? all : all.TakeLast(1).ToList();
            return new(row.ComplianceDocumentId, row.DocumentCode, row.DocumentName, row.Status.ToString(), row.DocumentNumber, row.IssuingAuthority, row.IssuedDate, row.ExpiryDate,
                shown.Select(version => new Response.VersionItem(version.DocumentVersionId, version.VersionNo, version.FileName, AsUtc(version.UploadedAt),
                    reviews?[version.DocumentVersionId].Select(review => new Response.ReviewItem(review.Result.ToString(), review.ReviewComment, AsUtc(review.ReviewedAt))).ToList())).ToList());
        }

        return new(dossiers.Select(dossier =>
        {
            var items = requirements.Where(item => item.ComplianceDossierId == dossier.ComplianceDossierId)
                .Select(item => new Response.RequirementItem(item.DossierRequirementId, item.RequirementName, item.HorseId, item.Status.ToString(), item.IsMandatory, Document(item.DossierRequirementId)))
                .Where(item => actor.IsManager || item.Document is not null).ToList();
            var q = quarantine is not null && quarantine.TryGetValue(dossier.ComplianceDossierId, out var quarantineRow) ? quarantineRow : null;
            var c = customs is not null && customs.TryGetValue(dossier.ComplianceDossierId, out var customsRow) ? customsRow : null;
            return new Response.DossierItem(dossier.ComplianceDossierId, dossier.DossierCode, dossier.DossierType.ToString(), dossier.Status.ToString(), AsUtc(dossier.OpenedAt), AsUtc(dossier.ApprovedAt), items,
                q is null ? null : new(q.QuarantineProofId, q.Status.ToString(), q.BcpName, AsUtc(q.InspectionAt), AsUtc(q.VerifiedAt), q.OfficialVeterinarianName, q.Conditions, q.VerificationNote),
                c is null ? null : new(c.CustomsClearanceProofId, c.Status.ToString(), c.CustomsPortName, c.MrnNumber, AsUtc(c.ClearanceAt), AsUtc(c.VerifiedAt), c.VerificationNote));
        }).ToList());
    }

    // ------------------------------------------------------------------ operations

    private static Response.OperationsBlock OperationsBlock(List<HandoverRow> handovers, List<IncidentRow> incidents, List<AuditRow> audits) => new(
        handovers.Select(item => new Response.HandoverItem(item.Id, item.Code, item.Type.ToString(), item.Status.ToString(), AsUtc(item.CompletedAt))).ToList(),
        incidents.Select(item => new Response.IncidentItem(item.Id, item.Code, item.Category.ToString(), item.Severity.ToString(), item.Status.ToString(), AsUtc(item.OccurredAt))).ToList(),
        audits.Select(item => new Response.AuditDossierItem(item.Id, item.ActionType, item.TargetEntity, item.Summary, item.Visibility.ToString().ToUpperInvariant(), AsUtc(item.CreatedAt))).ToList());

    // ------------------------------------------------------------------ finance (managers only)

    private async Task<Response.FinanceBlock> FinanceBlockAsync(TransportRow transport, CancellationToken cancellationToken)
    {
        var invoices = await db.Invoices.AsNoTracking().Where(item => item.BookingId == transport.BookingId).OrderBy(item => item.CreatedAt).ThenBy(item => item.InvoiceId)
            .Select(item => new { item.InvoiceId, item.InvoiceCode, item.Type, item.Status, item.TotalAmount, item.Currency, item.IssuedAt, item.DueDate, item.PaidAt }).ToListAsync(cancellationToken);
        var invoiceIds = invoices.Select(item => item.InvoiceId).ToArray();
        var receipts = (await db.Receipts.AsNoTracking().Where(item => invoiceIds.Contains(item.InvoiceId)).OrderBy(item => item.CreatedAt).ThenBy(item => item.ReceiptId)
                .Select(item => new { item.InvoiceId, item.ReceiptId, item.ReceiptCode, item.Status, item.PaymentMethod, item.Amount, item.PaidAt, item.ConfirmedAt }).ToListAsync(cancellationToken))
            .ToLookup(item => item.InvoiceId);
        // Only the saved report is shown; reading an audit must not create or change one.
        var report = await db.FinancialReports.AsNoTracking().Where(item => item.TransportId == transport.TransportId)
            .Select(item => new { item.ReportCode, item.TotalInvoicedAmount, item.TotalPaidAmount, item.TotalEmergencyCost, item.OutstandingAmount, item.SummaryNote, item.GeneratedAt })
            .SingleOrDefaultAsync(cancellationToken);
        var costs = await db.EmergencyCostRequests.AsNoTracking().Where(item => item.Status == EmergencyCostStatus.APPROVED && item.IncidentReport.TransportId == transport.TransportId)
            .OrderBy(item => item.IncidentReport.IncidentCode).ThenBy(item => item.RequestCode)
            .Select(item => new { item.RequestCode, item.IncidentReport.IncidentCode, item.Category, item.ApprovedAmount }).ToListAsync(cancellationToken);
        return new(invoices.Select(item => new Response.FinanceInvoice(item.InvoiceId, item.InvoiceCode, item.Type.ToString(), item.Status.ToString(), Vnd.ToLong(item.TotalAmount), item.Currency,
                AsUtc(item.IssuedAt), AsUtc(item.DueDate), AsUtc(item.PaidAt),
                receipts[item.InvoiceId].Select(receipt => new Response.ReceiptItem(receipt.ReceiptId, receipt.ReceiptCode, receipt.Status.ToString(), receipt.PaymentMethod.ToString(),
                    Vnd.ToLong(receipt.Amount), AsUtc(receipt.PaidAt), AsUtc(receipt.ConfirmedAt))).ToList())).ToList(),
            report is null ? null : new(report.ReportCode, Vnd.ToLong(report.TotalInvoicedAmount), Vnd.ToLong(report.TotalPaidAmount), Vnd.ToLong(report.TotalEmergencyCost),
                Vnd.ToLong(report.OutstandingAmount), report.SummaryNote, AsUtc(report.GeneratedAt)),
            costs.Select(item => new Response.EmergencyCostItem(item.RequestCode, item.IncidentCode, item.Category.ToString(), Vnd.ToLong(item.ApprovedAmount ?? 0))).ToList());
    }

    // ------------------------------------------------------------------ timeline

    /// <summary>
    /// Everything that happened, oldest first. A transport has no status history, so its own milestones come from its time columns.
    /// The rows are already filtered for the role; a customer also gets no incident title, which can hold internal detail.
    /// </summary>
    private static List<Response.TimelineItem> Timeline(TransportRow transport, Actor actor, List<HandoverRow> handovers, List<IncidentRow> incidents, List<AuditRow> audits, int limit)
    {
        var items = new List<Response.TimelineItem>();
        void Milestone(DateTime? at, string title)
        {
            if (at is { } value) items.Add(new(AsUtc(value), "TRANSPORT", title, transport.TransportId, "PUBLIC"));
        }
        Milestone(transport.CreatedAt, "Transport created");
        Milestone(transport.ActualDepartureAt, "Transport departed");
        Milestone(transport.ActualArrivalAt, "Transport arrived");
        Milestone(transport.CompletedAt, "Transport completed");
        Milestone(transport.CancelledAt, "Transport cancelled");
        items.AddRange(handovers.Select(item => new Response.TimelineItem(AsUtc(item.At), "HANDOVER", $"Handover {item.Code}: {Pretty(item.Type.ToString())}, {Pretty(item.Status.ToString()).ToLowerInvariant()}",
            item.Id, item.Type is HandoverType.PICKUP or HandoverType.DELIVERY ? "PUBLIC" : "INTERNAL")));
        items.AddRange(incidents.Select(item => new Response.TimelineItem(AsUtc(item.OccurredAt), "INCIDENT",
            actor.IsManager ? $"Incident {item.Code}: {item.Title}" : $"Incident {item.Code} reported", item.Id,
            item.Severity is IncidentSeverity.HIGH or IncidentSeverity.CRITICAL ? "PUBLIC" : "INTERNAL")));
        items.AddRange(audits.Select(item => new Response.TimelineItem(AsUtc(item.CreatedAt), "AUDIT", item.Summary ?? $"{item.ActionType} on {item.TargetEntity}", item.Id,
            item.Visibility.ToString().ToUpperInvariant())));
        return items.OrderBy(item => item.At).ThenBy(item => item.Source, StringComparer.Ordinal).ThenBy(item => item.Title, StringComparer.Ordinal).Take(limit).ToList();
    }
}
