using Microsoft.EntityFrameworkCore;
using Npgsql;
using Npgsql.NameTranslation;
using Pegaxus.Repository;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.PersonnelAssignmentService;

namespace Pegaxus.Service.Test;

public class PersonnelAssignmentPostgresTests
{
    private sealed class PostgresFactAttribute : FactAttribute
    {
        public PostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PEGAXUS_TEST_CONNECTION_STRING")))
                Skip = "Set PEGAXUS_TEST_CONNECTION_STRING to an isolated PostgreSQL test database.";
        }
    }
    private sealed class EnumTranslator : INpgsqlNameTranslator
    {
        public string TranslateTypeName(string name) => new NpgsqlSnakeCaseNameTranslator().TranslateTypeName(name);
        public string TranslateMemberName(string name) => name;
    }

    [PostgresFact]
    public async Task PostgreSql_ScheduleRaceVersionsLinksAndOutboxRollback()
    {
        var connectionString = Environment.GetEnvironmentVariable("PEGAXUS_TEST_CONNECTION_STRING")!;
        var schema = "personnel_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(connectionString);
        await admin.OpenAsync();
        await Execute(admin, $"CREATE SCHEMA {schema}; SET search_path TO {schema};");
        var builder = new NpgsqlDataSourceBuilder(new NpgsqlConnectionStringBuilder(connectionString) { SearchPath = schema }.ConnectionString);
        var translator = new EnumTranslator();
        foreach (var type in typeof(EmployeeRole).Assembly.GetTypes().Where(x => x.IsEnum && x.Namespace == typeof(EmployeeRole).Namespace))
            builder.MapEnum(type, translator.TranslateTypeName(type.Name), translator);
        await using var source = builder.Build();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(source).Options;
        try
        {
            Guid actor, driver, escort1, escort2, route1, route2, segment1, segment2, replacement1, replacement2;
            DateTimeOffset version;
            await using (var db = new AppDbContext(options))
            {
                await Execute(admin, db.Database.GenerateCreateScript());
                actor = (await PersonnelAssignmentTests.EmployeeAsync(db, EmployeeRole.LOGISTICS_MANAGER)).AccountId;
                driver = (await PersonnelAssignmentTests.EmployeeAsync(db, EmployeeRole.DRIVER)).EmployeeId;
                escort1 = (await PersonnelAssignmentTests.EmployeeAsync(db, EmployeeRole.ESCORT)).EmployeeId;
                escort2 = (await PersonnelAssignmentTests.EmployeeAsync(db, EmployeeRole.ESCORT)).EmployeeId;
                replacement1 = (await PersonnelAssignmentTests.EmployeeAsync(db, EmployeeRole.DRIVER)).EmployeeId;
                replacement2 = (await PersonnelAssignmentTests.EmployeeAsync(db, EmployeeRole.DRIVER)).EmployeeId;
                var first = await PersonnelAssignmentTests.RouteAsync(db); var second = await PersonnelAssignmentTests.RouteAsync(db);
                route1 = first.Route.RoutePlanId; route2 = second.Route.RoutePlanId;
                segment1 = first.Segment.RoutePlanRouteId; segment2 = second.Segment.RoutePlanRouteId;
                version = PersonnelAssignmentTests.Version(first.Route);
            }
            await using (var first = new AppDbContext(options))
            await using (var second = new AppDbContext(options))
            {
                var results = await Task.WhenAll(
                    Attempt(() => PersonnelAssignmentTests.CreateService(first).AssignAsync(route1, Assign(version, segment1, driver, escort1), actor)),
                    Attempt(() => PersonnelAssignmentTests.CreateService(second).AssignAsync(route2, Assign(version, segment2, driver, escort2), actor)));
                Assert.Single(results, x => x is null);
                Assert.Equal("SCHEDULE_CONFLICT", Assert.IsType<ConflictException>(Assert.Single(results, x => x is not null)).ErrorCode);
            }
            Guid winner, winningSegment, winningEscort;
            await using (var db = new AppDbContext(options))
            {
                var assigned = await db.RoutePlanRoutes.SingleAsync(x => x.DriverEmployeeId == driver);
                winner = assigned.RoutePlanId; winningSegment = assigned.RoutePlanRouteId; winningEscort = assigned.EscortEmployeeId!.Value;
                var route = await db.RoutePlans.SingleAsync(x => x.RoutePlanId == winner);
                version = PersonnelAssignmentTests.Version(route);
                Assert.Equal(2, await db.EmployeeTransportPlans.CountAsync());
                Assert.Single(await db.CatalogAuditLogs.ToListAsync());
                Assert.Equal(4, await db.OutboxMessages.CountAsync()); // Two emails, two SignalR messages.
                var view = await PersonnelAssignmentTests.CreateService(db).GetAsync(route.TransportPlanId, actor);
                Assert.Equal("FULLY_ASSIGNED", view.Phase2.PersonnelStatus);
                var retry = await PersonnelAssignmentTests.CreateService(db).AssignAsync(winner, Assign(version, winningSegment, driver, winningEscort), actor);
                Assert.Equal(version, retry.UpdatedAt); Assert.Equal(4, await db.OutboxMessages.CountAsync());
            }
            await using (var first = new AppDbContext(options))
            await using (var second = new AppDbContext(options))
            {
                // Reused contexts must reload rows after acquiring locks.
                await first.RoutePlans.SingleAsync(x => x.RoutePlanId == winner);
                await second.RoutePlans.SingleAsync(x => x.RoutePlanId == winner);
                var results = await Task.WhenAll(
                    Attempt(() => PersonnelAssignmentTests.CreateService(first).ReplaceAsync(winner, winningSegment, new() { LastUpdatedAt = version, DriverEmployeeId = replacement1, Reason = "Sick" }, actor)),
                    Attempt(() => PersonnelAssignmentTests.CreateService(second).ReplaceAsync(winner, winningSegment, new() { LastUpdatedAt = version, DriverEmployeeId = replacement2, Reason = "Sick" }, actor)));
                Assert.Single(results, x => x is null);
                Assert.Equal("ROUTE_PLAN_MODIFIED", Assert.IsType<ConflictException>(Assert.Single(results, x => x is not null)).ErrorCode);
            }
            long audits, notifications, outbox;
            Guid? currentDriver;
            await using (var db = new AppDbContext(options))
            {
                var route = await db.RoutePlans.SingleAsync(x => x.RoutePlanId == winner);
                version = PersonnelAssignmentTests.Version(route);
                currentDriver = (await db.RoutePlanRoutes.SingleAsync(x => x.RoutePlanRouteId == winningSegment)).DriverEmployeeId;
                Assert.False(await db.EmployeeTransportPlans.AnyAsync(x => x.EmployeeId == driver));
                Assert.True(await db.EmployeeTransportPlans.AnyAsync(x => x.EmployeeId == currentDriver && x.Responsibility == "DRIVER"));
                audits = await db.CatalogAuditLogs.LongCountAsync(); notifications = await db.Notifications.LongCountAsync(); outbox = await db.OutboxMessages.LongCountAsync();
            }
            await Execute(admin, """
                CREATE FUNCTION reject_personnel_audit() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'test audit failure'; END $$;
                CREATE TRIGGER reject_personnel_audit BEFORE INSERT ON "CatalogAuditLog"
                FOR EACH ROW EXECUTE FUNCTION reject_personnel_audit();
                """);
            await using (var db = new AppDbContext(options))
                await Assert.ThrowsAsync<DbUpdateException>(() => PersonnelAssignmentTests.CreateService(db).ReplaceAsync(winner, winningSegment,
                    new() { LastUpdatedAt = version, DriverEmployeeId = driver, Reason = "Rollback" }, actor));
            await using (var db = new AppDbContext(options))
            {
                Assert.Equal(version, PersonnelAssignmentTests.Version(await db.RoutePlans.SingleAsync(x => x.RoutePlanId == winner)));
                Assert.Equal(currentDriver, (await db.RoutePlanRoutes.SingleAsync(x => x.RoutePlanRouteId == winningSegment)).DriverEmployeeId);
                Assert.Equal(2, await db.EmployeeTransportPlans.CountAsync());
                Assert.Equal(audits, await db.CatalogAuditLogs.LongCountAsync()); Assert.Equal(notifications, await db.Notifications.LongCountAsync()); Assert.Equal(outbox, await db.OutboxMessages.LongCountAsync());
            }
        }
        finally { await Execute(admin, $"SET search_path TO public; DROP SCHEMA {schema} CASCADE;"); }
    }
    private static Request.AssignRequest Assign(DateTimeOffset version, Guid segment, Guid driver, Guid escort) => new()
    { LastUpdatedAt = version, Segments = [new() { RoutePlanRouteId = segment, DriverEmployeeId = driver, EscortEmployeeId = escort }] };
    private static async Task<Exception?> Attempt(Func<Task> action)
    {
        try { await action(); return null; } catch (Exception ex) { return ex; }
    }
    private static async Task Execute(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection); await command.ExecuteNonQueryAsync();
    }
}
