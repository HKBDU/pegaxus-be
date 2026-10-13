using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Npgsql.NameTranslation;
using Pegaxus.API.Middleware;
using Pegaxus.API.Services;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.NotificationService;

using RouteChange = Pegaxus.Service.RouteChangeService;

namespace Pegaxus.Service.Test;

public class RouteChangePostgresTests
{
    [PostgresFact]
    public async Task BackupActivation_PreservesHistoryAndDurableNotifications()
    {
        await using var scope = await DatabaseScope.CreateAsync();
        await using var db = scope.Db();
        var f = await RouteChangeTests.Fixture.CreateAsync(db);
        var backup = await f.AlternateAsync();
        await Service(db).CreateAsync(backup.RoutePlanId, f.Coordinator.AccountId, f.Proposal());
        var result = await Service(db).DecideAsync(backup.RoutePlanId, f.Manager.AccountId,
            new() { Decision = "APPROVE", LastUpdatedAt = RouteChangeTests.Fixture.Version(backup) });
        Assert.Equal("ACTIVE", result.Status);
        await using var check = scope.Db();
        Assert.Equal(3, await check.RoutePlanRoutes.CountAsync());
        Assert.Null((await check.RoutePlanRoutes.SingleAsync(x => x.RoutePlanRouteId == f.Completed.RoutePlanRouteId)).SupersededAt);
        Assert.NotNull((await check.RoutePlanRoutes.SingleAsync(x => x.RoutePlanRouteId == f.Current.RoutePlanRouteId)).SupersededAt);
        Assert.Equal(4, await check.Notifications.CountAsync());
        Assert.Equal(4, await check.OutboxMessages.CountAsync());
        Assert.Empty(await check.IncidentReports.ToListAsync());
        var options = new DbContextOptionsBuilder<AppDbContext>().UseTestNpgsql(scope.Source).Options;
        await CheckOutboxClaimsAsync(options);
        await CheckRequirementReferenceRaceAsync(options, f.Manager.AccountId);
    }

    [PostgresFact]
    public async Task ConcurrentDecisions_OnlyOneRouteVersionSucceeds()
    {
        await using var scope = await DatabaseScope.CreateAsync();
        Guid routeId, managerId; DateTimeOffset version;
        await using (var setup = scope.Db())
        {
            var f = await RouteChangeTests.Fixture.CreateAsync(setup);
            var backup = await f.AlternateAsync();
            routeId = backup.RoutePlanId; managerId = f.Manager.AccountId; version = RouteChangeTests.Fixture.Version(backup);
        }
        async Task<string> Decide(string decision)
        {
            await using var db = scope.Db();
            try { return (await Service(db).DecideAsync(routeId, managerId,
                new() { Decision = decision, LastUpdatedAt = version, Note = "Manager reviewed the emergency" })).Status; }
            catch (ConflictException error) { return error.ErrorCode; }
            catch (Exception error) when (error.GetBaseException() is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure }) { return "ROUTE_PLAN_MODIFIED"; }
        }
        var results = await Task.WhenAll(Decide("APPROVE"), Decide("REJECT"));
        Assert.Single(results, x => x == "ROUTE_PLAN_MODIFIED");
        Assert.Single(results, x => x is "ACTIVE" or "CONFIRMED");
        await using var check = scope.Db();
        Assert.Single(await check.CatalogAuditLogs.ToListAsync());
    }

    [PostgresFact]
    public async Task FailedAudit_RollsBackActivationAndNotifications()
    {
        await using var scope = await DatabaseScope.CreateAsync();
        Guid routeId, managerId, segmentId; DateTimeOffset version;
        await using (var setup = scope.Db())
        {
            var f = await RouteChangeTests.Fixture.CreateAsync(setup);
            var backup = await f.AlternateAsync();
            routeId = backup.RoutePlanId; managerId = f.Manager.AccountId;
            segmentId = f.Current.RoutePlanRouteId; version = RouteChangeTests.Fixture.Version(backup);
            await setup.Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION reject_uc48_audit() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'audit unavailable'; END $$;
                CREATE TRIGGER reject_uc48_audit BEFORE INSERT ON "CatalogAuditLog" FOR EACH ROW EXECUTE FUNCTION reject_uc48_audit();
                """);
        }
        await using (var failing = scope.Db())
            await Assert.ThrowsAsync<DbUpdateException>(() => Service(failing).DecideAsync(routeId, managerId,
                new() { Decision = "APPROVE", LastUpdatedAt = version }));
        await using var check = scope.Db();
        Assert.Equal(RoutePlanStatus.CONFIRMED, (await check.RoutePlans.SingleAsync(x => x.RoutePlanId == routeId)).Status);
        Assert.Null((await check.RoutePlanRoutes.SingleAsync(x => x.RoutePlanRouteId == segmentId)).SupersededAt);
        Assert.Empty(await check.Notifications.ToListAsync()); Assert.Empty(await check.OutboxMessages.ToListAsync());
    }

    [PostgresFact]
    public async Task EmergencyMigration_PreservesExistingOutsourcingData()
    {
        await using var scope = await DatabaseScope.CreateAsync();
        await using var db = scope.Db();
        var fixture = await RouteChangeTests.Fixture.CreateAsync(db);
        fixture.Current.ServiceType = "Freight";
        fixture.Current.ProviderReference = "Existing carrier booking";
        await db.SaveChangesAsync();

        var migration = new Pegaxus.Repository.Migrations.EmergencyRouteChanges();
        var generator = db.GetService<IMigrationsSqlGenerator>();
        foreach (var operations in new[] { migration.DownOperations, migration.UpOperations })
        {
            foreach (var command in generator.Generate(operations))
                await db.Database.ExecuteSqlRawAsync(command.CommandText);
            var preserved = await db.RoutePlanRoutes.AsNoTracking()
                .Where(x => x.RoutePlanRouteId == fixture.Current.RoutePlanRouteId)
                .Select(x => new { x.ServiceType, x.ProviderReference }).SingleAsync();
            Assert.Equal("Freight", preserved.ServiceType);
            Assert.Equal("Existing carrier booking", preserved.ProviderReference);
        }
    }

    [PostgresFact]
    public async Task Migration_UpDownUp_AndSnapshotMatchCurrentModel()
    {
        await using var scope = await DatabaseScope.CreateAsync();
        await using var db = scope.Db();
        Assert.False(db.Database.HasPendingModelChanges());
        var migrator = db.GetInfrastructure().GetRequiredService<IMigrator>();
        await migrator.MigrateAsync("20261006100000_AlignRoutePlanningWithDbV7");
        await migrator.MigrateAsync();
        // EF 8 finalizes the cached snapshot on the first check; use a fresh context after migration.
        await using var check = scope.Db();
        Assert.False(check.Database.HasPendingModelChanges());
    }

    private static async Task CheckOutboxClaimsAsync(DbContextOptions<AppDbContext> options)
    {
        await using var db = new AppDbContext(options);
        await db.OutboxMessages.ExecuteDeleteAsync();
        var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        for (var index = 0; index < 26; index++)
        {
            Pegaxus.Service.OutboxService.OutboxQueue.AddEmail(db,
                new() { To = "outbox@example.com", Subject = index.ToString(), Body = "Test" }, now);
            var message = db.ChangeTracker.Entries<OutboxMessage>().Last().Entity;
            message.Status = index switch { 20 or 23 => "PROCESSING", 21 => "FAILED", 24 => "DEAD", 25 => "PROCESSED", _ => "PENDING" };
            if (index is 20 or 23) message.LockedAt = index == 20 ? now.AddMinutes(-11) : now;
            if (index == 22) message.AvailableAt = now.AddHours(1);
        }
        await db.SaveChangesAsync();
        var mail = new ConcurrentMail();
        await using var services = new ServiceCollection().AddScoped(_ => new AppDbContext(options))
            .AddSingleton<Pegaxus.Service.MailService.IService>(mail).BuildServiceProvider();
        var batch = typeof(TransactionalOutboxWorker).GetMethod("ProcessBatchAsync",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        async Task RunBatchAsync()
        {
            using var worker = new TransactionalOutboxWorker(services.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<TransactionalOutboxWorker>.Instance);
            await (Task)batch.Invoke(worker, [CancellationToken.None])!;
        }
        await RunBatchAsync();
        Assert.Equal(20, mail.Subjects.Count);
        await Task.WhenAll(RunBatchAsync(), RunBatchAsync());
        Assert.Equal(22, mail.Subjects.Count);
        Assert.Equal(22, mail.Subjects.Distinct().Count());
        Assert.DoesNotContain("22", mail.Subjects);
        Assert.DoesNotContain("23", mail.Subjects);
        Assert.DoesNotContain("24", mail.Subjects);
        Assert.DoesNotContain("25", mail.Subjects);
        Assert.Equal(23, await db.OutboxMessages.CountAsync(message => message.Status == "PROCESSED"));
    }

    private static async Task CheckRequirementReferenceRaceAsync(DbContextOptions<AppDbContext> options, Guid actorId)
    {
        await using var seed = new AppDbContext(options);
        var requirement = new ComplianceRequirement
        {
            ComplianceRequirementId = Guid.NewGuid(), RequirementCode = "RACE", RequirementName = "Race check",
            Status = ComplianceRequirementStatus.ACTIVE, Category = ComplianceRequirementCategory.Other
        };
        var template = new DossierTemplate { DossierTemplateId = Guid.NewGuid(), TemplateCode = "RACE", TemplateName = "Race check" };
        seed.AddRange(requirement, template);
        await seed.SaveChangesAsync();
        await using var writer = new AppDbContext(options);
        await using var writeTransaction = await writer.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        Assert.True(await writer.ComplianceRequirements.AnyAsync(item => item.ComplianceRequirementId == requirement.ComplianceRequirementId && !item.IsDeleted));
        writer.DossierTemplateItems.Add(new DossierTemplateItem
        {
            DossierTemplateItemId = Guid.NewGuid(), DossierTemplateId = template.DossierTemplateId,
            ComplianceRequirementId = requirement.ComplianceRequirementId, SequenceNo = 1
        });
        await writer.SaveChangesAsync();
        await using var deleter = new AppDbContext(options);
        var deleteError = await Record.ExceptionAsync(() =>
            new Pegaxus.Service.ComplianceRequirementService.Service(deleter, null!, null!, null!)
                .DeleteAsync(requirement.ComplianceRequirementId, actorId));
        var writeError = await Record.ExceptionAsync(() => writeTransaction.CommitAsync());
        Assert.True(deleteError is not null || writeError is not null, "Deletion and reference insertion must not both commit.");
        var conflict = deleteError ?? writeError;
        Assert.Equal(PostgresErrorCodes.SerializationFailure,
            Assert.IsType<PostgresException>(conflict is DbUpdateException update ? update.InnerException : conflict).SqlState);
        await using var check = new AppDbContext(options);
        Assert.False(await check.DossierTemplateItems.AnyAsync(item =>
            item.ComplianceRequirementId == requirement.ComplianceRequirementId && item.ComplianceRequirement.IsDeleted));
    }

    [Theory]
    [InlineData(PostgresErrorCodes.SerializationFailure, false)]
    [InlineData(PostgresErrorCodes.SerializationFailure, true)]
    [InlineData(PostgresErrorCodes.DeadlockDetected, false)]
    [InlineData(PostgresErrorCodes.DeadlockDetected, true)]
    public async Task PostgreSqlWriteConflicts_Return409WithoutDatabaseDetails(string sqlState, bool wrapped)
    {
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var postgres = new PostgresException("Database internals", "ERROR", "ERROR", sqlState);
        Exception error = wrapped ? new DbUpdateException("Database internals", postgres) : postgres;
        await new GlobalExceptionHandlerMiddleware(NullLogger<GlobalExceptionHandlerMiddleware>.Instance)
            .InvokeAsync(context, _ => Task.FromException(error));
        Assert.Equal(409, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        var response = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.Contains("CONCURRENT_UPDATE", response);
        Assert.DoesNotContain("Database internals", response);
    }

    private sealed class ConcurrentMail : Pegaxus.Service.MailService.IService
    {
        public System.Collections.Concurrent.ConcurrentBag<string> Subjects { get; } = [];
        public Task SendMail(Pegaxus.Service.MailService.MailContent mailContent, CancellationToken cancellationToken = default)
        {
            Subjects.Add(mailContent.Subject);
            return Task.CompletedTask;
        }
    }

    private static RouteChange.Service Service(AppDbContext db) => new(db,
        new Pegaxus.Service.NotificationService.Service(db, new NoopNotificationRealtimeService()), new RouteChangeTests.Fixture.FixedClock(),
        new Pegaxus.Service.RoutePlanService.Service(db, TestSupport.CreateNotifications(db)));


    private sealed class EnumNames : INpgsqlNameTranslator
    {
        private readonly NpgsqlSnakeCaseNameTranslator names = new();
        public string TranslateTypeName(string name) => names.TranslateTypeName(name);
        public string TranslateMemberName(string name) => name;
    }

    private sealed class DatabaseScope(string admin, string name, NpgsqlDataSource source) : IAsyncDisposable
    {
        internal NpgsqlDataSource Source => source;
        internal AppDbContext Db() => new(new DbContextOptionsBuilder<AppDbContext>().UseTestNpgsql(source).Options);
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
