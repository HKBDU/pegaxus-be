using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;

using static Pegaxus.Service.Test.EmergencyCostTests;
using static Pegaxus.Service.Test.HorseHealthLogTests;
using static Pegaxus.Service.Test.IncidentLifecycleTests;
using static Pegaxus.Service.Test.VehicleIncidentPostgresTests;
using static Pegaxus.Service.Test.VehicleIncidentTests;

namespace Pegaxus.Service.Test;

[Collection("PostgresSharedDatabase")]
public class EmergencyCostPostgresTests
{
    [PostgresFact]
    public async Task Postgres_CostQueriesTranslateScopeFiltersSortingAndLatestHealth()
    {
        await WithDatabaseAsync(async options =>
        {
            await using var db = new AppDbContext(options); var f = await Scenario.CreateAsync(db);
            var incident = AddIncident(db, f); await db.SaveChangesAsync();
            var costs = new List<Pegaxus.Service.IncidentService.Response.CostRequest>();
            foreach (var actor in new[] { f.Driver, f.Escort, f.Coordinator })
                costs.Add(await f.Service.CreateCostAsync(incident.IncidentReportId, CostRequest(), actor.AccountId));
            await f.Service.DecideCostAsync(costs[0].EmergencyCostRequestId, Decision(costs[0]), f.Manager.AccountId);
            await f.Service.CancelCostAsync(costs[1].EmergencyCostRequestId,
                new() { LastUpdatedAt = costs[1].UpdatedAt, CancellationReason = Reason }, f.Escort.AccountId);
            var horseId = await db.BookingHorses.Where(item => item.BookingId == f.Transport.BookingId).Select(item => item.HorseId).FirstAsync();
            foreach (var (time, status) in new[] { (Scenario.Now.AddHours(-1), HorseHealthStatus.CRITICAL), (Scenario.Now, HorseHealthStatus.NORMAL) })
                db.HorseHealthLogs.Add(new HorseHealthLog { HorseHealthLogId = Guid.NewGuid(), TransportId = f.Transport.TransportId,
                    HorseId = horseId, EmployeeId = f.Escort.EmployeeId, LogType = HorseHealthLogType.ROUTINE_CHECK,
                    HealthStatus = status, CheckedAt = time, CreatedAt = time, UpdatedAt = time });
            await db.SaveChangesAsync();
            var page = await f.Service.ListCostsAsync(new() { PageSize = 1 }, f.Manager.AccountId);
            Assert.Equal(1, page.TotalItems); Assert.Equal(new Pegaxus.Service.IncidentService.Response.CostCounts(1, 1, 0, 1), page.Counts);
            Assert.Equal(costs[2].EmergencyCostRequestId, Assert.Single(page.Items).EmergencyCostRequestId);
            Assert.Equal(3, (await f.Service.ListCostsAsync(new(), f.Coordinator.AccountId)).TotalItems);
            Assert.Equal(3, (await f.Service.ListCostsAsync(new(), f.Specialist.AccountId)).TotalItems);
            Assert.Single((await f.Service.ListCostsAsync(new(), f.Driver.AccountId)).Items);
            foreach (var keyword in new[] { costs[2].RequestCode.ToLowerInvariant(), incident.IncidentCode.ToLowerInvariant() })
            {
                var filtered = await f.Service.ListCostsAsync(new() { Keyword = " " + keyword + " ", Status = "PENDING,REJECTED", Category = "TOWING",
                    IncidentReportId = incident.IncidentReportId, TransportId = f.Transport.TransportId,
                    FromDate = DateOnly.FromDateTime(Scenario.Now), ToDate = DateOnly.FromDateTime(Scenario.Now) }, f.Manager.AccountId);
                Assert.Equal(costs[2].EmergencyCostRequestId, Assert.Single(filtered.Items).EmergencyCostRequestId); Assert.Equal(page.Counts, filtered.Counts);
            }
            foreach (var field in new[] { "requestedAt", "updatedAt", "requestedAmount", "status" })
            {
                var sorted = await f.Service.ListCostsAsync(new() { Status = "PENDING,APPROVED,CANCELLED", SortBy = field + ":desc", PageSize = 2 }, f.Manager.AccountId);
                Assert.Equal(3, sorted.TotalItems); Assert.Equal(2, sorted.TotalPages); Assert.Equal(2, sorted.Items.Count);
            }
            var detail = await f.Service.GetCostAsync(costs[2].EmergencyCostRequestId, f.Manager.AccountId);
            Assert.Equal(2, detail.OtherRequests!.Count); Assert.Equal(2, detail.HorseHealthSummary!.Total);
            Assert.Equal(1, detail.HorseHealthSummary.Normal); Assert.Equal(1, detail.HorseHealthSummary.NoData); Assert.Equal(0, detail.HorseHealthSummary.Critical);
            Assert.Equal(f.Segment.RoutePlanId, detail.CurrentRoute!.RoutePlanId);
            Assert.Equal(DateTimeKind.Unspecified, (await db.EmergencyCostRequests.AsNoTracking().FirstAsync()).RequestedAt.Kind);
            Assert.False(db.Database.HasPendingModelChanges());
        });
    }

    [PostgresFact]
    public async Task Postgres_EveryCostWriteRollsBackCostIncidentDecisionEvidenceAndNotifications()
    {
        await WithDatabaseAsync(async options =>
        {
            await using var seed = new AppDbContext(options); var f = await Scenario.CreateAsync(seed); var incident = AddIncident(seed, f); await seed.SaveChangesAsync();
            foreach (var failure in new[] { "save", "sign", "upload" })
            {
                var audit = new SaveAudit { FailAfterSave = failure == "save" };
                var storage = new Storage { FailSign = failure == "sign", FailAtConfirmation = failure == "upload" ? 2 : null };
                await using var writer = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(options).AddInterceptors(audit).Options);
                var request = CostRequest(); request.PhotoFiles = [Photo(), Photo()];
                await Assert.ThrowsAsync<IOException>(() => CreateService(writer, storage).CreateCostAsync(incident.IncidentReportId, request, f.Coordinator.AccountId));
                await using var check = new AppDbContext(options);
                Assert.Empty(await check.EmergencyCostRequests.ToArrayAsync()); Assert.Empty(await check.IncidentPhotos.ToArrayAsync());
                Assert.Empty(await check.Notifications.ToArrayAsync()); Assert.Empty(await check.OutboxMessages.ToArrayAsync());
                Assert.Equal(IncidentStatus.REPORTED, (await check.IncidentReports.SingleAsync()).Status); Assert.Empty(storage.Deletes);
                Assert.Equal(failure == "upload" ? 0 : 1, audit.Calls);
            }
            var create = CostRequest(); create.PhotoFiles = [Photo()];
            var cost = await f.Service.CreateCostAsync(incident.IncidentReportId, create, f.Driver.AccountId);
            foreach (var action in new[] { "cancel", "decide" })
            foreach (var failure in new[] { "save", "sign" })
            {
                var audit = new SaveAudit { FailAfterSave = failure == "save" }; var storage = new Storage { FailSign = failure == "sign" };
                await using var writer = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(options).AddInterceptors(audit).Options);
                var service = CreateService(writer, storage);
                await Assert.ThrowsAsync<IOException>(() => action == "decide"
                    ? service.DecideCostAsync(cost.EmergencyCostRequestId, Decision(cost), f.Manager.AccountId)
                    : service.CancelCostAsync(cost.EmergencyCostRequestId, new() { LastUpdatedAt = cost.UpdatedAt, CancellationReason = Reason }, f.Driver.AccountId));
                Assert.Equal(1, audit.Calls);
                await using var check = new AppDbContext(options); var item = await check.EmergencyCostRequests.SingleAsync();
                Assert.Equal(EmergencyCostStatus.PENDING, item.Status); Assert.Equal(cost.UpdatedAt, DbTime.AsUtc(item.UpdatedAt));
                Assert.Null(item.ApprovedAmount); Assert.Null(item.CancelledAt); Assert.Null(item.CancellationReason);
                Assert.Empty(await check.CostApprovalDecisions.ToArrayAsync()); Assert.Single(await check.IncidentPhotos.ToArrayAsync());
                Assert.Single(await check.Notifications.ToArrayAsync()); Assert.Single(await check.AccountNotifications.ToArrayAsync());
                Assert.Single(await check.OutboxMessages.ToArrayAsync()); Assert.Equal(IncidentStatus.REPORTED, (await check.IncidentReports.SingleAsync()).Status);
            }
        });
    }

    [PostgresFact]
    public async Task Postgres_ConcurrentDecisionsOrCancellationProduceOneWinnerAndOneSetOfEffects()
    {
        await WithDatabaseAsync(async options =>
        {
            await using var seed = new AppDbContext(options); var f = await Scenario.CreateAsync(seed); var incident = AddIncident(seed, f); await seed.SaveChangesAsync();
            foreach (var firstAction in new[] { "approve", "cancel" })
            {
                var cost = await f.Service.CreateCostAsync(incident.IncidentReportId, CostRequest(), f.Driver.AccountId);
                var notificationsBefore = await seed.Notifications.CountAsync(); var decisionsBefore = await seed.CostApprovalDecisions.CountAsync();
                var pause = new IncidentLifecyclePostgresTests.PauseBeforeSave(); var barrier = new CompetingSnapshot();
                await using var first = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(options).AddInterceptors(pause).Options);
                await using var second = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(options).AddInterceptors(barrier).Options);
                var winner = firstAction == "approve"
                    ? CreateService(first, f.Storage).DecideCostAsync(cost.EmergencyCostRequestId, Decision(cost), f.Manager.AccountId)
                    : CreateService(first, f.Storage).CancelCostAsync(cost.EmergencyCostRequestId, new() { LastUpdatedAt = cost.UpdatedAt, CancellationReason = Reason }, f.Driver.AccountId);
                try
                {
                    await pause.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
                    var competing = CreateService(second, f.Storage).DecideCostAsync(cost.EmergencyCostRequestId, Decision(cost), f.Manager.AccountId);
                    await barrier.Started.Task.WaitAsync(TimeSpan.FromSeconds(20)); pause.Release.TrySetResult();
                    Assert.Equal(firstAction == "approve" ? "APPROVED" : "CANCELLED", (await winner).Status);
                    Assert.Equal("EMERGENCY_COST_ALREADY_DECIDED", (await Assert.ThrowsAsync<ConflictException>(() => competing)).ErrorCode);
                    Assert.True(barrier.Attempts >= 2);
                }
                finally { pause.Release.TrySetResult(); }
                await using var check = new AppDbContext(options);
                Assert.Equal(notificationsBefore + 1, await check.Notifications.CountAsync());
                Assert.Equal(decisionsBefore + (firstAction == "approve" ? 1 : 0), await check.CostApprovalDecisions.CountAsync());
            }
        });
    }

    [PostgresFact]
    public async Task Postgres_CostCreationSerializesWithIncidentClosure_StaleContextIsReloaded()
    {
        await WithDatabaseAsync(async options =>
        {
            await using var seed = new AppDbContext(options); var f = await Scenario.CreateAsync(seed); var incident = AddIncident(seed, f); await seed.SaveChangesAsync();
            var pause = new IncidentLifecyclePostgresTests.PauseBeforeSave(); var barrier = new CompetingSnapshot();
            await using var first = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(options).AddInterceptors(pause).Options);
            await using var second = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(options).AddInterceptors(barrier).Options);
            var winner = CreateService(first, f.Storage).ResolveAsync(incident.IncidentReportId,
                new() { LastUpdatedAt = DbTime.AsUtc(incident.UpdatedAt), ResolutionSummary = Summary, EmergencySettlementConfirmed = true }, f.Manager.AccountId);
            await second.IncidentReports.SingleAsync();
            try
            {
                await pause.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
                var competing = CreateService(second, f.Storage).CreateCostAsync(incident.IncidentReportId, CostRequest(), f.Driver.AccountId);
                await barrier.Started.Task.WaitAsync(TimeSpan.FromSeconds(20)); pause.Release.TrySetResult();
                Assert.Equal("RESOLVED", (await winner).Status);
                Assert.Equal("INVALID_INCIDENT_STATUS", (await Assert.ThrowsAsync<UnprocessableEntityException>(() => competing)).ErrorCode);
                Assert.True(barrier.Attempts >= 2);
            }
            finally { pause.Release.TrySetResult(); }
            await using var check = new AppDbContext(options); Assert.Empty(await check.EmergencyCostRequests.ToArrayAsync());
            Assert.Empty(await check.CostApprovalDecisions.ToArrayAsync());
        });
    }
}
