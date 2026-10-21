using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Repository.Extensions;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;

using static Pegaxus.Service.Test.HorseHealthLogTests;
using static Pegaxus.Service.Test.IncidentLifecycleTests;
using static Pegaxus.Service.Test.VehicleIncidentPostgresTests;
using static Pegaxus.Service.Test.VehicleIncidentTests;

namespace Pegaxus.Service.Test;

[Collection("PostgresSharedDatabase")]
public class IncidentLifecyclePostgresTests
{
    [PostgresFact]
    public async Task Postgres_ListTranslatesFiltersCountsSearchSortingAndPagination_AndReadsDoNotAcceptIncidents()
    {
        await WithDatabaseAsync(async options =>
        {
            await using var db = new AppDbContext(options); var f = await Scenario.CreateAsync(db);
            var report = (await f.Service.ReportVehicleAsync(f.Report(), f.Driver.AccountId)).Value;
            var low = AddIncident(db, f, severity: IncidentSeverity.LOW); low.EmergencySettlementRequired = false;
            var pending = AddIncident(db, f, IncidentStatus.PENDING, IncidentSeverity.CRITICAL); pending.OccurredAt = Scenario.Now.Date;
            AddCost(db, pending, f.Driver, EmergencyCostStatus.PENDING); AddIncident(db, f, IncidentStatus.RESOLVED); await db.SaveChangesAsync();
            var page = await f.Service.ListAsync(new() { Status = "REPORTED,PENDING", PageSize = 1 }, f.Driver.AccountId);
            Assert.Equal(pending.IncidentReportId, Assert.Single(page.Items).IncidentReportId);
            Assert.Equal(new Pegaxus.Service.IncidentService.Response.Counts(2, 1, 1, 2), page.Counts);
            Assert.Equal(3, page.TotalItems); Assert.Equal(3, page.TotalPages);
            var filtered = await f.Service.ListAsync(new() { HasPendingCost = true, TransportId = f.Transport.TransportId, Category = "VEHICLE",
                Severity = "CRITICAL", EmergencySettlementRequired = true, OccurredFrom = DateOnly.FromDateTime(Scenario.Now), OccurredTo = DateOnly.FromDateTime(Scenario.Now) }, f.Specialist.AccountId);
            Assert.Equal(pending.IncidentReportId, Assert.Single(filtered.Items).IncidentReportId);
            Assert.Equal(page.Counts, filtered.Counts);
            foreach (var search in new[] { report.IncidentCode, report.Title, f.Driver.FullName, f.Location.LocationName, f.Vehicle.LicensePlate!, f.Transport.TransportCode })
                Assert.Contains((await f.Service.ListAsync(new() { Search = " " + search.ToUpperInvariant() + " " }, f.Coordinator.AccountId)).Items, row => row.IncidentReportId == report.IncidentReportId);
            foreach (var field in new[] { "severity", "occurredAt", "updatedAt", "status" })
            {
                var asc = await f.Service.ListAsync(new() { SortBy = field + ":asc" }, f.Manager.AccountId);
                var desc = await f.Service.ListAsync(new() { SortBy = field + ":desc" }, f.Manager.AccountId);
                Assert.Equal(4, asc.TotalItems); Assert.Equal(4, desc.TotalItems);
                if (field == "severity")
                { Assert.Equal(low.IncidentReportId, asc.Items[0].IncidentReportId); Assert.Equal(pending.IncidentReportId, desc.Items[0].IncidentReportId); }
            }
            Assert.IsType<Pegaxus.Service.IncidentService.CustomerResponse.Incident>(await f.Service.GetAsync(report.IncidentReportId, f.CustomerAccountId));
            Assert.Equal(IncidentStatus.REPORTED, (await db.IncidentReports.AsNoTracking().SingleAsync(item => item.IncidentReportId == report.IncidentReportId)).Status);
        });
    }

    [PostgresFact]
    public async Task Postgres_EveryLifecycleWriteRollsBackIncidentCostsAndOutbox_WhenSaveOrResponseSigningFails()
    {
        await WithDatabaseAsync(async options =>
        {
            await using var seed = new AppDbContext(options); var f = await Scenario.CreateAsync(seed);
            var request = f.Report(); request.PhotoFiles = [Photo()]; request.VehicleOperable = false; request.ReplacementVehicleRequired = true;
            var created = (await f.Service.ReportVehicleAsync(request, f.Driver.AccountId)).Value;
            var incident = await seed.IncidentReports.SingleAsync(); var cost = AddCost(seed, incident, f.Driver, EmergencyCostStatus.REJECTED); await seed.SaveChangesAsync();
            var notificationCount = await seed.Notifications.CountAsync(); var outboxCount = await seed.OutboxMessages.CountAsync();
            foreach (var action in new[] { "resolve", "cancel", "self-resolve", "raise-severity" })
            {
                cost.Status = action == "cancel" ? EmergencyCostStatus.PENDING : EmergencyCostStatus.REJECTED; await seed.SaveChangesAsync();
                foreach (var failure in new[] { "save", "sign" })
                {
                    var audit = new SaveAudit { FailAfterSave = failure == "save" };
                    await using var writer = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(options).AddInterceptors(audit).Options);
                    var storage = new Storage { FailSign = failure == "sign" };
                    await Assert.ThrowsAsync<IOException>(() => ApplyAsync(CreateService(writer, storage), incident, action,
                        action == "self-resolve" ? f.Escort.AccountId : f.Manager.AccountId));
                    Assert.Equal(1, audit.Calls);
                    await using var verify = new AppDbContext(options); var item = await verify.IncidentReports.SingleAsync();
                    Assert.Equal(IncidentStatus.REPORTED, item.Status); Assert.Equal(IncidentSeverity.HIGH, item.Severity);
                    Assert.Equal(created.UpdatedAt, DbTime.AsUtc(item.UpdatedAt)); Assert.Null(item.CancelledAt); Assert.Null(item.ResolvedAt);
                    Assert.Equal(cost.Status, (await verify.EmergencyCostRequests.SingleAsync()).Status);
                    Assert.Null((await verify.EmergencyCostRequests.SingleAsync()).CancelledAt);
                    Assert.Equal(notificationCount, await verify.Notifications.CountAsync()); Assert.Equal(notificationCount, await verify.AccountNotifications.CountAsync());
                    Assert.Equal(outboxCount, await verify.OutboxMessages.CountAsync()); Assert.Equal(VehicleStatus.OUT_OF_SERVICE, (await verify.Vehicles.SingleAsync()).Status);
                    Assert.Single(await verify.IncidentPhotos.ToArrayAsync());
                }
            }
            cost.Status = EmergencyCostStatus.PENDING; await seed.SaveChangesAsync();
            var saved = new SaveAudit();
            await using (var writer = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(options).AddInterceptors(saved).Options))
                Assert.Equal("CANCELLED", (await ApplyAsync(CreateService(writer, f.Storage), incident, "cancel", f.Manager.AccountId)).Status);
            Assert.Equal(1, saved.Calls);
            await using var check = new AppDbContext(options);
            Assert.Equal(EmergencyCostStatus.CANCELLED, (await check.EmergencyCostRequests.SingleAsync()).Status);
            Assert.Equal(5, await check.Notifications.CountAsync(item => item.CreatedAt > Scenario.Now));
        });
    }

    [PostgresFact]
    public async Task Postgres_ConcurrentLifecycleChangesRejectStaleVersion_WithoutDuplicateEffects()
    {
        await WithDatabaseAsync(async options =>
        {
            await using var seed = new AppDbContext(options); var f = await Scenario.CreateAsync(seed); var item = AddIncident(seed, f); await seed.SaveChangesAsync();
            var pause = new PauseBeforeSave(); var barrier = new CompetingSnapshot();
            await using var first = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(options).AddInterceptors(pause).Options);
            await using var second = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(options).AddInterceptors(barrier).Options);
            var winner = ApplyAsync(CreateService(first, f.Storage), item, "raise-severity", f.Coordinator.AccountId);
            try
            {
                await pause.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
                var competing = ApplyAsync(CreateService(second, f.Storage), item, "resolve", f.Manager.AccountId);
                await barrier.Started.Task.WaitAsync(TimeSpan.FromSeconds(20)); pause.Release.TrySetResult();
                Assert.Equal("CRITICAL", (await winner).Severity);
                Assert.Equal("INCIDENT_MODIFIED", (await Assert.ThrowsAsync<ConflictException>(() => competing)).ErrorCode); Assert.True(barrier.Attempts >= 2);
            }
            finally { pause.Release.TrySetResult(); }
            await using var verify = new AppDbContext(options);
            Assert.Equal(IncidentStatus.REPORTED, (await verify.IncidentReports.SingleAsync()).Status);
            Assert.Equal(4, await verify.Notifications.CountAsync()); Assert.Equal(5, await verify.OutboxMessages.CountAsync());
            Assert.Equal(1, await verify.AccountNotifications.CountAsync(row => row.AccountId == f.CustomerAccountId));
        });
    }

    [PostgresFact]
    public async Task Postgres_ResolveRetriesSnapshotAndSeesNewPendingCost_BeforeClosing()
    {
        await WithDatabaseAsync(async options =>
        {
            await using var seed = new AppDbContext(options); var f = await Scenario.CreateAsync(seed); var item = AddIncident(seed, f); await seed.SaveChangesAsync();
            await using var costWriter = new AppDbContext(options); await using var transaction = await costWriter.Database.BeginSerializableAsync();
            await costWriter.LockRowsAsync(costWriter.Transports.Where(transport => transport.TransportId == f.Transport.TransportId), transport => transport.TransportId);
            var report = await costWriter.IncidentReports.SingleAsync(); var requester = await costWriter.Employees.SingleAsync(employee => employee.EmployeeId == f.Driver.EmployeeId);
            AddCost(costWriter, report, requester, EmergencyCostStatus.PENDING); await costWriter.SaveChangesAsync();
            var barrier = new CompetingSnapshot();
            await using var writer = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(options).AddInterceptors(barrier).Options);
            var closing = ApplyAsync(CreateService(writer, f.Storage), item, "resolve", f.Manager.AccountId);
            await barrier.Started.Task.WaitAsync(TimeSpan.FromSeconds(20)); await transaction.CommitAsync();
            Assert.Equal("UNRESOLVED_EMERGENCY_COSTS", (await Assert.ThrowsAsync<UnprocessableEntityException>(() => closing)).ErrorCode);
            Assert.True(barrier.Attempts >= 2); await using var verify = new AppDbContext(options);
            Assert.Equal(IncidentStatus.REPORTED, (await verify.IncidentReports.SingleAsync()).Status);
            Assert.Equal(EmergencyCostStatus.PENDING, (await verify.EmergencyCostRequests.SingleAsync()).Status); Assert.Empty(await verify.Notifications.ToArrayAsync());
        });
    }

    internal sealed class PauseBeforeSave : SaveChangesInterceptor
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult(); await Release.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken); return result;
        }
    }
}
