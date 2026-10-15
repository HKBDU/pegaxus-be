using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Repository.Extensions;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;

using static Pegaxus.Service.Common.DbTime;

namespace Pegaxus.Service.DossierTemplateService;

public sealed class Service(AppDbContext db) : IDossierTemplateService
{
    private const string DuplicateCode = "Template code already exists. Please use a unique template code.";
    private const string DuplicateItem = "Duplicate ComplianceRequirement or Sequence Number detected in the same template. Each requirement and sequence number must be unique within a DossierTemplate.";
    private const string UsedTemplate = "Cannot delete this DossierTemplate because it is currently linked to existing ComplianceDossiers. Please change its status to INACTIVE instead.";

    public async Task<Response.ListResponse> GetListAsync(Request.ListRequest request, CancellationToken cancellationToken = default)
    {
        if (request.PageIndex < 1 || request.PageSize is < 1 or > 100 ||
            (long)(request.PageIndex - 1) * request.PageSize > int.MaxValue)
            throw new BadRequestException("PageIndex must be positive and PageSize must be between 1 and 100.");

        var query = db.DossierTemplates.AsNoTracking().Where(template => !template.IsDeleted);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToLowerInvariant();
            query = query.Where(template => template.TemplateCode.ToLower().Contains(search) || template.TemplateName.ToLower().Contains(search));
        }
        if (request.Status is not null)
        {
            var status = ParseStatus(request.Status);
            query = query.Where(template => template.Status == status);
            if (status == DossierTemplateStatus.ACTIVE)
            {
                var today = DateOnly.FromDateTime(DateTime.UtcNow);
                query = query.Where(template => (template.EffectiveFrom == null || template.EffectiveFrom <= today) &&
                    (template.EffectiveTo == null || template.EffectiveTo >= today));
            }
        }

        var count = await query.CountAsync(cancellationToken);
        var items = await Project(query.OrderBy(template => template.TemplateCode).ThenBy(template => template.DossierTemplateId)
                .Skip((request.PageIndex - 1) * request.PageSize).Take(request.PageSize))
            .ToListAsync(cancellationToken);
        return new Response.ListResponse
        {
            Items = items, PageIndex = request.PageIndex, PageSize = request.PageSize,
            TotalItems = count, TotalPages = Paging.TotalPages(count, request.PageSize)
        };
    }

    public async Task<Response.TemplateResponse> GetAsync(Guid templateId, CancellationToken cancellationToken = default) =>
        await Project(db.DossierTemplates.AsNoTracking().Where(template => !template.IsDeleted && template.DossierTemplateId == templateId))
            .SingleOrDefaultAsync(cancellationToken)
        ?? throw new NotFoundException("DossierTemplate not found.", "DOSSIER_TEMPLATE_NOT_FOUND");

    public async Task<Response.TemplateResponse> CreateAsync(Request.SaveRequest request, CancellationToken cancellationToken = default)
    {
        var status = ParseStatus(request.Status);
        if (status == DossierTemplateStatus.INACTIVE)
            throw new BadRequestException("Initial status must be DRAFT or ACTIVE.");
        await using var transaction = await db.Database.BeginSerializableAsync(cancellationToken);
        await ValidateSaveAsync(request, status, null, cancellationToken);
        var now = UtcNow();
        var template = new DossierTemplate
        {
            DossierTemplateId = Guid.NewGuid(), VersionNo = 1, CreatedAt = now, UpdatedAt = now
        };
        Apply(template, request, status);
        db.DossierTemplates.Add(template);
        await SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(template.DossierTemplateId, cancellationToken);
    }

    public async Task<Response.TemplateResponse> UpdateAsync(Guid templateId, Request.SaveRequest request, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginSerializableAsync(cancellationToken);
        var template = await FindAsync(templateId, cancellationToken);
        var status = ParseStatus(request.Status);
        ValidateTransition(template.Status, status);
        await ValidateSaveAsync(request, status, templateId, cancellationToken);

        template.VersionNo++;
        template.UpdatedAt = UtcNow();
        // Delete before inserting so swapping sequence numbers cannot violate the unique indexes.
        db.DossierTemplateItems.RemoveRange(template.DossierTemplateItems);
        await SaveAsync(cancellationToken);
        template.DossierTemplateItems.Clear();
        Apply(template, request, status);
        db.DossierTemplateItems.AddRange(template.DossierTemplateItems);
        await SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(templateId, cancellationToken);
    }

    public async Task<Response.TemplateResponse> ChangeStatusAsync(Guid templateId, Request.ChangeStatusRequest request, CancellationToken cancellationToken = default)
    {
        var template = await FindAsync(templateId, cancellationToken);
        var status = ParseStatus(request.Status);
        ValidateTransition(template.Status, status);
        if (status == DossierTemplateStatus.ACTIVE)
        {
            ValidateDates(template.EffectiveFrom, template.EffectiveTo, status);
            await ValidateItemsAsync(template.DossierTemplateItems.Select(item => new Request.ItemRequest
            {
                ComplianceRequirementId = item.ComplianceRequirementId, SequenceNo = item.SequenceNo
            }).ToList(), cancellationToken);
        }
        if (template.Status != status)
        {
            template.Status = status;
            template.VersionNo++;
            template.UpdatedAt = UtcNow();
            await SaveAsync(cancellationToken);
        }
        return await GetAsync(templateId, cancellationToken);
    }

    public async Task DeleteAsync(Guid templateId, Guid accountId, CancellationToken cancellationToken = default)
    {
        var template = await FindAsync(templateId, cancellationToken);
        if (await db.ComplianceDossiers.AnyAsync(dossier => dossier.DossierTemplateId == templateId, cancellationToken))
            throw new ConflictException(UsedTemplate, "DOSSIER_TEMPLATE_IN_USE");
        template.IsDeleted = true;
        template.Status = DossierTemplateStatus.INACTIVE;
        template.DeletedAt = template.UpdatedAt = UtcNow();
        template.DeletedByAccountId = accountId;
        template.VersionNo++;
        await SaveAsync(cancellationToken);
    }

    private async Task<DossierTemplate> FindAsync(Guid templateId, CancellationToken cancellationToken) =>
        await db.DossierTemplates.Include(template => template.DossierTemplateItems)
            .SingleOrDefaultAsync(template => !template.IsDeleted && template.DossierTemplateId == templateId, cancellationToken)
        ?? throw new NotFoundException("DossierTemplate not found.", "DOSSIER_TEMPLATE_NOT_FOUND");

    private async Task ValidateSaveAsync(Request.SaveRequest request, DossierTemplateStatus status, Guid? templateId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.TemplateCode) || request.TemplateCode.Trim().Length > 50)
            throw new BadRequestException("TemplateCode is required and must not exceed 50 characters.");
        if (string.IsNullOrWhiteSpace(request.TemplateName) || request.TemplateName.Trim().Length > 200)
            throw new BadRequestException("TemplateName is required and must not exceed 200 characters.");
        ValidateDates(request.EffectiveFrom, request.EffectiveTo, status);
        var code = request.TemplateCode.Trim();
        if (await db.DossierTemplates.AnyAsync(template => template.TemplateCode == code && template.DossierTemplateId != templateId, cancellationToken))
            throw new ConflictException(DuplicateCode, "DUPLICATE_TEMPLATE_CODE");
        await ValidateItemsAsync(request.Items, cancellationToken);
    }

    private async Task ValidateItemsAsync(List<Request.ItemRequest>? items, CancellationToken cancellationToken)
    {
        if (items is null || items.Count == 0)
            throw new BadRequestException("DossierTemplate must contain at least one DossierTemplateItem.");
        if (items.Any(item => item is null || item.ComplianceRequirementId == Guid.Empty || item.SequenceNo <= 0))
            throw new BadRequestException("Each item must contain a ComplianceRequirement and a positive SequenceNo.");
        var ids = items.Select(item => item.ComplianceRequirementId).ToArray();
        if (ids.Distinct().Count() != items.Count || items.Select(item => item.SequenceNo).Distinct().Count() != items.Count)
            throw new BadRequestException(DuplicateItem, "DUPLICATE_TEMPLATE_ITEM");
        if (await db.ComplianceRequirements.CountAsync(requirement => ids.Contains(requirement.ComplianceRequirementId) &&
                !requirement.IsDeleted && requirement.Status == ComplianceRequirementStatus.ACTIVE, cancellationToken) != ids.Length)
            throw new BadRequestException("All template items must reference existing ACTIVE ComplianceRequirements.", "INVALID_COMPLIANCE_REQUIREMENT");
    }

    private static void ValidateDates(DateOnly? from, DateOnly? to, DossierTemplateStatus status)
    {
        if (from > to)
            throw new BadRequestException("EffectiveTo must be on or after EffectiveFrom.");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (status == DossierTemplateStatus.ACTIVE && (from > today || to < today))
            throw new BadRequestException("An ACTIVE DossierTemplate must be effective today.");
    }

    private static DossierTemplateStatus ParseStatus(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        "DRAFT" => DossierTemplateStatus.DRAFT,
        "ACTIVE" => DossierTemplateStatus.ACTIVE,
        "INACTIVE" => DossierTemplateStatus.INACTIVE,
        _ => throw new BadRequestException("Status must be DRAFT, ACTIVE or INACTIVE.")
    };

    private static void ValidateTransition(DossierTemplateStatus current, DossierTemplateStatus next)
    {
        if (current != next && (current, next) is not
            ((DossierTemplateStatus.DRAFT, DossierTemplateStatus.ACTIVE) or
             (DossierTemplateStatus.ACTIVE, DossierTemplateStatus.INACTIVE)))
            throw new BadRequestException("Allowed status transitions are DRAFT to ACTIVE and ACTIVE to INACTIVE.", "INVALID_TEMPLATE_STATUS_TRANSITION");
    }

    private static void Apply(DossierTemplate template, Request.SaveRequest request, DossierTemplateStatus status)
    {
        template.TemplateCode = request.TemplateCode.Trim();
        template.TemplateName = request.TemplateName.Trim();
        template.Description = request.Description?.Trim();
        template.EffectiveFrom = request.EffectiveFrom;
        template.EffectiveTo = request.EffectiveTo;
        template.Status = status;
        foreach (var item in request.Items)
            template.DossierTemplateItems.Add(new DossierTemplateItem
            {
                DossierTemplateItemId = Guid.NewGuid(), DossierTemplateId = template.DossierTemplateId,
                ComplianceRequirementId = item.ComplianceRequirementId, SequenceNo = item.SequenceNo,
                IsMandatory = item.IsMandatory, AppliesPerHorse = item.AppliesPerHorse,
                Instruction = item.Instruction?.Trim(), CreatedAt = template.UpdatedAt, UpdatedAt = template.UpdatedAt
            });
    }

    private IQueryable<Response.TemplateResponse> Project(IQueryable<DossierTemplate> query) => query.Select(template => new Response.TemplateResponse
    {
        DossierTemplateId = template.DossierTemplateId, TemplateCode = template.TemplateCode,
        TemplateName = template.TemplateName, Description = template.Description, Status = template.Status.ToString(),
        VersionNo = template.VersionNo, EffectiveFrom = template.EffectiveFrom, EffectiveTo = template.EffectiveTo,
        CanDelete = !db.ComplianceDossiers.Any(dossier => dossier.DossierTemplateId == template.DossierTemplateId),
        CreatedAt = new DateTimeOffset(DateTime.SpecifyKind(template.CreatedAt, DateTimeKind.Utc)),
        UpdatedAt = new DateTimeOffset(DateTime.SpecifyKind(template.UpdatedAt, DateTimeKind.Utc)),
        Items = template.DossierTemplateItems.OrderBy(item => item.SequenceNo).Select(item => new Response.ItemResponse
        {
            DossierTemplateItemId = item.DossierTemplateItemId, ComplianceRequirementId = item.ComplianceRequirementId,
            RequirementCode = item.ComplianceRequirement.RequirementCode, RequirementName = item.ComplianceRequirement.RequirementName,
            SequenceNo = item.SequenceNo, IsMandatory = item.IsMandatory, AppliesPerHorse = item.AppliesPerHorse, Instruction = item.Instruction
        }).ToList()
    });

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("DossierTemplate changed. Reload it and try again.", "DOSSIER_TEMPLATE_CONFLICT");
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres &&
            postgres.ConstraintName == "DossierTemplate_template_code_key")
        {
            throw new ConflictException(DuplicateCode, "DUPLICATE_TEMPLATE_CODE");
        }
    }

}
