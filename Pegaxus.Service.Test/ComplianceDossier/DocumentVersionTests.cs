using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.ComplianceDossierService;
using Pegaxus.Service.Exceptions;

using static Pegaxus.Service.Test.DocumentUploadTests;

namespace Pegaxus.Service.Test;

public class DocumentVersionTests
{
    [Fact]
    public async Task UnreviewedVersion_ReplacesAllFilesInPlaceUsingVerifiedAssets()
    {
        await using var db = TestSupport.CreateDb();
        var (account, dossier, requirement) = await SeedAsync(db);
        var storage = new RecordingStorage();
        var service = CreateService(db, storage);
        var initial = ValidRequest(dossier);
        initial.Files.Add(File("old.png", "image/png"));
        var first = await service.UploadDocumentAsync(account.AccountId, dossier.ComplianceDossierId, requirement.DossierRequirementId, initial);
        var oldUrls = storage.Uploaded.Select(item => item.StorageUrl).ToArray();
        var result = await service.SubmitDocumentVersionAsync(account.AccountId, dossier.ComplianceDossierId, requirement.DossierRequirementId,
            new Request.DocumentUploadRequest { Files = [File("new.pdf", "application/pdf"), File("new.png", "image/png"), File("page.jpg", "image/jpeg")], UploadNote = " clearer scan " });
        Assert.Equal(first.DocumentVersionId, result.DocumentVersionId);
        Assert.Equal(1, result.VersionNo);
        Assert.False(result.IsNewVersion);
        var version = Assert.Single(await db.DocumentVersions.ToListAsync());
        Assert.Equal("new.pdf", version.FileName);
        Assert.Equal("clearer scan", version.UploadNote);
        Assert.Equal(2, await db.DocumentAttachments.CountAsync());
        Assert.Empty(storage.Deleted);
        Assert.DoesNotContain(version.FileUrl, oldUrls);
        Assert.Equal("REF-100", (await db.ComplianceDocuments.SingleAsync()).DocumentNumber);
        Assert.Equal("DOCUMENT_VERSION_UPDATED", (await db.AuditDossiers.OrderBy(item => item.CreatedAt).LastAsync()).ActionType);
    }

    [Theory]
    [InlineData(DocumentReviewResult.REJECTED)]
    [InlineData(DocumentReviewResult.REVISION_REQUIRED)]
    public async Task ReviewedVersion_CreatesNextVersionPreservingFilesAndReviews(DocumentReviewResult reviewResult)
    {
        await using var db = TestSupport.CreateDb();
        var (account, dossier, requirement) = await SeedAsync(db);
        var storage = new RecordingStorage();
        var service = CreateService(db, storage);
        var request = ValidRequest(dossier);
        request.Files.Add(File("old.png", "image/png"));
        var first = await service.UploadDocumentAsync(account.AccountId, dossier.ComplianceDossierId, requirement.DossierRequirementId, request);
        var version = await db.DocumentVersions.SingleAsync();
        var oldFileUrl = version.FileUrl;
        var attachmentId = (await db.DocumentAttachments.SingleAsync()).DocumentAttachmentId;
        var (reviewer, employee) = await TestSupport.AddLogisticsManagerAsync(db);
        employee.Role = EmployeeRole.TRANSPORT_SPECIALIST;
        var review = new DocumentReview { DocumentReviewId = Guid.NewGuid(), DocumentVersionId = version.DocumentVersionId,
            ReviewedByAccountId = reviewer.AccountId, Result = reviewResult, ReviewComment = "Blurred scan", ReviewedAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow };
        db.DocumentReviews.Add(review);
        requirement.Status = DossierRequirementStatus.REJECTED;
        version.ComplianceDocument.Status = ComplianceDocumentStatus.REJECTED;
        await db.SaveChangesAsync();
        var result = await service.SubmitDocumentVersionAsync(account.AccountId, dossier.ComplianceDossierId, requirement.DossierRequirementId,
            new Request.DocumentUploadRequest { Files = [File("new.pdf", "application/pdf")], DocumentNumber = "CORRECTED" });
        Assert.True(result.IsNewVersion);
        Assert.Equal(2, result.VersionNo);
        Assert.NotEqual(first.DocumentVersionId, result.DocumentVersionId);
        Assert.Equal(2, await db.DocumentVersions.CountAsync());
        Assert.Equal(oldFileUrl, version.FileUrl);
        Assert.Equal(attachmentId, (await db.DocumentAttachments.SingleAsync()).DocumentAttachmentId);
        Assert.Equal(version.DocumentVersionId, (await db.DocumentReviews.SingleAsync()).DocumentVersionId);
        Assert.Empty(storage.Deleted);
        Assert.Equal(DossierRequirementStatus.PROVIDED, requirement.Status);
        Assert.Equal(ComplianceDocumentStatus.PROVIDED, version.ComplianceDocument.Status);
        var history = await service.GetDocumentVersionsAsync(reviewer.AccountId, dossier.ComplianceDossierId, requirement.DossierRequirementId);
        Assert.Equal(new[] { 2, 1 }, history.Select(item => item.VersionNo));
        Assert.Empty(history[0].Reviews);
        Assert.Equal(review.DocumentReviewId, Assert.Single(history[1].Reviews).DocumentReviewId);
        Assert.Equal(2, history[1].Files.Count);
        var edited = await service.SubmitDocumentVersionAsync(account.AccountId, dossier.ComplianceDossierId, requirement.DossierRequirementId,
            new Request.DocumentUploadRequest { Files = [File("clearer.pdf", "application/pdf")] });
        Assert.Equal(result.DocumentVersionId, edited.DocumentVersionId);
        Assert.Equal(2, edited.VersionNo);
        Assert.False(edited.IsNewVersion);
        Assert.Equal(2, await db.DocumentVersions.CountAsync());
        Assert.Equal(oldFileUrl, version.FileUrl);
        Assert.DoesNotContain(oldFileUrl, storage.Deleted);
        Assert.Single(await db.DocumentReviews.ToListAsync());
    }

    [Fact]
    public async Task MetadataOnly_UpdatesDocumentWithoutFilesVersionsOrStatusChanges()
    {
        await using var db = TestSupport.CreateDb();
        var (account, dossier, requirement) = await SeedAsync(db);
        var storage = new RecordingStorage();
        var service = CreateService(db, storage);
        var first = await service.UploadDocumentAsync(account.AccountId, dossier.ComplianceDossierId, requirement.DossierRequirementId, ValidRequest(dossier));
        requirement.Status = DossierRequirementStatus.REJECTED;
        var document = await db.ComplianceDocuments.SingleAsync();
        document.Status = ComplianceDocumentStatus.REJECTED;
        await db.SaveChangesAsync();
        var result = await service.UpdateDocumentMetadataAsync(account.AccountId, dossier.ComplianceDossierId, requirement.DossierRequirementId,
            new Request.DocumentMetadataRequest { DocumentNumber = " fixed " });
        Assert.Equal("fixed", result.DocumentNumber);
        Assert.Equal("Weatherbys", result.IssuingAuthority);
        Assert.Equal(ComplianceDocumentStatus.REJECTED, document.Status);
        Assert.Equal(DossierRequirementStatus.REJECTED, requirement.Status);
        Assert.Equal(first.DocumentVersionId, (await db.DocumentVersions.SingleAsync()).DocumentVersionId);
        Assert.Single(storage.Uploaded);
        Assert.Empty(storage.Deleted);
        Assert.Equal("DOCUMENT_METADATA_UPDATED", (await db.AuditDossiers.OrderBy(item => item.CreatedAt).LastAsync()).ActionType);
    }

    [Theory]
    [InlineData(true, "This ComplianceDocument has already been satisfied and locked by Transport Specialist. Please contact support to unlock.")]
    [InlineData(false, "Cannot submit new DocumentVersions while ComplianceDossier is undergoing official authority review.")]
    public async Task LockedDocument_BlocksFilesAndMetadata(bool satisfied, string message)
    {
        await using var db = TestSupport.CreateDb();
        var (account, dossier, requirement) = await SeedAsync(db);
        var service = CreateService(db, new RecordingStorage());
        await service.UploadDocumentAsync(account.AccountId, dossier.ComplianceDossierId, requirement.DossierRequirementId, ValidRequest(dossier));
        if (satisfied) requirement.Status = DossierRequirementStatus.SATISFIED;
        else dossier.Status = ComplianceDossierStatus.SUBMITTED;
        await db.SaveChangesAsync();
        var storage = new RecordingStorage();
        service = CreateService(db, storage);
        var error = await Assert.ThrowsAsync<ConflictException>(() => service.SubmitDocumentVersionAsync(account.AccountId,
            dossier.ComplianceDossierId, requirement.DossierRequirementId, ValidRequest(dossier)));
        Assert.Equal(message, error.Message);
        error = await Assert.ThrowsAsync<ConflictException>(() => service.UpdateDocumentMetadataAsync(account.AccountId,
            dossier.ComplianceDossierId, requirement.DossierRequirementId, new Request.DocumentMetadataRequest { DocumentNumber = "fixed" }));
        Assert.Equal(message, error.Message);
        Assert.Empty(storage.Uploaded);
        Assert.Single(await service.GetDocumentVersionsAsync(account.AccountId, dossier.ComplianceDossierId, requirement.DossierRequirementId));
    }

    [Fact]
    public async Task ForeignCustomer_CannotEditMetadataFilesOrReadHistory()
    {
        await using var db = TestSupport.CreateDb();
        var (account, dossier, requirement) = await SeedAsync(db);
        var service = CreateService(db, new RecordingStorage());
        await service.UploadDocumentAsync(account.AccountId, dossier.ComplianceDossierId, requirement.DossierRequirementId, ValidRequest(dossier));
        var (foreign, _) = await TestSupport.AddCustomerAsync(db);
        await Assert.ThrowsAsync<ForbiddenException>(() => service.SubmitDocumentVersionAsync(foreign.AccountId,
            dossier.ComplianceDossierId, requirement.DossierRequirementId, ValidRequest(dossier)));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.UpdateDocumentMetadataAsync(foreign.AccountId,
            dossier.ComplianceDossierId, requirement.DossierRequirementId, new Request.DocumentMetadataRequest { DocumentNumber = "fixed" }));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.GetDocumentVersionsAsync(foreign.AccountId, dossier.ComplianceDossierId, requirement.DossierRequirementId));
    }

    [Fact]
    public async Task ReviewDuringStorage_CreatesNextVersionInsteadOfOverwritingReviewedFile()
    {
        await using var db = TestSupport.CreateDb();
        var (account, dossier, requirement) = await SeedAsync(db);
        await CreateService(db, new RecordingStorage()).UploadDocumentAsync(account.AccountId, dossier.ComplianceDossierId,
            requirement.DossierRequirementId, ValidRequest(dossier));
        var version = await db.DocumentVersions.SingleAsync();
        var oldUrl = version.FileUrl;
        var (reviewer, _) = await TestSupport.AddLogisticsManagerAsync(db);
        var storage = new RecordingStorage { AfterUpload = async () =>
        {
            db.DocumentReviews.Add(new DocumentReview { DocumentReviewId = Guid.NewGuid(), DocumentVersionId = version.DocumentVersionId,
                ReviewedByAccountId = reviewer.AccountId, Result = DocumentReviewResult.REJECTED, ReviewedAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow });
            requirement.Status = DossierRequirementStatus.REJECTED;
            version.ComplianceDocument.Status = ComplianceDocumentStatus.REJECTED;
            await db.SaveChangesAsync();
        } };
        var result = await CreateService(db, storage).SubmitDocumentVersionAsync(account.AccountId, dossier.ComplianceDossierId,
            requirement.DossierRequirementId, ValidRequest(dossier));
        Assert.Equal(2, result.VersionNo);
        Assert.Equal(oldUrl, version.FileUrl);
        Assert.Single(await db.DocumentReviews.ToListAsync());
        Assert.Empty(storage.Deleted);
    }

    [Fact]
    public async Task ConcurrentReplacement_RejectsStaleUploadWithoutDeletingCommittedFiles()
    {
        await using var db = TestSupport.CreateDb();
        var (account, dossier, requirement) = await SeedAsync(db);
        await CreateService(db, new RecordingStorage()).UploadDocumentAsync(account.AccountId, dossier.ComplianceDossierId,
            requirement.DossierRequirementId, ValidRequest(dossier));
        var winnerStorage = new RecordingStorage();
        var storage = new RecordingStorage { AfterUpload = async () =>
        {
            await CreateService(db, winnerStorage).SubmitDocumentVersionAsync(account.AccountId, dossier.ComplianceDossierId,
                requirement.DossierRequirementId, ValidRequest(dossier));
        } };
        await Assert.ThrowsAsync<ConflictException>(() => CreateService(db, storage).SubmitDocumentVersionAsync(account.AccountId,
            dossier.ComplianceDossierId, requirement.DossierRequirementId, ValidRequest(dossier)));
        Assert.Equal(winnerStorage.Uploaded.Single().StorageUrl, (await db.DocumentVersions.AsNoTracking().SingleAsync()).FileUrl);
        Assert.Empty(storage.Deleted);
        Assert.DoesNotContain(winnerStorage.Uploaded.Single().StorageUrl, storage.Deleted);
    }

    [Fact]
    public async Task FailedReplacement_RetainsOriginalAndDirectlyUploadedAssets()
    {
        var interceptor = new FailingSaveInterceptor();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).AddInterceptors(interceptor)
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
        var (account, dossier, requirement) = await SeedAsync(db);
        var initialStorage = new RecordingStorage();
        var request = ValidRequest(dossier);
        request.Files.Add(File("old.png", "image/png"));
        await CreateService(db, initialStorage).UploadDocumentAsync(account.AccountId, dossier.ComplianceDossierId, requirement.DossierRequirementId, request);
        interceptor.Fail = true;
        var storage = new RecordingStorage();
        await Assert.ThrowsAsync<DbUpdateException>(() => CreateService(db, storage).SubmitDocumentVersionAsync(account.AccountId,
            dossier.ComplianceDossierId, requirement.DossierRequirementId, ValidRequest(dossier)));
        Assert.Equal(initialStorage.Uploaded[0].StorageUrl, (await db.DocumentVersions.AsNoTracking().SingleAsync()).FileUrl);
        Assert.Equal(initialStorage.Uploaded[1].StorageUrl, (await db.DocumentAttachments.AsNoTracking().SingleAsync()).FileUrl);
        Assert.Empty(storage.Deleted);
    }

    [Fact]
    public async Task LostReplacementCommitAcknowledgment_RetainsOldAndNewObjects()
    {
        var name = Guid.NewGuid().ToString();
        var root = new InMemoryDatabaseRoot();
        await using var originalDb = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(name, root).ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
        var (account, dossier, requirement) = await SeedAsync(originalDb);
        await CreateService(originalDb, new RecordingStorage()).UploadDocumentAsync(account.AccountId, dossier.ComplianceDossierId,
            requirement.DossierRequirementId, ValidRequest(dossier));
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(name, root).ReplaceService<IDbContextTransactionManager, LostCommitTransactionManager>()
            .ConfigureWarnings(warnings => warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning)).Options);
        var storage = new RecordingStorage();
        await Assert.ThrowsAsync<IOException>(() => CreateService(db, storage).SubmitDocumentVersionAsync(account.AccountId,
            dossier.ComplianceDossierId, requirement.DossierRequirementId, ValidRequest(dossier)));
        Assert.Empty(storage.Deleted);
        Assert.Equal(storage.Uploaded.Single().StorageUrl, (await db.DocumentVersions.AsNoTracking().SingleAsync()).FileUrl);
    }

    [Theory]
    [InlineData(EmployeeRole.TRANSPORT_SPECIALIST)]
    [InlineData(EmployeeRole.LOGISTICS_MANAGER)]
    [InlineData(EmployeeRole.DRIVER)]
    public async Task Employees_CannotReplaceFilesOrMetadata(EmployeeRole role)
    {
        await using var db = TestSupport.CreateDb();
        var (account, dossier, requirement) = await SeedAsync(db);
        var service = CreateService(db, new RecordingStorage());
        await service.UploadDocumentAsync(account.AccountId, dossier.ComplianceDossierId, requirement.DossierRequirementId, ValidRequest(dossier));
        var (employeeAccount, employee) = await TestSupport.AddLogisticsManagerAsync(db);
        employee.Role = role;
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ForbiddenException>(() => service.SubmitDocumentVersionAsync(employeeAccount.AccountId,
            dossier.ComplianceDossierId, requirement.DossierRequirementId, ValidRequest(dossier)));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.UpdateDocumentMetadataAsync(employeeAccount.AccountId,
            dossier.ComplianceDossierId, requirement.DossierRequirementId, new Request.DocumentMetadataRequest { DocumentNumber = "changed" }));
        if (role == EmployeeRole.DRIVER)
            await Assert.ThrowsAsync<ForbiddenException>(() => service.GetDocumentVersionsAsync(employeeAccount.AccountId,
                dossier.ComplianceDossierId, requirement.DossierRequirementId));
    }

    [Fact]
    public async Task MissingDocument_DoesNotTurnResubmissionIntoInitialUpload()
    {
        await using var db = TestSupport.CreateDb();
        var (account, dossier, requirement) = await SeedAsync(db);
        requirement.Status = DossierRequirementStatus.PROVIDED;
        await db.SaveChangesAsync();
        var storage = new RecordingStorage();
        await Assert.ThrowsAsync<NotFoundException>(() => CreateService(db, storage).SubmitDocumentVersionAsync(account.AccountId,
            dossier.ComplianceDossierId, requirement.DossierRequirementId, ValidRequest(dossier)));
        Assert.Empty(storage.Uploaded);
    }

    [Fact]
    public async Task MetadataValidation_RejectsBlankNumberAndInvalidExpiryWithoutChangingVersion()
    {
        await using var db = TestSupport.CreateDb();
        var (account, dossier, requirement) = await SeedAsync(db);
        var service = CreateService(db, new RecordingStorage());
        await service.UploadDocumentAsync(account.AccountId, dossier.ComplianceDossierId, requirement.DossierRequirementId, ValidRequest(dossier));
        var oldUrl = (await db.DocumentVersions.SingleAsync()).FileUrl;
        await Assert.ThrowsAsync<BadRequestException>(() => service.UpdateDocumentMetadataAsync(account.AccountId,
            dossier.ComplianceDossierId, requirement.DossierRequirementId, new Request.DocumentMetadataRequest { DocumentNumber = " " }));
        await Assert.ThrowsAsync<BadRequestException>(() => service.UpdateDocumentMetadataAsync(account.AccountId,
            dossier.ComplianceDossierId, requirement.DossierRequirementId, new Request.DocumentMetadataRequest { ExpiryDate = dossier.Transport.Booking.RequestedDepartureDate }));
        Assert.Equal(oldUrl, (await db.DocumentVersions.SingleAsync()).FileUrl);
        Assert.Equal("REF-100", (await db.ComplianceDocuments.SingleAsync()).DocumentNumber);
    }

    [Fact]
    public async Task ApprovalDuringVerification_BlocksCommitWithoutDeletingAssets()
    {
        await using var db = TestSupport.CreateDb();
        var (account, dossier, requirement) = await SeedAsync(db);
        var service = CreateService(db, new RecordingStorage());
        await service.UploadDocumentAsync(account.AccountId, dossier.ComplianceDossierId, requirement.DossierRequirementId, ValidRequest(dossier));
        var oldUrl = (await db.DocumentVersions.SingleAsync()).FileUrl;
        var storage = new RecordingStorage { AfterUpload = async () => { requirement.Status = DossierRequirementStatus.SATISFIED; await db.SaveChangesAsync(); } };
        await Assert.ThrowsAsync<ConflictException>(() => CreateService(db, storage).SubmitDocumentVersionAsync(account.AccountId,
            dossier.ComplianceDossierId, requirement.DossierRequirementId, ValidRequest(dossier)));
        Assert.Equal(oldUrl, (await db.DocumentVersions.SingleAsync()).FileUrl);
        Assert.Empty(storage.Deleted);
        Assert.DoesNotContain(oldUrl, storage.Deleted);
    }
}
