using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.ComplianceDossierService;
using Pegaxus.Service.Exceptions;
using DossierService = Pegaxus.Service.ComplianceDossierService.Service;
using DossierEntity = Pegaxus.Repository.Entities.ComplianceDossier;

namespace Pegaxus.Service.Test;

public class DocumentReviewTests
{
    [Fact]
    public async Task Approval_RecordsActorVersionAndUtcTimeWithoutAutomaticallyReadyingOrSubmitting()
    {
        await using var db = TestSupport.CreateDb();
        var (specialist, dossier, requirement, version) = await SeedAsync(db);
        var now = DateTime.UtcNow;
        var result = await CreateService(db).ApproveDocumentAsync(specialist, dossier.ComplianceDossierId, requirement.DossierRequirementId,
            version.DocumentVersionId, Approval(version));
        var review = await db.DocumentReviews.SingleAsync();
        Assert.Equal(DocumentReviewResult.APPROVED, review.Result);
        Assert.Equal(specialist, review.ReviewedByAccountId);
        Assert.Equal(version.DocumentVersionId, review.DocumentVersionId);
        Assert.InRange(review.ReviewedAt, now, DateTime.UtcNow);
        Assert.Equal(review.DocumentReviewId, result.Review.DocumentReviewId);
        Assert.Equal("SATISFIED", result.RequirementStatus);
        Assert.Equal(DossierRequirementStatus.SATISFIED, requirement.Status);
        Assert.NotNull(requirement.SatisfiedAt);
        Assert.Equal(ComplianceDocumentStatus.APPROVED, version.ComplianceDocument.Status);
        Assert.Equal(ComplianceDossierStatus.IN_PREPARATION, dossier.Status);
        Assert.Null(dossier.ReadyForSubmissionAt);
        Assert.Empty(await db.AuthoritySubmissions.ToListAsync());
        var audit = await db.AuditDossiers.SingleAsync();
        Assert.Equal("DOCUMENT_REVIEW_APPROVED", audit.ActionType);
        Assert.Equal(specialist, audit.ActorAccountId);
        Assert.Equal(review.DocumentReviewId, audit.TargetId);
    }

    [Fact]
    public async Task ReadyForSubmission_RequiresAnExplicitSpecialistRequest()
    {
        await using var db = TestSupport.CreateDb();
        var (specialist, dossier, requirement, version) = await SeedAsync(db);
        var service = CreateService(db);
        await service.ApproveDocumentAsync(specialist, dossier.ComplianceDossierId, requirement.DossierRequirementId, version.DocumentVersionId, Approval(version));
        var result = await service.MarkReadyForSubmissionAsync(specialist, dossier.ComplianceDossierId);
        Assert.Equal("READY_FOR_SUBMISSION", result.Status);
        Assert.Equal(ComplianceDossierStatus.READY_FOR_SUBMISSION, dossier.Status);
        Assert.NotNull(dossier.ReadyForSubmissionAt);
        Assert.Empty(await db.AuthoritySubmissions.ToListAsync());
    }

    [Fact]
    public async Task PendingMandatoryRequirement_BlocksReadinessButOptionalRequirementsDoNot()
    {
        await using var db = TestSupport.CreateDb();
        var (specialist, dossier, requirement, version) = await SeedAsync(db);
        var service = CreateService(db);
        await Assert.ThrowsAsync<ConflictException>(() => service.MarkReadyForSubmissionAsync(specialist, dossier.ComplianceDossierId));
        var optional = new DossierRequirement { DossierRequirementId = Guid.NewGuid(), ComplianceDossier = dossier,
            ComplianceRequirement = requirement.ComplianceRequirement, IsMandatory = false, Status = DossierRequirementStatus.PENDING };
        db.DossierRequirements.Add(optional);
        await db.SaveChangesAsync();
        await service.ApproveDocumentAsync(specialist, dossier.ComplianceDossierId, requirement.DossierRequirementId, version.DocumentVersionId, Approval(version));
        await service.MarkReadyForSubmissionAsync(specialist, dossier.ComplianceDossierId);
        Assert.Equal(ComplianceDossierStatus.READY_FOR_SUBMISSION, dossier.Status);
    }

    [Theory]
    [InlineData(DossierRequirementStatus.PROVIDED)]
    [InlineData(DossierRequirementStatus.REJECTED)]
    public async Task Revocation_AppendsReviewPreservingApprovalAndInvalidatingMandatoryReadiness(DossierRequirementStatus status)
    {
        await using var db = TestSupport.CreateDb();
        var (specialist, dossier, requirement, version) = await SeedAsync(db);
        var service = CreateService(db);
        var approved = await service.ApproveDocumentAsync(specialist, dossier.ComplianceDossierId, requirement.DossierRequirementId, version.DocumentVersionId, Approval(version));
        var original = await db.DocumentReviews.SingleAsync();
        var reviewedAt = original.ReviewedAt;
        await service.MarkReadyForSubmissionAsync(specialist, dossier.ComplianceDossierId);
        var result = await service.RevokeDocumentReviewAsync(specialist, dossier.ComplianceDossierId, requirement.DossierRequirementId,
            version.DocumentVersionId, approved.Review.DocumentReviewId, new Request.RevokeDocumentReviewRequest { Status = status, Reason = "Wrong signature" });
        Assert.Equal(DocumentReviewResult.APPROVED, original.Result);
        Assert.Equal(reviewedAt, original.ReviewedAt);
        Assert.Equal(2, await db.DocumentReviews.CountAsync());
        Assert.Equal("REVOKED", result.Review.Result);
        Assert.Equal(status, requirement.Status);
        Assert.Null(requirement.SatisfiedAt);
        Assert.Equal(status == DossierRequirementStatus.PROVIDED ? ComplianceDocumentStatus.PROVIDED : ComplianceDocumentStatus.REJECTED, version.ComplianceDocument.Status);
        Assert.Equal(ComplianceDossierStatus.IN_PREPARATION, dossier.Status);
        Assert.Null(dossier.ReadyForSubmissionAt);
        Assert.Equal("Wrong signature", result.Review.ReviewComment);
        Assert.Single(await db.DocumentVersions.ToListAsync());
    }

    [Fact]
    public async Task ExpiredDocument_RequiresExplicitExceptionReason()
    {
        await using var db = TestSupport.CreateDb();
        var (specialist, dossier, requirement, version) = await SeedAsync(db);
        version.ComplianceDocument.ExpiryDate = dossier.Transport.Booking.RequestedDepartureDate.AddDays(-1);
        await db.SaveChangesAsync();
        var request = Approval(version);
        var service = CreateService(db);
        var error = await Assert.ThrowsAsync<ConflictException>(() => service.ApproveDocumentAsync(specialist, dossier.ComplianceDossierId,
            requirement.DossierRequirementId, version.DocumentVersionId, request));
        Assert.Equal("Warning: Expiry date of this DocumentVersion is before the transport departure date. Are you sure you want to approve?", error.Message);
        Assert.Empty(await db.DocumentReviews.ToListAsync());
        request.ExpiryExceptionReason = "Accepted by the competent authority";
        await service.ApproveDocumentAsync(specialist, dossier.ComplianceDossierId, requirement.DossierRequirementId, version.DocumentVersionId, request);
        Assert.Equal("Accepted by the competent authority", (await db.AuditDossiers.SingleAsync()).Details!.RootElement.GetProperty("Confirmation").GetProperty("ExpiryExceptionReason").GetString());
    }

    [Theory]
    [InlineData(EmployeeRole.LOGISTICS_MANAGER)]
    [InlineData(EmployeeRole.DRIVER)]
    [InlineData(EmployeeRole.ESCORT)]
    public async Task NonSpecialistEmployees_CannotApproveRevokeOrReady(EmployeeRole role)
    {
        await using var db = TestSupport.CreateDb();
        var (_, dossier, requirement, version) = await SeedAsync(db);
        var (account, employee) = await TestSupport.AddLogisticsManagerAsync(db);
        employee.Role = role;
        await db.SaveChangesAsync();
        var service = CreateService(db);
        var error = await Assert.ThrowsAsync<ForbiddenException>(() => service.ApproveDocumentAsync(account.AccountId, dossier.ComplianceDossierId,
            requirement.DossierRequirementId, version.DocumentVersionId, Approval(version)));
        Assert.Equal("Only Transport Specialists can perform DocumentReview.", error.Message);
        await Assert.ThrowsAsync<ForbiddenException>(() => service.RevokeDocumentReviewAsync(account.AccountId, dossier.ComplianceDossierId,
            requirement.DossierRequirementId, version.DocumentVersionId, Guid.NewGuid(), new() { Status = DossierRequirementStatus.PROVIDED, Reason = "Correction" }));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.MarkReadyForSubmissionAsync(account.AccountId, dossier.ComplianceDossierId));
    }

    [Fact]
    public async Task Customer_CannotApprove()
    {
        await using var db = TestSupport.CreateDb();
        var (_, dossier, requirement, version) = await SeedAsync(db);
        var (customer, _) = await TestSupport.AddCustomerAsync(db);
        await Assert.ThrowsAsync<ForbiddenException>(() => CreateService(db).ApproveDocumentAsync(customer.AccountId, dossier.ComplianceDossierId,
            requirement.DossierRequirementId, version.DocumentVersionId, Approval(version)));
    }

    [Theory]
    [InlineData(ComplianceDossierStatus.SUBMITTED)]
    [InlineData(ComplianceDossierStatus.PENDING)]
    [InlineData(ComplianceDossierStatus.APPROVED)]
    [InlineData(ComplianceDossierStatus.CLOSED)]
    public async Task SubmittedDossier_BlocksAllReviewMutations(ComplianceDossierStatus status)
    {
        await using var db = TestSupport.CreateDb();
        var (specialist, dossier, requirement, version) = await SeedAsync(db);
        dossier.Status = status;
        await db.SaveChangesAsync();
        var service = CreateService(db);
        await Assert.ThrowsAsync<ConflictException>(() => service.ApproveDocumentAsync(specialist, dossier.ComplianceDossierId,
            requirement.DossierRequirementId, version.DocumentVersionId, Approval(version)));
        await Assert.ThrowsAsync<ConflictException>(() => service.RevokeDocumentReviewAsync(specialist, dossier.ComplianceDossierId,
            requirement.DossierRequirementId, version.DocumentVersionId, Guid.NewGuid(), new() { Status = DossierRequirementStatus.PROVIDED, Reason = "Correction" }));
        await Assert.ThrowsAsync<ConflictException>(() => service.MarkReadyForSubmissionAsync(specialist, dossier.ComplianceDossierId));
        Assert.Empty(await db.DocumentReviews.ToListAsync());
    }

    [Fact]
    public async Task OldVersionAndStaleFileOrMetadataSnapshots_CannotBeApproved()
    {
        await using var db = TestSupport.CreateDb();
        var (specialist, dossier, requirement, version) = await SeedAsync(db);
        var service = CreateService(db);
        var request = Approval(version);
        version.UploadedAt = version.UploadedAt.AddSeconds(1);
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ConflictException>(() => service.ApproveDocumentAsync(specialist, dossier.ComplianceDossierId,
            requirement.DossierRequirementId, version.DocumentVersionId, request));
        request = Approval(version);
        version.ComplianceDocument.UpdatedAt = version.ComplianceDocument.UpdatedAt.AddSeconds(1);
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ConflictException>(() => service.ApproveDocumentAsync(specialist, dossier.ComplianceDossierId,
            requirement.DossierRequirementId, version.DocumentVersionId, request));
        var next = new DocumentVersion { DocumentVersionId = Guid.NewGuid(), ComplianceDocument = version.ComplianceDocument,
            UploadedByAccountId = version.UploadedByAccountId, VersionNo = 2, FileName = "v2.pdf", FileUrl = "private-file-v2", UploadedAt = DateTime.UtcNow };
        db.DocumentVersions.Add(next);
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ConflictException>(() => service.ApproveDocumentAsync(specialist, dossier.ComplianceDossierId,
            requirement.DossierRequirementId, version.DocumentVersionId, Approval(version)));
        Assert.Empty(await db.DocumentReviews.ToListAsync());
    }

    [Fact]
    public async Task Approval_RequiresMetadataConfirmationAndCannotBeDuplicated()
    {
        await using var db = TestSupport.CreateDb();
        var (specialist, dossier, requirement, version) = await SeedAsync(db);
        var service = CreateService(db);
        var request = Approval(version);
        request.MetadataConfirmed = false;
        await Assert.ThrowsAsync<BadRequestException>(() => service.ApproveDocumentAsync(specialist, dossier.ComplianceDossierId,
            requirement.DossierRequirementId, version.DocumentVersionId, request));
        request.MetadataConfirmed = true;
        await service.ApproveDocumentAsync(specialist, dossier.ComplianceDossierId, requirement.DossierRequirementId, version.DocumentVersionId, request);
        await Assert.ThrowsAsync<ConflictException>(() => service.ApproveDocumentAsync(specialist, dossier.ComplianceDossierId,
            requirement.DossierRequirementId, version.DocumentVersionId, request));
        Assert.Single(await db.DocumentReviews.ToListAsync());
    }

    [Fact]
    public async Task ReapprovalAfterRevocation_PreservesHistoryAndRejectsStaleRevocation()
    {
        await using var db = TestSupport.CreateDb();
        var (specialist, dossier, requirement, version) = await SeedAsync(db);
        var service = CreateService(db);
        var first = await service.ApproveDocumentAsync(specialist, dossier.ComplianceDossierId, requirement.DossierRequirementId,
            version.DocumentVersionId, Approval(version));
        var revoke = new Request.RevokeDocumentReviewRequest { Status = DossierRequirementStatus.PROVIDED, Reason = "Check signature again" };
        await service.RevokeDocumentReviewAsync(specialist, dossier.ComplianceDossierId, requirement.DossierRequirementId,
            version.DocumentVersionId, first.Review.DocumentReviewId, revoke);
        await Assert.ThrowsAsync<ConflictException>(() => service.RevokeDocumentReviewAsync(specialist, dossier.ComplianceDossierId,
            requirement.DossierRequirementId, version.DocumentVersionId, first.Review.DocumentReviewId, revoke));
        var second = await service.ApproveDocumentAsync(specialist, dossier.ComplianceDossierId, requirement.DossierRequirementId,
            version.DocumentVersionId, Approval(version));
        await Assert.ThrowsAsync<ConflictException>(() => service.RevokeDocumentReviewAsync(specialist, dossier.ComplianceDossierId,
            requirement.DossierRequirementId, version.DocumentVersionId, first.Review.DocumentReviewId, revoke));
        Assert.NotEqual(first.Review.DocumentReviewId, second.Review.DocumentReviewId);
        Assert.Equal(3, await db.DocumentReviews.CountAsync());
        Assert.Equal(DocumentReviewResult.APPROVED, (await db.DocumentReviews.SingleAsync(item =>
            item.DocumentReviewId == first.Review.DocumentReviewId)).Result);
        Assert.Equal(DossierRequirementStatus.SATISFIED, requirement.Status);
    }

    [Theory]
    [InlineData(DossierRequirementStatus.PROVIDED, "")]
    [InlineData(DossierRequirementStatus.PROVIDED, "   ")]
    [InlineData(DossierRequirementStatus.SATISFIED, "Correction")]
    [InlineData(DossierRequirementStatus.WAIVED, "Correction")]
    public async Task InvalidRevocation_DoesNotChangeApproval(DossierRequirementStatus status, string reason)
    {
        await using var db = TestSupport.CreateDb();
        var (specialist, dossier, requirement, version) = await SeedAsync(db);
        var service = CreateService(db);
        var approval = await service.ApproveDocumentAsync(specialist, dossier.ComplianceDossierId, requirement.DossierRequirementId,
            version.DocumentVersionId, Approval(version));
        await Assert.ThrowsAsync<BadRequestException>(() => service.RevokeDocumentReviewAsync(specialist, dossier.ComplianceDossierId,
            requirement.DossierRequirementId, version.DocumentVersionId, approval.Review.DocumentReviewId, new() { Status = status, Reason = reason }));
        Assert.Single(await db.DocumentReviews.ToListAsync());
        Assert.Equal(DossierRequirementStatus.SATISFIED, requirement.Status);
    }

    [Fact]
    public async Task InactiveOrDeletedSpecialist_CannotApprove()
    {
        await using var db = TestSupport.CreateDb();
        var (specialist, dossier, requirement, version) = await SeedAsync(db);
        var employee = await db.Employees.Include(item => item.Account).SingleAsync(item => item.AccountId == specialist);
        var service = CreateService(db);
        employee.IsActive = false;
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ForbiddenException>(() => service.ApproveDocumentAsync(specialist, dossier.ComplianceDossierId,
            requirement.DossierRequirementId, version.DocumentVersionId, Approval(version)));
        employee.IsActive = true;
        employee.Account.IsDeleted = true;
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ForbiddenException>(() => service.ApproveDocumentAsync(specialist, dossier.ComplianceDossierId,
            requirement.DossierRequirementId, version.DocumentVersionId, Approval(version)));
        Assert.Empty(await db.DocumentReviews.ToListAsync());
    }

    [Fact]
    public async Task ForeignOrDeletedRequirementAndForeignVersion_CannotBeReviewed()
    {
        await using var db = TestSupport.CreateDb();
        var (specialist, dossier, requirement, version) = await SeedAsync(db);
        var (_, otherDossier, otherRequirement, otherVersion) = await SeedAsync(db);
        var service = CreateService(db);
        await Assert.ThrowsAsync<NotFoundException>(() => service.ApproveDocumentAsync(specialist, dossier.ComplianceDossierId,
            otherRequirement.DossierRequirementId, otherVersion.DocumentVersionId, Approval(otherVersion)));
        await Assert.ThrowsAsync<NotFoundException>(() => service.ApproveDocumentAsync(specialist, otherDossier.ComplianceDossierId,
            requirement.DossierRequirementId, version.DocumentVersionId, Approval(version)));
        await Assert.ThrowsAsync<NotFoundException>(() => service.ApproveDocumentAsync(specialist, dossier.ComplianceDossierId,
            requirement.DossierRequirementId, otherVersion.DocumentVersionId, Approval(otherVersion)));
        requirement.IsDeleted = true;
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<NotFoundException>(() => service.ApproveDocumentAsync(specialist, dossier.ComplianceDossierId,
            requirement.DossierRequirementId, version.DocumentVersionId, Approval(version)));
        Assert.Empty(await db.DocumentReviews.ToListAsync());
    }

    [Fact]
    public async Task StaleReadyDossier_CannotBeSubmittedAfterMandatoryApprovalRevocation()
    {
        await using var db = TestSupport.CreateDb();
        var (specialist, dossier, requirement, version) = await SeedAsync(db);
        var service = CreateService(db);
        var approval = await service.ApproveDocumentAsync(specialist, dossier.ComplianceDossierId, requirement.DossierRequirementId,
            version.DocumentVersionId, Approval(version));
        await service.MarkReadyForSubmissionAsync(specialist, dossier.ComplianceDossierId);
        await using var otherDb = new AppDbContext((DbContextOptions<AppDbContext>)db.GetService<IDbContextOptions>());
        await CreateService(otherDb).RevokeDocumentReviewAsync(specialist, dossier.ComplianceDossierId, requirement.DossierRequirementId,
            version.DocumentVersionId, approval.Review.DocumentReviewId, new() { Status = DossierRequirementStatus.PROVIDED, Reason = "Wrong signature" });
        using var content = new MemoryStream([1]);
        var request = new Request.RecordAuthoritySubmissionRequest
        {
            AuthorityName = "Authority", SubmissionReference = "REF-REVIEW", SubmittedAt = DateTimeOffset.UtcNow,
            ProofFile = new Microsoft.AspNetCore.Http.FormFile(content, 0, 1, "proof", "proof.pdf")
            { Headers = new Microsoft.AspNetCore.Http.HeaderDictionary(), ContentType = "application/pdf" }
        };
        await Assert.ThrowsAsync<ConflictException>(() => service.RecordAuthoritySubmissionAsync(specialist, dossier.ComplianceDossierId, request));
        Assert.Empty(await db.AuthoritySubmissions.ToListAsync());
    }

    [Fact]
    public async Task FailedSave_PreservesPersistedDocumentRequirementAndReviewHistory()
    {
        var interceptor = new FailingReviewSaveInterceptor();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).AddInterceptors(interceptor)
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
        var (specialist, dossier, requirement, version) = await SeedAsync(db);
        interceptor.FailSave = true;
        await Assert.ThrowsAsync<DbUpdateException>(() => CreateService(db).ApproveDocumentAsync(specialist, dossier.ComplianceDossierId,
            requirement.DossierRequirementId, version.DocumentVersionId, Approval(version)));
        Assert.Empty(await db.DocumentReviews.AsNoTracking().ToListAsync());
        Assert.Empty(await db.AuditDossiers.AsNoTracking().ToListAsync());
        Assert.Equal(DossierRequirementStatus.PROVIDED, (await db.DossierRequirements.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(ComplianceDocumentStatus.PROVIDED, (await db.ComplianceDocuments.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task RevokedApproval_AllowsMetadataAndNewVersionWithoutRemovingReviewHistory()
    {
        await using var db = TestSupport.CreateDb();
        var (specialist, dossier, requirement, version) = await SeedAsync(db);
        var service = CreateService(db);
        var approval = await service.ApproveDocumentAsync(specialist, dossier.ComplianceDossierId,
            requirement.DossierRequirementId, version.DocumentVersionId, Approval(version));
        await service.RevokeDocumentReviewAsync(specialist, dossier.ComplianceDossierId, requirement.DossierRequirementId,
            version.DocumentVersionId, approval.Review.DocumentReviewId,
            new Request.RevokeDocumentReviewRequest { Status = DossierRequirementStatus.PROVIDED, Reason = "Replace incorrect file" });
        var storage = new DocumentUploadTests.RecordingStorage();
        service = new DossierService(db, storage, new Pegaxus.Service.ComplianceDossierService.Validator.InitializeRequestValidator(),
            null!, storage, Microsoft.Extensions.Logging.Abstractions.NullLogger<DossierService>.Instance);
        var customerId = version.UploadedByAccountId;
        var metadata = await service.UpdateDocumentMetadataAsync(customerId, dossier.ComplianceDossierId,
            requirement.DossierRequirementId, new Request.DocumentMetadataRequest { DocumentNumber = "EHC-101" });
        Assert.Equal("EHC-101", metadata.DocumentNumber);
        var replacement = await service.SubmitDocumentVersionAsync(customerId, dossier.ComplianceDossierId,
            requirement.DossierRequirementId, DocumentUploadTests.ValidRequest(dossier));
        Assert.True(replacement.IsNewVersion);
        Assert.Equal(2, replacement.VersionNo);
        Assert.Equal("private-file", version.FileUrl);
        Assert.Equal(2, await db.DocumentReviews.CountAsync());
        Assert.Empty(storage.Deleted);
        var latest = await db.DocumentVersions.SingleAsync(item => item.DocumentVersionId == replacement.DocumentVersionId);
        await service.ApproveDocumentAsync(specialist, dossier.ComplianceDossierId, requirement.DossierRequirementId,
            latest.DocumentVersionId, Approval(latest));
        Assert.Equal(DossierRequirementStatus.SATISFIED, requirement.Status);
        Assert.Equal(3, await db.DocumentReviews.CountAsync());
    }

    private sealed class FailingReviewSaveInterceptor : SaveChangesInterceptor
    {
        public bool FailSave { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            FailSave ? throw new DbUpdateException("Simulated review save failure") : ValueTask.FromResult(result);
    }

    [Fact]
    public void RevokedEnumMigration_IsDiscoverableAndAddsNativePostgresValue()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused").Options);
        var sql = db.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>().GenerateScript(
            "20261002090000_AllowReaddingRemovedDossierRequirements", "20261004000000_AddRevokedDocumentReviewResult");
        Assert.Contains("ALTER TYPE document_review_result ADD VALUE IF NOT EXISTS 'REVOKED'", sql);
    }

    private static Request.ApproveDocumentRequest Approval(DocumentVersion version) => new()
    {
        MetadataConfirmed = true, VersionUploadedAt = version.UploadedAt, DocumentUpdatedAt = version.ComplianceDocument.UpdatedAt,
        ReviewNote = "Metadata, signature and stamp verified"
    };

    private static async Task<(Guid Specialist, DossierEntity Dossier, DossierRequirement Requirement, DocumentVersion Version)> SeedAsync(AppDbContext db)
    {
        var (specialist, employee) = await TestSupport.AddLogisticsManagerAsync(db);
        employee.Role = EmployeeRole.TRANSPORT_SPECIALIST;
        var (customer, customerEntity) = await TestSupport.AddCustomerAsync(db);
        var (booking, _) = await TestSupport.AddPendingBookingAsync(db, customerEntity, withHorse: true);
        var now = DateTime.SpecifyKind(DateTime.UtcNow.AddMinutes(-1), DateTimeKind.Unspecified);
        var requirement = new DossierRequirement
        {
            DossierRequirementId = Guid.NewGuid(), IsMandatory = true, Status = DossierRequirementStatus.PROVIDED,
            ComplianceRequirement = new ComplianceRequirement { ComplianceRequirementId = Guid.NewGuid(), RequirementCode = "EHC", RequirementName = "EHC Certificate" }
        };
        var dossier = new DossierEntity { ComplianceDossierId = Guid.NewGuid(), DossierCode = "DOS-REVIEW", Status = ComplianceDossierStatus.IN_PREPARATION,
            Transport = new Transport { TransportId = Guid.NewGuid(), TransportCode = "TP-REVIEW", Booking = booking }, DossierRequirements = [requirement] };
        var document = new ComplianceDocument { ComplianceDocumentId = Guid.NewGuid(), DossierRequirement = requirement, DocumentCode = "CD-REVIEW",
            DocumentName = "EHC Certificate", Status = ComplianceDocumentStatus.PROVIDED, DocumentNumber = "EHC-100", IssuingAuthority = "OV",
            IssuedDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-2), ExpiryDate = booking.RequestedDepartureDate.AddDays(30),
            CreatedByAccountId = customer.AccountId, UpdatedByAccountId = customer.AccountId, CreatedAt = now, UpdatedAt = now };
        var version = new DocumentVersion { DocumentVersionId = Guid.NewGuid(), ComplianceDocument = document, VersionNo = 1,
            FileName = "ehc.pdf", FileUrl = "private-file", MimeType = "application/pdf", UploadedByAccountId = customer.AccountId, UploadedAt = now, CreatedAt = now };
        db.ComplianceDossiers.Add(dossier);
        db.DocumentVersions.Add(version);
        await db.SaveChangesAsync();
        return (specialist.AccountId, dossier, requirement, version);
    }

    private static DossierService CreateService(AppDbContext db) => new(db, null!,
        new Pegaxus.Service.ComplianceDossierService.Validator.InitializeRequestValidator(), null!, null!,
        Microsoft.Extensions.Logging.Abstractions.NullLogger<DossierService>.Instance);
}
