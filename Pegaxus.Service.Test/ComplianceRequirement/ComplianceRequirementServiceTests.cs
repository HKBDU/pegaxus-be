using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Entities;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Pegaxus.API.Controllers;
using Pegaxus.Service.Exceptions;
using CatalogRequest = Pegaxus.Service.ComplianceRequirementService.Request;
using CatalogService = Pegaxus.Service.ComplianceRequirementService.Service;
using Pegaxus.Service.ComplianceRequirementService.Validator;

namespace Pegaxus.Service.Test;

public class ComplianceRequirementServiceTests
{
    [Fact]
    public async Task CatalogLifecycle_PreservesHistoryAndHidesDeletedRequirements()
    {
        await using var db = TestSupport.CreateDb();
        var (actor, _) = await TestSupport.AddLogisticsManagerAsync(db);
        var service = CreateService(db);
        var request = ValidRequest();
        var created = await service.CreateAsync(request, actor.AccountId);
        Assert.Equal("CR-EHC-01", created.RequirementCode);
        Assert.Equal("ACTIVE", created.Status);
        Assert.Equal("QUARANTINE", created.Category);
        Assert.Equal("GB", created.Country);
        Assert.True(created.CanDelete);

        request.RequirementName = "Updated EHC";
        var updated = await service.UpdateAsync(created.ComplianceRequirementId, request, actor.AccountId);
        Assert.Equal("Updated EHC", updated.RequirementName);
        await service.UpdateStatusAsync(created.ComplianceRequirementId, new() { Status = "INACTIVE" }, actor.AccountId);
        Assert.Empty((await service.GetListAsync(new() { Status = "ACTIVE" })).Items);
        Assert.Single((await service.GetListAsync(new() { Status = "INACTIVE", Country = "gb", Category = "QUARANTINE" })).Items);

        await service.DeleteAsync(created.ComplianceRequirementId, actor.AccountId);
        var stored = await db.ComplianceRequirements.SingleAsync();
        Assert.True(stored.IsDeleted);
        Assert.NotNull(stored.DeletedAt);
        Assert.Equal(actor.AccountId, stored.DeletedByAccountId);
        Assert.Empty((await service.GetListAsync(new())).Items);
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetAsync(created.ComplianceRequirementId));
        await Assert.ThrowsAsync<NotFoundException>(() => service.UpdateAsync(created.ComplianceRequirementId, request, actor.AccountId));
        await Assert.ThrowsAsync<NotFoundException>(() => service.UpdateStatusAsync(created.ComplianceRequirementId, new() { Status = "ACTIVE" }, actor.AccountId));
        await Assert.ThrowsAsync<NotFoundException>(() => service.DeleteAsync(created.ComplianceRequirementId, actor.AccountId));

        var logs = await db.CatalogAuditLogs.OrderBy(log => log.CreatedAt).ToListAsync();
        Assert.Equal(new[] { "CREATE", "UPDATE", "STATUS_CHANGED", "DELETE" }, logs.Select(log => log.ActionType));
        Assert.All(logs, log => Assert.Equal(actor.AccountId, log.ActorAccountId));
        Assert.Equal("Export Health Certificate", logs[1].BeforeData.RootElement.GetProperty("RequirementName").GetString());
        Assert.True(logs[3].AfterData.RootElement.GetProperty("IsDeleted").GetBoolean());
    }

    [Theory]
    [InlineData("ACTIVE")]
    [InlineData("INACTIVE")]
    [InlineData("DELETED")]
    public async Task DuplicateCode_IsRejectedIncludingReservedHistoricalCodes(string state)
    {
        await using var db = TestSupport.CreateDb();
        var service = CreateService(db);
        var actor = Guid.NewGuid();
        var first = await service.CreateAsync(ValidRequest(), actor);
        if (state == "INACTIVE") await service.UpdateStatusAsync(first.ComplianceRequirementId, new() { Status = "INACTIVE" }, actor);
        if (state == "DELETED") await service.DeleteAsync(first.ComplianceRequirementId, actor);
        var duplicate = await Assert.ThrowsAsync<ConflictException>(() => service.CreateAsync(ValidRequest(), actor));
        Assert.Equal("DUPLICATE_REQUIREMENT_CODE", duplicate.ErrorCode);

        var request = ValidRequest();
        request.RequirementCode = "CR-OTHER";
        var other = await service.CreateAsync(request, actor);
        await Assert.ThrowsAsync<ConflictException>(() => service.UpdateAsync(other.ComplianceRequirementId, ValidRequest(), actor));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReferencedRequirement_CannotBeDeletedButCanBeDeactivated(bool templateReference)
    {
        await using var db = TestSupport.CreateDb();
        var service = CreateService(db);
        var actor = Guid.NewGuid();
        var created = await service.CreateAsync(ValidRequest(), actor);
        if (templateReference)
            db.DossierTemplateItems.Add(new DossierTemplateItem
            {
                DossierTemplateItemId = Guid.NewGuid(),
                DossierTemplateId = Guid.NewGuid(),
                ComplianceRequirementId = created.ComplianceRequirementId
            });
        else
            db.DossierRequirements.Add(new DossierRequirement
            {
                DossierRequirementId = Guid.NewGuid(),
                ComplianceDossierId = Guid.NewGuid(),
                ComplianceRequirementId = created.ComplianceRequirementId,
                IsDeleted = true
            });
        await db.SaveChangesAsync();

        Assert.False((await service.GetAsync(created.ComplianceRequirementId)).CanDelete);
        var error = await Assert.ThrowsAsync<ConflictException>(() => service.DeleteAsync(created.ComplianceRequirementId, actor));
        Assert.Equal("REQUIREMENT_IN_USE", error.ErrorCode);
        Assert.Contains("deactivate", error.Message);
        await service.UpdateStatusAsync(created.ComplianceRequirementId, new() { Status = "INACTIVE" }, actor);
        Assert.False((await db.ComplianceRequirements.SingleAsync()).IsDeleted);
        Assert.Equal(1, await db.DossierTemplateItems.CountAsync() + await db.DossierRequirements.CountAsync());
        Assert.Equal(2, await db.CatalogAuditLogs.CountAsync());

        var update = ValidRequest();
        update.RequirementName = "Confirmed update";
        var confirmation = await Assert.ThrowsAsync<CatalogService.ReferenceImpactException>(() =>
            service.UpdateAsync(created.ComplianceRequirementId, update, actor));
        Assert.Equal(templateReference ? 1 : 0, confirmation.ReferenceCount.DossierTemplateItemCount);
        Assert.Equal(templateReference ? 0 : 1, confirmation.ReferenceCount.LinkedDossierCount);
        Assert.Equal("Export Health Certificate", (await service.GetAsync(created.ComplianceRequirementId)).RequirementName);
        update.AcknowledgeReferenceImpact = true;
        await service.UpdateAsync(created.ComplianceRequirementId, update, actor);
        var audit = await db.CatalogAuditLogs.SingleAsync(log => log.ActionType == "UPDATE");
        Assert.NotNull(audit.ReferenceCountSnapshot);
        Assert.Equal("Confirmed update", (await service.GetAsync(created.ComplianceRequirementId)).RequirementName);
    }

    [Theory]
    [InlineData("code")]
    [InlineData("name")]
    [InlineData("category")]
    [InlineData("status")]
    [InlineData("country")]
    [InlineData("validity")]
    [InlineData("bcp")]
    [InlineData("missingValidity")]
    [InlineData("missingValidityRequired")]
    [InlineData("nonEuropeanCountry")]
    [InlineData("nullStatus")]
    [InlineData("nullCategory")]
    [InlineData("nullCode")]
    [InlineData("issuingAuthority")]
    public async Task InvalidInput_DoesNotWriteData(string field)
    {
        await using var db = TestSupport.CreateDb();
        var service = CreateService(db);
        var request = ValidRequest();
        switch (field)
        {
            case "code": request.RequirementCode = " "; break;
            case "name": request.RequirementName = new string('x', 201); break;
            case "category": request.Category = "999"; break;
            case "status": request.Status = "999"; break;
            case "country": request.Country = "ZZ"; break;
            case "validity": request.ValidityPeriodDays = 0; break;
            case "bcp": request.BcpCode = new string('x', 51); break;
            case "missingValidity": request.ValidityPeriodDays = null; break;
            case "missingValidityRequired": request.ValidityRequired = null; break;
            case "nonEuropeanCountry": request.Country = "US"; break;
            case "nullStatus": request.Status = null!; break;
            case "nullCategory": request.Category = null!; break;
            case "nullCode": request.RequirementCode = null!; break;
            case "issuingAuthority": request.IssuingAuthority = new string('x', 201); break;
        }
        Assert.False(new SaveRequestValidator().Validate(request).IsValid);
        await Assert.ThrowsAsync<BadRequestException>(() => service.CreateAsync(request, Guid.NewGuid()));
        Assert.Empty(db.ComplianceRequirements);
        Assert.Empty(db.CatalogAuditLogs);
    }

    [Theory]
    [InlineData("TransportSpecialist", "POST", true)]
    [InlineData("Admin", "DELETE", false)]
    [InlineData("LogisticsManager", "GET", true)]
    [InlineData("LogisticsManager", "PUT", false)]
    [InlineData("Customer", "POST", false)]
    [InlineData("Driver", "PATCH", false)]
    [InlineData("Escort", "DELETE", false)]
    public async Task CatalogAuthorization_EnforcesPolicies(string role, string method, bool allowed)
    {
        using var provider = AuthorizationPolicyTests.CreateProvider();
        var action = typeof(ComplianceRequirementsController).GetMethods().First(action =>
            action.GetCustomAttributes<HttpMethodAttribute>().Any(attribute => attribute.HttpMethods.Contains(method)));
        var policy = action.GetCustomAttribute<AuthorizeAttribute>()!.Policy!;
        var user = new ClaimsPrincipal(new ClaimsIdentity([new System.Security.Claims.Claim(ClaimTypes.Role, role)], "test"));
        var authorization = provider.GetRequiredService<IAuthorizationService>();
        Assert.Equal(allowed, (await authorization.AuthorizeAsync(user, null, policy)).Succeeded);
    }

    [Fact]
    public async Task List_FiltersSortsAndPaginates_AndStatusCanReactivate()
    {
        await using var db = TestSupport.CreateDb();
        var service = CreateService(db);
        var actor = Guid.NewGuid();
        var request = ValidRequest();
        request.Country = null;
        var first = await service.CreateAsync(request, actor);
        request.RequirementCode = "CR-CHEDA-01";
        request.Country = "EU";
        var second = await service.CreateAsync(request, actor);
        var page = await service.GetListAsync(new() { PageSize = 1, PageIndex = 2, SortBy = "requirementCode:asc" });
        Assert.Equal(2, page.TotalItems);
        Assert.Equal(2, page.TotalPages);
        Assert.Equal(first.ComplianceRequirementId, Assert.Single(page.Items).ComplianceRequirementId);
        Assert.Equal(second.ComplianceRequirementId, Assert.Single((await service.GetListAsync(new()
        { Country = "EU", BcpCode = "gb-01", Search = "cheda", Category = "QUARANTINE" })).Items).ComplianceRequirementId);
        Assert.Empty((await service.GetListAsync(new() { PageIndex = int.MaxValue, PageSize = 100 })).Items);
        await Assert.ThrowsAsync<BadRequestException>(() => service.GetListAsync(new() { PageSize = 0 }));
        await Assert.ThrowsAsync<BadRequestException>(() => service.GetListAsync(new() { SortBy = "invalid" }));
        await Assert.ThrowsAsync<BadRequestException>(() => service.UpdateStatusAsync(first.ComplianceRequirementId, new() { Status = "999" }, actor));
        await service.UpdateStatusAsync(first.ComplianceRequirementId, new() { Status = "INACTIVE" }, actor);
        await service.UpdateStatusAsync(first.ComplianceRequirementId, new() { Status = "ACTIVE" }, actor);
        Assert.Equal("ACTIVE", (await service.GetAsync(first.ComplianceRequirementId)).Status);
    }

    [Fact]
    public async Task Validators_PreserveOptionalFieldsAndNormalizedValues()
    {
        await using var db = TestSupport.CreateDb();
        var request = ValidRequest();
        request.Country = " ";
        request.Category = " horse_health ";
        request.Status = " active ";
        request.ValidityRequired = false;
        request.ValidityPeriodDays = null;
        Assert.True(new SaveRequestValidator().Validate(request).IsValid);
        var result = await CreateService(db).CreateAsync(request, Guid.NewGuid());
        Assert.Equal("HORSE_HEALTH", result.Category);
        Assert.Null(result.Country);
        Assert.False(result.ValidityRequired);
        Assert.Null(result.ValidityPeriodDays);

        Assert.True(new ListRequestValidator().Validate(new CatalogRequest.ListRequest
            { Status = " ", Category = " ", Country = null, PageIndex = int.MaxValue }).IsValid);
        Assert.False(new ListRequestValidator().Validate(new CatalogRequest.ListRequest { Category = "999" }).IsValid);
        Assert.False(new ListRequestValidator().Validate(new CatalogRequest.ListRequest { Country = "US" }).IsValid);
        Assert.False(new StatusRequestValidator().Validate(new CatalogRequest.StatusRequest { Status = null! }).IsValid);
        Assert.True(new StatusRequestValidator().Validate(new CatalogRequest.StatusRequest { Status = " inactive " }).IsValid);
    }

    private static Pegaxus.Service.ComplianceRequirementService.IService CreateService(Pegaxus.Repository.AppDbContext db) =>
        new CatalogService(db, new ListRequestValidator(), new SaveRequestValidator(), new StatusRequestValidator());

    private static CatalogRequest.SaveRequest ValidRequest() => new()
    {
        RequirementCode = " cr-ehc-01 ",
        RequirementName = "Export Health Certificate",
        Country = "gb",
        Category = "QUARANTINE",
        BcpCode = "GB-01",
        ValidityRequired = true,
        ValidityPeriodDays = 40,
        LegalBasis = "Regulation (EU) 2016/429"
    };
}
