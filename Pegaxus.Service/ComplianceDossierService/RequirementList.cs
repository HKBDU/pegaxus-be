using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;

using static Pegaxus.Service.Common.DbTime;

namespace Pegaxus.Service.ComplianceDossierService;

public sealed partial class Service
{
    public async Task<Response.RequirementListResponse> GetRequirementsAsync(Guid accountId, Guid dossierId,
        Request.RequirementListRequest request, CancellationToken cancellationToken = default)
    {
        await ViewableDossierAsync(accountId, dossierId, cancellationToken);
        if (request.PageIndex < 1 || request.PageSize is < 1 or > 100 || (long)(request.PageIndex - 1) * request.PageSize > int.MaxValue)
            throw new BadRequestException("PageIndex must be positive and PageSize must be between 1 and 100.");
        if (request.HorseId == Guid.Empty)
            throw new BadRequestException("HorseId must not be empty.");
        var query = _db.DossierRequirements.AsNoTracking().Where(item => item.ComplianceDossierId == dossierId && !item.IsDeleted);
        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            if (!EnumParser.TryParseDefined<DossierRequirementStatus>(request.Status.Trim(), out var status) || !Enum.IsDefined(status))
                throw new BadRequestException("Invalid DossierRequirement status filter", "INVALID_REQUIREMENT_STATUS");
            query = query.Where(item => item.Status == status);
        }
        if (request.HorseId.HasValue)
            query = query.Where(item => item.HorseId == request.HorseId);
        IOrderedQueryable<DossierRequirement> ordered = request.SortBy?.Trim().ToLowerInvariant() switch
        {
            null or "createdat:asc" => query.OrderBy(item => item.CreatedAt),
            "duedate:asc" => query.OrderBy(item => item.DueDate),
            "duedate:desc" => query.OrderByDescending(item => item.DueDate),
            "status:asc" => query.OrderBy(item => item.Status),
            "status:desc" => query.OrderByDescending(item => item.Status),
            _ => throw new BadRequestException("The sort parameter is invalid", "INVALID_SORT_PARAMETER")
        };
        var total = await query.CountAsync(cancellationToken);
        var items = await ordered.ThenBy(item => item.DossierRequirementId).Skip(Paging.Offset(request.PageIndex, request.PageSize)).Take(request.PageSize)
            .Select(item => new Response.RequirementListItemResponse
            {
                DossierRequirementId = item.DossierRequirementId, ComplianceRequirementId = item.ComplianceRequirementId,
                RequirementName = item.ComplianceRequirement.RequirementName, HorseId = item.HorseId, HorseName = item.Horse == null ? null : item.Horse.HorseName,
                Status = item.Status.ToString(), IsMandatory = item.IsMandatory, DueDate = item.DueDate, ValidUntil = item.ValidUntil,
                SatisfiedAt = AsUtc(item.SatisfiedAt), WaivedReason = item.WaivedReason, Note = item.Note,
                Document = item.ComplianceDocument == null ? null : new Response.RequirementDocumentResponse
                {
                    ComplianceDocumentId = item.ComplianceDocument.ComplianceDocumentId, DocumentCode = item.ComplianceDocument.DocumentCode,
                    Status = item.ComplianceDocument.Status.ToString(), LatestVersionNo = item.ComplianceDocument.DocumentVersions.Max(version => (int?)version.VersionNo)
                }
            }).ToListAsync(cancellationToken);
        return new Response.RequirementListResponse
        {
            Items = items, PageIndex = request.PageIndex, PageSize = request.PageSize, TotalItems = total, TotalPages = Paging.TotalPages(total, request.PageSize)
        };
    }
}
