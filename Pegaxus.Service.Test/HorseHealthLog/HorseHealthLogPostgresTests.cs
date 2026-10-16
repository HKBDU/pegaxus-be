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
                Assert.Single(logs.Where(log => log.LogType == HorseHealthLogType.OTHER).Select(log => log.PhotoUrl).Distinct());
                Assert.Equal(2, await verify.Notifications.CountAsync()); Assert.Equal(2, await verify.AccountNotifications.CountAsync());
                Assert.Equal(2, await verify.OutboxMessages.CountAsync()); Assert.Empty(await verify.IncidentReports.ToListAsync());
            }
            Assert.Equal(2, f.Storage.Uploads.Count); Assert.Empty(f.Storage.Deletes);
            foreach (var failure in new[] { "upload", "save", "sign", "batch" })
            {
                var storage = new Storage { FailUpload = failure == "upload", FailSign = failure == "sign" };
                var failSave = new WriteAudit { FailAfterSave = failure is "save" or "batch" };
                await using var writer = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(options).AddInterceptors(failSave).Options);
                var service = CreateService(writer, storage, TestSupport.CreateNotifications(writer));
                var request = f.Request(); request.CheckedAt = Fixture.CheckTime.AddHours(-1); request.HealthStatus = "CRITICAL"; request.Files = [Photo()];
                if (failure == "batch")
                    await Assert.ThrowsAsync<IOException>(() => service.MarkAllNormalAsync(f.Transport.TransportId, request, f.Escort.AccountId));
                else await Assert.ThrowsAsync<IOException>(() => service.CreateAsync(f.Transport.TransportId, request, f.Escort.AccountId));
                Assert.Single(storage.Uploads); Assert.Equal(storage.Uploads, storage.Deletes);
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
            Assert.Single(retained.Uploads); Assert.Empty(retained.Deletes);
            await using (var verify = new AppDbContext(options))
            {
                Assert.Equal(5, await verify.HorseHealthLogs.CountAsync()); Assert.Equal(4, await verify.Notifications.CountAsync());
                Assert.Equal(4, await verify.OutboxMessages.CountAsync());
                var replay = await CreateService(verify, retained, TestSupport.CreateNotifications(verify)).CreateAsync(f.Transport.TransportId, uncertainRequest, f.Escort.AccountId);
                Assert.False(replay.Created); Assert.False(replay.Value.Effects.EmergencyNotified); Assert.Single(retained.Uploads);
            }
            var retriedStorage = new Storage(); var retryAudit = new WriteAudit { ConflictOnceAfterSave = true };
            await using (var writer = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(options).AddInterceptors(retryAudit).Options))
            {
                var request = f.Request(); request.CheckedAt = Fixture.CheckTime.AddHours(-3); request.HealthStatus = "CRITICAL"; request.Files = [Photo()];
                var result = await CreateService(writer, retriedStorage, TestSupport.CreateNotifications(writer)).CreateAsync(f.Transport.TransportId, request, f.Escort.AccountId);
                Assert.True(result.Created); Assert.Equal(2, retryAudit.Calls);
                Assert.Equal(2, retriedStorage.Uploads.Count); Assert.Equal(retriedStorage.Uploads[0], Assert.Single(retriedStorage.Deletes));
            }
            await using (var verify = new AppDbContext(options))
            {
                Assert.Equal(6, await verify.HorseHealthLogs.CountAsync()); Assert.Equal(6, await verify.Notifications.CountAsync());
                Assert.Equal(6, await verify.OutboxMessages.CountAsync());
                Assert.Contains(await verify.HorseHealthLogs.ToListAsync(), log => log.PhotoUrl == Storage.Url(retriedStorage.Uploads[1]));
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
                var storage = new Storage { AfterUpload = async () => { uploaded.TrySetResult(); await release.Task.WaitAsync(TimeSpan.FromSeconds(15)); } };
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
                    Assert.Single(storage.Uploads); Assert.Empty(storage.Deletes); Assert.Empty(competingStorage.Uploads);
                }
                finally { release.TrySetResult(); }
                await using var verify = new AppDbContext(options);
                var checkedAt = DbTime.Truncate(request.CheckedAt!.Value);
                var logs = await verify.HorseHealthLogs.Where(log => log.TransportId == f.Transport.TransportId && log.CheckedAt == checkedAt).ToArrayAsync();
                Assert.Equal(batch ? 2 : 1, logs.Length); Assert.Equal(logs.Length, logs.Select(log => log.HorseId).Distinct().Count());
                Assert.Single(logs.Select(log => log.PhotoUrl).Distinct());
                Assert.Equal(2, await verify.Notifications.CountAsync()); Assert.Equal(2, await verify.OutboxMessages.CountAsync());
            }
        });
    }

    private static async Task WithDatabaseAsync(Func<DbContextOptions<AppDbContext>, Task> test)
    {
        var settings = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("PEGAXUS_TEST_CONNECTION_STRING"))
            { Database = "pegaxus_hl170_test_" + Guid.NewGuid().ToString("N") };
        var builder = new NpgsqlDataSourceBuilder(settings.ConnectionString); var translator = new EnumTranslator();
        foreach (var type in typeof(HorseHealthLogType).Assembly.GetTypes().Where(type => type.IsEnum && type.Namespace == typeof(HorseHealthLogType).Namespace))
            builder.MapEnum(type, translator.TranslateTypeName(type.Name), translator);
        await using var source = builder.Build();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseTestNpgsql(source).Options;
        await using var db = new AppDbContext(options);
        try { await db.Database.EnsureCreatedAsync(); await test(options); }
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
