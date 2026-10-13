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

public class MissingDocumentReminderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ManualReminder_RequestsSelectedItemsWithDeadlineAuditEmailAndWebNotification()
    {
        await using var db = TestSupport.CreateDb();
        var seed = await SeedAsync(db, 45);
        var mail = new RecordingMail();
        var service = CreateService(db, mail);
        var result = await service.RequestMissingDocumentsAsync(seed.Specialist, seed.Dossier.ComplianceDossierId, RequestFor(seed));
        Assert.Equal(2, result.Requirements.Count);
        Assert.All(seed.Dossier.DossierRequirements, item => { Assert.Equal(DossierRequirementStatus.REQUESTED, item.Status); Assert.Equal(Today.AddDays(5), item.DueDate); });
        Assert.Equal(2, await db.DocumentRequests.CountAsync());
        Assert.All(await db.DocumentRequests.ToListAsync(), item => Assert.Equal(DocumentRequestType.MISSING_DOCUMENT, item.RequestType));
        Assert.Equal(2, await db.AuditDossiers.CountAsync(item => item.ActionType == "DOSSIER_REQUIREMENT_REMINDED"));
        Assert.Equal(seed.Customer, (await db.AccountNotifications.SingleAsync()).AccountId);
        Assert.Contains("EHC", (await db.Notifications.SingleAsync()).Message);
        Assert.Contains("PASSPORT", Assert.Single(mail.Sent).Body);
        Assert.True(result.EmailSent);
        Assert.NotNull((await db.MissingDocumentReminders.SingleAsync()).EmailSentAt);
        Assert.Equal(ComplianceDossierStatus.IN_PREPARATION, seed.Dossier.Status);
        Assert.Empty(await db.AuthoritySubmissions.ToListAsync());
    }

    [Theory]
    [InlineData(ComplianceDossierStatus.DRAFT)]
    [InlineData(ComplianceDossierStatus.READY_FOR_SUBMISSION)]
    [InlineData(ComplianceDossierStatus.SUBMITTED)]
    [InlineData(ComplianceDossierStatus.REJECTED)]
    [InlineData(ComplianceDossierStatus.APPROVED)]
    [InlineData(ComplianceDossierStatus.CLOSED)]
    public async Task NonPreparingDossier_CannotBeReminded(ComplianceDossierStatus status)
    {
        await using var db = TestSupport.CreateDb();
        var seed = await SeedAsync(db, 45);
        seed.Dossier.Status = status;
        await db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<ConflictException>(() => CreateService(db, new RecordingMail())
            .RequestMissingDocumentsAsync(seed.Specialist, seed.Dossier.ComplianceDossierId, RequestFor(seed)));
        Assert.Equal("Không thể gửi nhắc nhở khi hồ sơ đã hoàn tất hoặc đã đóng.", error.Message);
        Assert.Empty(await db.MissingDocumentReminders.ToListAsync());
    }

    [Fact]
    public async Task AlreadyUploadedOrSatisfiedItems_AreNotMissing()
    {
        await using var db = TestSupport.CreateDb();
        var seed = await SeedAsync(db, 45);
        var requirements = seed.Dossier.DossierRequirements.ToList();
        requirements[0].Status = DossierRequirementStatus.SATISFIED;
        db.DocumentVersions.Add(new DocumentVersion { DocumentVersionId = Guid.NewGuid(), VersionNo = 1, FileName = "passport.pdf", FileUrl = "stored-file",
            UploadedByAccountId = seed.Customer, UploadedAt = Now.UtcDateTime, ComplianceDocument = new ComplianceDocument
            { ComplianceDocumentId = Guid.NewGuid(), DossierRequirement = requirements[1], DocumentCode = "UPLOADED", DocumentName = "Passport",
                Status = ComplianceDocumentStatus.PROVIDED, CreatedByAccountId = seed.Customer, UpdatedByAccountId = seed.Customer } });
        await db.SaveChangesAsync();
        var service = CreateService(db, new RecordingMail());
        Assert.Empty(await service.GetMissingRequirementsAsync(seed.Specialist, seed.Dossier.ComplianceDossierId));
        var error = await Assert.ThrowsAsync<ConflictException>(() => service.RequestMissingDocumentsAsync(seed.Specialist,
            seed.Dossier.ComplianceDossierId, RequestFor(seed)));
        Assert.Equal("Tất cả các mục giấy tờ đã được tải lên hoặc đã được duyệt. Không có giấy tờ nào cần nhắc nộp.", error.Message);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("duplicate")]
    [InlineData("foreign")]
    [InlineData("past")]
    [InlineData("afterDeparture")]
    public async Task InvalidSelectionOrDeadline_IsAtomic(string invalid)
    {
        await using var db = TestSupport.CreateDb();
        var seed = await SeedAsync(db, 45);
        var request = RequestFor(seed);
        switch (invalid)
        {
            case "empty": request.RequirementIds = []; break;
            case "duplicate": request.RequirementIds = [request.RequirementIds[0], request.RequirementIds[0]]; break;
            case "foreign": request.RequirementIds.Add(Guid.NewGuid()); break;
            case "past": request.DueDate = Today.AddDays(-1); break;
            case "afterDeparture": request.DueDate = Today.AddDays(46); break;
        }
        await Assert.ThrowsAnyAsync<Pegaxus.Service.Exceptions.Exceptions>(() => CreateService(db, new RecordingMail())
            .RequestMissingDocumentsAsync(seed.Specialist, seed.Dossier.ComplianceDossierId, request));
        Assert.All(await db.DossierRequirements.AsNoTracking().ToListAsync(), item => Assert.Equal(DossierRequirementStatus.PENDING, item.Status));
        Assert.Empty(await db.DocumentRequests.ToListAsync());
        Assert.Empty(await db.Notifications.ToListAsync());
    }

    [Fact]
    public async Task OnlyActiveSpecialists_CanListOrRequestMissingDocuments()
    {
        await using var db = TestSupport.CreateDb();
        var seed = await SeedAsync(db, 45);
        var (driver, employee) = await TestSupport.AddLogisticsManagerAsync(db);
        employee.Role = EmployeeRole.DRIVER;
        await db.SaveChangesAsync();
        var service = CreateService(db, new RecordingMail());
        foreach (var actor in new[] { seed.Customer, driver.AccountId })
        {
            await Assert.ThrowsAsync<ForbiddenException>(() => service.GetMissingRequirementsAsync(actor, seed.Dossier.ComplianceDossierId));
            await Assert.ThrowsAsync<ForbiddenException>(() => service.RequestMissingDocumentsAsync(actor, seed.Dossier.ComplianceDossierId, RequestFor(seed)));
        }
    }

    [Theory]
    [InlineData(45, false, 1)]
    [InlineData(60, false, 1)]
    [InlineData(61, false, 0)]
    [InlineData(21, true, 1)]
    [InlineData(22, true, 0)]
    [InlineData(-1, false, 0)]
    public async Task AutomaticScan_UsesDomestic21AndInternational60DayWindows(int days, bool domestic, int expected)
    {
        await using var db = TestSupport.CreateDb();
        var seed = await SeedAsync(db, days, domestic);
        var mail = new RecordingMail();
        var service = CreateService(db, mail);
        Assert.Equal(expected, await service.QueueAutomaticMissingDocumentRemindersAsync());
        await service.DeliverPendingMissingDocumentRemindersAsync();
        Assert.Equal(expected, mail.Sent.Count);
        Assert.Equal(expected, await db.Notifications.CountAsync());
        Assert.All(seed.Dossier.DossierRequirements, item => Assert.Equal(DossierRequirementStatus.PENDING, item.Status));
        Assert.Empty(await db.DocumentRequests.ToListAsync());
    }

    [Fact]
    public async Task AutomaticScan_IsDailyAndDoesNotDuplicateAfterRestart()
    {
        await using var db = TestSupport.CreateDb();
        await SeedAsync(db, 45);
        var clock = new FixedTimeProvider(Now);
        var service = CreateService(db, new RecordingMail(), clock);
        Assert.Equal(1, await service.QueueAutomaticMissingDocumentRemindersAsync());
        await using var otherDb = new AppDbContext((DbContextOptions<AppDbContext>)db.GetService<IDbContextOptions>());
        Assert.Equal(0, await CreateService(otherDb, new RecordingMail(), clock).QueueAutomaticMissingDocumentRemindersAsync());
        await service.DeliverPendingMissingDocumentRemindersAsync();
        clock.Now = Now.AddDays(1);
        Assert.Equal(1, await service.QueueAutomaticMissingDocumentRemindersAsync());
        Assert.Equal(2, await db.MissingDocumentReminders.CountAsync());
    }

    [Fact]
    public async Task FailedEmail_RemainsDurableAndRetriesWithoutDuplicatingRequestsOrNotifications()
    {
        await using var db = TestSupport.CreateDb();
        var seed = await SeedAsync(db, 45);
        var clock = new FixedTimeProvider(Now);
        var mail = new RecordingMail { Fail = true };
        var service = CreateService(db, mail, clock);
        var result = await service.RequestMissingDocumentsAsync(seed.Specialist, seed.Dossier.ComplianceDossierId, RequestFor(seed));
        Assert.False(result.EmailSent);
        Assert.NotNull(result.DeliveryWarning);
        Assert.Null((await db.MissingDocumentReminders.SingleAsync()).EmailSentAt);
        clock.Now = Now.AddMinutes(6);
        mail.Fail = false;
        await service.DeliverPendingMissingDocumentRemindersAsync();
        Assert.NotNull((await db.MissingDocumentReminders.SingleAsync()).EmailSentAt);
        Assert.Single(mail.Sent);
        Assert.Single(await db.Notifications.ToListAsync());
        Assert.Equal(2, await db.DocumentRequests.CountAsync());
        await service.DeliverPendingMissingDocumentRemindersAsync();
        Assert.Single(mail.Sent);
    }

    [Fact]
    public async Task ManualReminder_ReusesOpenMissingRequestsAndSuppressesSameDayAutomaticReminder()
    {
        await using var db = TestSupport.CreateDb();
        var seed = await SeedAsync(db, 45);
        var service = CreateService(db, new RecordingMail());
        await service.RequestMissingDocumentsAsync(seed.Specialist, seed.Dossier.ComplianceDossierId, RequestFor(seed));
        var request = RequestFor(seed);
        request.DueDate = Today.AddDays(7);
        await service.RequestMissingDocumentsAsync(seed.Specialist, seed.Dossier.ComplianceDossierId, request);
        Assert.Equal(2, await db.DocumentRequests.CountAsync());
        Assert.All(await db.DocumentRequests.ToListAsync(), item => Assert.Equal(request.DueDate, item.DueDate));
        Assert.Equal(0, await service.QueueAutomaticMissingDocumentRemindersAsync());
    }

    [Fact]
    public async Task CompletedOrCancelledDossiers_AreExcludedFromAutomaticScan()
    {
        await using var db = TestSupport.CreateDb();
        var first = await SeedAsync(db, 45);
        var second = await SeedAsync(db, 45);
        first.Dossier.Status = ComplianceDossierStatus.SUBMITTED;
        second.Dossier.Transport.Booking.Status = BookingStatus.CANCELLED;
        await db.SaveChangesAsync();
        Assert.Equal(0, await CreateService(db, new RecordingMail()).QueueAutomaticMissingDocumentRemindersAsync());
        Assert.Empty(await db.MissingDocumentReminders.ToListAsync());
    }

    [Fact]
    public async Task DeletedRejectedAndWaivedRequirements_AreNotMissing()
    {
        await using var db = TestSupport.CreateDb();
        var first = await SeedAsync(db, 45);
        var second = await SeedAsync(db, 45);
        var requirements = first.Dossier.DossierRequirements.ToList();
        requirements[0].IsDeleted = true;
        requirements[1].Status = DossierRequirementStatus.REJECTED;
        foreach (var item in second.Dossier.DossierRequirements)
            item.Status = DossierRequirementStatus.WAIVED;
        await db.SaveChangesAsync();
        var service = CreateService(db, new RecordingMail());
        Assert.Empty(await service.GetMissingRequirementsAsync(first.Specialist, first.Dossier.ComplianceDossierId));
        Assert.Equal(0, await service.QueueAutomaticMissingDocumentRemindersAsync());
    }

    [Fact]
    public async Task PendingRetry_CancelsAfterAllSelectedDocumentsAreProvided()
    {
        await using var db = TestSupport.CreateDb();
        var seed = await SeedAsync(db, 45);
        var clock = new FixedTimeProvider(Now);
        var mail = new RecordingMail { Fail = true };
        var service = CreateService(db, mail, clock);
        await service.RequestMissingDocumentsAsync(seed.Specialist, seed.Dossier.ComplianceDossierId, RequestFor(seed));
        foreach (var item in seed.Dossier.DossierRequirements)
            item.Status = DossierRequirementStatus.PROVIDED;
        await db.SaveChangesAsync();
        clock.Now = Now.AddMinutes(6);
        mail.Fail = false;
        await service.DeliverPendingMissingDocumentRemindersAsync();
        Assert.NotNull((await db.MissingDocumentReminders.SingleAsync()).CancelledAt);
        Assert.Empty(mail.Sent);
    }

    [Fact]
    public async Task Email_EncodesNotesAndListsOnlyRequirementsStillMissingOnRetry()
    {
        await using var db = TestSupport.CreateDb();
        var seed = await SeedAsync(db, 45);
        var clock = new FixedTimeProvider(Now);
        var mail = new RecordingMail { Fail = true };
        var service = CreateService(db, mail, clock);
        var request = RequestFor(seed);
        request.Note = "<script>alert(1)</script>";
        await service.RequestMissingDocumentsAsync(seed.Specialist, seed.Dossier.ComplianceDossierId, request);
        seed.Dossier.DossierRequirements.Single(item => item.ComplianceRequirement.RequirementCode == "PASSPORT").Status = DossierRequirementStatus.PROVIDED;
        await db.SaveChangesAsync();
        clock.Now = Now.AddMinutes(6);
        mail.Fail = false;
        await service.DeliverPendingMissingDocumentRemindersAsync();
        var body = Assert.Single(mail.Sent).Body;
        Assert.Contains("EHC", body);
        Assert.DoesNotContain("PASSPORT", body);
        Assert.DoesNotContain("<script>", body);
        Assert.Contains("&lt;script&gt;", body);
    }

    [Fact]
    public async Task SaveFailure_PreservesRequirementsAndDoesNotSendNotificationsOrEmail()
    {
        var interceptor = new FailingSaveInterceptor();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).AddInterceptors(interceptor)
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
        var seed = await SeedAsync(db, 45);
        var mail = new RecordingMail();
        interceptor.Fail = true;
        await Assert.ThrowsAsync<DbUpdateException>(() => CreateService(db, mail).RequestMissingDocumentsAsync(seed.Specialist,
            seed.Dossier.ComplianceDossierId, RequestFor(seed)));
        Assert.Empty(await db.MissingDocumentReminders.AsNoTracking().ToListAsync());
        Assert.Empty(await db.Notifications.AsNoTracking().ToListAsync());
        Assert.Empty(await db.DocumentRequests.AsNoTracking().ToListAsync());
        Assert.All(await db.DossierRequirements.AsNoTracking().ToListAsync(), item => Assert.Equal(DossierRequirementStatus.PENDING, item.Status));
        Assert.Empty(mail.Sent);
    }

    [Fact]
    public void OutboxMigration_IsDiscoverableAndAddsDailyUniquenessWithoutDroppingExistingData()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused").Options);
        var script = db.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>().GenerateScript(
            "20261002090000_AllowReaddingRemovedDossierRequirements", "20261004100000_AddMissingDocumentReminders");
        Assert.Contains("MissingDocumentReminder_daily_key", script);
        Assert.Contains("automatic_date IS NOT NULL", script);
        Assert.Contains("requirement_ids uuid[]", script);
        Assert.DoesNotContain("DROP TABLE", script);
    }

    [Fact]
    public async Task ManualReminder_AllowsRequestedInitialUploadWithoutLosingRequest()
    {
        await using var db = TestSupport.CreateDb();
        var seed = await SeedAsync(db, 45);
        var mail = new RecordingMail();
        var service = CreateService(db, mail);
        var requirement = seed.Dossier.DossierRequirements.First();
        var request = RequestFor(seed);
        request.RequirementIds = [requirement.DossierRequirementId];
        await service.RequestMissingDocumentsAsync(seed.Specialist, seed.Dossier.ComplianceDossierId, request);
        Assert.Equal(DossierRequirementStatus.REQUESTED, requirement.Status);
        var storage = new DocumentUploadTests.RecordingStorage();
        service = new DossierService(db, storage, new Pegaxus.Service.ComplianceDossierService.Validator.InitializeRequestValidator(),
            new Pegaxus.Service.NotificationService.Service(db, new Pegaxus.Service.NotificationService.NoopNotificationRealtimeService()),
            storage, Microsoft.Extensions.Logging.Abstractions.NullLogger<DossierService>.Instance, mail, new FixedTimeProvider(Now));
        var upload = await service.UploadDocumentAsync(seed.Customer, seed.Dossier.ComplianceDossierId,
            requirement.DossierRequirementId, DocumentUploadTests.ValidRequest(seed.Dossier));
        Assert.Equal(1, upload.VersionNo);
        Assert.Equal(DossierRequirementStatus.PROVIDED, requirement.Status);
        Assert.Equal(DocumentRequestType.MISSING_DOCUMENT, (await db.DocumentRequests.SingleAsync()).RequestType);
        Assert.Single(await db.MissingDocumentReminders.ToListAsync());
        Assert.DoesNotContain(await service.GetMissingRequirementsAsync(seed.Specialist, seed.Dossier.ComplianceDossierId),
            item => item.DossierRequirementId == requirement.DossierRequirementId);
    }

    private sealed class FailingSaveInterceptor : SaveChangesInterceptor
    {
        public bool Fail { get; set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            Fail ? throw new DbUpdateException("Simulated save failure") : ValueTask.FromResult(result);
    }

    private static DateOnly Today => DateOnly.FromDateTime(Now.UtcDateTime);
    private static Request.MissingDocumentsRequest RequestFor((Guid Specialist, Guid Customer, ComplianceDossier Dossier) seed) => new()
    { RequirementIds = seed.Dossier.DossierRequirements.Select(item => item.DossierRequirementId).ToList(), DueDate = Today.AddDays(5), Note = "Vui lòng nộp bản scan rõ nét." };

    private static DossierService CreateService(AppDbContext db, RecordingMail mail, FixedTimeProvider? clock = null) => new(db, null!,
        new Pegaxus.Service.ComplianceDossierService.Validator.InitializeRequestValidator(),
        new Pegaxus.Service.NotificationService.Service(db, new Pegaxus.Service.NotificationService.NoopNotificationRealtimeService()),
        null!, Microsoft.Extensions.Logging.Abstractions.NullLogger<DossierService>.Instance, mail, clock ?? new FixedTimeProvider(Now));

    private static async Task<(Guid Specialist, Guid Customer, ComplianceDossier Dossier)> SeedAsync(AppDbContext db, int days, bool domestic = false)
    {
        var (specialist, employee) = await TestSupport.AddLogisticsManagerAsync(db);
        employee.Role = EmployeeRole.TRANSPORT_SPECIALIST;
        var (customer, customerEntity) = await TestSupport.AddCustomerAsync(db);
        var (booking, _) = await TestSupport.AddPendingBookingAsync(db, customerEntity, withHorse: true);
        booking.RequestedDepartureDate = Today.AddDays(days);
        booking.OriginCountry = " France ";
        booking.DestinationCountry = domestic ? "france" : "Italy";
        var dossier = new ComplianceDossier { ComplianceDossierId = Guid.NewGuid(), DossierCode = "DOS-MISSING", Status = ComplianceDossierStatus.IN_PREPARATION,
            Transport = new Transport { TransportId = Guid.NewGuid(), TransportCode = "TP-MISSING", Booking = booking } };
        foreach (var code in new[] { "EHC", "PASSPORT" })
            dossier.DossierRequirements.Add(new DossierRequirement { DossierRequirementId = Guid.NewGuid(), IsMandatory = true,
                Status = DossierRequirementStatus.PENDING, ComplianceRequirement = new ComplianceRequirement { ComplianceRequirementId = Guid.NewGuid(),
                    RequirementCode = code, RequirementName = code } });
        db.ComplianceDossiers.Add(dossier);
        await db.SaveChangesAsync();
        return (specialist.AccountId, customer.AccountId, dossier);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class RecordingMail : Mail.IService
    {
        public List<Mail.MailContent> Sent { get; } = [];
        public bool Fail { get; set; }
        public Task SendMail(Mail.MailContent content, CancellationToken cancellationToken = default)
        {
            if (Fail) throw new IOException("SMTP unavailable");
            Sent.Add(content);
            return Task.CompletedTask;
        }
    }
}
