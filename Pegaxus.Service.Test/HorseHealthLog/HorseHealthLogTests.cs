using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Common;
using Pegaxus.Service.DocumentStorageService;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.HorseHealthLogService.Validator;
using Pegaxus.Service.MediaService;
using Pegaxus.Service.NotificationService;

using HealthLogs = Pegaxus.Service.HorseHealthLogService;

namespace Pegaxus.Service.Test;

public class HorseHealthLogTests
{
    [Fact]
    public async Task Create_PersistsFullImmutableCheckWithSignedPhotoAndCurrentEscort()
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        var request = f.Request();
        request.TemperatureCelsius = 38.2m; request.HeartRateBpm = 38; request.RespiratoryRateBpm = 14;
        request.HealthStatus = "AT_RISK"; request.HydrationStatus = "MILD_DEHYDRATION";
        request.BehaviorNote = " Restless "; request.SymptomNote = " Sweating "; request.InjuryNote = " Scratch ";
        request.FeedingNote = " Half hay "; request.HydrationNote = " Drank water ";
        request.TreatmentProvided = " Cleaned "; request.MedicationGiven = " Medication ";
        request.LocationDescription = " Lille "; request.GeneralNote = " Details ";
        request.Files = [Photo()];
        var result = await f.Service.CreateAsync(f.Transport.TransportId, request, f.Escort.AccountId);
        Assert.True(result.Created);
        var log = result.Value;
        Assert.Equal(f.Transport.TransportId, log.TransportId); Assert.Equal(f.Horse.HorseId, log.Horse.HorseId);
        Assert.Equal(f.Horse.PassportNumber, log.Horse.PassportNumber); Assert.Equal(f.Escort.EmployeeId, log.CheckedBy.EmployeeId);
        Assert.Equal(f.Escort.FullName, log.CheckedBy.FullName); Assert.Equal(f.Escort.EmployeeCode, log.CheckedBy.EmployeeCode);
        Assert.Equal(DbTime.AsUtc(DbTime.Truncate(request.CheckedAt!.Value)), log.CheckedAt);
        Assert.Equal(38.2m, log.TemperatureCelsius); Assert.Equal(38, log.HeartRateBpm); Assert.Equal(14, log.RespiratoryRateBpm);
        Assert.Equal("AT_RISK", log.HealthStatus); Assert.Equal("MILD_DEHYDRATION", log.HydrationStatus);
        Assert.Equal("Restless", log.BehaviorNote); Assert.Equal("Sweating", log.SymptomNote); Assert.Equal("Scratch", log.InjuryNote);
        Assert.Equal("Half hay", log.FeedingNote); Assert.Equal("Drank water", log.HydrationNote);
        Assert.Equal("Cleaned", log.TreatmentProvided); Assert.Equal("Medication", log.MedicationGiven);
        Assert.Equal("Details", log.GeneralNote); Assert.Equal("Lille", log.LocationDescription);
        Assert.False(log.Effects.EmergencyNotified); Assert.False(log.Effects.CanReportIncident);
        Assert.True(log.HasPhoto); Assert.Null(log.GpsCoordinates);
        var photo = Assert.Single(log.Photos); Assert.Equal(Storage.SignedUrl, photo.PhotoUrl);
        Assert.Null(photo.Latitude); Assert.Null(photo.Longitude); Assert.Null(photo.CapturedAt); Assert.Null(photo.Caption);
        var stored = await db.HorseHealthLogs.SingleAsync();
        Assert.Equal(Storage.Url(Assert.Single(f.Storage.Uploads)), Assert.Single(stored.PhotoUrls));
        Assert.Equal(DbTime.AsUtc(Fixture.Now), log.CreatedAt); Assert.Equal(log.CreatedAt, log.UpdatedAt);
        Assert.Empty(f.Storage.Deletes); Assert.Empty(await db.IncidentReports.ToListAsync()); Assert.Empty(await db.Notifications.ToListAsync());
    }

    [Theory]
    [InlineData("CRITICAL", false)]
    [InlineData("NORMAL", true)]
    [InlineData("CRITICAL", true)]
    public async Task Emergency_IsAtomicTargetsOnlyActiveManagersAndAssignedCoordinatorsAndReplayHasNoEffects(string healthStatus, bool vet)
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        var inactive = await EmployeeAsync(db, EmployeeRole.LOGISTICS_MANAGER); inactive.IsActive = false;
        var deleted = await EmployeeAsync(db, EmployeeRole.LOGISTICS_MANAGER); deleted.IsDeleted = true;
        var blocked = await EmployeeAsync(db, EmployeeRole.LOGISTICS_MANAGER); blocked.Account.Status = AccountStatus.INACTIVE;
        var unassigned = await EmployeeAsync(db, EmployeeRole.FLEET_ROUTE_COORDINATOR);
        (await db.EmployeeTransportPlans.SingleAsync()).Responsibility = "COORDINATOR";
        await db.SaveChangesAsync();
        var request = f.Request(); request.HealthStatus = healthStatus;
        request.VeterinarianRequired = vet; request.VeterinarianNote = vet ? " Call vet " : null;
        request.Files = [Photo()];
        var result = await f.Service.CreateAsync(f.Transport.TransportId, request, f.Escort.AccountId);
        Assert.True(result.Created); Assert.True(result.Value.Effects.EmergencyNotified);
        Assert.Equal(healthStatus == "CRITICAL", result.Value.Effects.CanReportIncident);
        var alerts = await db.Notifications.ToArrayAsync(); Assert.Equal(2, alerts.Length);
        Assert.All(alerts, alert =>
        {
            Assert.Equal(NotificationType.HORSE_HEALTH, alert.NotificationType); Assert.Equal(NotificationPriority.URGENT, alert.Priority);
            Assert.Equal("Critical horse health alert", alert.Title); Assert.Equal("HorseHealthLog", alert.ReferenceType);
            Assert.Equal(result.Value.HorseHealthLogId, alert.ReferenceId);
            Assert.Equal($"/admin/ongoing-transports/{f.Transport.TransportId}/health", alert.ActionUrl);
        });
        Assert.Equal(new[] { f.Manager.AccountId, f.Coordinator.AccountId }.Order(), (await db.AccountNotifications.Select(item => item.AccountId).ToListAsync()).Order());
        Assert.Equal(2, f.Realtime.Messages.Count);
        request.HealthStatus = "NORMAL"; request.GeneralNote = "Must not overwrite";
        result = await f.Service.CreateAsync(f.Transport.TransportId, request, f.Escort.AccountId);
        Assert.False(result.Created); Assert.Equal(healthStatus, result.Value.HealthStatus);
        Assert.Null(result.Value.GeneralNote); Assert.Equal(vet ? "Call vet" : null, result.Value.VeterinarianNote);
        Assert.False(result.Value.Effects.EmergencyNotified); Assert.False(result.Value.Effects.CanReportIncident);
        Assert.Equal(1, await db.HorseHealthLogs.CountAsync()); Assert.Equal(2, await db.Notifications.CountAsync());
        Assert.Equal(2, f.Realtime.Messages.Count); Assert.Single(f.Storage.Uploads); Assert.Empty(f.Storage.Deletes);
        Assert.Empty(await db.IncidentReports.ToListAsync());
    }

    [Fact]
    public async Task Batch_UsesOnePhotoForEveryHorseAndReplayCreatesNothing()
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        var request = new HealthLogs.Request.RecordRequest { LogType = "PRE_TRIP_CHECK", CheckedAt = Fixture.CheckTime,
            GeneralNote = " All well ", LocationDescription = " Stable ", Files = [Photo()] };
        var result = await f.Service.MarkAllNormalAsync(f.Transport.TransportId, request, f.Escort.AccountId);
        Assert.True(result.Created); Assert.Equal(2, result.Value.CreatedCount); Assert.Equal(2, result.Value.Logs.Count);
        Assert.All(result.Value.Logs, log =>
        {
            Assert.Equal("NORMAL", log.HealthStatus); Assert.Equal("NORMAL", log.HydrationStatus);
            Assert.Equal("PRE_TRIP_CHECK", log.LogType); Assert.Equal(f.Escort.EmployeeId, log.CheckedBy.EmployeeId);
            Assert.True(log.HasPhoto); Assert.False(log.VeterinarianRequired);
        });
        var logs = await db.HorseHealthLogs.ToArrayAsync();
        Assert.Single(logs.SelectMany(log => log.PhotoUrls).Distinct()); Assert.Single(f.Storage.Uploads); Assert.Equal(0, f.Storage.Signs);
        Assert.All(logs, log =>
        {
            Assert.Null(log.TemperatureCelsius); Assert.Null(log.HeartRateBpm); Assert.Null(log.RespiratoryRateBpm);
            Assert.Equal("All well", log.GeneralNote); Assert.Equal("Stable", log.LocationDescription);
        });
        result = await f.Service.MarkAllNormalAsync(f.Transport.TransportId, request, f.Escort.AccountId);
        Assert.False(result.Created); Assert.Equal(0, result.Value.CreatedCount); Assert.Equal(2, result.Value.Logs.Count);
        Assert.Equal(2, await db.HorseHealthLogs.CountAsync()); Assert.Single(f.Storage.Uploads);
        Assert.Empty(await db.Notifications.ToListAsync()); Assert.Empty(f.Realtime.Messages);
    }

    [Fact]
    public async Task Batch_PartialReplayPreservesExistingAbnormalCheckAndCreatesOnlyMissingHorse()
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        var request = f.Request(); request.HealthStatus = "CRITICAL";
        var first = await f.Service.CreateAsync(f.Transport.TransportId, request, f.Escort.AccountId);
        var result = await f.Service.MarkAllNormalAsync(f.Transport.TransportId,
            new() { LogType = request.LogType, CheckedAt = request.CheckedAt }, f.Escort.AccountId);
        Assert.True(result.Created); Assert.Equal(1, result.Value.CreatedCount);
        Assert.Equal("CRITICAL", result.Value.Logs.Single(log => log.Horse.HorseId == f.Horse.HorseId).HealthStatus);
        Assert.Contains(result.Value.Logs, log => log.HorseHealthLogId == first.Value.HorseHealthLogId);
        Assert.Equal(2, await db.HorseHealthLogs.CountAsync()); Assert.Equal(2, await db.Notifications.CountAsync());
    }

    [Theory]
    [InlineData("upload")]
    [InlineData("sign")]
    [InlineData("save")]
    public async Task Failures_RollBackBeforePersistenceAndCleanUploadedPhoto(string failure)
    {
        var audit = new SaveAudit();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)).AddInterceptors(audit).Options);
        var f = await Fixture.CreateAsync(db);
        f.Storage.FailUpload = failure == "upload"; f.Storage.FailSign = failure == "sign";
        audit.Enabled = true; audit.Fail = failure == "save";
        var request = f.Request(); request.HealthStatus = "CRITICAL"; request.Files = [Photo()];
        await Assert.ThrowsAsync<IOException>(() => f.Service.CreateAsync(f.Transport.TransportId, request, f.Escort.AccountId));
        Assert.Single(f.Storage.Uploads); Assert.Equal(f.Storage.Uploads, f.Storage.Deletes);
        Assert.Empty(f.Realtime.Messages);
        // InMemory has no rollback; PostgreSQL below verifies rollback after a successful SaveChanges.
        if (failure != "sign")
        {
            Assert.Equal(0, await db.HorseHealthLogs.CountAsync()); Assert.Equal(0, await db.Notifications.CountAsync());
        }
    }

    [Fact]
    public async Task Create_UsesOneSaveForLogAndAlertsAndDefaultsCheckTime()
    {
        var audit = new SaveAudit();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)).AddInterceptors(audit).Options);
        var f = await Fixture.CreateAsync(db); audit.Enabled = true;
        var request = f.Request(); request.CheckedAt = null; request.HealthStatus = "CRITICAL";
        var result = await f.Service.CreateAsync(f.Transport.TransportId, request, f.Escort.AccountId);
        Assert.Equal(DbTime.AsUtc(Fixture.Now), result.Value.CheckedAt); Assert.Equal(1, audit.Calls);
        Assert.True(audit.HadLogAndAlerts); Assert.Equal(2, f.Realtime.Messages.Count);
    }

    [Theory]
    [InlineData("logType", "")]
    [InlineData("logType", "1")]
    [InlineData("logType", "routine_check")]
    [InlineData("healthStatus", "UNKNOWN")]
    [InlineData("healthStatus", "1")]
    [InlineData("hydrationStatus", "")]
    [InlineData("hydrationStatus", "1")]
    [InlineData("future", "")]
    [InlineData("precision", "")]
    [InlineData("vetRequired", "")]
    [InlineData("vetUnexpected", "")]
    [InlineData("noteLength", "")]
    public async Task Validation_RejectsInvalidEnumsNotesPrecisionAndFutureChecks(string field, string value)
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db); var request = f.Request();
        switch (field)
        {
            case "logType": request.LogType = value; break;
            case "healthStatus": request.HealthStatus = value; break;
            case "hydrationStatus": request.HydrationStatus = value; break;
            case "future": request.CheckedAt = DbTime.AsUtc(Fixture.Now).AddMinutes(5).AddTicks(1); break;
            case "precision": request.TemperatureCelsius = 38.25m; break;
            case "vetRequired": request.VeterinarianRequired = true; break;
            case "vetUnexpected": request.VeterinarianNote = " "; break;
            case "noteLength": request.TreatmentProvided = new string('x', 2001); break;
        }
        Assert.Equal("VALIDATION_ERROR", (await Assert.ThrowsAsync<BadRequestException>(() =>
            f.Service.CreateAsync(f.Transport.TransportId, request, f.Escort.AccountId))).ErrorCode);
        Assert.Equal(0, await db.HorseHealthLogs.CountAsync()); Assert.Empty(f.Storage.Uploads);
    }

    [Theory]
    [InlineData(29.9, null, null)]
    [InlineData(43.1, null, null)]
    [InlineData(null, 9, null)]
    [InlineData(null, 201, null)]
    [InlineData(null, null, 1)]
    [InlineData(null, null, 101)]
    public async Task VitalSigns_RejectBiologicallyImpossibleValues(double? temperature, int? heartRate, int? respiratoryRate)
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db); var request = f.Request();
        request.TemperatureCelsius = temperature.HasValue ? (decimal)temperature.Value : null;
        request.HeartRateBpm = heartRate; request.RespiratoryRateBpm = respiratoryRate;
        Assert.Equal("VITAL_SIGNS_OUT_OF_RANGE", (await Assert.ThrowsAsync<UnprocessableEntityException>(() =>
            f.Service.CreateAsync(f.Transport.TransportId, request, f.Escort.AccountId))).ErrorCode);
    }

    [Theory]
    [InlineData(30, 10, 2)]
    [InlineData(43, 200, 100)]
    public async Task VitalSigns_AcceptRangeBoundariesAndFiveMinuteClockSkew(int temperature, int heartRate, int respiratoryRate)
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db); var request = f.Request();
        request.TemperatureCelsius = temperature; request.HeartRateBpm = heartRate; request.RespiratoryRateBpm = respiratoryRate;
        request.CheckedAt = DbTime.AsUtc(Fixture.Now).AddMinutes(5);
        Assert.True((await f.Service.CreateAsync(f.Transport.TransportId, request, f.Escort.AccountId)).Created);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("pdf")]
    [InlineData("mime")]
    [InlineData("signature")]
    [InlineData("oversized")]
    [InlineData("length")]
    public async Task Photos_RejectInvalidMetadataAndContentBeforeStorage(string kind)
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db); var request = f.Request();
        var bytes = kind == "signature" ? "invalid"u8.ToArray() : Png;
        request.Files = [new FormFile(new MemoryStream(bytes), 0, kind switch { "empty" => 0, "oversized" => 5 * 1024 * 1024 + 1,
            "length" => bytes.Length + 1, _ => bytes.Length }, "files", kind == "pdf" ? "file.pdf" : "file.png")
            { Headers = new HeaderDictionary(), ContentType = kind == "mime" ? "image/jpeg" : kind == "pdf" ? "application/pdf" : "image/png" }];
        Assert.Equal("INVALID_IMAGE", (await Assert.ThrowsAsync<BadRequestException>(() =>
            f.Service.CreateAsync(f.Transport.TransportId, request, f.Escort.AccountId))).ErrorCode);
        Assert.Empty(f.Storage.Uploads); Assert.Empty(f.Storage.Deletes); Assert.Equal(0, await db.HorseHealthLogs.CountAsync());
    }

    [Theory]
    [InlineData("wrongRole", false)]
    [InlineData("unassigned", false)]
    [InlineData("superseded", false)]
    [InlineData("executed", true)]
    [InlineData("outsourced", false)]
    [InlineData("draft", false)]
    public async Task Access_UsesActualEscortRoleAndNonSupersededAssignment(string scenario, bool allowed)
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        if (scenario == "wrongRole") f.Escort.Role = EmployeeRole.DRIVER;
        if (scenario == "unassigned") f.Segment.EscortEmployeeId = null;
        if (scenario is "superseded" or "executed") f.Segment.SupersededAt = Fixture.Now;
        if (scenario == "executed") f.Segment.ActualDepartureAt = Fixture.Now.AddHours(-1);
        if (scenario == "draft") f.RoutePlan.Status = RoutePlanStatus.DRAFT;
        if (scenario == "outsourced")
        {
            f.Segment.TransportProvider = PersonnelAssignmentTests.Provider(); db.Add(f.Segment.TransportProvider);
        }
        await db.SaveChangesAsync();
        if (allowed) Assert.True((await f.Service.CreateAsync(f.Transport.TransportId, f.Request(), f.Escort.AccountId)).Created);
        else
        {
            var error = await Assert.ThrowsAsync<ForbiddenException>(() => f.Service.CreateAsync(f.Transport.TransportId, f.Request(), f.Escort.AccountId));
            Assert.Equal("Only the assigned escort can record horse health logs", error.Message);
            await Assert.ThrowsAsync<ForbiddenException>(() => f.Service.MarkAllNormalAsync(f.Transport.TransportId, new() { LogType = "OTHER" }, f.Escort.AccountId));
            Assert.Equal(0, await db.HorseHealthLogs.CountAsync());
        }
    }

    [Theory]
    [InlineData("inactiveAccount")]
    [InlineData("deletedAccount")]
    [InlineData("inactiveEmployee")]
    [InlineData("deletedEmployee")]
    [InlineData("missingProfile")]
    public async Task Access_RejectsInactiveOrMissingActor(string scenario)
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        if (scenario == "inactiveAccount") f.Escort.Account.Status = AccountStatus.INACTIVE;
        if (scenario == "deletedAccount") f.Escort.Account.IsDeleted = true;
        if (scenario == "inactiveEmployee") f.Escort.IsActive = false;
        if (scenario == "deletedEmployee") f.Escort.IsDeleted = true;
        if (scenario == "missingProfile") db.Remove(f.Escort);
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<UnauthorizedException>(() => f.Service.CreateAsync(f.Transport.TransportId, f.Request(), f.Escort.AccountId));
        Assert.Empty(f.Storage.Uploads);
    }

    [Theory]
    [InlineData(TransportStatus.CREATED)]
    [InlineData(TransportStatus.PLANNING)]
    [InlineData(TransportStatus.COMPLETED)]
    [InlineData(TransportStatus.CANCELLED)]
    public async Task Lifecycle_RejectsWritesOutsideReadyAndTransit(TransportStatus status)
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        f.Transport.Status = status; await db.SaveChangesAsync();
        Assert.Equal("INVALID_TRANSPORT_STATUS", (await Assert.ThrowsAsync<ConflictException>(() =>
            f.Service.CreateAsync(f.Transport.TransportId, f.Request(), f.Escort.AccountId))).ErrorCode);
        await Assert.ThrowsAsync<ConflictException>(() => f.Service.MarkAllNormalAsync(f.Transport.TransportId, new() { LogType = "OTHER" }, f.Escort.AccountId));
        Assert.Empty(f.Storage.Uploads);
    }

    [Fact]
    public async Task Membership_RejectsUnknownTransportForeignHorseAndEmptyBatch()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db); var request = f.Request();
        Assert.Equal("TRANSPORT_NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(() =>
            f.Service.CreateAsync(Guid.NewGuid(), request, f.Escort.AccountId))).ErrorCode);
        request.HorseId = Guid.NewGuid();
        Assert.Equal("HORSE_NOT_IN_TRANSPORT", (await Assert.ThrowsAsync<UnprocessableEntityException>(() =>
            f.Service.CreateAsync(f.Transport.TransportId, request, f.Escort.AccountId))).ErrorCode);
        db.BookingHorses.RemoveRange(await db.BookingHorses.ToArrayAsync()); await db.SaveChangesAsync();
        Assert.Equal("NO_HORSES_IN_TRANSPORT", (await Assert.ThrowsAsync<UnprocessableEntityException>(() =>
            f.Service.MarkAllNormalAsync(f.Transport.TransportId, new() { LogType = "OTHER" }, f.Escort.AccountId))).ErrorCode);
        Assert.Empty(f.Storage.Uploads);
    }

    internal static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a8N8AAAAASUVORK5CYII=");
    internal static IFormFile Photo() => new FormFile(new MemoryStream(Png), 0, Png.Length, "files", "photo.png")
        { Headers = new HeaderDictionary(), ContentType = "image/png" };

    internal static async Task<Employee> EmployeeAsync(AppDbContext db, EmployeeRole role)
    {
        var (_, employee) = await TestSupport.AddLogisticsManagerAsync(db); employee.Role = role;
        await db.SaveChangesAsync(); return employee;
    }

    internal sealed class Fixture(AppDbContext db)
    {
        internal static readonly DateTime Now = new(2026, 10, 16, 12, 0, 0, DateTimeKind.Unspecified);
        internal static DateTimeOffset CheckTime => DbTime.AsUtc(Now.AddMinutes(-1)).AddTicks(19);
        internal required Employee Manager { get; init; }
        internal required Employee Coordinator { get; init; }
        internal required Employee Escort { get; init; }
        internal required Horse Horse { get; init; }
        internal required Transport Transport { get; init; }
        internal required RoutePlan RoutePlan { get; init; }
        internal required RoutePlanRoute Segment { get; init; }
        internal Storage Storage { get; } = new();
        internal Realtime Realtime { get; } = new();
        internal HealthLogs.Service Service => CreateService(db, Storage, new Pegaxus.Service.NotificationService.Service(db, Realtime));
        internal HealthLogs.Request.CreateRequest Request() => new() { HorseId = Horse.HorseId, LogType = "ROUTINE_CHECK",
            HealthStatus = "NORMAL", HydrationStatus = "NORMAL", CheckedAt = CheckTime };

        internal static async Task<Fixture> CreateAsync(AppDbContext db)
        {
            var manager = await EmployeeAsync(db, EmployeeRole.LOGISTICS_MANAGER);
            var coordinator = await EmployeeAsync(db, EmployeeRole.FLEET_ROUTE_COORDINATOR);
            var escort = await EmployeeAsync(db, EmployeeRole.ESCORT);
            var (_, customer) = await TestSupport.AddCustomerAsync(db);
            var (booking, horse) = await TestSupport.AddPendingBookingAsync(db, customer, withHorse: true);
            horse!.PassportNumber = "826GB123456789"; horse.MicrochipNumber = "826123456789012";
            db.Add(new BookingHorse { Booking = booking, Horse = new Horse { HorseId = Guid.NewGuid(), Customer = customer, HorseName = "Thunder" } });
            var transport = new Transport { TransportId = Guid.NewGuid(), TransportCode = "TR-" + Guid.NewGuid().ToString("N"), Booking = booking,
                Status = TransportStatus.READY, CreatedAt = Now, UpdatedAt = Now };
            var plan = new TransportPlan { TransportPlanId = Guid.NewGuid(), PlanCode = "TP-" + Guid.NewGuid().ToString("N"), Transport = transport,
                Status = TransportPlanStatus.APPROVED, CreatedAt = Now, UpdatedAt = Now };
            var route = new RoutePlan { RoutePlanId = Guid.NewGuid(), RoutePlanCode = "RP-" + Guid.NewGuid().ToString("N"), TransportPlan = plan,
                Status = RoutePlanStatus.CONFIRMED, CreatedAt = Now, UpdatedAt = Now };
            var segment = PersonnelAssignmentTests.Segment(route, 1); segment.EscortEmployeeId = escort.EmployeeId;
            db.Add(segment); db.Add(new EmployeeTransportPlan { Employee = coordinator, TransportPlan = plan, Responsibility = "FLEET_ROUTE_COORDINATOR", IsPrimary = true });
            await db.SaveChangesAsync();
            return new(db) { Manager = manager, Coordinator = coordinator, Escort = escort, Horse = horse, Transport = transport, RoutePlan = route, Segment = segment };
        }
    }

    internal static HealthLogs.Service CreateService(AppDbContext db, Storage storage, INotificationService notifications)
    {
        var validator = new RecordRequestValidator(new FixedClock());
        return new(db, new FixedClock(), validator, new CreateRequestValidator(validator), new AddPhotosRequestValidator(), storage, storage, notifications, NullLogger<HealthLogs.Service>.Instance);
    }

    internal sealed class FixedClock : TimeProvider { public override DateTimeOffset GetUtcNow() => DbTime.AsUtc(Fixture.Now); }

    internal sealed class Storage : IMediaService, IDocumentStorageService
    {
        internal const string SignedUrl = "https://signed.example.com/health?signature=test";
        internal static string Url(Guid id) => $"https://res.cloudinary.com/test-cloud/image/authenticated/health/{id:N}.png";
        internal List<Guid> Uploads { get; } = [];
        internal List<Guid> Deletes { get; } = [];
        internal int Signs { get; private set; }
        internal bool FailUpload { get; set; }
        internal int? FailAtUpload { get; set; }
        internal bool FailSign { get; set; }
        internal Func<Task>? AfterUpload { get; set; }
        public async Task<string> UploadPrivateImageAsync(Guid imageId, PreviewFile image, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested(); Assert.True(Images.IsValid(image.Content, image.ContentType));
            Uploads.Add(imageId); if (AfterUpload is not null) await AfterUpload();
            if (FailUpload || Uploads.Count == FailAtUpload) throw new IOException("Planned upload failure"); return Url(imageId);
        }
        public Task DeletePrivateImageAsync(Guid imageId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested(); Deletes.Add(imageId); return Task.CompletedTask;
        }
        public Task<SignedDocumentUrl> CreateReadUrlAsync(string url, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested(); Signs++;
            if (FailSign) throw new IOException("Planned signing failure");
            return Task.FromResult(new SignedDocumentUrl(SignedUrl, DbTime.AsUtc(Fixture.Now).AddMinutes(5)));
        }
        public Task<string> UploadImageAsync(IFormFile file) => throw new NotSupportedException();
        public Task<(string FileUrl, string PublicId)> UploadFileAsync(IFormFile file) => throw new NotSupportedException();
        public Task DeleteFileAsync(string publicId) => throw new NotSupportedException();
        public Task<SignedUploadResult> CreateSignedUploadAsync(Guid accountId, string entity, string contentType, long sizeBytes) => throw new NotSupportedException();
        public Task<ConfirmedUploadResult> ConfirmUploadAsync(Guid accountId, string publicId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PreviewFile> DownloadPreviewAsync(string fileUrl, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ValidateContentAsync(PreviewFile file, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StoredDocumentFile> UploadAsync(Guid versionId, int sortOrder, IFormFile file, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(string storageUrl, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    internal sealed class Realtime : INotificationRealtimeService
    {
        internal List<NotificationRealtimeDto> Messages { get; } = [];
        public Task SendToAccountAsync(Guid accountId, NotificationRealtimeDto notification, CancellationToken cancellationToken = default)
        {
            Messages.Add(notification); return Task.CompletedTask;
        }
    }

    private sealed class SaveAudit : SaveChangesInterceptor
    {
        internal bool Enabled { get; set; }
        internal bool Fail { get; set; }
        internal int Calls { get; private set; }
        internal bool HadLogAndAlerts { get; private set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Enabled)
            {
                Calls++; HadLogAndAlerts = eventData.Context!.ChangeTracker.Entries<HorseHealthLog>().Any(entry => entry.State == EntityState.Added) &&
                    eventData.Context.ChangeTracker.Entries<Notification>().Count(entry => entry.State == EntityState.Added) == 2;
                if (Fail) throw new IOException("Planned database failure");
            }
            return ValueTask.FromResult(result);
        }
    }
}
