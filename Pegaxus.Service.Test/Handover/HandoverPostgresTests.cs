using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Npgsql.NameTranslation;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Repository.Extensions;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;

using static Pegaxus.Service.Test.HandoverTests;

namespace Pegaxus.Service.Test;

[Collection("PostgresSharedDatabase")]
public partial class HandoverPostgresTests
{
    [PostgresFact]
    public async Task Postgres_QueriesTranslate_LocksReloadTrackedRecords_CancellationAndOutboxRollBackTogether()
    {
        await WithDatabaseAsync(async options =>
        {
            await using var seed = new AppDbContext(options); var f = await Fixture.CreateAsync(seed);
            await using (var reader = new AppDbContext(options))
            {
                var service = f.CreateService(reader);
                Assert.Equal(3, (await service.GetListAsync(f.TransportId, new() { Status = "PLANNED,COMPLETED" }, f.Manager.AccountId)).TotalItems);
                Assert.Equal(2, (await service.GetListAsync(f.TransportId, new(), f.CustomerAccountId)).TotalItems);
                Assert.Null((await service.GetAsync(f.Pickup.HandoverRecordId, f.CustomerAccountId)).To.ContactPhone);
                Assert.Equal(new[] { "START" }, (await service.GetAsync(f.Transfer.HandoverRecordId, f.Driver.AccountId)).AvailableActions);
            }
            await using (var stale = new AppDbContext(options))
            {
                await stale.HandoverRecords.SingleAsync(item => item.HandoverRecordId == f.Pickup.HandoverRecordId);
                await using (var writer = new AppDbContext(options))
                    Assert.Equal("IN_PROGRESS", (await f.CreateService(writer).StartAsync(f.Pickup.HandoverRecordId, f.Manager.AccountId)).Status);
                Assert.Equal("INVALID_HANDOVER_STATUS", (await Assert.ThrowsAsync<ConflictException>(() => f.CreateService(stale)
                    .StartAsync(f.Pickup.HandoverRecordId, f.Manager.AccountId))).ErrorCode);
                Assert.Equal("HANDOVER_MODIFIED", (await Assert.ThrowsAsync<ConflictException>(() => f.CreateService(stale)
                    .CancelAsync(f.Pickup.HandoverRecordId, f.Cancel(f.Pickup), f.Manager.AccountId))).ErrorCode);
            }
            var failure = new SaveAudit { FailAfterSave = true };
            await using (var writer = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(options).AddInterceptors(failure).Options))
                await Assert.ThrowsAsync<IOException>(() => f.CreateService(writer).CancelAsync(f.Transfer.HandoverRecordId, f.Cancel(f.Transfer), f.Manager.AccountId));
            Assert.Equal(1, failure.Calls); Assert.Equal((1, 3, 3), failure.Writes); Assert.Empty(f.Realtime.Messages);
            await using (var verify = new AppDbContext(options))
            {
                var unchanged = await verify.HandoverRecords.SingleAsync(item => item.HandoverRecordId == f.Transfer.HandoverRecordId);
                Assert.Equal(HandoverStatus.PLANNED, unchanged.Status); Assert.Null(unchanged.CancelledAt); Assert.Null(unchanged.CancellationReason);
                Assert.Equal(f.Transfer.UpdatedAt, unchanged.UpdatedAt); Assert.Empty(await verify.Notifications.ToListAsync());
                Assert.Empty(await verify.AccountNotifications.ToListAsync()); Assert.Empty(await verify.OutboxMessages.ToListAsync());
            }
            var audit = new SaveAudit();
            await using (var writer = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(options).AddInterceptors(audit).Options))
                Assert.Equal("CANCELLED", (await f.CreateService(writer).CancelAsync(f.Transfer.HandoverRecordId, f.Cancel(f.Transfer), f.Coordinator.AccountId)).Status);
            Assert.Equal(1, audit.Calls); Assert.Equal((1, 3, 3), audit.Writes); Assert.Empty(f.Realtime.Messages);
            await using var final = new AppDbContext(options);
            var cancelled = await final.HandoverRecords.SingleAsync(item => item.HandoverRecordId == f.Transfer.HandoverRecordId);
            Assert.Equal(Fixture.Now, cancelled.CancelledAt); Assert.Equal(DateTimeKind.Unspecified, cancelled.CancelledAt!.Value.Kind);
            Assert.Equal(3, await final.Notifications.CountAsync()); Assert.Equal(3, await final.OutboxMessages.CountAsync());
            var transport = await final.Transports.SingleAsync(); Assert.Equal(TransportStatus.READY, transport.Status);
            Assert.Null(transport.ActualDepartureAt); Assert.Null(transport.ActualArrivalAt);
            Assert.All(await final.RoutePlanRoutes.ToListAsync(), segment => { Assert.Null(segment.ActualDepartureAt); Assert.Null(segment.ActualArrivalAt); });
        });
    }

    [PostgresFact]
    public async Task Postgres_ConcurrentCancellationCommitsOneRecordChangeAndOneNotificationPerRecipient()
    {
        await WithDatabaseAsync(async options =>
        {
            await using var seed = new AppDbContext(options); var f = await Fixture.CreateAsync(seed);
            var saved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var competingLock = new LockSignal();
            var audit = new SaveAudit { AfterSave = async () => { saved.TrySetResult(); await release.Task.WaitAsync(TimeSpan.FromSeconds(15)); } };
            await using var first = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(options).AddInterceptors(audit).Options);
            await using var second = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(options).AddInterceptors(competingLock).Options);
            var winner = f.CreateService(first).CancelAsync(f.Transfer.HandoverRecordId, f.Cancel(f.Transfer), f.Manager.AccountId);
            try
            {
                await saved.Task.WaitAsync(TimeSpan.FromSeconds(15));
                await using (var beforeCommit = new AppDbContext(options))
                { Assert.Equal(HandoverStatus.PLANNED, (await beforeCommit.HandoverRecords.SingleAsync(item => item.HandoverRecordId == f.Transfer.HandoverRecordId)).Status); Assert.Empty(await beforeCommit.Notifications.ToListAsync()); }
                var loser = Record.ExceptionAsync(() => f.CreateService(second).CancelAsync(f.Transfer.HandoverRecordId, f.Cancel(f.Transfer), f.Coordinator.AccountId));
                // The competing transaction has read its snapshot before its lock blocks behind the winner.
                await competingLock.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
                release.TrySetResult(); Assert.Equal("CANCELLED", (await winner).Status);
                var error = await loser; Assert.NotNull(error);
                Assert.True(PostgresErrors.IsWriteConflict(error) || error.InnerException is { } inner && PostgresErrors.IsWriteConflict(inner), error.ToString());
            }
            finally { release.TrySetResult(); }
            await using var verify = new AppDbContext(options);
            Assert.Equal(HandoverStatus.CANCELLED, (await verify.HandoverRecords.SingleAsync(item => item.HandoverRecordId == f.Transfer.HandoverRecordId)).Status);
            Assert.Equal(3, await verify.Notifications.CountAsync()); Assert.Equal(3, await verify.AccountNotifications.CountAsync()); Assert.Equal(3, await verify.OutboxMessages.CountAsync());
            Assert.Equal(3, await verify.AccountNotifications.Select(item => item.AccountId).Distinct().CountAsync());
            Assert.Equal("INVALID_HANDOVER_STATUS", (await Assert.ThrowsAsync<ConflictException>(() => f.CreateService(verify)
                .CancelAsync(f.Transfer.HandoverRecordId, f.Cancel(f.Transfer), f.Manager.AccountId))).ErrorCode);
        });
    }

    private static async Task WithDatabaseAsync(Func<DbContextOptions<AppDbContext>, Task> test)
    {
        var settings = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("PEGAXUS_TEST_CONNECTION_STRING"))
            { Database = "pegaxus_ho172_test_" + Guid.NewGuid().ToString("N") };
        var builder = new NpgsqlDataSourceBuilder(settings.ConnectionString); var translator = new EnumTranslator();
        foreach (var type in typeof(TransportStatus).Assembly.GetTypes().Where(type => type.IsEnum && type.Namespace == typeof(TransportStatus).Namespace))
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

    private sealed class LockSignal : DbCommandInterceptor
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("UPDATE \"Transport\"", StringComparison.Ordinal)) Entered.TrySetResult();
            return ValueTask.FromResult(result);
        }
    }

    private sealed class SaveAudit : SaveChangesInterceptor
    {
        internal int Calls { get; private set; }
        internal (int Records, int Notifications, int Outbox) Writes { get; private set; }
        internal bool FailAfterSave { get; init; }
        internal Func<Task>? AfterSave { get; init; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var tracker = eventData.Context!.ChangeTracker; Calls++;
            // Only the saves of the handover itself count; the financial report that follows the delivery commit is a save of its own.
            var writes = (tracker.Entries<HandoverRecord>().Count(entry => entry.State == EntityState.Modified),
                tracker.Entries<Notification>().Count(entry => entry.State == EntityState.Added), tracker.Entries<OutboxMessage>().Count(entry => entry.State == EntityState.Added));
            if (writes != (0, 0, 0)) Writes = writes;
            return ValueTask.FromResult(result);
        }
        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (AfterSave is not null) await AfterSave();
            if (FailAfterSave) throw new IOException("Planned failure after database writes");
            return result;
        }
    }
}
