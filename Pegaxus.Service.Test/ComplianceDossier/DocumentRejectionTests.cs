using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.ComplianceDossierService;
using Pegaxus.Service.Exceptions;

using DossierService = Pegaxus.Service.ComplianceDossierService.Service;
using Mail = Pegaxus.Service.MailService;

namespace Pegaxus.Service.Test;

public class DocumentRejectionTests
{
    [Fact]
    public async Task Rejection_CreatesImmutableReviewRequestAuditAndCustomerNotifications()
    {
        await using var db = TestSupport.CreateDb();
        var seed = await SeedAsync(db);
        var mail = new CaptureMailService();
        var service = CreateService(db, mail);
        var result = await service.RejectDocumentAsync(seed.Specialist, seed.Dossier.ComplianceDossierId,
            seed.Requirement.DossierRequirementId, seed.Version.DocumentVersionId, RejectRequest(seed.Version));
        var review = await db.DocumentReviews.SingleAsync();
        var request = await db.DocumentRequests.SingleAsync();
        Assert.Matches(@"^DREQ-\d{8}-\d{4}$", request.RequestCode);
        Assert.Equal(DocumentReviewResult.REJECTED, review.Result);
        Assert.Equal(seed.Specialist, review.ReviewedByAccountId);
        Assert.Equal(seed.Version.DocumentVersionId, review.DocumentVersionId);
        Assert.Equal(review.DocumentReviewId, request.DocumentReviewId);
        Assert.Equal(DocumentRequestType.REUPLOAD, request.RequestType);
        Assert.Equal(DocumentRequestStatus.OPEN, request.Status);
        Assert.Equal("UNCLEAR_IMAGE", request.ErrorCategory);
        Assert.Equal(RejectRequest(seed.Version).RejectionNote, request.Message);
        Assert.Equal(DossierRequirementStatus.REJECTED, seed.Requirement.Status);
        Assert.Equal(ComplianceDocumentStatus.REJECTED, seed.Version.ComplianceDocument.Status);
        Assert.Equal(ComplianceDossierStatus.IN_PREPARATION, seed.Dossier.Status);
        Assert.Null(seed.Dossier.ReadyForSubmissionAt);
        Assert.Null(seed.Requirement.SatisfiedAt);
        Assert.Equal(DateTimeKind.Utc, result.Review.ReviewedAt.Kind);
        Assert.Equal(2, await db.AuditDossiers.CountAsync());
        Assert.Equal(seed.Customer, (await db.AccountNotifications.SingleAsync()).AccountId);
        Assert.Contains(request.Message, (await db.Notifications.SingleAsync()).Message);
        Assert.True(result.EmailSent);
        Assert.Contains("scan", Assert.Single(mail.Sent).Body);
        Assert.Empty(await db.AuthoritySubmissions.ToListAsync());
        var detail = await service.GetRequirementAsync(seed.Customer, seed.Dossier.ComplianceDossierId, seed.Requirement.DossierRequirementId);
        Assert.Equal(request.Message, Assert.Single(detail.DocumentRequests).Message);
        var overview = await service.GetDossierAsync(seed.Customer, seed.Dossier.ComplianceDossierId, new());
        Assert.Equal(request.Message, Assert.Single(overview.TripRequirements).OpenDocumentRequest!.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("short reason")]
    [InlineData("              ")]
    public async Task ShortInstructions_BlockAllWrites(string note)
    {
        await using var db = TestSupport.CreateDb();
        var seed = await SeedAsync(db);
        var mail = new CaptureMailService();
        var request = RejectRequest(seed.Version);
        request.RejectionNote = note;
        var error = await Assert.ThrowsAsync<BadRequestException>(() => CreateService(db, mail).RejectDocumentAsync(seed.Specialist,
            seed.Dossier.ComplianceDossierId, seed.Requirement.DossierRequirementId, seed.Version.DocumentVersionId, request));
        Assert.Equal("Please provide detailed instructions in the DocumentRequest (minimum 15 characters) so the customer knows what to re-upload.", error.Message);
        Assert.Empty(await db.DocumentReviews.ToListAsync());
        Assert.Empty(await db.DocumentRequests.ToListAsync());
        Assert.Empty(await db.Notifications.ToListAsync());
        Assert.Empty(mail.Sent);
    }

    [Theory]
    [InlineData(ComplianceDossierStatus.SUBMITTED)]
    [InlineData(ComplianceDossierStatus.PENDING)]
    [InlineData(ComplianceDossierStatus.APPROVED)]
    [InlineData(ComplianceDossierStatus.CLOSED)]
    public async Task SubmittedDossier_BlocksDocumentRequests(ComplianceDossierStatus status)
    {
        await using var db = TestSupport.CreateDb();
        var seed = await SeedAsync(db);
        seed.Dossier.Status = status;
        await db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<ConflictException>(() => CreateService(db, new CaptureMailService()).RejectDocumentAsync(seed.Specialist,
            seed.Dossier.ComplianceDossierId, seed.Requirement.DossierRequirementId, seed.Version.DocumentVersionId, RejectRequest(seed.Version)));
        Assert.Equal("Cannot create DocumentRequests after dossier submission to official authorities.", error.Message);
        Assert.Empty(await db.DocumentRequests.ToListAsync());
    }

    [Fact]
    public async Task Rejection_CanUseExplicitRequestedAndRejectedStatuses()
    {
        await using var db = TestSupport.CreateDb();
        var seed = await SeedAsync(db);
        var request = RejectRequest(seed.Version);
        request.RequirementStatus = DossierRequirementStatus.REQUESTED;
        request.DossierStatus = ComplianceDossierStatus.REJECTED;
        await CreateService(db, new CaptureMailService()).RejectDocumentAsync(seed.Specialist, seed.Dossier.ComplianceDossierId,
            seed.Requirement.DossierRequirementId, seed.Version.DocumentVersionId, request);
        Assert.Equal(DossierRequirementStatus.REQUESTED, seed.Requirement.Status);
        Assert.Equal(ComplianceDossierStatus.REJECTED, seed.Dossier.Status);
    }

    [Fact]
    public async Task StaleOrDuplicateReview_CannotReject()
    {
        await using var db = TestSupport.CreateDb();
        var seed = await SeedAsync(db);
        var service = CreateService(db, new CaptureMailService());
        var request = RejectRequest(seed.Version);
        seed.Version.UploadedAt = seed.Version.UploadedAt.AddSeconds(1);
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ConflictException>(() => service.RejectDocumentAsync(seed.Specialist, seed.Dossier.ComplianceDossierId,
            seed.Requirement.DossierRequirementId, seed.Version.DocumentVersionId, request));
        request = RejectRequest(seed.Version);
        await service.RejectDocumentAsync(seed.Specialist, seed.Dossier.ComplianceDossierId, seed.Requirement.DossierRequirementId,
            seed.Version.DocumentVersionId, request);
        await Assert.ThrowsAsync<ConflictException>(() => service.RejectDocumentAsync(seed.Specialist, seed.Dossier.ComplianceDossierId,
            seed.Requirement.DossierRequirementId, seed.Version.DocumentVersionId, request));
        Assert.Single(await db.DocumentReviews.ToListAsync());
    }

    [Fact]
    public async Task CustomerAndDriver_CannotReject()
    {
        await using var db = TestSupport.CreateDb();
        var seed = await SeedAsync(db);
        var (driver, employee) = await TestSupport.AddLogisticsManagerAsync(db);
        employee.Role = EmployeeRole.DRIVER;
        await db.SaveChangesAsync();
        var service = CreateService(db, new CaptureMailService());
        foreach (var actor in new[] { seed.Customer, driver.AccountId })
            await Assert.ThrowsAsync<ForbiddenException>(() => service.RejectDocumentAsync(actor, seed.Dossier.ComplianceDossierId,
                seed.Requirement.DossierRequirementId, seed.Version.DocumentVersionId, RejectRequest(seed.Version)));
        Assert.Empty(await db.DocumentRequests.ToListAsync());
    }

    [Fact]
    public async Task Comments_AreOwnedAppendOnlyAndNotifyTheOtherParticipant()
    {
        await using var db = TestSupport.CreateDb();
        var seed = await SeedAsync(db);
        var service = CreateService(db, new CaptureMailService());
        var rejected = await service.RejectDocumentAsync(seed.Specialist, seed.Dossier.ComplianceDossierId, seed.Requirement.DossierRequirementId,
            seed.Version.DocumentVersionId, RejectRequest(seed.Version));
        var requestId = rejected.DocumentRequest.DocumentRequestId;
        var (stranger, _) = await TestSupport.AddCustomerAsync(db);
        await Assert.ThrowsAsync<ForbiddenException>(() => service.AddDocumentRequestCommentAsync(stranger.AccountId, seed.Dossier.ComplianceDossierId,
            seed.Requirement.DossierRequirementId, requestId, new() { Message = "Please clarify" }));
        await service.AddDocumentRequestCommentAsync(seed.Customer, seed.Dossier.ComplianceDossierId,
            seed.Requirement.DossierRequirementId, requestId, new() { Message = "Which page needs another scan?" });
        await service.AddDocumentRequestCommentAsync(seed.Specialist, seed.Dossier.ComplianceDossierId,
            seed.Requirement.DossierRequirementId, requestId, new() { Message = "Please scan the vaccination page." });
        var comments = await service.GetDocumentRequestCommentsAsync(seed.Customer, seed.Dossier.ComplianceDossierId,
            seed.Requirement.DossierRequirementId, requestId);
        Assert.Equal(2, comments.Count);
        Assert.Equal(seed.Customer, comments[0].AuthorAccountId);
        Assert.Equal(seed.Specialist, comments[1].AuthorAccountId);
        Assert.Equal(3, await db.Notifications.CountAsync());
        Assert.Equal(1, await db.AccountNotifications.CountAsync(item => item.AccountId == seed.Specialist));
    }

    [Fact]
    public async Task MailFailure_DoesNotLoseCommittedRequestAndReturnsDeliveryWarning()
    {
        await using var db = TestSupport.CreateDb();
        var seed = await SeedAsync(db);
        var result = await CreateService(db, new CaptureMailService { Fail = true }).RejectDocumentAsync(seed.Specialist,
            seed.Dossier.ComplianceDossierId, seed.Requirement.DossierRequirementId, seed.Version.DocumentVersionId, RejectRequest(seed.Version));
        Assert.False(result.EmailSent);
        Assert.NotNull(result.DeliveryWarning);
        Assert.Single(await db.DocumentRequests.ToListAsync());
        Assert.Single(await db.Notifications.ToListAsync());
    }

    [Theory]
    [InlineData("category")]
    [InlineData("deadline")]
    [InlineData("requirementStatus")]
    [InlineData("dossierStatus")]
    [InlineData("snapshot")]
    public async Task InvalidRejectionFields_DoNotWrite(string field)
    {
        await using var db = TestSupport.CreateDb();
        var seed = await SeedAsync(db);
        var request = RejectRequest(seed.Version);
        switch (field)
        {
            case "category": request.ErrorCategory = (Request.DocumentErrorCategory)999; break;
            case "deadline": request.RevisionDeadline = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1); break;
            case "requirementStatus": request.RequirementStatus = DossierRequirementStatus.SATISFIED; break;
            case "dossierStatus": request.DossierStatus = ComplianceDossierStatus.SUBMITTED; break;
            case "snapshot": request.DocumentUpdatedAt = default; break;
        }
        await Assert.ThrowsAsync<BadRequestException>(() => CreateService(db, new CaptureMailService()).RejectDocumentAsync(seed.Specialist,
            seed.Dossier.ComplianceDossierId, seed.Requirement.DossierRequirementId, seed.Version.DocumentVersionId, request));
        Assert.Empty(await db.DocumentReviews.ToListAsync());
        Assert.Empty(await db.DocumentRequests.ToListAsync());
    }

    [Fact]
    public async Task ForeignDeletedAndOldVersions_CannotBeRejected()
    {
        await using var db = TestSupport.CreateDb();
        var seed = await SeedAsync(db);
        var other = await SeedAsync(db);
        var service = CreateService(db, new CaptureMailService());
        await Assert.ThrowsAsync<NotFoundException>(() => service.RejectDocumentAsync(seed.Specialist, seed.Dossier.ComplianceDossierId,
            other.Requirement.DossierRequirementId, other.Version.DocumentVersionId, RejectRequest(other.Version)));
        await Assert.ThrowsAsync<NotFoundException>(() => service.RejectDocumentAsync(seed.Specialist, seed.Dossier.ComplianceDossierId,
            seed.Requirement.DossierRequirementId, other.Version.DocumentVersionId, RejectRequest(other.Version)));
        seed.Requirement.IsDeleted = true;
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<NotFoundException>(() => service.RejectDocumentAsync(seed.Specialist, seed.Dossier.ComplianceDossierId,
            seed.Requirement.DossierRequirementId, seed.Version.DocumentVersionId, RejectRequest(seed.Version)));
        seed.Requirement.IsDeleted = false;
        db.DocumentVersions.Add(new DocumentVersion { DocumentVersionId = Guid.NewGuid(), ComplianceDocument = seed.Version.ComplianceDocument,
            UploadedByAccountId = seed.Customer, VersionNo = 2, FileName = "v2.pdf", FileUrl = "new-file", UploadedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ConflictException>(() => service.RejectDocumentAsync(seed.Specialist, seed.Dossier.ComplianceDossierId,
            seed.Requirement.DossierRequirementId, seed.Version.DocumentVersionId, RejectRequest(seed.Version)));
        Assert.Empty(await db.DocumentRequests.ToListAsync());
    }

    [Fact]
    public async Task Email_EncodesInstructionsInsteadOfExecutingCustomerMarkup()
    {
        await using var db = TestSupport.CreateDb();
        var seed = await SeedAsync(db);
        var mail = new CaptureMailService();
        var request = RejectRequest(seed.Version);
        request.RejectionNote = "Please scan <script>alert(1)</script> again.";
        await CreateService(db, mail).RejectDocumentAsync(seed.Specialist, seed.Dossier.ComplianceDossierId,
            seed.Requirement.DossierRequirementId, seed.Version.DocumentVersionId, request);
        Assert.DoesNotContain("<script>", Assert.Single(mail.Sent).Body);
        Assert.Contains("&lt;script&gt;", mail.Sent[0].Body);
        Assert.Equal(request.RejectionNote, (await db.DocumentRequests.SingleAsync()).Message);
    }

    [Fact]
    public async Task FailedSave_DoesNotPersistReviewsRequestsOrNotificationsOrSendEmail()
    {
        var interceptor = new FailingSaveInterceptor();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).AddInterceptors(interceptor)
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
        var seed = await SeedAsync(db);
        var mail = new CaptureMailService();
        interceptor.Fail = true;
        await Assert.ThrowsAsync<DbUpdateException>(() => CreateService(db, mail).RejectDocumentAsync(seed.Specialist,
            seed.Dossier.ComplianceDossierId, seed.Requirement.DossierRequirementId, seed.Version.DocumentVersionId, RejectRequest(seed.Version)));
        Assert.Empty(await db.DocumentReviews.AsNoTracking().ToListAsync());
        Assert.Empty(await db.DocumentRequests.AsNoTracking().ToListAsync());
        Assert.Empty(await db.Notifications.AsNoTracking().ToListAsync());
        Assert.Equal(DossierRequirementStatus.PROVIDED, (await db.DossierRequirements.AsNoTracking().SingleAsync()).Status);
        Assert.Empty(mail.Sent);
    }

    [Fact]
    public async Task StaleReadiness_CannotSubmitAfterRejection()
    {
        await using var db = TestSupport.CreateDb();
        var seed = await SeedAsync(db);
        seed.Requirement.IsMandatory = false;
        seed.Dossier.Status = ComplianceDossierStatus.READY_FOR_SUBMISSION;
        await db.SaveChangesAsync();
        await using var otherDb = new AppDbContext((DbContextOptions<AppDbContext>)db.GetService<IDbContextOptions>());
        await CreateService(otherDb, new CaptureMailService()).RejectDocumentAsync(seed.Specialist, seed.Dossier.ComplianceDossierId,
            seed.Requirement.DossierRequirementId, seed.Version.DocumentVersionId, RejectRequest(seed.Version));
        using var content = new MemoryStream([1]);
        var request = new Request.RecordAuthoritySubmissionRequest
        {
            AuthorityName = "Authority", SubmissionReference = "REF-REJECT", SubmittedAt = DateTimeOffset.UtcNow,
            ProofFile = new Microsoft.AspNetCore.Http.FormFile(content, 0, 1, "proof", "proof.pdf")
            { Headers = new Microsoft.AspNetCore.Http.HeaderDictionary(), ContentType = "application/pdf" }
        };
        await Assert.ThrowsAsync<ConflictException>(() => CreateService(db, new CaptureMailService())
            .RecordAuthoritySubmissionAsync(seed.Specialist, seed.Dossier.ComplianceDossierId, request));
        Assert.Empty(await db.AuthoritySubmissions.ToListAsync());
    }

    [Fact]
    public void RevisionMigration_IsDiscoverableAndPreservesExistingRequests()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused").Options);
        var script = db.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>().GenerateScript(
            "20261002090000_AllowReaddingRemovedDossierRequirements", "20261003000000_AddDocumentRequestRevisions");
        Assert.Contains("DocumentRequestComment", script);
        Assert.Contains("ADD document_review_id uuid", script);
        Assert.DoesNotContain("DROP TABLE", script);
    }

    [Theory]
    [InlineData(DossierRequirementStatus.REJECTED)]
    [InlineData(DossierRequirementStatus.REQUESTED)]
    public async Task Rejection_AllowsCustomerReplacementPreservingReviewAndRequest(DossierRequirementStatus status)
    {
        await using var db = TestSupport.CreateDb();
        var seed = await SeedAsync(db);
        var mail = new CaptureMailService();
        var request = RejectRequest(seed.Version);
        request.RequirementStatus = status;
        var rejection = await CreateService(db, mail).RejectDocumentAsync(seed.Specialist,
            seed.Dossier.ComplianceDossierId, seed.Requirement.DossierRequirementId, seed.Version.DocumentVersionId, request);
        var storage = new DocumentUploadTests.RecordingStorage();
        var service = new DossierService(db, storage, new Pegaxus.Service.ComplianceDossierService.Validator.InitializeRequestValidator(),
            new Pegaxus.Service.NotificationService.Service(db, new Pegaxus.Service.NotificationService.NoopNotificationRealtimeService()),
            storage, Microsoft.Extensions.Logging.Abstractions.NullLogger<DossierService>.Instance, mail);
        var replacement = await service.SubmitDocumentVersionAsync(seed.Customer, seed.Dossier.ComplianceDossierId,
            seed.Requirement.DossierRequirementId, DocumentUploadTests.ValidRequest(seed.Dossier));
        Assert.True(replacement.IsNewVersion);
        Assert.Equal(2, replacement.VersionNo);
        Assert.Equal(DossierRequirementStatus.PROVIDED, seed.Requirement.Status);
        Assert.Equal("private-file", seed.Version.FileUrl);
        var review = await db.DocumentReviews.SingleAsync();
        Assert.Equal(rejection.Review.DocumentReviewId, review.DocumentReviewId);
        Assert.Equal(DocumentReviewResult.REJECTED, review.Result);
        Assert.Equal(rejection.DocumentRequest.DocumentRequestId, (await db.DocumentRequests.SingleAsync()).DocumentRequestId);
        Assert.Empty(storage.Deleted);
        var latest = await db.DocumentVersions.SingleAsync(item => item.DocumentVersionId == replacement.DocumentVersionId);
        await service.ApproveDocumentAsync(seed.Specialist, seed.Dossier.ComplianceDossierId,
            seed.Requirement.DossierRequirementId, latest.DocumentVersionId, new Request.ApproveDocumentRequest
            {
                MetadataConfirmed = true, VersionUploadedAt = latest.UploadedAt,
                DocumentUpdatedAt = latest.ComplianceDocument.UpdatedAt
            });
        Assert.Equal(DossierRequirementStatus.SATISFIED, seed.Requirement.Status);
        Assert.Equal(2, await db.DocumentReviews.CountAsync());
    }

    private sealed class FailingSaveInterceptor : SaveChangesInterceptor
    {
        public bool Fail { get; set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            Fail ? throw new DbUpdateException("Simulated save failure") : ValueTask.FromResult(result);
    }

    private static Request.RejectDocumentRequest RejectRequest(DocumentVersion version) => new()
    {
        ErrorCategory = Request.DocumentErrorCategory.UNCLEAR_IMAGE,
        RejectionNote = "Please scan the unclear stamp again.", RevisionDeadline = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1),
        VersionUploadedAt = version.UploadedAt, DocumentUpdatedAt = version.ComplianceDocument.UpdatedAt
    };

    private static DossierService CreateService(AppDbContext db, Mail.IMailService mail) => new(db, null!,
        new Pegaxus.Service.ComplianceDossierService.Validator.InitializeRequestValidator(),
        new Pegaxus.Service.NotificationService.Service(db, new Pegaxus.Service.NotificationService.NoopNotificationRealtimeService()),
        null!, Microsoft.Extensions.Logging.Abstractions.NullLogger<DossierService>.Instance, mail);

    private static async Task<(Guid Specialist, Guid Customer, ComplianceDossier Dossier, DossierRequirement Requirement, DocumentVersion Version)> SeedAsync(AppDbContext db)
    {
        var (specialist, employee) = await TestSupport.AddLogisticsManagerAsync(db);
        employee.Role = EmployeeRole.TRANSPORT_SPECIALIST;
        var (customer, customerEntity) = await TestSupport.AddCustomerAsync(db);
        var (booking, _) = await TestSupport.AddPendingBookingAsync(db, customerEntity, withHorse: true);
        var now = DateTime.SpecifyKind(DateTime.UtcNow.AddMinutes(-1), DateTimeKind.Unspecified);
        var requirement = new DossierRequirement { DossierRequirementId = Guid.NewGuid(), IsMandatory = true, Status = DossierRequirementStatus.PROVIDED,
            ComplianceRequirement = new ComplianceRequirement { ComplianceRequirementId = Guid.NewGuid(), RequirementCode = "COGGINS", RequirementName = "Coggins Test" } };
        var dossier = new ComplianceDossier { ComplianceDossierId = Guid.NewGuid(), DossierCode = "DOS-REJECT", Status = ComplianceDossierStatus.IN_PREPARATION,
            Transport = new Transport { TransportId = Guid.NewGuid(), TransportCode = "TP-REJECT", Booking = booking }, DossierRequirements = [requirement] };
        var document = new ComplianceDocument { ComplianceDocumentId = Guid.NewGuid(), DossierRequirement = requirement, DocumentCode = "CD-REJECT",
            DocumentName = "Coggins Test", Status = ComplianceDocumentStatus.PROVIDED, CreatedByAccountId = customer.AccountId,
            UpdatedByAccountId = customer.AccountId, CreatedAt = now, UpdatedAt = now };
        var version = new DocumentVersion { DocumentVersionId = Guid.NewGuid(), ComplianceDocument = document, VersionNo = 1, FileName = "coggins.pdf",
            FileUrl = "private-file", UploadedByAccountId = customer.AccountId, UploadedAt = now, CreatedAt = now };
        db.ComplianceDossiers.Add(dossier);
        db.DocumentVersions.Add(version);
        await db.SaveChangesAsync();
        return (specialist.AccountId, customer.AccountId, dossier, requirement, version);
    }

    private sealed class CaptureMailService : Mail.IMailService
    {
        public List<Mail.MailContent> Sent { get; } = [];
        public bool Fail { get; init; }
        public Task SendMail(Mail.MailContent content, CancellationToken cancellationToken = default)
        {
            if (Fail) throw new IOException("SMTP unavailable");
            Sent.Add(content);
            return Task.CompletedTask;
        }
    }
}
