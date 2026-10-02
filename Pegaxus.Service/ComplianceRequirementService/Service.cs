using FluentValidation;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;

namespace Pegaxus.Service.ComplianceRequirementService;

public sealed class Service(AppDbContext db,
    IValidator<Request.ListRequest> listValidator,
    IValidator<Request.SaveRequest> saveValidator,
    IValidator<Request.StatusRequest> statusValidator) : IService
{
    private const string DuplicateMessage = "A ComplianceRequirement with this code already exists. Please use a unique code or edit the existing record.";
    private const string ReferencedMessage = "Cannot delete this ComplianceRequirement because it is currently linked to existing DossierTemplates or active ComplianceDossiers. Please deactivate it instead.";

    public async Task<Response.ListResponse> GetListAsync(Request.ListRequest request, CancellationToken cancellationToken = default)
    {
        Validate(request, listValidator);
        var query = db.ComplianceRequirements.AsNoTracking().Where(item => !item.IsDeleted);
        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            var status = ParseStatus(request.Status);
            query = query.Where(item => item.Status == status);
        }
        if (!string.IsNullOrWhiteSpace(request.Category))
        {
            var category = ParseCategory(request.Category);
            query = query.Where(item => item.Category == category);
        }
        if (!string.IsNullOrWhiteSpace(request.Country))
        {
            var country = request.Country.Trim().ToUpperInvariant();
            query = query.Where(item => item.Country != null && item.Country.ToUpper() == country);
        }
        if (!string.IsNullOrWhiteSpace(request.BcpCode))
        {
            var bcp = request.BcpCode.Trim().ToUpperInvariant();
            query = query.Where(item => item.BcpCode != null && item.BcpCode.ToUpper() == bcp);
        }
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToLowerInvariant();
            query = query.Where(item => item.RequirementCode.ToLower().Contains(search) || item.RequirementName.ToLower().Contains(search));
        }
        var ordered = request.SortBy?.Trim().ToLowerInvariant() switch
        {
            "updatedat:desc" => query.OrderByDescending(item => item.UpdatedAt),
            "updatedat:asc" => query.OrderBy(item => item.UpdatedAt),
            "requirementname:asc" => query.OrderBy(item => item.RequirementName),
            "requirementname:desc" => query.OrderByDescending(item => item.RequirementName),
            "requirementcode:asc" => query.OrderBy(item => item.RequirementCode),
            "requirementcode:desc" => query.OrderByDescending(item => item.RequirementCode),
            _ => throw new BadRequestException("SortBy must be updatedAt, requirementName or requirementCode followed by :asc or :desc")
        };
        var total = await query.CountAsync(cancellationToken);
        var offset = ((long)request.PageIndex - 1) * request.PageSize;
        var items = offset >= total ? [] : await ProjectDetails(ordered
            .ThenBy(item => item.ComplianceRequirementId).Skip((int)offset).Take(request.PageSize)).ToListAsync(cancellationToken);
        return new Response.ListResponse
        {
            Items = items, PageIndex = request.PageIndex, PageSize = request.PageSize,
            TotalItems = total, TotalPages = (int)Math.Ceiling(total / (double)request.PageSize)
        };
    }

    public async Task<Response.DetailResponse> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        await ProjectDetails(db.ComplianceRequirements.AsNoTracking().Where(item => item.ComplianceRequirementId == id && !item.IsDeleted))
            .SingleOrDefaultAsync(cancellationToken) ?? throw NotFound();

    public async Task<Response.DetailResponse> CreateAsync(Request.SaveRequest request, Guid actorAccountId, CancellationToken cancellationToken = default)
    {
        Validate(request, saveValidator);
        var entity = new ComplianceRequirement { ComplianceRequirementId = Guid.NewGuid(), CreatedAt = UtcNow() };
        await ApplyAsync(entity, request, cancellationToken);
        db.ComplianceRequirements.Add(entity);
        await SaveAsync(entity, "CREATE", actorAccountId, JsonSerializer.SerializeToDocument(new { }), null, cancellationToken);
        return await GetAsync(entity.ComplianceRequirementId, cancellationToken);
    }

    public async Task<Response.DetailResponse> UpdateAsync(Guid id, Request.SaveRequest request, Guid actorAccountId, CancellationToken cancellationToken = default)
    {
        Validate(request, saveValidator);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var entity = await GetForUpdateAsync(id, cancellationToken);
        var detail = await GetAsync(id, cancellationToken);
        if (detail.IsReferenced && !request.AcknowledgeReferenceImpact)
            throw new ReferenceImpactException(detail.ReferenceCount);
        var before = Snapshot(entity);
        await ApplyAsync(entity, request, cancellationToken);
        await SaveAsync(entity, "UPDATE", actorAccountId, before, detail.ReferenceCount, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task UpdateStatusAsync(Guid id, Request.StatusRequest request, Guid actorAccountId, CancellationToken cancellationToken = default)
    {
        Validate(request, statusValidator);
        var status = ParseStatus(request.Status);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var entity = await GetForUpdateAsync(id, cancellationToken);
        var before = Snapshot(entity);
        entity.Status = status;
        await SaveAsync(entity, "STATUS_CHANGED", actorAccountId, before, null, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, Guid actorAccountId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var entity = await GetForUpdateAsync(id, cancellationToken);
        // Include historical references, even requirements belonging to deleted dossiers.
        if (await db.DossierTemplateItems.IgnoreQueryFilters().AnyAsync(item => item.ComplianceRequirementId == id, cancellationToken) ||
            await db.DossierRequirements.IgnoreQueryFilters().AnyAsync(item => item.ComplianceRequirementId == id, cancellationToken))
            throw new ConflictException(ReferencedMessage, "REQUIREMENT_IN_USE");
        var before = Snapshot(entity);
        entity.IsDeleted = true;
        entity.DeletedAt = UtcNow();
        entity.DeletedByAccountId = actorAccountId;
        await SaveAsync(entity, "DELETE", actorAccountId, before, null, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<ComplianceRequirement> GetForUpdateAsync(Guid id, CancellationToken cancellationToken)
    {
        // Serialize mutations and wait for in-flight foreign-key inserts before checking references.
        var query = db.Database.IsNpgsql()
            ? db.ComplianceRequirements.FromSqlInterpolated($"SELECT * FROM \"ComplianceRequirement\" WHERE compliance_requirement_id = {id} FOR UPDATE")
            : db.ComplianceRequirements.AsQueryable();
        return await query.SingleOrDefaultAsync(item => item.ComplianceRequirementId == id && !item.IsDeleted, cancellationToken)
            ?? throw NotFound();
    }

    private async Task ApplyAsync(ComplianceRequirement entity, Request.SaveRequest request, CancellationToken cancellationToken)
    {
        var status = ParseStatus(request.Status);
        var category = ParseCategory(request.Category);
        var country = TrimOptional(request.Country)?.ToUpperInvariant();
        var code = request.RequirementCode.Trim().ToUpperInvariant();
        if (await db.ComplianceRequirements.IgnoreQueryFilters().AnyAsync(item =>
            item.ComplianceRequirementId != entity.ComplianceRequirementId && item.RequirementCode.ToUpper() == code, cancellationToken))
            throw new ConflictException(DuplicateMessage, "DUPLICATE_REQUIREMENT_CODE");
        entity.RequirementCode = code;
        entity.RequirementName = request.RequirementName.Trim();
        entity.Country = country;
        entity.Category = category;
        entity.Status = status;
        entity.BcpCode = TrimOptional(request.BcpCode)?.ToUpperInvariant();
        entity.IssuingAuthority = TrimOptional(request.IssuingAuthority);
        entity.Description = TrimOptional(request.Description);
        entity.LegalBasis = TrimOptional(request.LegalBasis);
        entity.ValidityPeriodDays = request.ValidityPeriodDays;
        entity.ValidityRequired = request.ValidityRequired!.Value;
    }

    private async Task SaveAsync(ComplianceRequirement entity, string action, Guid actor, JsonDocument before, Response.ReferenceCounts? references, CancellationToken cancellationToken)
    {
        entity.UpdatedAt = UtcNow();
        db.CatalogAuditLogs.Add(new CatalogAuditLog
        {
            CatalogAuditLogId = Guid.NewGuid(), EntityType = nameof(ComplianceRequirement),
            EntityId = entity.ComplianceRequirementId, ActionType = action, ActorAccountId = actor,
            BeforeData = before, AfterData = Snapshot(entity), CreatedAt = entity.UpdatedAt,
            ReferenceCountSnapshot = references is null ? null : JsonSerializer.SerializeToDocument(references)
        });
        try
        {
            // EF commits the catalog change and its audit entry together.
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "ComplianceRequirement_requirement_code_key" })
        {
            throw new ConflictException(DuplicateMessage, "DUPLICATE_REQUIREMENT_CODE");
        }
    }

    private IQueryable<Response.DetailResponse> ProjectDetails(IQueryable<ComplianceRequirement> query) => query.Select(item => new Response.DetailResponse
    {
        ComplianceRequirementId = item.ComplianceRequirementId,
        RequirementCode = item.RequirementCode, RequirementName = item.RequirementName,
        Status = item.Status.ToString(), Category = CategoryName(item.Category), Country = item.Country,
        BcpCode = item.BcpCode, IssuingAuthority = item.IssuingAuthority, Description = item.Description,
        LegalBasis = item.LegalBasis, ValidityPeriodDays = item.ValidityPeriodDays, ValidityRequired = item.ValidityRequired,
        ReferenceCount = new Response.ReferenceCounts
        {
            DossierTemplateItemCount = db.DossierTemplateItems.IgnoreQueryFilters().Count(reference => reference.ComplianceRequirementId == item.ComplianceRequirementId),
            DossierRequirementCount = db.DossierRequirements.IgnoreQueryFilters().Count(reference => reference.ComplianceRequirementId == item.ComplianceRequirementId),
            LinkedDossierCount = db.DossierRequirements.IgnoreQueryFilters().Where(reference => reference.ComplianceRequirementId == item.ComplianceRequirementId)
                .Select(reference => reference.ComplianceDossierId).Distinct().Count()
        },
        CreatedAt = ToUtcOffset(item.CreatedAt), UpdatedAt = ToUtcOffset(item.UpdatedAt)
    });

    private static JsonDocument Snapshot(ComplianceRequirement entity) => JsonSerializer.SerializeToDocument(new
    {
        entity.ComplianceRequirementId, entity.RequirementCode, entity.RequirementName,
        Status = entity.Status.ToString(), Category = CategoryName(entity.Category), entity.Country,
        entity.BcpCode, entity.IssuingAuthority, entity.Description, entity.LegalBasis,
        entity.ValidityPeriodDays, entity.ValidityRequired, entity.IsDeleted, entity.DeletedByAccountId,
        DeletedAt = entity.DeletedAt.HasValue ? ToUtcOffset(entity.DeletedAt.Value) : (DateTimeOffset?)null,
        CreatedAt = ToUtcOffset(entity.CreatedAt), UpdatedAt = ToUtcOffset(entity.UpdatedAt)
    });

    private static void Validate<T>(T request, IValidator<T> validator)
    {
        var result = validator.Validate(request);
        if (!result.IsValid)
            throw new BadRequestException(string.Join(" ", result.Errors.Select(error => error.ErrorMessage)));
    }

    private static ComplianceRequirementStatus ParseStatus(string value) => Enum.Parse<ComplianceRequirementStatus>(value.Trim(), true);

    private static ComplianceRequirementCategory ParseCategory(string value) => Enum.Parse<ComplianceRequirementCategory>(value.Trim().Replace("_", ""), true);

    private static string CategoryName(ComplianceRequirementCategory category) => category switch
    {
        ComplianceRequirementCategory.HorseHealth => "HORSE_HEALTH",
        ComplianceRequirementCategory.HorseIdentity => "HORSE_IDENTITY",
        _ => category.ToString().ToUpperInvariant()
    };

    private static string? TrimOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static NotFoundException NotFound() => new("ComplianceRequirement not found");
    private static DateTime UtcNow() => DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
    private static DateTimeOffset ToUtcOffset(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    public sealed class ReferenceImpactException(Response.ReferenceCounts referenceCount) : ConflictException(
        $"This ComplianceRequirement is linked to {referenceCount.LinkedDossierCount} dossiers and {referenceCount.DossierTemplateItemCount} template items. Editing it may affect active records. Confirm that you accept responsibility for this change.",
        "REFERENCE_IMPACT_CONFIRMATION_REQUIRED")
    {
        public Response.ReferenceCounts ReferenceCount { get; } = referenceCount;
    }
}
