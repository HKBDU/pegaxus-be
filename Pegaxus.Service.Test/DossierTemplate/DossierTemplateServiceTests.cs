using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.DossierTemplateService.Validator;
using Pegaxus.Service.Exceptions;

using TemplateRequest = Pegaxus.Service.DossierTemplateService.Request;
using TemplateService = Pegaxus.Service.DossierTemplateService.Service;

namespace Pegaxus.Service.Test;

public class DossierTemplateServiceTests
{
    [Fact]
    public async Task Lifecycle_SavesItemsInOrder_IncrementsVersion_AndSoftDeletes()
    {
        await using var db = TestSupport.CreateDb();
        var (account, _) = await TestSupport.AddLogisticsManagerAsync(db);
        var request = await CreateRequestAsync(db);
        request.Status = "ACTIVE";
        var service = new TemplateService(db);

        var created = await service.CreateAsync(request);
        Assert.Equal("ACTIVE", created.Status);
        Assert.Equal(1, created.VersionNo);
        Assert.Equal(new[] { 1, 2, 3 }, created.Items.Select(item => item.SequenceNo));
        Assert.Equal(3, Assert.Single((await service.GetListAsync(new())).Items).ItemCount);
        Assert.True(created.CanDelete);

        request.Items.Reverse();
        request.Items[0].SequenceNo = 2;
        request.Items[1].SequenceNo = 1;
        request.Items.RemoveAt(2);
        request.Items[0].IsMandatory = false;
        request.TemplateName = "Updated template";
        var updated = await service.UpdateAsync(created.DossierTemplateId, request);
        Assert.Equal(2, updated.VersionNo);
        Assert.Equal(2, updated.Items.Count);
        Assert.False(updated.Items[1].IsMandatory);
        Assert.Equal(2, await db.DossierTemplateItems.CountAsync());

        await service.DeleteAsync(created.DossierTemplateId, account.AccountId);
        var deleted = await db.DossierTemplates.SingleAsync();
        Assert.True(deleted.IsDeleted);
        Assert.Equal(DossierTemplateStatus.INACTIVE, deleted.Status);
        Assert.Equal(account.AccountId, deleted.DeletedByAccountId);
        Assert.NotNull(deleted.DeletedAt);
        Assert.Empty((await service.GetListAsync(new())).Items);
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetAsync(created.DossierTemplateId));
        await Assert.ThrowsAsync<ConflictException>(() => service.CreateAsync(request));
    }

    [Fact]
    public async Task UsedTemplate_CannotBeDeleted_AndUpdatingDoesNotChangeDossierRequirements()
    {
        await using var db = TestSupport.CreateDb();
        var request = await CreateRequestAsync(db);
        var service = new TemplateService(db);
        var template = await service.CreateAsync(request);
        var dossier = new ComplianceDossier
        {
            ComplianceDossierId = Guid.NewGuid(),
            DossierCode = "DOS-01",
            DossierTemplateId = template.DossierTemplateId,
            DossierRequirements = new List<DossierRequirement>
            {
                new() { DossierRequirementId = Guid.NewGuid(), ComplianceRequirementId = request.Items[0].ComplianceRequirementId,
                    Instruction = "Historical instruction", IsMandatory = true }
            }
        };
        db.ComplianceDossiers.Add(dossier);
        await db.SaveChangesAsync();

        Assert.False((await service.GetAsync(template.DossierTemplateId)).CanDelete);
        var error = await Assert.ThrowsAsync<ConflictException>(() => service.DeleteAsync(template.DossierTemplateId, Guid.NewGuid()));
        Assert.Equal("Cannot delete this DossierTemplate because it is currently linked to existing ComplianceDossiers. Please change its status to INACTIVE instead.", error.Message);
        request.Items[0].Instruction = "New instruction";
        await service.UpdateAsync(template.DossierTemplateId, request);
        Assert.Equal("Historical instruction", (await db.DossierRequirements.SingleAsync()).Instruction);
        Assert.False((await db.DossierTemplates.SingleAsync()).IsDeleted);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("null-items")]
    [InlineData("null-item")]
    [InlineData("duplicate-requirement")]
    [InlineData("duplicate-sequence")]
    [InlineData("zero-sequence")]
    [InlineData("missing-requirement")]
    [InlineData("inactive-requirement")]
    [InlineData("deleted-requirement")]
    [InlineData("reversed-dates")]
    [InlineData("expired-active")]
    [InlineData("future-active")]
    [InlineData("invalid-status")]
    [InlineData("numeric-status")]
    [InlineData("inactive-initial-status")]
    [InlineData("empty-code")]
    [InlineData("long-name")]
    public async Task InvalidSave_RejectsWithoutPersisting(string invalid)
    {
        await using var db = TestSupport.CreateDb();
        var request = await CreateRequestAsync(db);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        switch (invalid)
        {
            case "empty": request.Items.Clear(); break;
            case "null-items": request.Items = null!; break;
            case "null-item": request.Items[0] = null!; break;
            case "duplicate-requirement": request.Items[1].ComplianceRequirementId = request.Items[0].ComplianceRequirementId; break;
            case "duplicate-sequence": request.Items[1].SequenceNo = request.Items[0].SequenceNo; break;
            case "zero-sequence": request.Items[0].SequenceNo = 0; break;
            case "missing-requirement": request.Items[0].ComplianceRequirementId = Guid.NewGuid(); break;
            case "inactive-requirement": (await db.ComplianceRequirements.FirstAsync()).Status = ComplianceRequirementStatus.INACTIVE; break;
            case "deleted-requirement": (await db.ComplianceRequirements.FirstAsync()).IsDeleted = true; break;
            case "reversed-dates": request.EffectiveFrom = today; request.EffectiveTo = today.AddDays(-1); break;
            case "expired-active": request.Status = "ACTIVE"; request.EffectiveTo = today.AddDays(-1); break;
            case "future-active": request.Status = "ACTIVE"; request.EffectiveFrom = today.AddDays(1); break;
            case "invalid-status": request.Status = "UNKNOWN"; break;
            case "numeric-status": request.Status = "2"; break;
            case "inactive-initial-status": request.Status = "INACTIVE"; break;
            case "empty-code": request.TemplateCode = " "; break;
            case "long-name": request.TemplateName = new string('x', 201); break;
        }
        await db.SaveChangesAsync();

        if (invalid is not ("missing-requirement" or "inactive-requirement" or "deleted-requirement" or
            "expired-active" or "future-active" or "inactive-initial-status"))
            Assert.False(new SaveRequestValidator().Validate(request).IsValid);
        await Assert.ThrowsAsync<BadRequestException>(() => new TemplateService(db).CreateAsync(request));
        Assert.Empty(await db.DossierTemplates.ToListAsync());
        Assert.Empty(await db.DossierTemplateItems.ToListAsync());
    }

    [Fact]
    public async Task Activation_RevalidatesRequirementsAndDates()
    {
        await using var db = TestSupport.CreateDb();
        var request = await CreateRequestAsync(db);
        var service = new TemplateService(db);
        var template = await service.CreateAsync(request);
        var requirement = await db.ComplianceRequirements.FirstAsync();
        requirement.Status = ComplianceRequirementStatus.INACTIVE;
        await db.SaveChangesAsync();
        var activate = new TemplateRequest.ChangeStatusRequest { Status = "ACTIVE" };
        await Assert.ThrowsAsync<BadRequestException>(() => service.ChangeStatusAsync(template.DossierTemplateId, activate));

        requirement.Status = ComplianceRequirementStatus.ACTIVE;
        var entity = await db.DossierTemplates.SingleAsync();
        entity.EffectiveTo = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<BadRequestException>(() => service.ChangeStatusAsync(template.DossierTemplateId, activate));
        entity.EffectiveFrom = entity.EffectiveTo = DateOnly.FromDateTime(DateTime.UtcNow);
        await db.SaveChangesAsync();

        Assert.Equal("ACTIVE", (await service.ChangeStatusAsync(template.DossierTemplateId, activate)).Status);
        Assert.Single((await service.GetListAsync(new() { Status = "ACTIVE" })).Items);
        Assert.Equal("INACTIVE", (await service.ChangeStatusAsync(template.DossierTemplateId,
            new() { Status = "INACTIVE" })).Status);
        Assert.Empty((await service.GetListAsync(new() { Status = "ACTIVE" })).Items);
    }

    [Fact]
    public void Validators_CheckPagingStatusAndValidSave()
    {
        var listValidator = new ListRequestValidator();
        Assert.True(listValidator.Validate(new TemplateRequest.ListRequest()).IsValid);
        Assert.False(listValidator.Validate(new TemplateRequest.ListRequest { PageIndex = 0 }).IsValid);
        Assert.False(listValidator.Validate(new TemplateRequest.ListRequest { PageSize = 101 }).IsValid);
        Assert.False(listValidator.Validate(new TemplateRequest.ListRequest { PageIndex = int.MaxValue, PageSize = 100 }).IsValid);
        Assert.False(listValidator.Validate(new TemplateRequest.ListRequest { Status = "2" }).IsValid);

        var statusValidator = new ChangeStatusRequestValidator();
        Assert.True(statusValidator.Validate(new TemplateRequest.ChangeStatusRequest { Status = " active " }).IsValid);
        Assert.False(statusValidator.Validate(new TemplateRequest.ChangeStatusRequest { Status = null! }).IsValid);
        Assert.False(statusValidator.Validate(new TemplateRequest.ChangeStatusRequest { Status = "2" }).IsValid);
        Assert.True(new SaveRequestValidator().Validate(new TemplateRequest.SaveRequest
        {
            TemplateCode = "DT-01", TemplateName = "Template",
            Items = [new() { ComplianceRequirementId = Guid.NewGuid(), SequenceNo = 1 }]
        }).IsValid);
    }

    private static async Task<TemplateRequest.SaveRequest> CreateRequestAsync(Pegaxus.Repository.AppDbContext db)
    {
        var requirements = Enumerable.Range(1, 3).Select(index => new ComplianceRequirement
        {
            ComplianceRequirementId = Guid.NewGuid(), RequirementCode = $"CR-{index}",
            RequirementName = $"Requirement {index}", Status = ComplianceRequirementStatus.ACTIVE
        }).ToList();
        db.ComplianceRequirements.AddRange(requirements);
        await db.SaveChangesAsync();
        return new TemplateRequest.SaveRequest
        {
            TemplateCode = "DT-UK-EU-01", TemplateName = "UK-to-EU template",
            Items = requirements.Select((requirement, index) => new TemplateRequest.ItemRequest
            {
                ComplianceRequirementId = requirement.ComplianceRequirementId, SequenceNo = index + 1,
                IsMandatory = true, Instruction = $"Instruction {index + 1}"
            }).ToList()
        };
    }
}
