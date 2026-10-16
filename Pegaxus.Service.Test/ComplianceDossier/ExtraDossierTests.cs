using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;

using DossierEntity = Pegaxus.Repository.Entities.ComplianceDossier;
using DossierService = Pegaxus.Service.ComplianceDossierService.Service;

namespace Pegaxus.Service.Test;

public class ExtraDossierTests
{
    private static DossierService Service(AppDbContext db) =>
        DocumentUploadTests.CreateService(db, new DocumentUploadTests.RecordingStorage());

    private static async Task<(DossierEntity Main, DossierEntity Extra)> AddExtraAsync(AppDbContext db, DossierEntity main,
        ComplianceDossierStatus status)
    {
        var extra = new DossierEntity
        {
            ComplianceDossierId = Guid.NewGuid(), DossierCode = "DOS-EXTRA", DossierType = DossierType.EXTRA, Status = status,
            TransportId = main.TransportId, CreatedAt = main.CreatedAt.AddMinutes(5)
        };
        db.ComplianceDossiers.Add(extra);
        await db.SaveChangesAsync();
        return (main, extra);
    }

    private static async Task<Pegaxus.Repository.Entities.Employee> AddSpecialistAsync(AppDbContext db, EmployeeRole role = EmployeeRole.TRANSPORT_SPECIALIST)
    {
        var (_, employee) = await TestSupport.AddLogisticsManagerAsync(db);
        employee.Role = role;
        await db.SaveChangesAsync();
        return employee;
    }

    [Fact]
    public async Task D1_WithAMainAndAnExtra_ReturnsTheMainOverviewAndBothSummaries()
    {
        await using var db = TestSupport.CreateDb();
        var (customer, main, _) = await DocumentUploadTests.SeedAsync(db);
        await AddExtraAsync(db, main, ComplianceDossierStatus.IN_PREPARATION);
        var specialist = await AddSpecialistAsync(db);

        var result = await Service(db).GetByBookingAsync(specialist.AccountId, main.Transport.BookingId, new());

        Assert.True(result.IsInitialized);
        Assert.Equal(("DOS-UPLOAD", "MAIN"), (result.DossierCode, result.DossierType));
        Assert.Equal(["DOS-UPLOAD", "DOS-EXTRA"], result.ComplianceDossiers.Select(x => x.DossierCode).ToArray());
        Assert.Equal(["MAIN", "EXTRA"], result.ComplianceDossiers.Select(x => x.DossierType).ToArray());
        Assert.NotNull(customer);
    }

    [Fact]
    public async Task Customer_NeverSeesADraftExtra_ButSeesItOnceItIsOpened()
    {
        await using var db = TestSupport.CreateDb();
        var (customer, main, _) = await DocumentUploadTests.SeedAsync(db);
        var (_, extra) = await AddExtraAsync(db, main, ComplianceDossierStatus.DRAFT);
        var service = Service(db);

        var hidden = await service.GetByBookingAsync(customer.AccountId, main.Transport.BookingId, new());
        var error = await Assert.ThrowsAsync<NotFoundException>(() => service.GetDossierAsync(customer.AccountId, extra.ComplianceDossierId, new()));

        Assert.Equal(["MAIN"], hidden.ComplianceDossiers.Select(x => x.DossierType).ToArray());
        Assert.Equal("DOSSIER_NOT_FOUND", error.ErrorCode);

        extra.Status = ComplianceDossierStatus.IN_PREPARATION;
        await db.SaveChangesAsync();
        var shown = await service.GetByBookingAsync(customer.AccountId, main.Transport.BookingId, new());
        var detail = await service.GetDossierAsync(customer.AccountId, extra.ComplianceDossierId, new());
        Assert.Equal(["MAIN", "EXTRA"], shown.ComplianceDossiers.Select(x => x.DossierType).ToArray());
        Assert.Equal(("EXTRA", "IN_PREPARATION"), (detail.DossierType, detail.Status));
    }

    [Fact]
    public async Task Customer_DoesNotSeeADraftMainEither_AndD1ReportsNotInitialized()
    {
        await using var db = TestSupport.CreateDb();
        var (customer, main, _) = await DocumentUploadTests.SeedAsync(db);
        main.Status = ComplianceDossierStatus.DRAFT;
        await db.SaveChangesAsync();

        var result = await Service(db).GetByBookingAsync(customer.AccountId, main.Transport.BookingId, new());

        Assert.False(result.IsInitialized);
        Assert.Contains("not been initialized", result.Message);
    }

    [Fact]
    public async Task Specialist_ViewsADraftExtra()
    {
        await using var db = TestSupport.CreateDb();
        var (_, main, _) = await DocumentUploadTests.SeedAsync(db);
        var (_, extra) = await AddExtraAsync(db, main, ComplianceDossierStatus.DRAFT);
        var specialist = await AddSpecialistAsync(db);

        var detail = await Service(db).GetDossierAsync(specialist.AccountId, extra.ComplianceDossierId, new());

        Assert.Equal(("EXTRA", "DRAFT"), (detail.DossierType, detail.Status));
    }

    [Fact]
    public async Task Customer_CannotUploadIntoADraftExtra_BecauseOfTheExistingWriteLock()
    {
        await using var db = TestSupport.CreateDb();
        var (customer, main, _) = await DocumentUploadTests.SeedAsync(db);
        var (_, extra) = await AddExtraAsync(db, main, ComplianceDossierStatus.DRAFT);
        var requirement = new Pegaxus.Repository.Entities.DossierRequirement
        {
            DossierRequirementId = Guid.NewGuid(), ComplianceDossierId = extra.ComplianceDossierId, IsMandatory = true,
            Status = DossierRequirementStatus.PENDING,
            ComplianceRequirement = new Pegaxus.Repository.Entities.ComplianceRequirement
            {
                ComplianceRequirementId = Guid.NewGuid(), RequirementCode = "EXTRA-PASS", RequirementName = "Extra permit",
                Category = ComplianceRequirementCategory.Transport
            }
        };
        db.DossierRequirements.Add(requirement);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(() => Service(db).UploadDocumentAsync(customer.AccountId,
            extra.ComplianceDossierId, requirement.DossierRequirementId, DocumentUploadTests.ValidRequest(main)));
    }

    [Fact]
    public async Task Dashboard_ListsEachDossierOnItsOwnRow_WithTypeAndCode()
    {
        await using var db = TestSupport.CreateDb();
        var (_, main, _) = await DocumentUploadTests.SeedAsync(db);
        await AddExtraAsync(db, main, ComplianceDossierStatus.IN_PREPARATION);
        var manager = await AddSpecialistAsync(db, EmployeeRole.LOGISTICS_MANAGER);

        var dashboard = await Service(db).GetDashboardAsync(manager.AccountId, new() { PageIndex = 1, PageSize = 20 });

        Assert.Equal(["DOS-EXTRA", "DOS-UPLOAD"], dashboard.Items.Select(x => x.DossierCode).Order().ToArray());
        Assert.Equal(["EXTRA", "MAIN"], dashboard.Items.Select(x => x.DossierType).Order().ToArray());
    }
}
