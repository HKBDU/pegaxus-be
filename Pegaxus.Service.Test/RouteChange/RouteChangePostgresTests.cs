using Pegaxus.API.Middleware;
using Pegaxus.API.Services;
using Pegaxus.Repository.Entities;
using Microsoft.Extensions.Logging.Abstractions;
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
    public async Task BackupLocks_MultipleSegments_KeepCompletedHistory()
    {
        await using var scope = await DatabaseScope.CreateAsync();
        await using var db = scope.Db();
        var fixture = await RouteChangeTests.Fixture.CreateAsync(db);
        var backup = new RoutePlan
        {
            RoutePlanId = Guid.NewGuid(), TransportPlanId = fixture.Plan.TransportPlanId,
            RoutePlanCode = "FULL_BACKUP", Status = RoutePlanStatus.CONFIRMED, PrimaryRoutePlanId = fixture.Plan.RoutePlanId
        };
        db.Add(backup);
        db.AddRange(new RoutePlanRoute
        {
            RoutePlanRouteId = Guid.NewGuid(), RoutePlanId = backup.RoutePlanId, RouteId = fixture.Completed.RouteId,
            SequenceNo = 1, RouteType = RouteType.BACKUP, PlannedDepartureAt = fixture.Completed.PlannedDepartureAt, PlannedArrivalAt = fixture.Completed.PlannedArrivalAt
        }, new RoutePlanRoute
        {
            RoutePlanRouteId = Guid.NewGuid(), RoutePlanId = backup.RoutePlanId, RouteId = fixture.Alternative.RouteId,
            SequenceNo = 2, RouteType = RouteType.BACKUP, PlannedDepartureAt = fixture.Current.PlannedDepartureAt, PlannedArrivalAt = fixture.Current.PlannedArrivalAt
        });
        await db.SaveChangesAsync();
        var request = fixture.Proposal();
        request.ReplacementSegments = null;
        request.BackupRoutePlanId = backup.RoutePlanId;
        var created = await Service(db).CreateAsync(fixture.Plan.RoutePlanId, fixture.Coordinator.AccountId, request);
        var result = await Service(db).DecideAsync(created.ChangeRequestId, fixture.Manager.AccountId,
            new() { Decision = "APPROVE", LastUpdatedAt = created.CreatedAt });
        var replacementId = Assert.Single(result.NewSegmentIds);
        Assert.Equal(fixture.Driver.EmployeeId, (await db.RoutePlanRoutes.SingleAsync(x => x.RoutePlanRouteId == replacementId)).DriverEmployeeId);
        Assert.Null((await db.RoutePlanRoutes.AsNoTracking().SingleAsync(x => x.RoutePlanRouteId == fixture.Completed.RoutePlanRouteId)).SupersededAt);
        Assert.Equal(2, await db.RoutePlanRoutes.CountAsync(x => x.RoutePlanId == backup.RoutePlanId));
    }

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
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(scope.Source).Options;
        Assert.Equal(0, await check.DocumentVersions.Where(item => item.ComplianceDocumentId == Guid.Empty)
            .OrderByDescending(item => item.VersionNo).Take(1)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.DocumentVersionId, item => item.DocumentVersionId)));
        await CheckOutboxClaimsAsync(options);
        await CheckRequirementReferenceRaceAsync(options, managerId);
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
