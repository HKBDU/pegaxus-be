using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Npgsql.NameTranslation;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Common;

using static Pegaxus.Service.Test.LocationTrackingTests;

namespace Pegaxus.Service.Test;

[Collection("PostgresSharedDatabase")]
public class LocationTrackingPostgresTests
{
    [PostgresFact]
    public async Task Postgres_GeofenceAndNotificationsRollBackAtomicallyAndOnlyCommittedSamplesReachMemory()
    {
        await WithDatabaseAsync(async options =>
        {
            await using var seed = new AppDbContext(options); var f = await Fixture.CreateAsync(seed);
            await using (var writer = new AppDbContext(options))
            {
                await f.CreateService(writer).RecordAsync(f.TransportId, f.Point(0.5m), f.Driver.AccountId);
                var snapshot = await f.CreateService(writer).GetAsync(f.TransportId, f.CustomerAccountId);
                Assert.Equal(2, snapshot.PlannedRoute.Segments.Count); Assert.Equal(0.5m, snapshot.Truck!.Longitude);
                Assert.Contains(f.CustomerAccountId, f.Realtime.Viewers); Assert.Contains(f.Manager.AccountId, f.Realtime.Viewers);
                Assert.DoesNotContain(f.Driver.AccountId, f.Realtime.Viewers);
            }
            // One driver runs both segments, so entering the end of segment 1 is timed by GPS and starts segment 2 in the same save.
            var point = f.Point(1); point.RecordedAt = point.RecordedAt!.Value.AddSeconds(15);
            var failed = new SaveAudit { FailAfterSave = true };
            await using (var writer = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(options).AddInterceptors(failed).Options))
                await Assert.ThrowsAsync<IOException>(() => f.CreateService(writer).RecordAsync(f.TransportId, point, f.Driver.AccountId));
            Assert.Equal(1, failed.Calls); Assert.Equal((2, 0, 0), failed.Added);
            Assert.Equal(0.5m, f.State.For(f.TransportId).Latest!.Longitude); Assert.Single(f.Realtime.Points);
            await using (var verify = new AppDbContext(options))
            {
                Assert.Null((await verify.RoutePlanRoutes.SingleAsync(segment => segment.RoutePlanRouteId == f.First.RoutePlanRouteId)).ActualArrivalAt);
                Assert.Null((await verify.RoutePlanRoutes.SingleAsync(segment => segment.RoutePlanRouteId == f.Second.RoutePlanRouteId)).ActualDepartureAt);
                Assert.Empty(await verify.OutboxMessages.ToListAsync());
            }
            var audit = new SaveAudit();
            await using (var writer = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(options).AddInterceptors(audit).Options))
            {
                await writer.RoutePlanRoutes.ToListAsync();
                await f.CreateService(writer).RecordAsync(f.TransportId, point, f.Driver.AccountId);
                Assert.Equal(1, audit.Calls); Assert.Equal((2, 0, 0), audit.Added);
                var later = f.Point(1); later.RecordedAt = point.RecordedAt!.Value.AddSeconds(15);
                await f.CreateService(writer).RecordAsync(f.TransportId, later, f.Driver.AccountId);
                Assert.Equal(1, audit.Calls);
            }
            await using (var verify = new AppDbContext(options))
            {
                var transport = await verify.Transports.SingleAsync();
                var first = await verify.RoutePlanRoutes.SingleAsync(item => item.RoutePlanRouteId == f.First.RoutePlanRouteId);
                var second = await verify.RoutePlanRoutes.SingleAsync(item => item.RoutePlanRouteId == f.Second.RoutePlanRouteId);
                Assert.Equal(DbTime.Truncate(point.RecordedAt!.Value), first.ActualArrivalAt); Assert.Equal(first.ActualArrivalAt, second.ActualDepartureAt);
                Assert.Equal(DateTimeKind.Unspecified, first.ActualArrivalAt!.Value.Kind);
                Assert.Null(second.ActualArrivalAt); Assert.Null(transport.ActualArrivalAt); Assert.Equal(TransportStatus.IN_TRANSIT, transport.Status);
            }
            var notificationFailure = new SaveAudit { FailAfterSave = true };
            await using (var writer = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(options).AddInterceptors(notificationFailure).Options))
                await Assert.ThrowsAsync<IOException>(() => f.CreateService(writer).PermissionDeniedAsync(f.TransportId, f.Driver.AccountId));
            Assert.Equal((0, 1, 1), notificationFailure.Added); Assert.Empty(f.State.For(f.TransportId).PermissionAlerts);
            await using (var verify = new AppDbContext(options))
            { Assert.Empty(await verify.Notifications.ToListAsync()); Assert.Empty(await verify.OutboxMessages.ToListAsync()); }
            var alerts = new SaveAudit();
            await using (var writer = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(options).AddInterceptors(alerts).Options))
            {
                Assert.True((await f.CreateService(writer).PermissionDeniedAsync(f.TransportId, f.Driver.AccountId)).Notified);
                Assert.False((await f.CreateService(writer).PermissionDeniedAsync(f.TransportId, f.Driver.AccountId)).Notified);
                Assert.Equal(1, alerts.Calls); Assert.Equal((0, 1, 1), alerts.Added);
            }
            await using var final = new AppDbContext(options);
            Assert.Single(await final.Notifications.ToListAsync()); Assert.Single(await final.AccountNotifications.ToListAsync());
            Assert.Single(await final.OutboxMessages.ToListAsync()); Assert.Empty(f.NotificationRealtime.Messages);
        });
    }

    [PostgresFact]
    public async Task Postgres_ConcurrentRequestsKeepLatestPointAndCooldownThreadSafe()
    {
        await WithDatabaseAsync(async options =>
        {
            await using var seed = new AppDbContext(options); var f = await Fixture.CreateAsync(seed);
            var saved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var hold = new SaveAudit { AfterSave = async () => { saved.TrySetResult(); await release.Task.WaitAsync(TimeSpan.FromSeconds(15)); } };
            await using var first = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(options).AddInterceptors(hold).Options);
            await using var second = new AppDbContext(options);
            var request = f.Point(1); var winner = f.CreateService(first).RecordAsync(f.TransportId, request, f.Driver.AccountId);
            try
            {
                await saved.Task.WaitAsync(TimeSpan.FromSeconds(15));
                Assert.Null(f.State.For(f.TransportId).Latest); Assert.Empty(f.Realtime.Points);
                var replay = f.CreateService(second).RecordAsync(f.TransportId, request, f.Driver.AccountId);
                Assert.False(replay.IsCompleted); release.TrySetResult(); await Task.WhenAll(winner, replay);
                Assert.Single(f.Realtime.Points); Assert.Equal(1, f.State.For(f.TransportId).Latest!.Longitude);
            }
            finally { release.TrySetResult(); }
            await using (var verify = new AppDbContext(options))
                Assert.Equal(Fixture.Now, (await verify.RoutePlanRoutes.SingleAsync(segment => segment.RoutePlanRouteId == f.First.RoutePlanRouteId)).ActualArrivalAt);
            await using var alert1 = new AppDbContext(options); await using var alert2 = new AppDbContext(options);
            var replies = await Task.WhenAll(f.CreateService(alert1).PermissionDeniedAsync(f.TransportId, f.Driver.AccountId),
                f.CreateService(alert2).PermissionDeniedAsync(f.TransportId, f.Driver.AccountId));
            Assert.Single(replies, reply => reply.Notified); Assert.Single(replies, reply => !reply.Notified);
            await using var final = new AppDbContext(options);
            Assert.Single(await final.Notifications.ToListAsync()); Assert.Single(await final.OutboxMessages.ToListAsync());
        });
    }

    private static async Task WithDatabaseAsync(Func<DbContextOptions<AppDbContext>, Task> test)
    {
        var settings = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("PEGAXUS_TEST_CONNECTION_STRING"))
            { Database = "pegaxus_gp171_test_" + Guid.NewGuid().ToString("N") };
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

    private sealed class SaveAudit : SaveChangesInterceptor
    {
        internal int Calls { get; private set; }
        internal (int Arrivals, int Notifications, int Outbox) Added { get; private set; }
        internal bool FailAfterSave { get; init; }
        internal Func<Task>? AfterSave { get; init; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var tracker = eventData.Context!.ChangeTracker; Calls++;
            Added = (tracker.Entries<Transport>().Count(entry => entry.State == EntityState.Modified) + tracker.Entries<RoutePlanRoute>().Count(entry => entry.State == EntityState.Modified),
                tracker.Entries<Notification>().Count(entry => entry.State == EntityState.Added), tracker.Entries<OutboxMessage>().Count(entry => entry.State == EntityState.Added));
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
