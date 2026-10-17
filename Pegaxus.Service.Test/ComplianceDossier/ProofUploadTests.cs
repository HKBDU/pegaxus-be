using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;

using DossierRequest = Pegaxus.Service.ComplianceDossierService.Request;
using AssetReference = Pegaxus.Service.UploadService.Request.UploadedAssetReference;

namespace Pegaxus.Service.Test;

public class ProofUploadTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Submit_UsesConfirmedMetadata_AndDetailsSignReadUrls(bool customs)
    {
        await using var db = TestSupport.CreateDb();
        var (accountId, dossierId) = await SeedAsync(db);
        var storage = new DocumentUploadTests.RecordingStorage();
        var service = DocumentUploadTests.CreateService(db, storage);
        var file = DocumentUploadTests.File("stamp.png", "image/png");
        var id = await SubmitAsync(service, customs, accountId, dossierId, [file]);
        var attachment = await db.ProofAttachments.SingleAsync();
        Assert.Equal("stamp.png", attachment.FileName);
        Assert.Equal("image/png", attachment.MimeType);
        Assert.EndsWith(file.PublicId, attachment.FileUrl);
        Assert.Equal(1, attachment.SortOrder);
        var files = customs
            ? (await service.GetCustomsClearanceProofAsync(accountId, id)).Files
            : (await service.GetQuarantineProofAsync(accountId, id)).Files;
        Assert.Contains("signature=test", Assert.Single(files).FileUrl);
        Assert.Single(storage.Uploaded);
    }

    [Theory]
    [InlineData(false, "stamp.pdf", "application/pdf", 10, "INVALID_FILE_TYPE")]
    [InlineData(true, "stamp.pdf", "application/pdf", 10, "INVALID_FILE_TYPE")]
    [InlineData(false, "stamp.png", "image/png", 5242881, "FILE_TOO_LARGE")]
    [InlineData(true, "stamp.png", "image/png", 5242881, "FILE_TOO_LARGE")]
    public async Task Submit_RejectsInvalidConfirmedMetadata(bool customs, string name, string mime, long size, string code)
    {
        await using var db = TestSupport.CreateDb();
        var (accountId, dossierId) = await SeedAsync(db);
        var storage = new DocumentUploadTests.RecordingStorage();
        var error = await Assert.ThrowsAnyAsync<Pegaxus.Service.Exceptions.Exceptions>(() => SubmitAsync(
            DocumentUploadTests.CreateService(db, storage), customs, accountId, dossierId,
            [DocumentUploadTests.File(name, mime, size)]));
        Assert.Equal(code, error.ErrorCode);
        Assert.Empty(await db.ProofAttachments.ToListAsync());
        Assert.Empty(storage.Deleted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Submit_ContentValidationFailureRetainsDirectUpload_AndPersistsNothing(bool customs)
    {
        await using var db = TestSupport.CreateDb();
        var (accountId, dossierId) = await SeedAsync(db);
        var storage = new DocumentUploadTests.RecordingStorage
        {
            ContentFailure = new BadRequestException("Invalid file content", "INVALID_FILE_TYPE")
        };
        await Assert.ThrowsAsync<BadRequestException>(() => SubmitAsync(DocumentUploadTests.CreateService(db, storage),
            customs, accountId, dossierId, [DocumentUploadTests.File("stamp.jpg", "image/jpeg")]));
        Assert.Empty(await db.ProofAttachments.ToListAsync());
        Assert.Empty(storage.Deleted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Submit_DuplicateReferencesRejectedBeforeConfirmation(bool customs)
    {
        await using var db = TestSupport.CreateDb();
        var (accountId, dossierId) = await SeedAsync(db);
        var storage = new DocumentUploadTests.RecordingStorage();
        var file = DocumentUploadTests.File("stamp.png", "image/png");
        var error = await Assert.ThrowsAsync<BadRequestException>(() => SubmitAsync(DocumentUploadTests.CreateService(db, storage),
            customs, accountId, dossierId, [file, file]));
        Assert.Equal("INVALID_PUBLIC_ID", error.ErrorCode);
        Assert.Empty(storage.Uploaded);
    }

    private static Task<Guid> SubmitAsync(Pegaxus.Service.ComplianceDossierService.Service service, bool customs,
        Guid accountId, Guid dossierId, List<AssetReference> files) => customs
        ? service.SubmitCustomsClearanceProofAsync(accountId, dossierId,
            new DossierRequest.SubmitCustomsClearanceProofRequest { CustomsPortName = "Port", Files = files })
        : service.SubmitQuarantineProofAsync(accountId, dossierId,
            new DossierRequest.SubmitQuarantineProofRequest { BcpName = "BCP", Files = files });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Submit_ForeignAccountAssetRejectedByRealOwnershipValidation(bool customs)
    {
        await using var db = TestSupport.CreateDb();
        var (accountId, dossierId) = await SeedAsync(db);
        var media = new CloudinaryUploadTests.TestCloudinary();
        var service = new Pegaxus.Service.ComplianceDossierService.Service(db, media,
            new Pegaxus.Service.ComplianceDossierService.Validator.InitializeRequestValidator(), TestSupport.CreateNotifications(db),
            new DocumentUploadTests.RecordingStorage(), Microsoft.Extensions.Logging.Abstractions.NullLogger<Pegaxus.Service.ComplianceDossierService.Service>.Instance);
        var error = await Assert.ThrowsAsync<BadRequestException>(() => SubmitAsync(service, customs, accountId, dossierId,
            [new AssetReference { PublicId = $"evidence/{Guid.NewGuid():D}/{Guid.NewGuid():N}" }]));
        Assert.Equal("INVALID_PUBLIC_ID", error.ErrorCode);
        Assert.Equal(0, media.Calls);
        Assert.Empty(await db.ProofAttachments.ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Submit_NullFilesReturnsEvidenceError(bool customs)
    {
        await using var db = TestSupport.CreateDb();
        var service = DocumentUploadTests.CreateService(db, new DocumentUploadTests.RecordingStorage());
        var error = await Assert.ThrowsAsync<BadRequestException>(() => SubmitAsync(service, customs, Guid.NewGuid(), Guid.NewGuid(), null!));
        Assert.Equal("EVIDENCE_UNCLEAR", error.ErrorCode);
    }

    internal static async Task<(Guid AccountId, Guid DossierId)> SeedAsync(AppDbContext db)
    {
        var (_, dossier, _) = await DocumentUploadTests.SeedAsync(db);
        dossier.Status = ComplianceDossierStatus.APPROVED;
        var (account, driver) = await TestSupport.AddLogisticsManagerAsync(db);
        driver.Role = EmployeeRole.DRIVER;
        db.EmployeeTransportPlans.Add(new EmployeeTransportPlan
        {
            Employee = driver,
            TransportPlan = new TransportPlan { TransportPlanId = Guid.NewGuid(), Transport = dossier.Transport, PlanCode = "PLAN-PROOF" }
        });
        db.QuarantineProofs.Add(new QuarantineProof
        {
            QuarantineProofId = Guid.NewGuid(), ComplianceDossier = dossier, BcpName = "BCP",
            SubmittedByEmployee = driver, Status = QuarantineProofStatus.Passed
        });
        await db.SaveChangesAsync();
        return (account.AccountId, dossier.ComplianceDossierId);
    }
}
