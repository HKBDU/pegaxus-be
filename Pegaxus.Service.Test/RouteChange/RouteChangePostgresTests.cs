using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Npgsql.NameTranslation;
using Pegaxus.Repository;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.NotificationService;
using RouteChange = Pegaxus.Service.RouteChangeService;

namespace Pegaxus.Service.Test;

public class RouteChangePostgresTests
{
    [PostgresFact]
    public async Task Migration_UpDownUp_AndSnapshotMatchCurrentModel()
    {
        await using var scope = await DatabaseScope.CreateAsync();
        await using var db = scope.Db();
        Assert.False(db.Database.HasPendingModelChanges());
        var migrator = db.GetInfrastructure().GetRequiredService<IMigrator>();
        await migrator.MigrateAsync("20261006100000_AlignRoutePlanningWithDbV7");
        await using (var connection = await scope.Source.OpenConnectionAsync())
        await using (var command = new NpgsqlCommand("SELECT to_regclass('\"RouteChangeRequest\"') IS NULL", connection))
            Assert.True((bool)(await command.ExecuteScalarAsync())!);
        await migrator.MigrateAsync();
        await using var connection2 = await scope.Source.OpenConnectionAsync();
        await using var command2 = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM pg_enum WHERE enumlabel = 'ROUTE_CHANGE' AND enumtypid = 'incident_category'::regtype)", connection2);
        Assert.True((bool)(await command2.ExecuteScalarAsync())!);
    }

    [PostgresFact]
    public async Task ConcurrentCreateAndApproval_ReplayOnce_PersistOutboxAndIncident()
    {
        await using var scope = await DatabaseScope.CreateAsync();
        Guid planId, coordinatorId, managerId;
        RouteChange.Request.CreateRequest proposal;
        await using (var setup = scope.Db())
        {
            var f = await RouteChangeTests.Fixture.CreateAsync(setup);
            planId = f.Plan.RoutePlanId;
            coordinatorId = f.Coordinator.AccountId;
            managerId = f.Manager.AccountId;
            proposal = f.Proposal();
            proposal.ClientRequestId = Guid.NewGuid();
        }
        async Task<RouteChange.Response.Created> Create()
        {
            await using var db = scope.Db();
            return await Service(db).CreateAsync(planId, coordinatorId, proposal);
        }
        var created = await Task.WhenAll(Create(), Create());
        Assert.Equal(created[0].ChangeRequestId, created[1].ChangeRequestId);
        var decision = new RouteChange.Request.DecisionRequest { Decision = "APPROVE", LastUpdatedAt = created[0].CreatedAt };
        async Task<RouteChange.Response.DecisionResult> Decide()
        {
            await using var db = scope.Db();
            return await Service(db).DecideAsync(created[0].ChangeRequestId, managerId, decision);
        }
        var results = await Task.WhenAll(Decide(), Decide());
        Assert.Equal(results[0].NewSegmentIds, results[1].NewSegmentIds);
        await using var check = scope.Db();
        Assert.Equal(1, await check.RouteChangeRequests.CountAsync());
        Assert.Equal(3, await check.RoutePlanRoutes.CountAsync());
        Assert.Equal(1, await check.RoutePlanRoutes.CountAsync(x => x.SupersededAt != null));
        Assert.Equal(1, await check.IncidentReports.CountAsync());
        Assert.Equal(1, await check.EmergencyCostRequests.CountAsync());
        Assert.Equal(4, await check.Notifications.CountAsync());
        Assert.Equal(8, await check.OutboxMessages.CountAsync());
        Assert.Equal(2, await check.CatalogAuditLogs.CountAsync());
        proposal.Note = "Changed after retry";
        Assert.Equal("IDEMPOTENCY_KEY_REUSED", (await Assert.ThrowsAsync<ConflictException>(() =>
            Service(check).CreateAsync(planId, coordinatorId, proposal))).ErrorCode);
    }

    [PostgresFact]
    public async Task FailedAudit_RollsBackRouteEtaCostsNotificationsAndDecision()
    {
        await using var scope = await DatabaseScope.CreateAsync();
        Guid requestId, managerId, oldSegmentId, planId;
        DateTimeOffset updatedAt;
        await using (var setup = scope.Db())
        {
            var f = await RouteChangeTests.Fixture.CreateAsync(setup);
            var created = await Service(setup).CreateAsync(f.Plan.RoutePlanId, f.Coordinator.AccountId, f.Proposal());
            requestId = created.ChangeRequestId;
            updatedAt = created.CreatedAt;
            managerId = f.Manager.AccountId;
            oldSegmentId = f.Current.RoutePlanRouteId;
            planId = f.Plan.TransportPlanId;
            await setup.Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION reject_uc48_audit() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'audit unavailable'; END $$;
                CREATE TRIGGER reject_uc48_audit BEFORE INSERT ON "CatalogAuditLog" FOR EACH ROW EXECUTE FUNCTION reject_uc48_audit();
                """);
        }
        await using (var failing = scope.Db())
            await Assert.ThrowsAsync<DbUpdateException>(() => Service(failing).DecideAsync(requestId, managerId,
                new() { Decision = "APPROVE", LastUpdatedAt = updatedAt }));
        await using var check = scope.Db();
        Assert.Equal("PENDING", (await check.RouteChangeRequests.SingleAsync()).Status);
        Assert.Null((await check.RoutePlanRoutes.SingleAsync(x => x.RoutePlanRouteId == oldSegmentId)).SupersededAt);
        Assert.Equal(2, await check.RoutePlanRoutes.CountAsync());
        Assert.Empty(await check.IncidentReports.ToListAsync());
        Assert.Empty(await check.EmergencyCostRequests.ToListAsync());
        Assert.Equal(1, await check.Notifications.CountAsync());
        Assert.Equal(2, await check.OutboxMessages.CountAsync());
        Assert.Equal(RouteChangeTests.Fixture.StartAt.AddHours(2).UtcDateTime, (await check.TransportPlans.SingleAsync(x => x.TransportPlanId == planId)).PlannedEndAt);
    }

    private static RouteChange.Service Service(AppDbContext db) => new(db,
        new Pegaxus.Service.NotificationService.Service(db, new NoopNotificationRealtimeService()), new RouteChangeTests.Fixture.FixedClock());

    [PostgresFact]
    public async Task CompetingDecisions_OnlyOneWins_OtherReturnsConflict()
    {
        await using var scope = await DatabaseScope.CreateAsync();
        Guid managerId;
        RouteChange.Response.Created created;
        await using (var setup = scope.Db())
        {
            var f = await RouteChangeTests.Fixture.CreateAsync(setup);
            managerId = f.Manager.AccountId;
            created = await Service(setup).CreateAsync(f.Plan.RoutePlanId, f.Coordinator.AccountId, f.Proposal());
        }
        async Task<string> Decide(string decision)
        {
            await using var db = scope.Db();
            try
            {
                return (await Service(db).DecideAsync(created.ChangeRequestId, managerId,
                    new() { Decision = decision, LastUpdatedAt = created.CreatedAt, Note = "Manager reviewed the emergency" })).Status;
            }
            catch (ConflictException error) { return error.ErrorCode; }
        }
        var results = await Task.WhenAll(Decide("APPROVE"), Decide("REJECT"));
        Assert.Single(results, x => x == "CHANGE_REQUEST_ALREADY_DECIDED");
        Assert.Single(results, x => x is "APPROVED" or "REJECTED");
        await using var check = scope.Db();
        Assert.Equal(2, await check.CatalogAuditLogs.CountAsync());
        Assert.Equal(results.Contains("APPROVED") ? 1 : 0, await check.IncidentReports.CountAsync());
    }

    internal sealed class PostgresFactAttribute : FactAttribute
    {
        public PostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PEGAXUS_TEST_CONNECTION_STRING")))
                Skip = "Set PEGAXUS_TEST_CONNECTION_STRING to a disposable PostgreSQL server; tests create and drop their own databases.";
        }
    }

    private sealed class EnumNames : INpgsqlNameTranslator
    {
        private readonly NpgsqlSnakeCaseNameTranslator names = new();
        public string TranslateTypeName(string name) => names.TranslateTypeName(name);
        public string TranslateMemberName(string name) => name;
    }

    private sealed class DatabaseScope(string admin, string name, NpgsqlDataSource source) : IAsyncDisposable
    {
        internal NpgsqlDataSource Source => source;
        internal AppDbContext Db() => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(source).Options);
        internal static async Task<DatabaseScope> CreateAsync()
        {
            var admin = Environment.GetEnvironmentVariable("PEGAXUS_TEST_CONNECTION_STRING")!;
            var name = "uc48_test_" + Guid.NewGuid().ToString("N");
            await using (var connection = new NpgsqlConnection(admin))
            {
                await connection.OpenAsync();
                await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection);
                await command.ExecuteNonQueryAsync();
            }
            var settings = new NpgsqlConnectionStringBuilder(admin) { Database = name };
            var builder = new NpgsqlDataSourceBuilder(settings.ConnectionString);
            var translator = new EnumNames();
            foreach (var type in typeof(AccountStatus).Assembly.GetTypes().Where(x => x.IsEnum && x.Namespace == typeof(AccountStatus).Namespace))
                builder.MapEnum(type, translator.TranslateTypeName(type.Name), translator);
            var scope = new DatabaseScope(admin, name, builder.Build());
            try
            {
                await using var db = scope.Db();
                await db.Database.MigrateAsync();
                await using var connection = await scope.Source.OpenConnectionAsync();
                await connection.ReloadTypesAsync();
                return scope;
            }
            catch { await scope.DisposeAsync(); throw; }
        }
        public async ValueTask DisposeAsync()
        {
            await source.DisposeAsync();
            if (!name.StartsWith("uc48_test_", StringComparison.Ordinal) || name.Length != 42)
                throw new InvalidOperationException("Refusing to drop an unexpected database.");
            await using var connection = new NpgsqlConnection(admin);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"DROP DATABASE \"{name}\" WITH (FORCE)", connection);
            await command.ExecuteNonQueryAsync();
        }
    }
}
