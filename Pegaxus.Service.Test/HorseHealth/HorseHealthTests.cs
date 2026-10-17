using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Common;
using Pegaxus.Service.DocumentStorageService;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.HorseHealthService.Validator;
using Pegaxus.Service.MediaService;

using Health = Pegaxus.Service.HorseHealthService;

namespace Pegaxus.Service.Test;

public class HorseHealthTests
{
    [Fact]
    public async Task Overview_UsesLatestCheckWithinTransportAndIncludesUncheckedHorses()
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        var (booking, _) = await TestSupport.AddPendingBookingAsync(db, f.Customer);
        var other = new Transport { TransportId = Guid.NewGuid(), Booking = booking, TransportCode = "TR-OTHER" };
        db.Add(other);
        db.Add(f.Log(9, other, f.Horse, Fixture.Now.AddDays(1)));
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var result = await f.Service.GetOverviewAsync(f.Transport.TransportId, f.Manager.AccountId);
        Assert.Equal(Fixture.ServerTime, result.ServerTime);
        Assert.Equal("IN_TRANSIT", result.TransportStatus);
        var horse = Assert.Single(result.Horses, item => item.Horse.HorseId == f.Horse.HorseId);
        Assert.Equal(4, horse.LogCount);
        Assert.Equal(f.Latest.HorseHealthLogId, horse.LatestLog!.HorseHealthLogId);
        Assert.Equal("AT_RISK", horse.LatestHealthStatus);
        Assert.Equal("Restless", horse.LatestLog.LatestNote);
        Assert.Equal(f.Recorder.EmployeeId, horse.LatestLog.CheckedBy.EmployeeId);
        Assert.Equal(f.Escort.EmployeeId, result.CurrentEscort!.EmployeeId);
        Assert.Equal(f.Escort.Phone, result.CurrentEscort.Phone);
        Assert.Equal("ESCORT", result.CurrentEscort.Role);
        Assert.Equal(f.Horse.PassportNumber, horse.Horse.PassportNumber);
        var uncheckedHorse = Assert.Single(result.Horses, item => item.Horse.HorseId == f.UncheckedHorse.HorseId);
        Assert.Null(uncheckedHorse.LatestHealthStatus); Assert.Null(uncheckedHorse.LatestLog); Assert.Equal(0, uncheckedHorse.LogCount);
        var customer = await f.Service.GetOverviewAsync(f.Transport.TransportId, f.Customer.AccountId);
        Assert.Null(customer.CurrentEscort!.Phone);
        Assert.Equal(horse.LatestLog, customer.Horses.Single(item => item.Horse.HorseId == f.Horse.HorseId).LatestLog);
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Empty(await db.Notifications.ToListAsync()); Assert.Empty(await db.OutboxMessages.ToListAsync());
        Assert.Empty(f.Storage.Reads);
    }

    [Fact]
    public async Task History_FiltersAndPaginatesInFixedStableOrder()
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        db.ChangeTracker.Clear();
        var page = await f.Service.GetLogsAsync(f.Transport.TransportId, f.Horse.HorseId, new() { PageSize = 2 }, f.Customer.AccountId);
        Assert.Equal(Fixture.ServerTime, page.ServerTime);
        Assert.Equal(4, page.TotalItems); Assert.Equal(2, page.TotalPages);
        Assert.Equal(f.Logs.Reverse().Take(2).Select(log => log.HorseHealthLogId), page.Items.Select(log => log.HorseHealthLogId));
        Assert.True(page.Items[0].HasPhoto); Assert.False(page.Items[1].HasPhoto);
        Assert.Equal(f.Recorder.FullName, page.Items[0].CheckedBy.FullName);
        page = await f.Service.GetLogsAsync(f.Transport.TransportId, f.Horse.HorseId, new() { PageIndex = 1, PageSize = 2 }, f.Customer.AccountId);
        Assert.Equal(f.Logs.Reverse().Skip(2).Select(log => log.HorseHealthLogId), page.Items.Select(log => log.HorseHealthLogId));
        page = await f.Service.GetLogsAsync(f.Transport.TransportId, f.Horse.HorseId, new()
        {
            LogType = " ROUTINE_CHECK,REST_STOP_CHECK,ROUTINE_CHECK ", HealthStatus = "CRITICAL,AT_RISK"
        }, f.Customer.AccountId);
        Assert.Equal(2, page.TotalItems); Assert.Equal(1, page.TotalPages);
        Assert.Equal(f.Logs.Reverse().Take(2).Select(log => log.HorseHealthLogId), page.Items.Select(log => log.HorseHealthLogId));
        page = await f.Service.GetLogsAsync(f.Transport.TransportId, f.UncheckedHorse.HorseId, new(), f.Customer.AccountId);
        Assert.Empty(page.Items); Assert.Equal(0, page.TotalItems); Assert.Equal(0, page.TotalPages);
        page = await f.Service.GetLogsAsync(f.Transport.TransportId, f.Horse.HorseId, new() { PageIndex = 10 }, f.Customer.AccountId);
        Assert.Empty(page.Items); Assert.Equal(4, page.TotalItems);
        Assert.Equal("HORSE_NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(() =>
            f.Service.GetLogsAsync(f.Transport.TransportId, Guid.NewGuid(), new(), f.Customer.AccountId))).ErrorCode);
        Assert.Empty(db.ChangeTracker.Entries()); Assert.Empty(f.Storage.Reads);
    }

    [Theory]
    [InlineData("logType", "0")]
    [InlineData("logType", "1")]
    [InlineData("logType", "routine_check")]
    [InlineData("logType", "ROUTINE_CHECK,")]
    [InlineData("logType", "ROUTINE_CHECK,,OTHER")]
    [InlineData("logType", "")]
    [InlineData("healthStatus", "NORMAL,BOGUS")]
    [InlineData("healthStatus", "1")]
    [InlineData("healthStatus", " ")]
    [InlineData("pageIndex", "-1")]
    [InlineData("pageIndex", "2147483647")]
    [InlineData("pageSize", "0")]
    [InlineData("pageSize", "101")]
    public async Task History_RejectsInvalidFiltersAndPagination(string field, string value)
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        var request = new Health.Request.ListRequest();
        switch (field)
        {
            case "logType": request.LogType = value; break;
            case "healthStatus": request.HealthStatus = value; break;
            case "pageIndex": request.PageIndex = int.Parse(value); break;
            case "pageSize": request.PageSize = int.Parse(value); break;
        }
        var error = await Assert.ThrowsAsync<FieldValidationException>(() =>
            f.Service.GetLogsAsync(f.Transport.TransportId, f.Horse.HorseId, request, f.Customer.AccountId));
        Assert.Equal("VALIDATION_ERROR", error.ErrorCode);
        Assert.Contains(error.Errors, item => item.Field == field);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Detail_PreservesHistoricalRecorderAndSignsPhotosAfterAuthorization(bool legacyStorage)
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        f.Latest.PhotoUrl = legacyStorage ? "s3://health/photo.jpg" : "https://res.cloudinary.com/test/image/authenticated/photo.jpg";
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        using var cancellation = new CancellationTokenSource();
        var result = await f.Service.GetAsync(f.Latest.HorseHealthLogId, f.Manager.AccountId, cancellation.Token);
        Assert.Equal(Fixture.ServerTime, result.ServerTime);
        Assert.Equal(f.Transport.TransportId, result.TransportId);
        Assert.Equal(f.Horse.PassportNumber, result.Horse.PassportNumber);
        Assert.Equal(DbTime.AsUtc(f.Latest.CheckedAt), result.CheckedAt);
        Assert.Equal(DbTime.AsUtc(f.Latest.CreatedAt), result.CreatedAt);
        Assert.Equal(DbTime.AsUtc(f.Latest.UpdatedAt), result.UpdatedAt);
        Assert.Equal(TimeSpan.Zero, result.CheckedAt.Offset);
        Assert.Equal("REST_STOP_CHECK", result.LogType); Assert.Equal("AT_RISK", result.HealthStatus);
        Assert.Equal("MILD_DEHYDRATION", result.HydrationStatus);
        Assert.Equal(f.Recorder.EmployeeId, result.CheckedBy.EmployeeId);
        Assert.Equal(38.2m, result.TemperatureCelsius); Assert.Equal(38, result.HeartRateBpm); Assert.Equal(14, result.RespiratoryRateBpm);
        Assert.Equal(f.Latest.BehaviorNote, result.BehaviorNote); Assert.Equal(f.Latest.SymptomNote, result.SymptomNote);
        Assert.Equal(f.Latest.InjuryNote, result.InjuryNote); Assert.Equal(f.Latest.FeedingNote, result.FeedingNote);
        Assert.Equal(f.Latest.HydrationNote, result.HydrationNote); Assert.Equal(f.Latest.TreatmentProvided, result.TreatmentProvided);
        Assert.Equal(f.Latest.MedicationGiven, result.MedicationGiven); Assert.Equal(f.Latest.GeneralNote, result.GeneralNote);
        Assert.Equal(f.Latest.LocationDescription, result.LocationDescription);
        Assert.True(result.VeterinarianRequired); Assert.Equal("Internal vet note", result.VeterinarianNote);
        Assert.True(result.HasPhoto); Assert.Null(result.GpsCoordinates);
        var photo = Assert.Single(result.Photos);
        Assert.Equal(Storage.SignedUrl, photo.PhotoUrl);
        Assert.Null(photo.Latitude); Assert.Null(photo.Longitude); Assert.Null(photo.CapturedAt); Assert.Null(photo.Caption);
        Assert.Equal((f.Latest.PhotoUrl, cancellation.Token), Assert.Single(f.Storage.Reads));
        var customer = await f.Service.GetAsync(f.Latest.HorseHealthLogId, f.Customer.AccountId);
        Assert.Equal(result with { VeterinarianNote = null, Photos = customer.Photos }, customer);
        Assert.Null(customer.VeterinarianNote);
        var noPhoto = await f.Service.GetAsync(f.Logs[0].HorseHealthLogId, f.Customer.AccountId);
        Assert.Empty(noPhoto.Photos); Assert.False(noPhoto.HasPhoto); Assert.Null(noPhoto.HydrationStatus);
        Assert.Equal(2, f.Storage.Reads.Count);
        Assert.Empty(db.ChangeTracker.Entries()); Assert.Empty(await db.Notifications.ToListAsync()); Assert.Empty(await db.OutboxMessages.ToListAsync());
    }

    [Theory]
    [InlineData(EmployeeRole.LOGISTICS_MANAGER)]
    [InlineData(EmployeeRole.TRANSPORT_SPECIALIST)]
    [InlineData(EmployeeRole.FLEET_ROUTE_COORDINATOR)]
    [InlineData(EmployeeRole.DRIVER)]
    [InlineData(EmployeeRole.ESCORT)]
    public async Task AllEmployeeRoles_UseDatabaseRoleAndActualCrewAssignment(EmployeeRole role)
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        f.Manager.Role = role;
        if (role == EmployeeRole.DRIVER) f.Segment.DriverEmployeeId = f.Manager.EmployeeId;
        if (role == EmployeeRole.ESCORT) f.Segment.EscortEmployeeId = f.Manager.EmployeeId;
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        Assert.Equal(2, (await f.Service.GetOverviewAsync(f.Transport.TransportId, f.Manager.AccountId)).Horses.Count);
        Assert.Equal(4, (await f.Service.GetLogsAsync(f.Transport.TransportId, f.Horse.HorseId, new(), f.Manager.AccountId)).TotalItems);
        Assert.Equal("Internal vet note", (await f.Service.GetAsync(f.Latest.HorseHealthLogId, f.Manager.AccountId)).VeterinarianNote);
    }

    [Fact]
    public async Task Ownership_UsesBookingAndHidesForeignResourcesBeforeLifecycle()
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        var (other, customer) = await TestSupport.AddCustomerAsync(db);
        f.Horse.Customer = customer; f.Horse.CustomerId = customer.CustomerId;
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        Assert.Equal(4, (await f.Service.GetLogsAsync(f.Transport.TransportId, f.Horse.HorseId, new(), f.Customer.AccountId)).TotalItems);
        Assert.Equal(f.Latest.HorseHealthLogId, (await f.Service.GetAsync(f.Latest.HorseHealthLogId, f.Customer.AccountId)).HorseHealthLogId);
        f.Storage.Reads.Clear();
        db.Attach(f.Transport); f.Transport.Status = TransportStatus.CANCELLED; await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        Assert.Equal("TRANSPORT_NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(() =>
            f.Service.GetOverviewAsync(f.Transport.TransportId, other.AccountId))).ErrorCode);
        Assert.Equal("TRANSPORT_NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(() =>
            f.Service.GetLogsAsync(f.Transport.TransportId, Guid.NewGuid(), new(), other.AccountId))).ErrorCode);
        Assert.Equal("HEALTH_LOG_NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(() =>
            f.Service.GetAsync(f.Latest.HorseHealthLogId, other.AccountId))).ErrorCode);
        Assert.Equal("HEALTH_LOG_NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(() =>
            f.Service.GetAsync(Guid.NewGuid(), f.Customer.AccountId))).ErrorCode);
        Assert.Empty(f.Storage.Reads);
    }

    [Theory]
    [InlineData("none", false)]
    [InlineData("wrongRole", false)]
    [InlineData("superseded", false)]
    [InlineData("executed", true)]
    [InlineData("outsourced", false)]
    [InlineData("draft", false)]
    [InlineData("completed", true)]
    [InlineData("assigned", true)]
    public async Task CrewAccess_ExcludesUnexecutedSupersededAndOutsourcedSegments(string scenario, bool allowed)
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        var segment = f.Segment;
        if (scenario == "none") segment.DriverEmployeeId = null;
        if (scenario == "wrongRole") { segment.DriverEmployeeId = null; segment.EscortEmployeeId = f.Driver.EmployeeId; }
        if (scenario is "superseded" or "executed") segment.SupersededAt = Fixture.Now;
        if (scenario == "superseded") segment.ActualDepartureAt = null;
        if (scenario == "outsourced")
        {
            segment.TransportProvider = new TransportProvider
            {
                TransportProviderId = Guid.NewGuid(), ProviderCode = "PROVIDER", ProviderName = "Provider", Status = ProviderStatus.ACTIVE
            };
            db.Add(segment.TransportProvider);
        }
        if (scenario == "draft") f.RoutePlan.Status = RoutePlanStatus.DRAFT;
        if (scenario == "completed") f.RoutePlan.Status = RoutePlanStatus.COMPLETED;
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        if (allowed)
            Assert.Equal(f.Transport.TransportId, (await f.Service.GetOverviewAsync(f.Transport.TransportId, f.Driver.AccountId)).TransportId);
        else
        {
            var error = await Assert.ThrowsAsync<ForbiddenException>(() => f.Service.GetOverviewAsync(f.Transport.TransportId, f.Driver.AccountId));
            Assert.Equal("You are not assigned to this transport", error.Message);
            Assert.Equal("FORBIDDEN", error.ErrorCode);
            await Assert.ThrowsAsync<ForbiddenException>(() => f.Service.GetAsync(f.Latest.HorseHealthLogId, f.Driver.AccountId));
            Assert.Empty(f.Storage.Reads);
        }
    }

    [Theory]
    [InlineData("accountInactive")]
    [InlineData("accountDeleted")]
    [InlineData("employeeInactive")]
    [InlineData("employeeDeleted")]
    [InlineData("missingProfile")]
    [InlineData("unknownRole")]
    [InlineData("unknownAccountType")]
    [InlineData("customerDeleted")]
    public async Task Actor_RejectsUnavailableAccountsAndProfiles(string scenario)
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        var accountId = f.Manager.AccountId;
        switch (scenario)
        {
            case "accountInactive": f.Manager.Account.Status = AccountStatus.INACTIVE; break;
            case "accountDeleted": f.Manager.Account.IsDeleted = true; break;
            case "employeeInactive": f.Manager.IsActive = false; break;
            case "employeeDeleted": f.Manager.IsDeleted = true; break;
            case "missingProfile": db.Remove(f.Manager); break;
            case "unknownRole": f.Manager.Role = (EmployeeRole)99; break;
            case "unknownAccountType": f.Manager.Account.AccountType = (AccountType)99; break;
            case "customerDeleted": f.Customer.IsDeleted = true; accountId = f.Customer.AccountId; break;
        }
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<UnauthorizedException>(() => f.Service.GetOverviewAsync(f.Transport.TransportId, accountId));
        await Assert.ThrowsAsync<UnauthorizedException>(() => f.Service.GetLogsAsync(f.Transport.TransportId, f.Horse.HorseId, new(), accountId));
        await Assert.ThrowsAsync<UnauthorizedException>(() => f.Service.GetAsync(f.Latest.HorseHealthLogId, accountId));
        Assert.Empty(f.Storage.Reads);
    }

    [Theory]
    [InlineData(TransportStatus.CREATED, RoutePlanStatus.ACTIVE, false)]
    [InlineData(TransportStatus.PLANNING, RoutePlanStatus.ACTIVE, false)]
    [InlineData(TransportStatus.CANCELLED, RoutePlanStatus.COMPLETED, false)]
    [InlineData(TransportStatus.READY, RoutePlanStatus.DRAFT, false)]
    [InlineData(TransportStatus.READY, RoutePlanStatus.CONFIRMED, true)]
    [InlineData(TransportStatus.IN_TRANSIT, RoutePlanStatus.ACTIVE, true)]
    [InlineData(TransportStatus.COMPLETED, RoutePlanStatus.COMPLETED, true)]
    public async Task Lifecycle_RequiresVisibleStatusAndEligibleRoutePlan(TransportStatus status, RoutePlanStatus planStatus, bool allowed)
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        f.Transport.Status = status; f.RoutePlan.Status = planStatus;
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        if (allowed)
            Assert.Equal(status.ToString(), (await f.Service.GetOverviewAsync(f.Transport.TransportId, f.Manager.AccountId)).TransportStatus);
        else
        {
            var error = await Assert.ThrowsAsync<ConflictException>(() => f.Service.GetOverviewAsync(f.Transport.TransportId, f.Manager.AccountId));
            Assert.Equal("INVALID_TRANSPORT_STATUS", error.ErrorCode);
            await Assert.ThrowsAsync<ConflictException>(() => f.Service.GetLogsAsync(f.Transport.TransportId, f.Horse.HorseId, new(), f.Manager.AccountId));
            await Assert.ThrowsAsync<ConflictException>(() => f.Service.GetAsync(f.Latest.HorseHealthLogId, f.Manager.AccountId));
            Assert.Empty(f.Storage.Reads);
        }
    }

    [Theory]
    [InlineData(RouteType.BACKUP, RoutePlanStatus.ACTIVE)]
    [InlineData(RouteType.RETURN, RoutePlanStatus.ACTIVE)]
    [InlineData(RouteType.BACKUP, RoutePlanStatus.COMPLETED)]
    public async Task CurrentEscort_FollowsActivatedContingencyAndCompletedHistory(RouteType type, RoutePlanStatus status)
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        var alternative = f.AddAlternative(type, status);
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var result = await f.Service.GetOverviewAsync(f.Transport.TransportId, f.Customer.AccountId);
        Assert.Equal(f.Driver.EmployeeId, result.CurrentEscort!.EmployeeId);
        Assert.Equal(f.Recorder.EmployeeId, result.Horses.Single(item => item.Horse.HorseId == f.Horse.HorseId).LatestLog!.CheckedBy.EmployeeId);
        db.Attach(alternative); alternative.TransportProvider = new TransportProvider
        {
            TransportProviderId = Guid.NewGuid(), ProviderCode = "OUTSOURCE", ProviderName = "Outsource", Status = ProviderStatus.ACTIVE
        };
        db.Add(alternative.TransportProvider);
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        Assert.Null((await f.Service.GetOverviewAsync(f.Transport.TransportId, f.Customer.AccountId)).CurrentEscort);
    }

    [Theory]
    [InlineData(TransportStatus.READY, true)]
    [InlineData(TransportStatus.IN_TRANSIT, false)]
    [InlineData(TransportStatus.COMPLETED, true)]
    public async Task CurrentEscort_UsesStatusFallbackButIgnoresUnactivatedBackup(TransportStatus status, bool hasCurrent)
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        f.AddAlternative(RouteType.BACKUP, RoutePlanStatus.CONFIRMED);
        f.Segment.ActualDepartureAt = null; f.Segment.ActualArrivalAt = null; f.Segment.SupersededAt = null;
        f.Transport.Status = status;
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var result = await f.Service.GetOverviewAsync(f.Transport.TransportId, f.Customer.AccountId);
        Assert.Equal(hasCurrent ? f.Escort.EmployeeId : (Guid?)null, result.CurrentEscort?.EmployeeId);
    }

    [Fact]
    public async Task Ids_RejectEmptyAndMissingResources()
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        await Assert.ThrowsAsync<FieldValidationException>(() => f.Service.GetOverviewAsync(Guid.Empty, f.Customer.AccountId));
        await Assert.ThrowsAsync<FieldValidationException>(() => f.Service.GetLogsAsync(f.Transport.TransportId, Guid.Empty, new(), f.Customer.AccountId));
        await Assert.ThrowsAsync<FieldValidationException>(() => f.Service.GetAsync(Guid.Empty, f.Customer.AccountId));
        Assert.Equal("TRANSPORT_NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(() =>
            f.Service.GetOverviewAsync(Guid.NewGuid(), f.Customer.AccountId))).ErrorCode);
        await Assert.ThrowsAsync<UnauthorizedException>(() => f.Service.GetAsync(f.Latest.HorseHealthLogId, Guid.NewGuid()));
    }

    [Fact]
    public void EffectiveJourney_KeepsExecutedHistoryAndPrimaryTailAfterBackupBranch()
    {
        var primaryId = Guid.NewGuid(); var backupId = Guid.NewGuid();
        var first = new EffectiveJourney.Segment(Guid.NewGuid(), primaryId, null, RoutePlanStatus.ACTIVE,
            Fixture.Now, Fixture.Now, 1, RouteType.PRIMARY, Fixture.Now, Fixture.Now.AddHours(-3), Fixture.Now.AddHours(-2));
        var superseded = first with { RoutePlanRouteId = Guid.NewGuid(), SequenceNo = 2, ActualDepartureAt = null, ActualArrivalAt = null };
        var prefix = first with { RoutePlanRouteId = Guid.NewGuid(), RoutePlanId = backupId, PrimaryRoutePlanId = primaryId,
            SupersededAt = null, ActualDepartureAt = null, ActualArrivalAt = null };
        var backup = prefix with { RoutePlanRouteId = Guid.NewGuid(), SequenceNo = 2, RouteType = RouteType.BACKUP,
            ActualDepartureAt = Fixture.Now.AddHours(-1), ActualArrivalAt = Fixture.Now };
        var tail = prefix with { RoutePlanRouteId = Guid.NewGuid(), SequenceNo = 3 };
        var segments = EffectiveJourney.Select([first, superseded, prefix, backup, tail]);
        Assert.Equal(new[] { first.RoutePlanRouteId, backup.RoutePlanRouteId, tail.RoutePlanRouteId }, segments.Select(segment => segment.RoutePlanRouteId));
        Assert.Equal(tail, EffectiveJourney.Current(segments, TransportStatus.COMPLETED));
        Assert.Null(EffectiveJourney.Current(segments, TransportStatus.IN_TRANSIT));
        var returning = prefix with { RoutePlanRouteId = Guid.NewGuid(), RoutePlanId = Guid.NewGuid(), RouteType = RouteType.RETURN,
            PlanUpdatedAt = Fixture.Now.AddHours(1), ActualDepartureAt = Fixture.Now.AddMinutes(1) };
        segments = EffectiveJourney.Select([first, superseded, prefix with { PlanStatus = RoutePlanStatus.COMPLETED },
            backup with { PlanStatus = RoutePlanStatus.COMPLETED }, tail with { PlanStatus = RoutePlanStatus.COMPLETED }, returning]);
        Assert.Equal(new[] { first.RoutePlanRouteId, backup.RoutePlanRouteId, returning.RoutePlanRouteId }, segments.Select(segment => segment.RoutePlanRouteId));
        Assert.Equal(returning, EffectiveJourney.Current(segments, TransportStatus.IN_TRANSIT));
        Assert.Empty(EffectiveJourney.Select([prefix, backup, tail]));
    }

    internal sealed class Fixture(AppDbContext db)
    {
        internal static readonly DateTime Now = new(2026, 10, 16, 8, 0, 0, DateTimeKind.Unspecified);
        internal static DateTimeOffset ServerTime => DbTime.AsUtc(Now.AddHours(1));
        internal Customer Customer { get; private init; } = null!;
        internal Employee Manager { get; private init; } = null!;
        internal Employee Driver { get; private init; } = null!;
        internal Employee Escort { get; private init; } = null!;
        internal Employee Recorder { get; private init; } = null!;
        internal Horse Horse { get; private init; } = null!;
        internal Horse UncheckedHorse { get; private init; } = null!;
        internal Transport Transport { get; private init; } = null!;
        internal RoutePlan RoutePlan { get; private init; } = null!;
        internal RoutePlanRoute Segment { get; private init; } = null!;
        internal HorseHealthLog[] Logs { get; private set; } = [];
        internal HorseHealthLog Latest => Logs[^1];
        internal Storage Storage { get; } = new();
        internal Health.Service Service => new(db, new FixedClock(), new ListRequestValidator(), Storage, Storage);

        internal static async Task<Fixture> CreateAsync(AppDbContext db)
        {
            var (_, customer) = await TestSupport.AddCustomerAsync(db);
            var (_, manager) = await TestSupport.AddLogisticsManagerAsync(db);
            var (_, driver) = await TestSupport.AddLogisticsManagerAsync(db); driver.Role = EmployeeRole.DRIVER;
            var (_, escort) = await TestSupport.AddLogisticsManagerAsync(db); escort.Role = EmployeeRole.ESCORT; escort.Phone = "+33123456789";
            var (_, recorder) = await TestSupport.AddLogisticsManagerAsync(db); recorder.Role = EmployeeRole.ESCORT;
            recorder.IsActive = false; recorder.IsDeleted = true; recorder.FullName = "Historical Escort";
            var (booking, horse) = await TestSupport.AddPendingBookingAsync(db, customer, withHorse: true);
            horse!.MicrochipNumber = "826123456789012"; horse.PassportNumber = "826GB123456789";
            var uncheckedHorse = new Horse { HorseId = Guid.NewGuid(), HorseName = "Unchecked", Customer = customer };
            db.Add(new BookingHorse { Booking = booking, Horse = uncheckedHorse });
            var transport = new Transport { TransportId = Guid.NewGuid(), Booking = booking, TransportCode = "TR-" + Guid.NewGuid().ToString("N"),
                Status = TransportStatus.IN_TRANSIT, ActualDepartureAt = Now.AddHours(-3), CreatedAt = Now, UpdatedAt = Now };
            var plan = new TransportPlan { TransportPlanId = Guid.NewGuid(), PlanCode = "TP-" + Guid.NewGuid().ToString("N"),
                Transport = transport, Status = TransportPlanStatus.IN_PROGRESS, CreatedAt = Now, UpdatedAt = Now };
            var route = new RoutePlan { RoutePlanId = Guid.NewGuid(), RoutePlanCode = "RP-" + Guid.NewGuid().ToString("N"),
                TransportPlan = plan, Status = RoutePlanStatus.ACTIVE, CreatedAt = Now, UpdatedAt = Now };
            var segment = PersonnelAssignmentTests.Segment(route, 1);
            segment.DriverEmployeeId = driver.EmployeeId; segment.EscortEmployeeId = escort.EmployeeId; segment.ActualDepartureAt = Now.AddHours(-3);
            db.Add(segment);
            var f = new Fixture(db) { Customer = customer, Manager = manager, Driver = driver, Escort = escort, Recorder = recorder,
                Horse = horse, UncheckedHorse = uncheckedHorse, Transport = transport, RoutePlan = route, Segment = segment };
            f.Logs = Enumerable.Range(1, 4).Select(index => f.Log(index, transport, horse, index == 1 ? Now.AddHours(-1) : Now)).ToArray();
            f.Logs[1].LogType = HorseHealthLogType.BORDER_CHECK; f.Logs[1].HealthStatus = HorseHealthStatus.OBSERVATION_REQUIRED;
            f.Logs[2].HealthStatus = HorseHealthStatus.CRITICAL;
            f.Latest.LogType = HorseHealthLogType.REST_STOP_CHECK; f.Latest.HealthStatus = HorseHealthStatus.AT_RISK;
            f.Latest.HydrationStatus = HydrationStatus.MILD_DEHYDRATION;
            f.Latest.TemperatureCelsius = 38.2m; f.Latest.HeartRateBpm = 38; f.Latest.RespiratoryRateBpm = 14;
            f.Latest.BehaviorNote = "Restless"; f.Latest.SymptomNote = "Sweating"; f.Latest.InjuryNote = "Scratch";
            f.Latest.FeedingNote = "Half hay"; f.Latest.HydrationNote = "Drank water";
            f.Latest.TreatmentProvided = "Cleaned"; f.Latest.MedicationGiven = "Medication";
            f.Latest.VeterinarianRequired = true; f.Latest.VeterinarianNote = "Internal vet note";
            f.Latest.LocationDescription = "Lille Rest Stop"; f.Latest.GeneralNote = "  ";
            f.Latest.PhotoUrl = "https://res.cloudinary.com/test/image/authenticated/photo.jpg";
            db.AddRange(f.Logs); await db.SaveChangesAsync(); return f;
        }

        internal HorseHealthLog Log(int index, Transport transport, Horse horse, DateTime checkedAt) => new()
        {
            HorseHealthLogId = Guid.Parse($"00000000-0000-0000-0000-{index:000000000000}"), Transport = transport, Horse = horse,
            Employee = Recorder, LogType = HorseHealthLogType.ROUTINE_CHECK, HealthStatus = HorseHealthStatus.NORMAL,
            CheckedAt = checkedAt, CreatedAt = Now.AddSeconds(Math.Min(index, 3)), UpdatedAt = Now.AddSeconds(10)
        };

        internal RoutePlanRoute AddAlternative(RouteType type, RoutePlanStatus status)
        {
            Segment.ActualArrivalAt = Now.AddHours(-1);
            var alternative = new RoutePlan { RoutePlanId = Guid.NewGuid(), TransportPlan = RoutePlan.TransportPlan,
                PrimaryRoutePlanId = RoutePlan.RoutePlanId, RoutePlanCode = "ALT-" + Guid.NewGuid().ToString("N"),
                Status = status, CreatedAt = Now.AddMinutes(1), UpdatedAt = Now.AddMinutes(2) };
            var prefix = PersonnelAssignmentTests.Segment(alternative, 1); prefix.RouteType = type == RouteType.BACKUP ? RouteType.PRIMARY : type;
            prefix.EscortEmployeeId = Manager.EmployeeId;
            var current = PersonnelAssignmentTests.Segment(alternative, 2); current.RouteType = type;
            current.EscortEmployeeId = Driver.EmployeeId;
            if (status != RoutePlanStatus.CONFIRMED) current.ActualDepartureAt = Now;
            if (status == RoutePlanStatus.COMPLETED) { current.ActualArrivalAt = Now.AddMinutes(1); Transport.Status = TransportStatus.COMPLETED; }
            db.AddRange(prefix, current); return current;
        }
    }

    internal sealed class FixedClock : TimeProvider { public override DateTimeOffset GetUtcNow() => Fixture.ServerTime; }

    internal sealed class Storage : IMediaService, IDocumentStorageService
    {
        internal const string SignedUrl = "https://signed.example.com/photo?signature=test";
        internal List<(string Url, CancellationToken Token)> Reads { get; } = [];
        public Task<SignedDocumentUrl> CreateReadUrlAsync(string storageUrl, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested(); Reads.Add((storageUrl, cancellationToken));
            return Task.FromResult(new SignedDocumentUrl(SignedUrl, Fixture.ServerTime.AddMinutes(5)));
        }
        public Task<string> UploadImageAsync(IFormFile file) => throw new NotSupportedException();
        public Task<(string FileUrl, string PublicId)> UploadFileAsync(IFormFile file) => throw new NotSupportedException();
        public Task DeleteFileAsync(string publicId) => throw new NotSupportedException();
        public Task<string> UploadPrivateImageAsync(Guid imageId, PreviewFile image, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeletePrivateImageAsync(Guid imageId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SignedUploadResult> CreateSignedUploadAsync(Guid accountId, string entity, string contentType, long sizeBytes) => throw new NotSupportedException();
        public Task<ConfirmedUploadResult> ConfirmUploadAsync(Guid accountId, string publicId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PreviewFile> DownloadPreviewAsync(string fileUrl, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ValidateContentAsync(PreviewFile file, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StoredDocumentFile> UploadAsync(Guid versionId, int sortOrder, IFormFile file, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(string storageUrl, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
