using Microsoft.AspNetCore.Http;
using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.DocumentStorageService;
using Pegaxus.Service.Exceptions;
using DossierService = Pegaxus.Service.ComplianceDossierService.Service;
using DossierRequest = Pegaxus.Service.ComplianceDossierService.Request;

namespace Pegaxus.Service.Test;

public class DocumentUploadTests
{
    [Fact]
    public async Task ClientMetadataAndUrls_AreIgnoredAndResolvedByPublicId()
    {
        await using var db = TestSupport.CreateDb();
        var (account, dossier, requirement) = await SeedAsync(db);
        var reference = File("trusted.pdf", "application/pdf");
        var request = ValidRequest(dossier);
        request.Files = JsonSerializer.Deserialize<DossierRequest.DocumentUploadRequest>($$"""
            {"files":[{"publicId":"{{reference.PublicId}}","fileUrl":"https://attacker.example/file.exe","mimeType":"application/zip","sizeBytes":1,"fileName":"fake.exe"}]}
            """, new JsonSerializerOptions(JsonSerializerDefaults.Web))!.Files;
        var storage = new RecordingStorage();
        var result = await CreateService(db, storage).UploadDocumentAsync(account.AccountId, dossier.ComplianceDossierId,
            requirement.DossierRequirementId, request);
        var version = await db.DocumentVersions.SingleAsync();
        Assert.Equal("trusted.pdf", version.FileName);
        Assert.Equal("application/pdf", version.MimeType);
        Assert.EndsWith(reference.PublicId, version.FileUrl);
        Assert.DoesNotContain("attacker", version.FileUrl);
        Assert.Equal("trusted.pdf", result.Files.Single().FileName);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("blank")]
    [InlineData("null")]
    public async Task InvalidAssetReferences_AreRejectedBeforeVerification(string invalid)
    {
        await using var db = TestSupport.CreateDb();
        var (account, dossier, requirement) = await SeedAsync(db);
        var request = ValidRequest(dossier);
        if (invalid == "duplicate") request.Files.Add(request.Files[0]);
        else if (invalid == "blank") request.Files[0].PublicId = " ";
        else request.Files[0] = null!;
        var storage = new RecordingStorage();
        var error = await Assert.ThrowsAsync<BadRequestException>(() => CreateService(db, storage).UploadDocumentAsync(account.AccountId,
            dossier.ComplianceDossierId, requirement.DossierRequirementId, request));
        Assert.Equal("INVALID_PUBLIC_ID", error.ErrorCode);
        Assert.Empty(storage.Uploaded);
        Assert.Empty(await db.DocumentVersions.ToListAsync());
    }

    [Fact]
    public async Task VirusScannerFailure_NeverPersistsOrDeletesDirectlyUploadedAssets()
    {
        await using var db = TestSupport.CreateDb();
        var (account, dossier, requirement) = await SeedAsync(db);
        var storage = new RecordingStorage { ContentFailure = new BadRequestException("Malware detected", "MALWARE_DETECTED") };
        await Assert.ThrowsAsync<BadRequestException>(() => CreateService(db, storage).UploadDocumentAsync(account.AccountId,
            dossier.ComplianceDossierId, requirement.DossierRequirementId, ValidRequest(dossier)));
        Assert.Empty(await db.DocumentVersions.ToListAsync());
        Assert.Empty(storage.Deleted);
    }

    [Theory]
    [InlineData(ComplianceDossierStatus.IN_PREPARATION, DossierRequirementStatus.PENDING)]
    [InlineData(ComplianceDossierStatus.IN_PREPARATION, DossierRequirementStatus.REJECTED)]
    [InlineData(ComplianceDossierStatus.REJECTED, DossierRequirementStatus.PENDING)]
    [InlineData(ComplianceDossierStatus.REJECTED, DossierRequirementStatus.REJECTED)]
    public async Task ValidUpload_CreatesDocumentAndExactlyOneFirstVersion(ComplianceDossierStatus dossierStatus, DossierRequirementStatus requirementStatus)
    {
        await using var db = TestSupport.CreateDb();
        var (account, dossier, requirement) = await SeedAsync(db);
        dossier.Status = dossierStatus;
        requirement.Status = requirementStatus;
        await db.SaveChangesAsync();
        var storage = new RecordingStorage();
        var request = ValidRequest(dossier);
        request.Files = [File("passport.pdf", "application/pdf", 4 * 1024 * 1024)];
        var result = await CreateService(db, storage).UploadDocumentAsync(account.AccountId, dossier.ComplianceDossierId,
            requirement.DossierRequirementId, request);
        Assert.Equal(1, result.VersionNo);
        Assert.Equal("PROVIDED", result.Status);
        var document = Assert.Single(await db.ComplianceDocuments.ToListAsync());
        Assert.Equal("REF-100", document.DocumentNumber);
        Assert.Equal("Weatherbys", document.IssuingAuthority);
        Assert.Equal(account.AccountId, document.CreatedByAccountId);
        Assert.Equal(requirement.DossierRequirementId, document.DossierRequirementId);
        var version = Assert.Single(await db.DocumentVersions.ToListAsync());
        Assert.Equal(document.ComplianceDocumentId, version.ComplianceDocumentId);
        Assert.Equal(1, version.VersionNo);
        Assert.Equal(4 * 1024 * 1024, version.FileSizeBytes);
        Assert.StartsWith("https://res.cloudinary.com/", version.FileUrl);
        Assert.DoesNotContain("signature", version.FileUrl);
        Assert.Empty(await db.DocumentAttachments.ToListAsync());
        Assert.Equal(DossierRequirementStatus.PROVIDED, requirement.Status);
        Assert.Null(requirement.SatisfiedAt);
        var audit = Assert.Single(await db.AuditDossiers.ToListAsync());
        Assert.Equal("DOCUMENT_VERSION_SUBMITTED", audit.ActionType);
        Assert.Equal(account.AccountId, audit.ActorAccountId);
        Assert.Equal(version.DocumentVersionId, audit.TargetId);
        Assert.Single(result.Files);
        Assert.Null(result.Files[0].DocumentAttachmentId);
        Assert.True(result.Files[0].ReadUrlExpiresAt > DateTimeOffset.UtcNow);
        Assert.Empty(storage.Deleted);
    }

    [Fact]
    public async Task ThreePages_BelongToOneVersionWithoutDuplicatingThePrimaryFile()
    {
        await using var db = TestSupport.CreateDb();
        var (account, dossier, requirement) = await SeedAsync(db);
        var request = ValidRequest(dossier);
        request.Files = [File("pedigree.jpg", "image/jpeg"), File("vaccination.jpeg", "image/jpeg"), File("microchip.png", "image/png")];
        var storage = new RecordingStorage();
        var result = await CreateService(db, storage).UploadDocumentAsync(account.AccountId, dossier.ComplianceDossierId,
            requirement.DossierRequirementId, request);
        var version = Assert.Single(await db.DocumentVersions.ToListAsync());
        var attachments = await db.DocumentAttachments.OrderBy(item => item.SortOrder).ToListAsync();
        Assert.Equal(2, attachments.Count);
        Assert.All(attachments, item => Assert.Equal(version.DocumentVersionId, item.DocumentVersionId));
        Assert.Equal(new[] { 1, 2 }, attachments.Select(item => item.SortOrder));
        Assert.Equal("pedigree.jpg", version.FileName);
        Assert.Equal(3, result.Files.Count);
        Assert.Equal(3, storage.Uploaded.Count);
        Assert.Single(await db.ComplianceDocuments.ToListAsync());
    }

    [Fact]
    public async Task ForeignCustomer_CannotUploadEvenWithGuessedRequirementId()
    {
        await using var db = TestSupport.CreateDb();
        var (_, dossier, _) = await SeedAsync(db);
        var (foreign, _) = await TestSupport.AddCustomerAsync(db);
        var storage = new RecordingStorage();
        var error = await Assert.ThrowsAsync<ForbiddenException>(() => CreateService(db, storage).UploadDocumentAsync(foreign.AccountId,
            dossier.ComplianceDossierId, Guid.NewGuid(), ValidRequest(dossier)));
        Assert.Equal(403, error.StatusCode);
        Assert.Empty(storage.Uploaded);
    }

    [Theory]
    [InlineData(EmployeeRole.TRANSPORT_SPECIALIST)]
    [InlineData(EmployeeRole.LOGISTICS_MANAGER)]
    [InlineData(EmployeeRole.DRIVER)]
    public async Task Employees_CannotUploadCustomerDocuments(EmployeeRole role)
    {
        await using var db = TestSupport.CreateDb();
        var (_, dossier, requirement) = await SeedAsync(db);
        var (account, employee) = await TestSupport.AddLogisticsManagerAsync(db);
        employee.Role = role;
        await db.SaveChangesAsync();
        var storage = new RecordingStorage();
        await Assert.ThrowsAsync<ForbiddenException>(() => CreateService(db, storage).UploadDocumentAsync(account.AccountId,
            dossier.ComplianceDossierId, requirement.DossierRequirementId, ValidRequest(dossier)));
        Assert.Empty(storage.Uploaded);
    }

    [Theory]
    [InlineData(ComplianceDossierStatus.DRAFT)]
    [InlineData(ComplianceDossierStatus.READY_FOR_SUBMISSION)]
    [InlineData(ComplianceDossierStatus.SUBMITTED)]
    [InlineData(ComplianceDossierStatus.PENDING)]
    [InlineData(ComplianceDossierStatus.APPROVED)]
    [InlineData(ComplianceDossierStatus.CLOSED)]
    public async Task LockedDossier_RejectsUploadBeforeStorage(ComplianceDossierStatus status)
    {
        await using var db = TestSupport.CreateDb();
        var (account, dossier, requirement) = await SeedAsync(db);
        dossier.Status = status;
        await db.SaveChangesAsync();
        var storage = new RecordingStorage();
        await Assert.ThrowsAsync<ConflictException>(() => CreateService(db, storage).UploadDocumentAsync(account.AccountId,
            dossier.ComplianceDossierId, requirement.DossierRequirementId, ValidRequest(dossier)));
        Assert.Empty(storage.Uploaded);
    }

    [Theory]
    [InlineData(DossierRequirementStatus.PROVIDED)]
    [InlineData(DossierRequirementStatus.SATISFIED)]
    [InlineData(DossierRequirementStatus.WAIVED)]
    [InlineData(DossierRequirementStatus.REQUESTED)]
    public async Task IneligibleRequirement_RejectsFirstVersion(DossierRequirementStatus status)
    {
        await using var db = TestSupport.CreateDb();
        var (account, dossier, requirement) = await SeedAsync(db);
        requirement.Status = status;
        await db.SaveChangesAsync();
        var storage = new RecordingStorage();
        await Assert.ThrowsAsync<ConflictException>(() => CreateService(db, storage).UploadDocumentAsync(account.AccountId,
            dossier.ComplianceDossierId, requirement.DossierRequirementId, ValidRequest(dossier)));
        Assert.Empty(storage.Uploaded);
    }

    [Fact]
    public async Task RejectedExistingDocument_PreservesItsVersionHistory()
    {
        await using var db = TestSupport.CreateDb();
        var (account, dossier, requirement) = await SeedAsync(db);
        requirement.Status = DossierRequirementStatus.REJECTED;
        var existing = new ComplianceDocument
        {
            ComplianceDocumentId = Guid.NewGuid(), DossierRequirementId = requirement.DossierRequirementId,
            CreatedByAccountId = account.AccountId, UpdatedByAccountId = account.AccountId, DocumentCode = "CD-OLD", DocumentName = "Passport",
            DocumentVersions = [new DocumentVersion
            {
                DocumentVersionId = Guid.NewGuid(), VersionNo = 1, UploadedByAccountId = account.AccountId,
                FileName = "old.pdf", FileUrl = "s3://documents/old.pdf"
            }]
        };
        db.ComplianceDocuments.Add(existing);
        await db.SaveChangesAsync();
        var storage = new RecordingStorage();
        var error = await Assert.ThrowsAsync<ConflictException>(() => CreateService(db, storage).UploadDocumentAsync(account.AccountId,
            dossier.ComplianceDossierId, requirement.DossierRequirementId, ValidRequest(dossier)));
        Assert.Equal("DOCUMENT_ALREADY_EXISTS", error.ErrorCode);
        Assert.Equal(existing.ComplianceDocumentId, Assert.Single(await db.ComplianceDocuments.ToListAsync()).ComplianceDocumentId);
        Assert.Equal("old.pdf", Assert.Single(await db.DocumentVersions.ToListAsync()).FileName);
        Assert.Empty(storage.Uploaded);
        Assert.Empty(storage.Deleted);
    }

    [Theory]
    [InlineData("large")]
    [InlineData("mime")]
    [InlineData("extension")]
    [InlineData("empty")]
    [InlineData("too-many")]
    [InlineData("missing")]
    public async Task InvalidFiles_DoNotWriteDatabaseOrStorage(string invalid)
    {
        await using var db = TestSupport.CreateDb();
        var (account, dossier, requirement) = await SeedAsync(db);
        var request = ValidRequest(dossier);
        request.Files = invalid switch
        {
            "large" => [File("passport.pdf", "application/pdf", 15 * 1024 * 1024)],
            "mime" => [File("passport.pdf", "application/zip")],
            "extension" => [File("passport.exe", "application/pdf")],
            "empty" => [File("passport.pdf", "application/pdf", 0)],
            "too-many" => Enumerable.Range(0, 11).Select(_ => File("passport.pdf", "application/pdf")).ToList(),
            _ => []
        };
        var storage = new RecordingStorage();
        var error = await Assert.ThrowsAnyAsync<Pegaxus.Service.Exceptions.Exceptions>(() => CreateService(db, storage).UploadDocumentAsync(account.AccountId,
            dossier.ComplianceDossierId, requirement.DossierRequirementId, request));
        if (invalid == "large") Assert.Equal("File size exceeds the maximum limit of 10MB. Please compress your file.", error.Message);
        if (invalid is "mime" or "extension") Assert.Equal("Invalid file format. Only PDF, JPG, JPEG, and PNG files are accepted for DocumentVersion.", error.Message);
        Assert.Empty(storage.Uploaded);
        Assert.Empty(await db.ComplianceDocuments.ToListAsync());
        Assert.Equal(DossierRequirementStatus.PENDING, requirement.Status);
    }

    [Theory]
    [InlineData("number")]
    [InlineData("authority")]
    [InlineData("issued")]
    [InlineData("future-issued")]
    [InlineData("expired")]
    [InlineData("departure-day")]
    [InlineData("valid-until")]
    [InlineData("missing-expiry")]
    public async Task InvalidMetadata_IsRejectedBeforeUpload(string invalid)
    {
        await using var db = TestSupport.CreateDb();
        var (account, dossier, requirement) = await SeedAsync(db);
        var request = ValidRequest(dossier);
        switch (invalid)
        {
            case "number": request.DocumentNumber = " "; break;
            case "authority": request.IssuingAuthority = ""; break;
            case "issued": request.IssuedDate = null; break;
            case "future-issued": request.IssuedDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1); break;
            case "expired": request.ExpiryDate = dossier.Transport.Booking.RequestedDepartureDate.AddDays(-1); break;
            case "departure-day": request.ExpiryDate = dossier.Transport.Booking.RequestedDepartureDate; break;
            case "valid-until": request.ExpiryDate = requirement.ValidUntil!.Value.AddDays(-1); break;
            case "missing-expiry": request.ExpiryDate = null; break;
        }
        var storage = new RecordingStorage();
        var error = await Assert.ThrowsAsync<BadRequestException>(() => CreateService(db, storage).UploadDocumentAsync(account.AccountId,
            dossier.ComplianceDossierId, requirement.DossierRequirementId, request));
        if (invalid is "expired" or "departure-day") Assert.Equal("The document expiry date must be after the planned transport departure date.", error.Message);
        Assert.Empty(storage.Uploaded);
    }

    [Fact]
    public async Task FailedAdditionalVerification_RetainsAssetsAndLeavesRequirementPending()
    {
        await using var db = TestSupport.CreateDb();
        var (account, dossier, requirement) = await SeedAsync(db);
        var request = ValidRequest(dossier);
        request.Files.Add(File("extra.png", "image/png"));
        var storage = new RecordingStorage { FailAtFile = 1 };
        await Assert.ThrowsAsync<IOException>(() => CreateService(db, storage).UploadDocumentAsync(account.AccountId,
            dossier.ComplianceDossierId, requirement.DossierRequirementId, request));
        Assert.Empty(storage.Deleted);
        Assert.Empty(await db.ComplianceDocuments.ToListAsync());
        Assert.Empty(await db.DocumentVersions.ToListAsync());
        Assert.Empty(await db.AuditDossiers.ToListAsync());
        Assert.Equal(DossierRequirementStatus.PENDING, requirement.Status);
    }

    [Fact]
    public async Task DossierChangedDuringVerification_IsRevalidatedAndAssetsAreRetained()
    {
        await using var db = TestSupport.CreateDb();
        var (account, dossier, requirement) = await SeedAsync(db);
        var storage = new RecordingStorage
        {
            AfterUpload = async () =>
            {
                await using var other = new AppDbContext((DbContextOptions<AppDbContext>)db.GetService<IDbContextOptions>());
                var changed = await other.ComplianceDossiers.SingleAsync(item => item.ComplianceDossierId == dossier.ComplianceDossierId);
                changed.Status = ComplianceDossierStatus.SUBMITTED;
                await other.SaveChangesAsync();
            }
        };
        await Assert.ThrowsAsync<ConflictException>(() => CreateService(db, storage).UploadDocumentAsync(account.AccountId,
            dossier.ComplianceDossierId, requirement.DossierRequirementId, ValidRequest(dossier)));
        Assert.Empty(storage.Deleted);
        Assert.Empty(await db.ComplianceDocuments.ToListAsync());
    }

    [Fact]
    public async Task DatabaseFailure_RollsBackWithoutDeletingReferencedAssets()
    {
        var interceptor = new FailingSaveInterceptor();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).AddInterceptors(interceptor)
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
        var (account, dossier, requirement) = await SeedAsync(db);
        interceptor.Fail = true;
        var storage = new RecordingStorage();
        await Assert.ThrowsAsync<DbUpdateException>(() => CreateService(db, storage).UploadDocumentAsync(account.AccountId,
            dossier.ComplianceDossierId, requirement.DossierRequirementId, ValidRequest(dossier)));
        Assert.Empty(storage.Deleted);
        Assert.Empty(await db.ComplianceDocuments.AsNoTracking().ToListAsync());
        Assert.Empty(await db.DocumentVersions.AsNoTracking().ToListAsync());
        Assert.Equal(DossierRequirementStatus.PENDING, (await db.DossierRequirements.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task LostCommitAcknowledgment_RetainsFilesEvenWhenRollbackReturnsSuccessfully()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ReplaceService<IDbContextTransactionManager, LostCommitTransactionManager>().Options);
        var (account, dossier, requirement) = await SeedAsync(db);
        var storage = new RecordingStorage();
        await Assert.ThrowsAsync<IOException>(() => CreateService(db, storage).UploadDocumentAsync(account.AccountId,
            dossier.ComplianceDossierId, requirement.DossierRequirementId, ValidRequest(dossier)));
        Assert.Empty(storage.Deleted);
        Assert.Single(await db.ComplianceDocuments.AsNoTracking().ToListAsync());
        Assert.Single(await db.DocumentVersions.AsNoTracking().ToListAsync());
    }

    internal static DossierRequest.DocumentUploadRequest ValidRequest(Pegaxus.Repository.Entities.ComplianceDossier dossier) => new()
    {
        Files = [File("passport.pdf", "application/pdf")], DocumentNumber = " REF-100 ", IssuingAuthority = " Weatherbys ",
        IssuedDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1), ExpiryDate = dossier.Transport.Booking.RequestedDepartureDate.AddDays(30)
    };

    private static readonly ConcurrentDictionary<string, (string Name, string MimeType, long Size, byte[] Content)> Assets = new();

    internal static Pegaxus.Service.UploadService.Request.UploadedAssetReference File(string name, string mimeType, long? length = null)
    {
        var bytes = mimeType switch
        {
            "image/png" => Convert.FromBase64String("iVBORw0KGgo="),
            "image/jpeg" => Convert.FromBase64String("/9j/"),
            _ => "%PDF-1.7 test"u8.ToArray()
        };
        if (length is > 0 and <= Pegaxus.Service.DocumentStorageService.Service.MaxFileSize)
        {
            var content = new byte[length.Value];
            bytes.AsSpan(0, Math.Min(bytes.Length, content.Length)).CopyTo(content);
            bytes = content;
        }
        var publicId = Guid.NewGuid().ToString("N");
        Assets[publicId] = (name, mimeType, length ?? bytes.Length, bytes);
        return new() { PublicId = publicId };
    }

    internal static async Task<(Account Account, Pegaxus.Repository.Entities.ComplianceDossier Dossier, DossierRequirement Requirement)> SeedAsync(AppDbContext db)
    {
        var (account, customer) = await TestSupport.AddCustomerAsync(db);
        var (booking, _) = await TestSupport.AddPendingBookingAsync(db, customer, withHorse: true);
        var requirement = new DossierRequirement
        {
            DossierRequirementId = Guid.NewGuid(), IsMandatory = true, Status = DossierRequirementStatus.PENDING,
            ValidUntil = booking.RequestedDepartureDate.AddDays(14),
            ComplianceRequirement = new ComplianceRequirement
            {
                ComplianceRequirementId = Guid.NewGuid(), RequirementCode = "PASS", RequirementName = "Horse Passport", ValidityRequired = true
            }
        };
        var dossier = new Pegaxus.Repository.Entities.ComplianceDossier
        {
            ComplianceDossierId = Guid.NewGuid(), DossierCode = "DOS-UPLOAD", Status = ComplianceDossierStatus.IN_PREPARATION,
            Transport = new Transport { TransportId = Guid.NewGuid(), TransportCode = "TP-UPLOAD", Booking = booking }, DossierRequirements = [requirement]
        };
        db.ComplianceDossiers.Add(dossier);
        await db.SaveChangesAsync();
        return (account, dossier, requirement);
    }

    internal static DossierService CreateService(AppDbContext db, RecordingStorage storage) => new(db, storage,
        new Pegaxus.Service.ComplianceDossierService.Validator.InitializeRequestValidator(), null!, storage, NullLogger<DossierService>.Instance);

    internal sealed class RecordingStorage : Pegaxus.Service.DocumentStorageService.IService, Pegaxus.Service.MediaService.IService
    {
        public Exception? ContentFailure { get; init; }
        public Task ValidateContentAsync(Pegaxus.Service.MediaService.PreviewFile file, CancellationToken cancellationToken = default) =>
            ContentFailure is null ? Task.CompletedTask : Task.FromException(ContentFailure);
        public List<StoredDocumentFile> Uploaded { get; } = [];
        public List<string> Deleted { get; } = [];
        public int? FailAtFile { get; init; }
        public Func<Task>? AfterUpload { get; init; }
        public Task<StoredDocumentFile> UploadAsync(Guid versionId, int sortOrder, IFormFile file, CancellationToken cancellationToken = default) => throw new NotSupportedException("UC21/UC22 must not upload binary files again.");
        public Task<string> UploadImageAsync(IFormFile file) => throw new NotSupportedException();
        public Task<(string FileUrl, string PublicId)> UploadFileAsync(IFormFile file) => throw new NotSupportedException();
        public Task DeleteFileAsync(string publicId) => throw new NotSupportedException();
        public Task<Pegaxus.Service.MediaService.SignedUploadResult> CreateSignedUploadAsync(Guid accountId, string entity, string contentType, long sizeBytes) => throw new NotSupportedException();
        public Task<Pegaxus.Service.MediaService.ConfirmedUploadResult> ConfirmUploadAsync(Guid accountId, string publicId, CancellationToken cancellationToken = default)
        {
            if (!Assets.TryGetValue(publicId, out var asset)) throw new NotFoundException("Upload not found", "UPLOAD_NOT_FOUND");
            return Task.FromResult(new Pegaxus.Service.MediaService.ConfirmedUploadResult(publicId,
                $"https://res.cloudinary.com/test-cloud/raw/authenticated/{publicId}", asset.Name, asset.MimeType, asset.Size));
        }
        public async Task<Pegaxus.Service.MediaService.PreviewFile> DownloadPreviewAsync(string storageUrl, CancellationToken cancellationToken = default)
        {
            if (Uploaded.Count == FailAtFile) throw new IOException("Planned verification failure");
            var asset = Assets[storageUrl.Split('/').Last()];
            Uploaded.Add(new StoredDocumentFile(storageUrl, asset.Name, asset.MimeType, asset.Size));
            if (AfterUpload is not null) await AfterUpload();
            return new(asset.Content, asset.MimeType);
        }
        public Task<SignedDocumentUrl> CreateReadUrlAsync(string storageUrl, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SignedDocumentUrl("https://api.cloudinary.com/v1_1/test-cloud/raw/download?signature=test", DateTimeOffset.UtcNow.AddMinutes(5)));
        public Task DeleteAsync(string storageUrl, CancellationToken cancellationToken = default)
        {
            Deleted.Add(storageUrl);
            return Task.CompletedTask;
        }
    }

    internal sealed class LostCommitTransactionManager : IDbContextTransactionManager
    {
        public LostCommitTransactionManager() { }
        public IDbContextTransaction? CurrentTransaction { get; private set; }
        public IDbContextTransaction BeginTransaction() => CurrentTransaction = new LostCommitTransaction();
        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default) => Task.FromResult(BeginTransaction());
        public void CommitTransaction() => CurrentTransaction!.Commit();
        public Task CommitTransactionAsync(CancellationToken cancellationToken = default) => CurrentTransaction!.CommitAsync(cancellationToken);
        public void RollbackTransaction() => CurrentTransaction!.Rollback();
        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) => CurrentTransaction!.RollbackAsync(cancellationToken);
        public void ResetState() => CurrentTransaction = null;
        public Task ResetStateAsync(CancellationToken cancellationToken = default) { ResetState(); return Task.CompletedTask; }
    }

    private sealed class LostCommitTransaction : IDbContextTransaction
    {
        public Guid TransactionId { get; } = Guid.NewGuid();
        public void Commit() => throw new IOException("Commit acknowledgment lost");
        public Task CommitAsync(CancellationToken cancellationToken = default) => throw new IOException("Commit acknowledgment lost");
        public void Rollback() { }
        public Task RollbackAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    internal sealed class FailingSaveInterceptor : SaveChangesInterceptor
    {
        public bool Fail { get; set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            Fail ? throw new DbUpdateException("Planned database failure") : ValueTask.FromResult(result);
    }
}
