using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Enum;
using Pegaxus.Repository.Extensions;
using Pegaxus.Service.Exceptions;

using static Pegaxus.Service.Common.DbTime;

namespace Pegaxus.Service.ComplianceDossierService;

public sealed partial class Service
{
    public async Task<IReadOnlyList<Response.QuarantineProofListItemResponse>> ListQuarantineProofsAsync(
        Guid accountId,
        Guid dossierId,
        string? status,
        CancellationToken cancellationToken = default)
    {
        var filter = ParseProofStatus<QuarantineProofStatus>(status, ToQuarantineStatusName);
        await EnsureProofReviewerAsync(accountId, dossierId, cancellationToken);

        var rows = await _db.QuarantineProofs
            .AsNoTracking()
            .Where(item => item.ComplianceDossierId == dossierId && (filter == null || item.Status == filter.Value))
            .OrderByDescending(item => item.SubmittedAt)
            .ThenBy(item => item.QuarantineProofId)
            .Select(item => new
            {
                item.QuarantineProofId, item.Status, item.BcpName, item.LocationId, item.InspectionAt,
                item.SubmittedByEmployeeId, SubmitterName = item.SubmittedByEmployee.FullName,
                item.SubmittedAt, item.IsFinalized, item.VerifiedAt
            })
            .ToListAsync(cancellationToken);

        return rows.Select(item => new Response.QuarantineProofListItemResponse
        {
            QuarantineProofId = item.QuarantineProofId,
            Status = ToQuarantineStatusName(item.Status),
            BcpName = item.BcpName,
            LocationId = item.LocationId,
            InspectionAt = AsUtc(item.InspectionAt),
            SubmittedBy = new Response.QuarantineEmployeeResponse { EmployeeId = item.SubmittedByEmployeeId, FullName = item.SubmitterName },
            SubmittedAt = AsUtc(item.SubmittedAt),
            IsFinalized = item.IsFinalized,
            VerifiedAt = AsUtc(item.VerifiedAt)
        }).ToList();
    }

    public async Task<IReadOnlyList<Response.CustomsClearanceProofListItemResponse>> ListCustomsClearanceProofsAsync(
        Guid accountId,
        Guid dossierId,
        string? status,
        CancellationToken cancellationToken = default)
    {
        var filter = ParseProofStatus<CustomsClearanceProofStatus>(status, ToCustomsStatusName);
        await EnsureProofReviewerAsync(accountId, dossierId, cancellationToken);

        var rows = await _db.CustomsClearanceProofs
            .AsNoTracking()
            .Where(item => item.ComplianceDossierId == dossierId && (filter == null || item.Status == filter.Value))
            .OrderByDescending(item => item.SubmittedAt)
            .ThenBy(item => item.CustomsClearanceProofId)
            .Select(item => new
            {
                item.CustomsClearanceProofId, item.Status, item.CustomsPortName, item.LocationId,
                item.SubmittedByEmployeeId, SubmitterName = item.SubmittedByEmployee.FullName,
                item.SubmittedAt, item.IsFinalized, item.VerifiedAt
            })
            .ToListAsync(cancellationToken);

        return rows.Select(item => new Response.CustomsClearanceProofListItemResponse
        {
            CustomsClearanceProofId = item.CustomsClearanceProofId,
            Status = ToCustomsStatusName(item.Status),
            CustomsPortName = item.CustomsPortName,
            LocationId = item.LocationId,
            SubmittedBy = new Response.QuarantineEmployeeResponse { EmployeeId = item.SubmittedByEmployeeId, FullName = item.SubmitterName },
            SubmittedAt = AsUtc(item.SubmittedAt),
            IsFinalized = item.IsFinalized,
            VerifiedAt = AsUtc(item.VerifiedAt)
        }).ToList();
    }

    /// <summary>Proof lists are a review queue: only active Transport Specialists and Logistics Managers read them.</summary>
    private async Task EnsureProofReviewerAsync(Guid accountId, Guid dossierId, CancellationToken cancellationToken)
    {
        var isReviewer = await _db.Employees.Active().AnyAsync(employee => employee.AccountId == accountId &&
            (employee.Role == EmployeeRole.TRANSPORT_SPECIALIST || employee.Role == EmployeeRole.LOGISTICS_MANAGER), cancellationToken);
        if (!isReviewer)
            throw new ForbiddenException("Only an active Transport Specialist or Logistics Manager can list proofs");

        if (!await _db.ComplianceDossiers.AnyAsync(item => item.ComplianceDossierId == dossierId, cancellationToken))
            throw new NotFoundException("Compliance dossier not found", "DOSSIER_NOT_FOUND");
    }

    /// <summary>Matches the status names the proof endpoints return (for example <c>PENDING_VERIFICATION</c>).</summary>
    private static TStatus? ParseProofStatus<TStatus>(string? status, Func<TStatus, string> name) where TStatus : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(status)) return null;
        var value = status.Trim().ToUpperInvariant();
        foreach (var item in Enum.GetValues<TStatus>())
            if (name(item) == value) return item;
        throw new BadRequestException("Proof status is invalid", "INVALID_PROOF_STATUS");
    }
}
