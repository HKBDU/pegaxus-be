using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Npgsql.NameTranslation;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;

using static Pegaxus.Service.Test.HorseHealthLogTests;

using HealthLogs = Pegaxus.Service.HorseHealthLogService;

namespace Pegaxus.Service.Test;

[Collection("PostgresSharedDatabase")]
public class HorseHealthLogPostgresTests
{
    [PostgresFact]
    public async Task Postgres_RecordingBatchAndOutboxAreAtomicAndFailedWritesCleanOnlyUncommittedPhotos()
    {
        await WithDatabaseAsync(async options =>
        {
            await using var seed = new AppDbContext(options); var f = await Fixture.CreateAsync(seed);
            f.Transport.Status = TransportStatus.IN_TRANSIT; await seed.SaveChangesAsync();
            var audit = new WriteAudit();
            await using (var writer = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(options).AddInterceptors(audit).Options))
            {
                var request = f.Request(); request.HealthStatus = "CRITICAL"; request.TemperatureCelsius = 38.2m; request.Files = [Photo()];
                var result = await CreateService(writer, f.Storage, TestSupport.CreateNotifications(writer)).CreateAsync(f.Transport.TransportId, request, f.Escort.AccountId);
                Assert.True(result.Created); Assert.True(result.Value.Effects.EmergencyNotified);
                Assert.Equal(38.2m, result.Value.TemperatureCelsius); Assert.Equal(Storage.SignedUrl, Assert.Single(result.Value.Photos).PhotoUrl);
                Assert.Equal(1, audit.Calls); Assert.Equal((1, 2, 2), audit.Added);
            }
            await using (var writer = new AppDbContext(options))
            {
                var service = CreateService(writer, f.Storage, TestSupport.CreateNotifications(writer));
                var replay = await service.CreateAsync(f.Transport.TransportId, f.Request(), f.Escort.AccountId);
                Assert.False(replay.Created); Assert.Equal("CRITICAL", replay.Value.HealthStatus); Assert.False(replay.Value.Effects.EmergencyNotified);
                var partial = await service.MarkAllNormalAsync(f.Transport.TransportId,
                    new() { LogType = "ROUTINE_CHECK", CheckedAt = Fixture.CheckTime }, f.Escort.AccountId);
                Assert.Equal(1, partial.Value.CreatedCount); Assert.Equal(2, partial.Value.Logs.Count);
                Assert.Equal("CRITICAL", partial.Value.Logs.Single(log => log.Horse.HorseId == f.Horse.HorseId).HealthStatus);
                var batch = new HealthLogs.Request.RecordRequest { LogType = "OTHER", CheckedAt = Fixture.CheckTime.AddMinutes(-1), Files = [Photo()] };
                var normal = await service.MarkAllNormalAsync(f.Transport.TransportId, batch, f.Escort.AccountId);
                Assert.Equal(2, normal.Value.CreatedCount); Assert.All(normal.Value.Logs, log => Assert.Equal("NORMAL", log.HealthStatus));
                Assert.Equal(0, (await service.MarkAllNormalAsync(f.Transport.TransportId, batch, f.Escort.AccountId)).Value.CreatedCount);
            }
            await using (var verify = new AppDbContext(options))
            {
                var logs = await verify.HorseHealthLogs.ToArrayAsync(); Assert.Equal(4, logs.Length);
                Assert.All(logs, log => { Assert.Equal(DateTimeKind.Unspecified, log.CheckedAt.Kind); Assert.Equal(0, log.CheckedAt.Ticks % 10); });
                Assert.Single(logs.Where(log => log.LogType == HorseHealthLogType.OTHER).SelectMany(log => log.PhotoUrls).Distinct());
                Assert.Equal(2, await verify.Notifications.CountAsync()); Assert.Equal(2, await verify.AccountNotifications.CountAsync());
                Assert.Equal(2, await verify.OutboxMessages.CountAsync()); Assert.Empty(await verify.IncidentReports.ToListAsync());
            }
            Assert.Equal(2, f.Storage.Confirmations.Count); Assert.Empty(f.Storage.Deletes);
            foreach (var failure in new[] { "upload", "save", "sign", "batch" })
            {
                var storage = new Storage { FailConfirmation = failure == "upload", FailSign = failure == "sign" };
                var failSave = new WriteAudit { FailAfterSave = failure is "save" or "batch" };
                await using var writer = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(options).AddInterceptors(failSave).Options);
                var service = CreateService(writer, storage, TestSupport.CreateNotifications(writer));
                var request = f.Request(); request.CheckedAt = Fixture.CheckTime.AddHours(-1); request.HealthStatus = "CRITICAL"; request.Files = [Photo()];
                if (failure == "batch")
                    await Assert.ThrowsAsync<IOException>(() => service.MarkAllNormalAsync(f.Transport.TransportId, request, f.Escort.AccountId));
                else await Assert.ThrowsAsync<IOException>(() => service.CreateAsync(f.Transport.TransportId, request, f.Escort.AccountId));
                Assert.Single(storage.Confirmations); Assert.Empty(storage.Deletes);
                await using var verify = new AppDbContext(options);
                Assert.Equal(4, await verify.HorseHealthLogs.CountAsync()); Assert.Equal(2, await verify.Notifications.CountAsync());
                Assert.Equal(2, await verify.AccountNotifications.CountAsync()); Assert.Equal(2, await verify.OutboxMessages.CountAsync());
            }
            var retained = new Storage();
            var uncertainRequest = f.Request(); uncertainRequest.CheckedAt = Fixture.CheckTime.AddHours(-2);
            uncertainRequest.HealthStatus = "CRITICAL"; uncertainRequest.Files = [Photo()];
            await using (var writer = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(options).AddInterceptors(new LostCommitAcknowledgment()).Options))
                await Assert.ThrowsAsync<IOException>(() => CreateService(writer, retained, TestSupport.CreateNotifications(writer))
                    .CreateAsync(f.Transport.TransportId, uncertainRequest, f.Escort.AccountId));
            Assert.Single(retained.Confirmations); Assert.Empty(retained.Deletes);
            await using (var verify = new AppDbContext(options))
            {
                Assert.Equal(5, await verify.HorseHealthLogs.CountAsync()); Assert.Equal(4, await verify.Notifications.CountAsync());
                Assert.Equal(4, await verify.OutboxMessages.CountAsync());
                var replay = await CreateService(verify, retained, TestSupport.CreateNotifications(verify)).CreateAsync(f.Transport.TransportId, uncertainRequest, f.Escort.AccountId);
                Assert.False(replay.Created); Assert.False(replay.Value.Effects.EmergencyNotified); Assert.Single(retained.Confirmations);
            }
            var retriedStorage = new Storage(); var retryAudit = new WriteAudit { ConflictOnceAfterSave = true };
            await using (var writer = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(options).AddInterceptors(retryAudit).Options))
            {
                var request = f.Request(); request.CheckedAt = Fixture.CheckTime.AddHours(-3); request.HealthStatus = "CRITICAL"; request.Files = [Photo()];
                var result = await CreateService(writer, retriedStorage, TestSupport.CreateNotifications(writer)).CreateAsync(f.Transport.TransportId, request, f.Escort.AccountId);
                Assert.True(result.Created); Assert.Equal(2, retryAudit.Calls);
                Assert.Equal(2, retriedStorage.Confirmations.Count); Assert.Empty(retriedStorage.Deletes);
            }
            await using (var verify = new AppDbContext(options))
            {
                Assert.Equal(6, await verify.HorseHealthLogs.CountAsync()); Assert.Equal(6, await verify.Notifications.CountAsync());
                Assert.Equal(6, await verify.OutboxMessages.CountAsync());
                Assert.Contains(await verify.HorseHealthLogs.ToListAsync(), log => log.PhotoUrls.Contains(Storage.Url(retriedStorage.Confirmations[1])));
            }
            await using var stale = new AppDbContext(options);
            await stale.Transports.SingleAsync(item => item.TransportId == f.Transport.TransportId);
            await using (var changer = new AppDbContext(options))
                await changer.Transports.Where(item => item.TransportId == f.Transport.TransportId).ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Status, TransportStatus.COMPLETED));
            Assert.Equal("INVALID_TRANSPORT_STATUS", (await Assert.ThrowsAsync<ConflictException>(() =>
                CreateService(stale, retained, TestSupport.CreateNotifications(stale)).CreateAsync(f.Transport.TransportId, uncertainRequest, f.Escort.AccountId))).ErrorCode);
        });
    }

    [PostgresFact]
    public async Task Postgres_ConcurrentSingleAndBatchReplaysCreateOneCheckPerHorseAndOneSetOfAlerts()
    {
        await WithDatabaseAsync(async options =>
        {
            await using var seed = new AppDbContext(options); var f = await Fixture.CreateAsync(seed);
            foreach (var batch in new[] { false, true })
            {
                var uploaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var storage = new Storage { AfterConfirmation = async () => { uploaded.TrySetResult(); await release.Task.WaitAsync(TimeSpan.FromSeconds(15)); } };
                var competingStorage = new Storage(); var barrier = new CompetingSnapshot();
                await using var first = new AppDbContext(options);
                await using var second = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(options).AddInterceptors(barrier).Options);
                var request = f.Request(); request.CheckedAt = Fixture.CheckTime.AddMinutes(batch ? -11 : -10);
                request.HealthStatus = "CRITICAL"; request.Files = [Photo()];
                var firstService = CreateService(first, storage, TestSupport.CreateNotifications(first));
                async Task<int> WriteFirstAsync() => batch
                    ? (await firstService.MarkAllNormalAsync(f.Transport.TransportId, request, f.Escort.AccountId)).Value.CreatedCount
                    : (await firstService.CreateAsync(f.Transport.TransportId, request, f.Escort.AccountId)).Created ? 1 : 0;
                var winner = WriteFirstAsync();
                try
                {
                    await uploaded.Task.WaitAsync(TimeSpan.FromSeconds(15));
                    var competing = CreateService(second, competingStorage, TestSupport.CreateNotifications(second)).CreateAsync(f.Transport.TransportId, request, f.Escort.AccountId);
                    await barrier.Started.Task.WaitAsync(TimeSpan.FromSeconds(15)); release.TrySetResult();
                    Assert.Equal(batch ? 2 : 1, await winner);
                    var replay = await competing;
                    Assert.False(replay.Created); Assert.Equal(batch ? "NORMAL" : "CRITICAL", replay.Value.HealthStatus);
                    Assert.False(replay.Value.Effects.EmergencyNotified); Assert.False(replay.Value.Effects.CanReportIncident);
                    Assert.True(barrier.Attempts >= 2, "The competing serializable snapshot must conflict and retry.");
                    Assert.Single(storage.Confirmations); Assert.Empty(storage.Deletes); Assert.Empty(competingStorage.Confirmations);
                }
                finally { release.TrySetResult(); }
                await using var verify = new AppDbContext(options);
                var checkedAt = DbTime.Truncate(request.CheckedAt!.Value);
                var logs = await verify.HorseHealthLogs.Where(log => log.TransportId == f.Transport.TransportId && log.CheckedAt == checkedAt).ToArrayAsync();
                Assert.Equal(batch ? 2 : 1, logs.Length); Assert.Equal(logs.Length, logs.Select(log => log.HorseId).Distinct().Count());
                Assert.Single(logs.SelectMany(log => log.PhotoUrls).Distinct());
                Assert.Equal(2, await verify.Notifications.CountAsync()); Assert.Equal(2, await verify.OutboxMessages.CountAsync());
            }
        });
    }

    [PostgresFact]
    public async Task Postgres_AppendRollsBackAndPreservesUploadedEvidence()
    {
        foreach (var failure in new[] { "save", "sign", "lostCommit", "retry" })
        await WithDatabaseAsync(async options =>
        {
            await using var seed = new AppDbContext(options); var f = await Fixture.CreateAsync(seed);
            var request = f.Request(); request.Files = [Photo()]; request.GeneralNote = "Immutable";
            var id = (await f.Service.CreateAsync(f.Transport.TransportId, request, f.Escort.AccountId)).Value.HorseHealthLogId;
            var original = (await seed.HorseHealthLogs.SingleAsync()).PhotoUrls.ToArray();
            var storage = new Storage { FailSign = failure == "sign" };
            var audit = new WriteAudit { FailAfterSave = failure == "save", ConflictOnceAfterSave = failure == "retry" };
            var writerOptions = new DbContextOptionsBuilder<AppDbContext>(options)
                .AddInterceptors(failure == "lostCommit" ? new LostCommitAcknowledgment() : audit).Options;
            await using (var writer = new AppDbContext(writerOptions))
            {
                Func<Task<HealthLogs.Response.PhotosResponse>> append = () => CreateService(writer, storage, TestSupport.CreateNotifications(writer))
                    .AddPhotosAsync(f.Transport.TransportId, id, new() { Files = [Photo(), Photo()] }, f.Escort.AccountId);
                if (failure == "retry") Assert.Equal(3, (await append()).Photos.Count);
                else await Assert.ThrowsAsync<IOException>(append);
            }
            await using var verify = new AppDbContext(options); var log = await verify.HorseHealthLogs.SingleAsync();
            Assert.Equal("Immutable", log.GeneralNote); Assert.Empty(await verify.Notifications.ToListAsync());
            Assert.Equal(failure == "retry" ? 4 : 2, storage.Confirmations.Count);
            if (failure == "retry")
            {
                Assert.Equal(original.Concat(storage.Confirmations.Skip(2).Select(Storage.Url)), log.PhotoUrls);
                Assert.Empty(storage.Deletes); Assert.Equal(2, audit.Calls);
            }
            else if (failure == "lostCommit")
            {
                Assert.Equal(original.Concat(storage.Confirmations.Select(Storage.Url)), log.PhotoUrls); Assert.Empty(storage.Deletes);
            }
            else
            {
                Assert.Equal(original, log.PhotoUrls); Assert.Empty(storage.Deletes);
            }
        });
    }

    [PostgresFact]
    public async Task Postgres_AppendReloadsTrackedPhotosBeforeCheckingLimitAndNeverOverwritesEvidence()
    {
        await WithDatabaseAsync(async options =>
        {
            await using var seed = new AppDbContext(options); var f = await Fixture.CreateAsync(seed);
            var id = (await f.Service.CreateAsync(f.Transport.TransportId, f.Request(), f.Escort.AccountId)).Value.HorseHealthLogId;
            await using var stale = new AppDbContext(options); await stale.HorseHealthLogs.SingleAsync();
            var firstStorage = new Storage(); var secondStorage = new Storage();
            await using (var first = new AppDbContext(options))
                await CreateService(first, firstStorage, TestSupport.CreateNotifications(first)).AddPhotosAsync(f.Transport.TransportId, id,
                    new() { Files = [Photo(), Photo(), Photo()] }, f.Escort.AccountId);
            var secondService = CreateService(stale, secondStorage, TestSupport.CreateNotifications(stale));
            Assert.Equal(5, (await secondService.AddPhotosAsync(f.Transport.TransportId, id,
                new() { Files = [Photo(), Photo()] }, f.Escort.AccountId)).Photos.Count);
            await Assert.ThrowsAsync<UnprocessableEntityException>(() => secondService.AddPhotosAsync(f.Transport.TransportId, id,
                new() { Files = [Photo()] }, f.Escort.AccountId));
            await using var verify = new AppDbContext(options);
            Assert.Equal(firstStorage.Confirmations.Concat(secondStorage.Confirmations).Select(Storage.Url), (await verify.HorseHealthLogs.SingleAsync()).PhotoUrls);
            Assert.Empty(firstStorage.Deletes); Assert.Empty(secondStorage.Deletes);
        });
    }

    [PostgresFact]
    public async Task Postgres_ConcurrentAppendsRetryWithoutLostPhotosOrExceedingFive()
    {
        foreach (var (existing, count, bothSucceed) in new[] { (0, 2, true), (3, 2, false) })
        await WithDatabaseAsync(async options =>
        {
            await using var seed = new AppDbContext(options); var f = await Fixture.CreateAsync(seed);
            var request = f.Request(); request.Files = Enumerable.Range(0, existing).Select(_ => Photo()).ToList();
            var id = (await f.Service.CreateAsync(f.Transport.TransportId, request, f.Escort.AccountId)).Value.HorseHealthLogId;
            var original = (await seed.HorseHealthLogs.SingleAsync()).PhotoUrls.ToArray();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var snapshot = new CompetingSnapshot();
            var firstStorage = new Storage { AfterConfirmation = async () => { entered.TrySetResult(); await release.Task.WaitAsync(TimeSpan.FromSeconds(15)); } };
            var secondStorage = new Storage();
            await using var first = new AppDbContext(options);
            await using var second = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(options).AddInterceptors(snapshot).Options);
            var winner = CreateService(first, firstStorage, TestSupport.CreateNotifications(first)).AddPhotosAsync(f.Transport.TransportId, id,
                new() { Files = Enumerable.Range(0, count).Select(_ => Photo()).ToList() }, f.Escort.AccountId);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Task<HealthLogs.Response.PhotosResponse>? competing = null;
            try
            {
                competing = CreateService(second, secondStorage, TestSupport.CreateNotifications(second)).AddPhotosAsync(f.Transport.TransportId, id,
                    new() { Files = Enumerable.Range(0, count).Select(_ => Photo()).ToList() }, f.Escort.AccountId);
                await snapshot.Started.Task.WaitAsync(TimeSpan.FromSeconds(15));
            }
            finally { release.TrySetResult(); }
            await winner;
            Assert.NotNull(competing);
            if (bothSucceed) Assert.Equal(existing + 2 * count, (await competing).Photos.Count);
            else Assert.Equal("PHOTO_LIMIT_EXCEEDED", (await Assert.ThrowsAsync<UnprocessableEntityException>(() => competing)).ErrorCode);
            Assert.True(snapshot.Attempts >= 2);
            await using var verify = new AppDbContext(options); var log = await verify.HorseHealthLogs.SingleAsync();
            Assert.Equal(original.Concat(firstStorage.Confirmations.Concat(secondStorage.Confirmations).Select(Storage.Url)), log.PhotoUrls);
            Assert.Equal(bothSucceed ? count : 0, secondStorage.Confirmations.Count);
            Assert.Empty(firstStorage.Deletes); Assert.Empty(secondStorage.Deletes);
        });
    }

    [PostgresFact]
    public async Task Migration_PhotoArraysPreserveLegacyUrlsRejectSixAndRefuseLossyRollback()
    {
        await WithDatabaseAsync(async options =>
        {
            await using var db = new AppDbContext(options); var f = await Fixture.CreateAsync(db);
            var request = f.Request(); request.Files = [Photo()];
            var id = (await f.Service.CreateAsync(f.Transport.TransportId, request, f.Escort.AccountId)).Value.HorseHealthLogId;
            request.HorseId = await db.BookingHorses.Where(item => item.BookingId == f.Transport.BookingId && item.HorseId != f.Horse.HorseId)
                .Select(item => item.HorseId).SingleAsync(); request.Files = [];
            await f.Service.CreateAsync(f.Transport.TransportId, request, f.Escort.AccountId);
            var original = (await db.HorseHealthLogs.SingleAsync(item => item.HorseHealthLogId == id)).PhotoUrls.Single();
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20261016090000_AddFlow4Schema");
            Assert.Equal(original, await db.Database.SqlQuery<string>($"""SELECT photo_url AS "Value" FROM "HorseHealthLog" WHERE horse_health_log_id = {id}""").SingleAsync());
            await migrator.MigrateAsync();
            await using var check = new AppDbContext(options);
            Assert.False(check.Database.HasPendingModelChanges());
            var log = await check.HorseHealthLogs.SingleAsync(item => item.HorseHealthLogId == id);
            Assert.Equal(new[] { original }, log.PhotoUrls);
            Assert.Empty((await check.HorseHealthLogs.SingleAsync(item => item.HorseHealthLogId != id)).PhotoUrls);
            log.PhotoUrls = Enumerable.Range(0, 6).Select(_ => original).ToArray();
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => check.SaveChangesAsync());
            Assert.Equal(PostgresErrorCodes.CheckViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
            log.PhotoUrls = [original, original + "-second"]; await check.SaveChangesAsync();
            var rollback = await Assert.ThrowsAsync<PostgresException>(() => check.GetService<IMigrator>().MigrateAsync("20261016090000_AddFlow4Schema"));
            Assert.Contains("multiple photos", rollback.MessageText);
            await using var verify = new AppDbContext(options);
            Assert.Equal(2, (await verify.HorseHealthLogs.SingleAsync(item => item.HorseHealthLogId == id)).PhotoUrls.Length);
        }, migrate: true);
    }

    private static async Task WithDatabaseAsync(Func<DbContextOptions<AppDbContext>, Task> test, bool migrate = false)
    {
        var settings = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("PEGAXUS_TEST_CONNECTION_STRING"))
            { Database = "pegaxus_hl170_test_" + Guid.NewGuid().ToString("N") };
        var builder = new NpgsqlDataSourceBuilder(settings.ConnectionString); var translator = new EnumTranslator();
        foreach (var type in typeof(HorseHealthLogType).Assembly.GetTypes().Where(type => type.IsEnum && type.Namespace == typeof(HorseHealthLogType).Namespace))
            builder.MapEnum(type, translator.TranslateTypeName(type.Name), translator);
        await using var source = builder.Build();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseTestNpgsql(source).Options;
        await using var db = new AppDbContext(options);
        try
        {
            if (migrate) await db.Database.MigrateAsync();
            else await db.Database.EnsureCreatedAsync();
            await test(options);
        }
        finally { await db.Database.EnsureDeletedAsync(); }
    }

    private sealed class EnumTranslator : INpgsqlNameTranslator
    {
        public string TranslateTypeName(string name) => new NpgsqlSnakeCaseNameTranslator().TranslateTypeName(name);
        public string TranslateMemberName(string name) => name;
    }

    private sealed class WriteAudit : SaveChangesInterceptor
    {
        internal int Calls { get; private set; }
        internal (int Logs, int Notifications, int Outbox) Added { get; private set; }
        internal bool FailAfterSave { get; init; }
        internal bool ConflictOnceAfterSave { get; init; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var tracker = eventData.Context!.ChangeTracker; Calls++;
            Added = (tracker.Entries<HorseHealthLog>().Count(entry => entry.State == EntityState.Added),
                tracker.Entries<Notification>().Count(entry => entry.State == EntityState.Added),
                tracker.Entries<OutboxMessage>().Count(entry => entry.State == EntityState.Added));
            return ValueTask.FromResult(result);
        }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default) =>
            FailAfterSave ? throw new IOException("Planned failure after database writes") :
            ConflictOnceAfterSave && Calls == 1 ? throw new InvalidOperationException("Planned serialization failure",
                new PostgresException("Planned serialization failure", "ERROR", "ERROR", PostgresErrorCodes.SerializationFailure)) : ValueTask.FromResult(result);
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
            if (command.CommandText.StartsWith("UPDATE \"Transport\"", StringComparison.Ordinal))
            {
                if (++Attempts == 1)
                {
                    // Pin the competing snapshot before releasing the winner, so the retry is deterministic.
                    await eventData.Context!.Set<Transport>().AnyAsync(cancellationToken);
                    Started.TrySetResult();
                }
            }
            return result;
        }
    }
}
