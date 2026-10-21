using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Npgsql.NameTranslation;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;

using static Pegaxus.Service.Test.HorseHealthLogTests;
using static Pegaxus.Service.Test.HorseIncidentTests;

using Incidents = Pegaxus.Service.IncidentService;

namespace Pegaxus.Service.Test;

[Collection("PostgresSharedDatabase")]
public class HorseIncidentPostgresTests
{
    [PostgresFact]
    public async Task Postgres_ReportAdditionAndFollowUpAreAtomic_WithLogsPhotosAlertsAndDurableMail()
    {
        await WithDatabaseAsync(async options =>
        {
            await using var seed = new AppDbContext(options); var f = await Scenario.CreateAsync(seed);
            foreach (var failure in new[] { "save", "sign", "upload" })
            {
                var storage = new Storage { FailSign = failure == "sign", FailAtConfirmation = failure == "upload" ? 2 : null };
                await using var writer = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(options)
                    .AddInterceptors(new SaveAudit { FailAfterSave = failure == "save" }).Options);
                var request = f.Report(); request.Horses.Add(f.HorseRequest(f.SecondHorseId)); request.PhotoFiles = [Photo(), Photo()];
                await Assert.ThrowsAsync<IOException>(() => CreateIncidentService(writer, storage).ReportHorseHealthAsync(request, f.Driver.AccountId));
                await using var check = new AppDbContext(options);
                Assert.Empty(await check.IncidentReports.ToArrayAsync()); Assert.Empty(await check.HorseHealthIncidentDetails.ToArrayAsync());
                Assert.Empty(await check.HorseHealthLogs.ToArrayAsync()); Assert.Empty(await check.IncidentPhotos.ToArrayAsync());
                Assert.Empty(await check.Notifications.ToArrayAsync()); Assert.Empty(await check.AccountNotifications.ToArrayAsync());
                Assert.Empty(await check.OutboxMessages.ToArrayAsync()); Assert.Empty(storage.Deletes);
            }
            Incidents.Response.Incident incident;
            var audit = new SaveAudit(); var originalRequest = f.Report(); originalRequest.PhotoFiles = [Photo()];
            originalRequest.Horses[0].MedicationGiven = "Original medication"; originalRequest.Horses[0].IsolationRequired = true;
            originalRequest.Horses[0].BodyTemperatureCelsius = 39.8m;
            await using (var writer = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(options).AddInterceptors(audit).Options))
            {
                incident = (await CreateIncidentService(writer, f.Storage).ReportHorseHealthAsync(originalRequest, f.Driver.AccountId)).Value;
                Assert.Equal(1, audit.Calls); Assert.Equal((1, 1, 1, 1, 4, 4, 5), audit.Added);
                Assert.True(incident.HorseDetails[0].IsolationRequired); Assert.Equal(39.8m, incident.HorseDetails[0].BodyTemperatureCelsius);
                Assert.Equal(Storage.SignedUrl, Assert.Single(incident.Photos).PhotoUrl);
            }
            foreach (var operation in new[] { "add", "followUp" })
            foreach (var failure in new[] { "save", "sign" })
            {
                var storage = new Storage { FailSign = failure == "sign" };
                await using var writer = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(options)
                    .AddInterceptors(new SaveAudit { FailAfterSave = failure == "save" }).Options);
                var service = CreateIncidentService(writer, storage);
                if (operation == "add")
                    await Assert.ThrowsAsync<IOException>(() => service.AddHorseDetailsAsync(incident.IncidentReportId,
                        new() { Horses = [f.HorseRequest(f.SecondHorseId)], PhotoFiles = [Photo()] }, f.Escort.AccountId));
                else
                    await Assert.ThrowsAsync<IOException>(() => service.FollowUpHorseAsync(incident.IncidentReportId, incident.HorseDetails[0].HorseHealthIncidentDetailId,
                        new() { LastUpdatedAt = incident.UpdatedAt, OutcomeNote = "Uncommitted outcome", HorseTransportable = false }, f.Manager.AccountId));
                await using var check = new AppDbContext(options);
                Assert.Single(await check.HorseHealthIncidentDetails.ToArrayAsync()); Assert.Single(await check.HorseHealthLogs.ToArrayAsync());
                Assert.Single(await check.IncidentPhotos.ToArrayAsync()); Assert.Equal(4, await check.Notifications.CountAsync());
                Assert.Equal(4, await check.AccountNotifications.CountAsync()); Assert.Equal(5, await check.OutboxMessages.CountAsync());
                Assert.Null((await check.HorseHealthIncidentDetails.SingleAsync()).OutcomeNote);
                Assert.Equal(incident.UpdatedAt, DbTime.AsUtc((await check.IncidentReports.SingleAsync()).UpdatedAt)); Assert.Empty(storage.Deletes);
            }
            await using (var writer = new AppDbContext(options))
            {
                incident = await CreateIncidentService(writer, f.Storage).AddHorseDetailsAsync(incident.IncidentReportId,
                    new() { Horses = [f.HorseRequest(f.SecondHorseId)], PhotoFiles = [Photo()] }, f.Escort.AccountId);
                Assert.Equal(2, incident.HorseDetails.Count); Assert.Equal(2, incident.PhotoCount);
            }
            await using var stale = new AppDbContext(options);
            await stale.IncidentReports.SingleAsync(); await stale.HorseHealthIncidentDetails.ToListAsync();
            await using (var writer = new AppDbContext(options))
            {
                var request = new Incidents.Request.FollowUpHorseRequest { LastUpdatedAt = incident.UpdatedAt.AddTicks(9),
                    MedicationGiven = "Follow-up medication", OutcomeNote = "Vet examination complete", HorseTransportable = false };
                var updated = await CreateIncidentService(writer, f.Storage).FollowUpHorseAsync(incident.IncidentReportId, incident.HorseDetails[0].HorseHealthIncidentDetailId,
                    request, f.Coordinator.AccountId);
                Assert.Contains("] Vet examination complete", updated.HorseDetails[0].OutcomeNote); Assert.False(updated.HorseDetails[0].HorseTransportable);
            }
            Assert.Equal("INCIDENT_MODIFIED", (await Assert.ThrowsAsync<ConflictException>(() => CreateIncidentService(stale, f.Storage).FollowUpHorseAsync(
                incident.IncidentReportId, incident.HorseDetails[0].HorseHealthIncidentDetailId,
                new() { LastUpdatedAt = incident.UpdatedAt, OutcomeNote = "Stale writer" }, f.Manager.AccountId))).ErrorCode);
            await using var verify = new AppDbContext(options);
            Assert.Equal(2, await verify.HorseHealthIncidentDetails.CountAsync()); Assert.Equal(2, await verify.HorseHealthLogs.CountAsync());
            Assert.Equal(2, await verify.IncidentPhotos.CountAsync()); Assert.Equal(10, await verify.Notifications.CountAsync());
            Assert.Equal(12, await verify.OutboxMessages.CountAsync()); Assert.Equal(DateTimeKind.Unspecified, (await verify.IncidentReports.SingleAsync()).UpdatedAt.Kind);
            Assert.Equal("Original medication", (await verify.HorseHealthLogs.SingleAsync(log => log.HorseId == f.Horse.HorseId)).MedicationGiven);
            Assert.False(verify.Database.HasPendingModelChanges());
        });
    }

    [PostgresFact]
    public async Task Postgres_ConcurrentOfflineReplaysAndAdditionsNeverDuplicateRecords_FollowUpsRejectStaleVersion()
    {
        await WithDatabaseAsync(async options =>
        {
            await using var seed = new AppDbContext(options); var f = await Scenario.CreateAsync(seed);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var storage = new Storage { AfterConfirmation = async () => { entered.TrySetResult(); await release.Task.WaitAsync(TimeSpan.FromSeconds(20)); } };
            var competitorStorage = new Storage(); var barrier = new CompetingSnapshot();
            await using var first = new AppDbContext(options);
            await using var second = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(options).AddInterceptors(barrier).Options);
            var request = f.Report(); request.PhotoFiles = [Photo()];
            var winner = CreateIncidentService(first, storage).ReportHorseHealthAsync(request, f.Driver.AccountId);
            Incidents.Response.Incident incident;
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
                var competing = CreateIncidentService(second, competitorStorage).ReportHorseHealthAsync(request, f.Driver.AccountId);
                await barrier.Started.Task.WaitAsync(TimeSpan.FromSeconds(20)); release.TrySetResult();
                var created = await winner; Assert.True(created.Created); incident = created.Value;
                var replay = await competing; Assert.False(replay.Created); Assert.Equal(incident.IncidentReportId, replay.Value.IncidentReportId);
                Assert.Equal(0, replay.Value.Effects!.HealthLogsCreated); Assert.Equal(false, replay.Value.Effects.NotifiedPriority);
                Assert.True(barrier.Attempts >= 2); Assert.Empty(competitorStorage.Confirmations); Assert.Single(storage.Confirmations);
            }
            finally { release.TrySetResult(); }
            var addEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var addRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var addStorage = new Storage { AfterConfirmation = async () => { addEntered.TrySetResult(); await addRelease.Task.WaitAsync(TimeSpan.FromSeconds(20)); } };
            var addBarrier = new CompetingSnapshot();
            await using var addFirst = new AppDbContext(options);
            await using var addSecond = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(options).AddInterceptors(addBarrier).Options);
            var addRequest = new Incidents.Request.AddHorseDetailsRequest { Horses = [f.HorseRequest(f.SecondHorseId)], PhotoFiles = [Photo()] };
            var added = CreateIncidentService(addFirst, addStorage).AddHorseDetailsAsync(incident.IncidentReportId, addRequest, f.Escort.AccountId);
            try
            {
                await addEntered.Task.WaitAsync(TimeSpan.FromSeconds(20));
                var competing = CreateIncidentService(addSecond, competitorStorage).AddHorseDetailsAsync(incident.IncidentReportId, addRequest, f.Escort.AccountId);
                await addBarrier.Started.Task.WaitAsync(TimeSpan.FromSeconds(20)); addRelease.TrySetResult();
                incident = await added; Assert.Equal(2, incident.HorseDetails.Count);
                Assert.Equal("HORSE_DETAIL_ALREADY_EXISTS", (await Assert.ThrowsAsync<ConflictException>(() => competing)).ErrorCode);
                Assert.True(addBarrier.Attempts >= 2); Assert.Empty(competitorStorage.Confirmations);
            }
            finally { addRelease.TrySetResult(); }
            var save = new PausedSave(); var patchBarrier = new CompetingSnapshot();
            await using var patchFirst = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(options).AddInterceptors(save).Options);
            await using var patchSecond = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(options).AddInterceptors(patchBarrier).Options);
            var patch = new Incidents.Request.FollowUpHorseRequest { LastUpdatedAt = incident.UpdatedAt, OutcomeNote = "First examination" };
            var detailId = incident.HorseDetails[0].HorseHealthIncidentDetailId;
            var patched = CreateIncidentService(patchFirst, f.Storage).FollowUpHorseAsync(incident.IncidentReportId, detailId, patch, f.Manager.AccountId);
            try
            {
                await save.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
                var competing = CreateIncidentService(patchSecond, competitorStorage).FollowUpHorseAsync(incident.IncidentReportId, detailId, patch, f.Coordinator.AccountId);
                await patchBarrier.Started.Task.WaitAsync(TimeSpan.FromSeconds(20)); save.Release.TrySetResult();
                Assert.Contains("] First examination", (await patched).HorseDetails.Single(horse => horse.HorseHealthIncidentDetailId == detailId).OutcomeNote);
                Assert.Equal("INCIDENT_MODIFIED", (await Assert.ThrowsAsync<ConflictException>(() => competing)).ErrorCode);
                Assert.True(patchBarrier.Attempts >= 2);
            }
            finally { save.Release.TrySetResult(); }
            await using var verify = new AppDbContext(options);
            Assert.Single(await verify.IncidentReports.ToArrayAsync()); Assert.Equal(2, await verify.HorseHealthIncidentDetails.CountAsync());
            Assert.Equal(2, await verify.HorseHealthLogs.CountAsync()); Assert.Equal(2, await verify.IncidentPhotos.CountAsync());
            Assert.Equal(10, await verify.Notifications.CountAsync()); Assert.Equal(12, await verify.OutboxMessages.CountAsync());
        });
    }

    [PostgresFact]
    public async Task Postgres_LostCommitAcknowledgmentReplayPreservesEvidenceAndDoesNotRepeatLogsOrAlerts()
    {
        await WithDatabaseAsync(async options =>
        {
            await using var seed = new AppDbContext(options); var f = await Scenario.CreateAsync(seed); var request = f.Report(); request.PhotoFiles = [Photo()];
            await using (var writer = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(options).AddInterceptors(new LostCommitAcknowledgment()).Options))
                await Assert.ThrowsAsync<IOException>(() => CreateIncidentService(writer, f.Storage).ReportHorseHealthAsync(request, f.Driver.AccountId));
            await using var verify = new AppDbContext(options);
            var replay = await CreateIncidentService(verify, f.Storage).ReportHorseHealthAsync(request, f.Driver.AccountId);
            Assert.False(replay.Created); Assert.Single(await verify.IncidentReports.ToArrayAsync()); Assert.Single(await verify.HorseHealthIncidentDetails.ToArrayAsync());
            Assert.Single(await verify.HorseHealthLogs.ToArrayAsync()); Assert.Single(await verify.IncidentPhotos.ToArrayAsync());
            Assert.Equal(4, await verify.Notifications.CountAsync()); Assert.Equal(5, await verify.OutboxMessages.CountAsync());
            Assert.Single(f.Storage.Confirmations); Assert.Empty(f.Storage.Deletes);
        });
    }

    private static async Task WithDatabaseAsync(Func<DbContextOptions<AppDbContext>, Task> test)
    {
        var settings = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("PEGAXUS_TEST_CONNECTION_STRING"))
            { Database = "pegaxus_incident203_" + Guid.NewGuid().ToString("N") };
        var builder = new NpgsqlDataSourceBuilder(settings.ConnectionString); var translator = new EnumTranslator();
        foreach (var type in typeof(IncidentStatus).Assembly.GetTypes().Where(type => type.IsEnum && type.Namespace == typeof(IncidentStatus).Namespace))
            builder.MapEnum(type, translator.TranslateTypeName(type.Name), translator);
        await using var source = builder.Build();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseTestNpgsql(source).Options;
        await using var db = new AppDbContext(options);
        try { await db.Database.MigrateAsync(); await test(options); }
        finally { await db.Database.EnsureDeletedAsync(); }
    }

    private sealed class EnumTranslator : INpgsqlNameTranslator
    {
        public string TranslateTypeName(string name) => new NpgsqlSnakeCaseNameTranslator().TranslateTypeName(name);
        public string TranslateMemberName(string name) => name;
    }

    private sealed class SaveAudit : SaveChangesInterceptor
    {
        internal int Calls { get; private set; }
        internal bool FailAfterSave { get; init; }
        internal (int Reports, int Details, int Logs, int Photos, int Notifications, int Recipients, int Outbox) Added { get; private set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var tracker = eventData.Context!.ChangeTracker; Calls++;
            Added = (tracker.Entries<IncidentReport>().Count(entry => entry.State == EntityState.Added),
                tracker.Entries<HorseHealthIncidentDetail>().Count(entry => entry.State == EntityState.Added),
                tracker.Entries<HorseHealthLog>().Count(entry => entry.State == EntityState.Added), tracker.Entries<IncidentPhoto>().Count(entry => entry.State == EntityState.Added),
                tracker.Entries<Notification>().Count(entry => entry.State == EntityState.Added), tracker.Entries<AccountNotification>().Count(entry => entry.State == EntityState.Added),
                tracker.Entries<OutboxMessage>().Count(entry => entry.State == EntityState.Added));
            return ValueTask.FromResult(result);
        }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default) =>
            FailAfterSave ? throw new IOException("Planned failure after database writes") : ValueTask.FromResult(result);
    }

    private sealed class PausedSave : SaveChangesInterceptor
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult(); await Release.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken); return result;
        }
    }

    private sealed class LostCommitAcknowledgment : DbTransactionInterceptor
    {
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default) =>
            throw new IOException("Planned lost commit acknowledgment");
    }

    private sealed class CompetingSnapshot : DbCommandInterceptor
    {
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Attempts { get; private set; }
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("UPDATE \"Transport\"", StringComparison.Ordinal) && ++Attempts == 1)
            {
                await eventData.Context!.Set<Transport>().AnyAsync(cancellationToken); Started.TrySetResult();
            }
            return result;
        }
    }
}
