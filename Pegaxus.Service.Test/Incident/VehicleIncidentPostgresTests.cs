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
using static Pegaxus.Service.Test.VehicleIncidentTests;

using Incidents = Pegaxus.Service.IncidentService;

namespace Pegaxus.Service.Test;

[Collection("PostgresSharedDatabase")]
public class VehicleIncidentPostgresTests
{
    [PostgresFact]
    public async Task Postgres_ReportAndUpdateAreAtomic_WithPhotosVehicleNotificationsAndDurableMail()
    {
        await WithDatabaseAsync(async options =>
        {
            await using var seed = new AppDbContext(options); var f = await Scenario.CreateAsync(seed);
            foreach (var failure in new[] { "save", "sign", "upload" })
            {
                var storage = new Storage { FailSign = failure == "sign", FailAtConfirmation = failure == "upload" ? 2 : null };
                var audit = new SaveAudit { FailAfterSave = failure == "save" };
                await using var writer = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(options).AddInterceptors(audit).Options);
                var request = f.Report(); request.VehicleOperable = false; request.ReplacementVehicleRequired = true; request.PhotoFiles = [Photo(), Photo()];
                await Assert.ThrowsAsync<IOException>(() => CreateService(writer, storage).ReportVehicleAsync(request, f.Driver.AccountId));
                await using var check = new AppDbContext(options);
                Assert.Empty(await check.IncidentReports.ToArrayAsync()); Assert.Empty(await check.VehicleIncidentDetails.ToArrayAsync());
                Assert.Empty(await check.IncidentPhotos.ToArrayAsync()); Assert.Empty(await check.Notifications.ToArrayAsync());
                Assert.Empty(await check.AccountNotifications.ToArrayAsync()); Assert.Empty(await check.OutboxMessages.ToArrayAsync());
                Assert.Equal(VehicleStatus.IN_USE, (await check.Vehicles.SingleAsync()).Status); Assert.Empty(storage.Deletes);
            }
            Incidents.Response.Incident incident;
            var saved = new SaveAudit();
            await using (var writer = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(options).AddInterceptors(saved).Options))
            {
                var request = f.Report(); request.VehicleOperable = false; request.ReplacementVehicleRequired = true;
                request.EstimatedRepairCost = 1_000_000; request.EstimatedRepairHours = 6.5m; request.PhotoFiles = [Photo()];
                incident = (await CreateService(writer, f.Storage).ReportVehicleAsync(request, f.Driver.AccountId)).Value;
                Assert.Equal(1, saved.Calls); Assert.Equal((1, 1, 1, 4, 4, 5), saved.Added);
                Assert.Equal("OUT_OF_SERVICE", incident.VehicleDetail!.Vehicle.Status); Assert.Equal(1_000_000, incident.VehicleDetail.EstimatedRepairCost);
                Assert.Equal(Storage.SignedUrl, Assert.Single(incident.Photos).PhotoUrl);
            }
            foreach (var failure in new[] { "save", "sign" })
            {
                var storage = new Storage { FailSign = failure == "sign" };
                await using var writer = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(options).AddInterceptors(new SaveAudit { FailAfterSave = failure == "save" }).Options);
                var request = new Incidents.Request.UpdateVehicleRequest { LastUpdatedAt = incident.UpdatedAt, RepairNote = "Uncommitted repair", PhotoFiles = [Photo()] };
                await Assert.ThrowsAsync<IOException>(() => CreateService(writer, storage).UpdateVehicleAsync(incident.IncidentReportId, request, f.Manager.AccountId));
                await using var check = new AppDbContext(options);
                Assert.Single(await check.IncidentPhotos.ToArrayAsync()); Assert.Equal(4, await check.Notifications.CountAsync());
                Assert.Equal(5, await check.OutboxMessages.CountAsync()); Assert.Null((await check.VehicleIncidentDetails.SingleAsync()).RepairNote);
                Assert.Equal(incident.UpdatedAt, DbTime.AsUtc((await check.IncidentReports.SingleAsync()).UpdatedAt)); Assert.Empty(storage.Deletes);
            }
            await using var stale = new AppDbContext(options);
            await stale.IncidentReports.SingleAsync(); await stale.VehicleIncidentDetails.SingleAsync();
            await using (var writer = new AppDbContext(options))
            {
                var updated = await CreateService(writer, f.Storage).UpdateVehicleAsync(incident.IncidentReportId,
                    new() { LastUpdatedAt = incident.UpdatedAt.AddTicks(9), RepairNote = "Mechanic arrived", EstimatedRepairHours = 2.5m }, f.Coordinator.AccountId);
                Assert.Equal("Mechanic arrived", updated.VehicleDetail!.RepairNote); Assert.Equal(2.5m, updated.VehicleDetail.EstimatedRepairHours);
                Assert.True(updated.UpdatedAt > incident.UpdatedAt);
            }
            Assert.Equal("INCIDENT_MODIFIED", (await Assert.ThrowsAsync<ConflictException>(() => CreateService(stale, f.Storage).UpdateVehicleAsync(incident.IncidentReportId,
                new() { LastUpdatedAt = incident.UpdatedAt, RepairNote = "Stale writer" }, f.Driver.AccountId))).ErrorCode);
            await using var verify = new AppDbContext(options);
            Assert.Equal(7, await verify.Notifications.CountAsync()); Assert.Equal(8, await verify.OutboxMessages.CountAsync());
            Assert.Equal(DateTimeKind.Unspecified, (await verify.IncidentReports.SingleAsync()).UpdatedAt.Kind);
            Assert.False(verify.Database.HasPendingModelChanges());
        });
    }

    [PostgresFact]
    public async Task Postgres_ConcurrentOfflineReplaysCreateOneIncidentAndOneSetOfEffects_ConcurrentPatchesRejectStaleVersion()
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
            var request = f.Report(); request.PhotoFiles = [Photo()]; request.Category = "BOTH"; request.VehicleOperable = false; request.ReplacementVehicleRequired = true;
            var winner = CreateService(first, storage).ReportVehicleAsync(request, f.Driver.AccountId);
            Incidents.Response.Incident incident;
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
                var competing = CreateService(second, competitorStorage).ReportVehicleAsync(request, f.Driver.AccountId);
                await barrier.Started.Task.WaitAsync(TimeSpan.FromSeconds(20)); release.TrySetResult();
                var created = await winner; Assert.True(created.Created); incident = created.Value;
                var replay = await competing; Assert.False(replay.Created); Assert.Equal(incident.IncidentReportId, replay.Value.IncidentReportId);
                Assert.Equal(new Incidents.Response.Effects(false, false, VehicleStatusChanged: false, CanAddHorseDetails: false), replay.Value.Effects);
                Assert.True(barrier.Attempts >= 2); Assert.Empty(competitorStorage.Confirmations); Assert.Single(storage.Confirmations);
            }
            finally { release.TrySetResult(); }
            var patchEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var patchRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var patchStorage = new Storage { AfterConfirmation = async () => { patchEntered.TrySetResult(); await patchRelease.Task.WaitAsync(TimeSpan.FromSeconds(20)); } };
            var patchBarrier = new CompetingSnapshot();
            await using var patchFirst = new AppDbContext(options);
            await using var patchSecond = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(options).AddInterceptors(patchBarrier).Options);
            var patch = new Incidents.Request.UpdateVehicleRequest { LastUpdatedAt = incident.UpdatedAt, RepairNote = "Mechanic arrived", PhotoFiles = [Photo()] };
            var patched = CreateService(patchFirst, patchStorage).UpdateVehicleAsync(incident.IncidentReportId, patch, f.Manager.AccountId);
            try
            {
                await patchEntered.Task.WaitAsync(TimeSpan.FromSeconds(20));
                var competing = CreateService(patchSecond, competitorStorage).UpdateVehicleAsync(incident.IncidentReportId, patch, f.Coordinator.AccountId);
                await patchBarrier.Started.Task.WaitAsync(TimeSpan.FromSeconds(20)); patchRelease.TrySetResult();
                Assert.Equal(2, (await patched).PhotoCount);
                Assert.Equal("INCIDENT_MODIFIED", (await Assert.ThrowsAsync<ConflictException>(() => competing)).ErrorCode);
                Assert.True(patchBarrier.Attempts >= 2); Assert.Empty(competitorStorage.Confirmations);
            }
            finally { patchRelease.TrySetResult(); }
            await using var verify = new AppDbContext(options);
            Assert.Single(await verify.IncidentReports.ToArrayAsync()); Assert.Single(await verify.VehicleIncidentDetails.ToArrayAsync());
            Assert.Equal(2, await verify.IncidentPhotos.CountAsync()); Assert.Equal(8, await verify.Notifications.CountAsync());
            Assert.Equal(9, await verify.OutboxMessages.CountAsync()); Assert.Equal(VehicleStatus.OUT_OF_SERVICE, (await verify.Vehicles.SingleAsync()).Status);
        });
    }

    [PostgresFact]
    public async Task Postgres_LostCommitAcknowledgmentReplayPreservesEvidenceAndDoesNotRepeatAlerts()
    {
        await WithDatabaseAsync(async options =>
        {
            await using var seed = new AppDbContext(options); var f = await Scenario.CreateAsync(seed); var request = f.Report(); request.PhotoFiles = [Photo()];
            await using (var writer = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(options).AddInterceptors(new LostCommitAcknowledgment()).Options))
                await Assert.ThrowsAsync<IOException>(() => CreateService(writer, f.Storage).ReportVehicleAsync(request, f.Driver.AccountId));
            await using var verify = new AppDbContext(options);
            var replay = await CreateService(verify, f.Storage).ReportVehicleAsync(request, f.Driver.AccountId);
            Assert.False(replay.Created); Assert.Single(await verify.IncidentReports.ToArrayAsync()); Assert.Single(await verify.IncidentPhotos.ToArrayAsync());
            Assert.Equal(4, await verify.Notifications.CountAsync()); Assert.Equal(5, await verify.OutboxMessages.CountAsync());
            Assert.Single(f.Storage.Confirmations); Assert.Empty(f.Storage.Deletes);
        });
    }

    internal static async Task WithDatabaseAsync(Func<DbContextOptions<AppDbContext>, Task> test)
    {
        var settings = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("PEGAXUS_TEST_CONNECTION_STRING"))
            { Database = "pegaxus_incident202_" + Guid.NewGuid().ToString("N") };
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

    internal sealed class SaveAudit : SaveChangesInterceptor
    {
        internal int Calls { get; private set; }
        internal bool FailAfterSave { get; init; }
        internal (int Reports, int Details, int Photos, int Notifications, int Recipients, int Outbox) Added { get; private set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var tracker = eventData.Context!.ChangeTracker; Calls++;
            Added = (tracker.Entries<IncidentReport>().Count(entry => entry.State == EntityState.Added),
                tracker.Entries<VehicleIncidentDetail>().Count(entry => entry.State == EntityState.Added), tracker.Entries<IncidentPhoto>().Count(entry => entry.State == EntityState.Added),
                tracker.Entries<Notification>().Count(entry => entry.State == EntityState.Added), tracker.Entries<AccountNotification>().Count(entry => entry.State == EntityState.Added),
                tracker.Entries<OutboxMessage>().Count(entry => entry.State == EntityState.Added));
            return ValueTask.FromResult(result);
        }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default) =>
            FailAfterSave ? throw new IOException("Planned failure after database writes") : ValueTask.FromResult(result);
    }

    private sealed class LostCommitAcknowledgment : DbTransactionInterceptor
    {
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default) =>
            throw new IOException("Planned lost commit acknowledgment");
    }

    internal sealed class CompetingSnapshot : DbCommandInterceptor
    {
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Attempts { get; private set; }
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("UPDATE \"Transport\"", StringComparison.Ordinal) && ++Attempts == 1)
            {
                await eventData.Context!.Set<Transport>().AnyAsync(cancellationToken);
                Started.TrySetResult();
            }
            return result;
        }
    }
}
