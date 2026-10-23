using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;

using static Pegaxus.Service.Test.EmergencyReroutingTests;
using static Pegaxus.Service.Test.IncidentLifecyclePostgresTests;
using static Pegaxus.Service.Test.IncidentLifecycleTests;
using static Pegaxus.Service.Test.VehicleIncidentPostgresTests;
using static Pegaxus.Service.Test.VehicleIncidentTests;

using Incidents = Pegaxus.Service.IncidentService;

namespace Pegaxus.Service.Test;

[Collection("PostgresSharedDatabase")]
public class EmergencyReroutingPostgresTests
{
    [PostgresFact]
    public async Task Postgres_ContextHoldAndExpiryTranslate_KeepCanonicalStateAndDurableOutbox_AcrossFreshContexts()
    {
        await WithDatabaseAsync(async options =>
        {
            await using var seed = new AppDbContext(options); var f = await Scenario.CreateAsync(seed); var item = AddIncident(seed, f);
            seed.Add(PersonnelAssignmentTests.Segment(f.Segment.RoutePlan, 2)); var backup = AddPlan(seed, f, RouteType.BACKUP);
            AddDossier(seed, f, DossierType.MAIN); await seed.SaveChangesAsync(); var audit = new SaveAudit();
            await using (var writer = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(options).AddInterceptors(audit).Options))
            {
                var service = CreateService(writer, f.Storage);
                var context = await service.GetRerouteContextAsync(item.IncidentReportId, f.Coordinator.AccountId);
                Assert.Equal(backup.RoutePlanId, Assert.Single(context.BackupPlans).RoutePlanId); Assert.Single(context.RemainingSegments);
                await service.EscalateRerouteAsync(item.IncidentReportId, new() { Reason = Reason, BlockedRoutePlanIds = [backup.RoutePlanId] }, f.Coordinator.AccountId);
                Assert.Equal(1, audit.Calls);
            }
            var saved = new SaveAudit();
            await using (var writer = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(options).AddInterceptors(saved).Options))
                await CreateService(writer, f.Storage).HoldRouteAsync(item.IncidentReportId, HoldRequest(), f.Manager.AccountId);
            Assert.Equal(1, saved.Calls);
            await using var reader = new AppDbContext(options); var clock = new MutableClock(); var read = CreateService(reader, f.Storage, clock);
            var staff = Assert.IsType<Incidents.Response.Incident>(await read.GetAsync(item.IncidentReportId, f.Manager.AccountId));
            Assert.Equal("ACTIVE", staff.Hold!.Status); Assert.DoesNotContain("HOLD_ROUTE", staff.AvailableActions);
            var customer = Assert.IsType<Incidents.CustomerResponse.Incident>(await read.GetAsync(item.IncidentReportId, f.CustomerAccountId));
            Assert.Equal(staff.Hold, customer.Hold);
            var persisted = await reader.Notifications.AsNoTracking().SingleAsync(hold => hold.ExpiresAt != null);
            Assert.Equal(DateTimeKind.Unspecified, persisted.ExpiresAt!.Value.Kind);
            Assert.False(await reader.AccountNotifications.AnyAsync(row => row.NotificationId == persisted.NotificationId));
            clock.Now = clock.Now.AddMinutes(15); Assert.Equal(1, await read.QueueExpiredHoldsAsync());
            await using var restart = new AppDbContext(options); var restarted = CreateService(restart, f.Storage, clock);
            Assert.Equal(0, await restarted.QueueExpiredHoldsAsync());
            Assert.Equal("EXPIRED", (await restarted.GetRerouteContextAsync(item.IncidentReportId, f.Manager.AccountId)).Hold!.Status);
            Assert.Equal(11, await restart.Notifications.CountAsync()); Assert.Equal(9, await restart.AccountNotifications.CountAsync());
            Assert.Equal(11, await restart.OutboxMessages.CountAsync()); Assert.Equal(2, await restart.OutboxMessages.CountAsync(message => message.MessageType == "EMAIL"));
            Assert.False(await restart.AccountNotifications.AnyAsync(row => row.AccountId == f.CustomerAccountId));
            Assert.Equal(TransportStatus.IN_TRANSIT, (await restart.Transports.SingleAsync()).Status);
            Assert.Equal(ComplianceDossierStatus.APPROVED, (await restart.ComplianceDossiers.SingleAsync()).Status);
            Assert.False(restart.Database.HasPendingModelChanges());
        });
    }

    [PostgresFact]
    public async Task Postgres_EachCommandAndExpiryRollsBackDecisionAlertsAndOutbox_OnSaveFailure()
    {
        await WithDatabaseAsync(async options =>
        {
            await using var seed = new AppDbContext(options); var f = await Scenario.CreateAsync(seed); var item = AddIncident(seed, f); await seed.SaveChangesAsync();
            foreach (var action in new[] { "hold", "escalation" })
            {
                var failure = new SaveAudit { FailAfterSave = true };
                await using var writer = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(options).AddInterceptors(failure).Options);
                await Assert.ThrowsAsync<IOException>(() => WriteAsync(CreateService(writer, f.Storage), item.IncidentReportId, action,
                    action == "hold" ? f.Manager.AccountId : f.Coordinator.AccountId));
                Assert.Equal(1, failure.Calls);
                await using var verify = new AppDbContext(options);
                Assert.Equal(IncidentStatus.REPORTED, (await verify.IncidentReports.SingleAsync()).Status);
                Assert.Equal(Scenario.Now, (await verify.IncidentReports.SingleAsync()).UpdatedAt);
                Assert.Empty(await verify.Notifications.ToArrayAsync()); Assert.Empty(await verify.AccountNotifications.ToArrayAsync()); Assert.Empty(await verify.OutboxMessages.ToArrayAsync());
            }
            await using (var writer = new AppDbContext(options))
                await CreateService(writer, f.Storage).HoldRouteAsync(item.IncidentReportId, HoldRequest(), f.Manager.AccountId);
            var clock = new MutableClock { Now = DbTime.AsUtc(Scenario.Now.AddMinutes(15)) }; var failedExpiry = new SaveAudit { FailAfterSave = true };
            await using (var writer = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(options).AddInterceptors(failedExpiry).Options))
                await Assert.ThrowsAsync<IOException>(() => CreateService(writer, f.Storage, clock).QueueExpiredHoldsAsync());
            Assert.Equal(1, failedExpiry.Calls);
            await using var retry = new AppDbContext(options);
            Assert.Equal(5, await retry.Notifications.CountAsync()); Assert.Equal(4, await retry.OutboxMessages.CountAsync());
            Assert.Equal(1, await CreateService(retry, f.Storage, clock).QueueExpiredHoldsAsync());
            Assert.Equal(9, await retry.Notifications.CountAsync()); Assert.Equal(8, await retry.OutboxMessages.CountAsync());
        });
    }

    [PostgresFact]
    public async Task Postgres_ConcurrentHoldsOnSameOrDifferentIncidents_ShareTransportLockAndProduceOneDecision()
    {
        foreach (var sameIncident in new[] { true, false })
            await WithDatabaseAsync(async options =>
            {
                await using var seed = new AppDbContext(options); var f = await Scenario.CreateAsync(seed); var item = AddIncident(seed, f);
                var other = sameIncident ? item : AddIncident(seed, f); await seed.SaveChangesAsync();
                var pause = new PauseBeforeSave(); var barrier = new CompetingSnapshot();
                await using var first = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(options).AddInterceptors(pause).Options);
                await using var second = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(options).AddInterceptors(barrier).Options);
                var winner = CreateService(first, f.Storage).HoldRouteAsync(item.IncidentReportId, HoldRequest(), f.Manager.AccountId);
                try
                {
                    await pause.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
                    var competitor = CreateService(second, f.Storage).HoldRouteAsync(other.IncidentReportId, HoldRequest(), f.Manager.AccountId);
                    await barrier.Started.Task.WaitAsync(TimeSpan.FromSeconds(20)); pause.Release.TrySetResult();
                    Assert.Equal("ACTIVE", (await winner).Hold.Status);
                    Assert.Equal("HOLD_ALREADY_ACTIVE", (await Assert.ThrowsAsync<ConflictException>(() => competitor)).ErrorCode);
                    Assert.True(barrier.Attempts >= 2);
                }
                finally { pause.Release.TrySetResult(); }
                await using var verify = new AppDbContext(options);
                Assert.Single(await verify.Notifications.Where(hold => hold.ExpiresAt != null).ToArrayAsync());
                Assert.Equal(5, await verify.Notifications.CountAsync()); Assert.Equal(4, await verify.AccountNotifications.CountAsync()); Assert.Equal(4, await verify.OutboxMessages.CountAsync());
                if (!sameIncident) Assert.Equal(IncidentStatus.REPORTED, (await verify.IncidentReports.SingleAsync(row => row.IncidentReportId == other.IncidentReportId)).Status);
            });
    }

    [PostgresFact]
    public async Task Postgres_ConcurrentExpiryWorkers_CommitOneMarkerAndOneSetOfAlerts()
    {
        await WithDatabaseAsync(async options =>
        {
            await using var seed = new AppDbContext(options); var f = await Scenario.CreateAsync(seed); var item = AddIncident(seed, f); await seed.SaveChangesAsync();
            await CreateService(seed, f.Storage).HoldRouteAsync(item.IncidentReportId, HoldRequest(), f.Manager.AccountId);
            var version = item.UpdatedAt; var clock = new MutableClock { Now = DbTime.AsUtc(Scenario.Now.AddMinutes(15)) };
            var pause = new PauseBeforeSave(); var barrier = new CompetingSnapshot();
            await using var first = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(options).AddInterceptors(pause).Options);
            await using var second = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(options).AddInterceptors(barrier).Options);
            var winner = CreateService(first, f.Storage, clock).QueueExpiredHoldsAsync();
            try
            {
                await pause.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
                var competitor = CreateService(second, f.Storage, clock).QueueExpiredHoldsAsync();
                await barrier.Started.Task.WaitAsync(TimeSpan.FromSeconds(20)); pause.Release.TrySetResult();
                Assert.Equal(1, await winner); Assert.Equal(0, await competitor); Assert.True(barrier.Attempts >= 2);
            }
            finally { pause.Release.TrySetResult(); }
            await using var verify = new AppDbContext(options);
            Assert.Single(await verify.Notifications.Where(row => row.ReferenceType == "IncidentHoldExpired").ToArrayAsync());
            Assert.Equal(9, await verify.Notifications.CountAsync()); Assert.Equal(7, await verify.AccountNotifications.CountAsync()); Assert.Equal(8, await verify.OutboxMessages.CountAsync());
            Assert.Single(await verify.OutboxMessages.Where(message => message.MessageType == "EMAIL").ToArrayAsync());
            Assert.Equal(version, (await verify.IncidentReports.SingleAsync()).UpdatedAt);
        });
    }
}
