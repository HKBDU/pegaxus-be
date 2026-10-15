using System.Text.RegularExpressions;
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
using Pegaxus.Repository.Migrations;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.VehicleService;

namespace Pegaxus.Service.Test;

// These tests create schemas in a shared database; parallel runs make Npgsql see duplicate enum type names.
[Collection("PostgresSharedDatabase")]
public class VehiclePostgresTests
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
    public async Task PostgreSql_MigrationSchedulingConcurrencyUniquenessAndAtomicRollback()
    {
        var connectionString = Environment.GetEnvironmentVariable("PEGAXUS_TEST_CONNECTION_STRING")!;
        var schema = "vehicle_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(connectionString);
        await admin.OpenAsync();
        await Execute(admin, $"CREATE SCHEMA {schema}; SET search_path TO {schema};");
        var settings = new NpgsqlConnectionStringBuilder(connectionString) { SearchPath = schema };
        var sourceBuilder = new NpgsqlDataSourceBuilder(settings.ConnectionString);
        var translator = new EnumTranslator();
        foreach (var type in typeof(VehicleStatus).Assembly.GetTypes().Where(x => x.IsEnum && x.Namespace == typeof(VehicleStatus).Namespace))
            sourceBuilder.MapEnum(type, translator.TranslateTypeName(type.Name), translator);
        await using var source = sourceBuilder.Build();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseTestNpgsql(source).Options;
        try
        {
            await using (var modelDb = new AppDbContext(options))
            {
                var baseline = modelDb.Database.GenerateCreateScript();
                baseline = Regex.Replace(baseline, @"CREATE TYPE vehicle_status AS ENUM \([^;]+;", match =>
                    match.Value.Replace(", 'SUSPENDED', 'OUT_OF_SERVICE'", ""));
                baseline = baseline.Replace(" WHERE is_deleted = false", "");
                await Execute(admin, baseline);
                var migration = new AddVehicleCatalog();
                await Migrate(admin, modelDb, migration.UpOperations);
                Assert.Equal(2L, await Scalar(admin, "SELECT count(*) FROM pg_enum e JOIN pg_type t ON t.oid=e.enumtypid JOIN pg_namespace n ON n.oid=t.typnamespace WHERE n.nspname=current_schema() AND t.typname='vehicle_status' AND e.enumlabel IN ('SUSPENDED','OUT_OF_SERVICE')"));
                Assert.Equal(2L, await Scalar(admin, "SELECT count(*) FROM pg_indexes WHERE schemaname=current_schema() AND indexname IN ('Vehicle_license_plate_key','Vehicle_vehicle_code_key') AND indexdef LIKE '%WHERE%is_deleted%false%'"));
                await Migrate(admin, modelDb, migration.DownOperations);
                Assert.Equal(0L, await Scalar(admin, "SELECT count(*) FROM pg_indexes WHERE schemaname=current_schema() AND indexname IN ('Vehicle_license_plate_key','Vehicle_vehicle_code_key') AND indexdef LIKE '%WHERE%'"));
                await Migrate(admin, modelDb, migration.UpOperations);
            }

            Guid actor;
            Response.VehicleResponse vehicle;
            await using (var db = new AppDbContext(options))
            {
                actor = (await TestSupport.AddLogisticsManagerAsync(db)).Account.AccountId;
                vehicle = await VehicleServiceTests.CreateService(db).CreateAsync(VehicleServiceTests.ValidRequest(), actor);
                Assert.Single(await db.OutboxMessages.ToListAsync());
                var segment = await VehicleServiceTests.AddAssignmentAsync(db, await db.Vehicles.SingleAsync());
                var service = VehicleServiceTests.CreateService(db);
                var unavailable = await service.GetListAsync(new() { AvailableFrom = VehicleServiceTests.Today.AddDays(2), AvailableTo = VehicleServiceTests.Today.AddDays(2) }, actor);
                Assert.Empty(unavailable.Items);
                Assert.Single((await service.GetAsync(vehicle.VehicleId, actor)).UpcomingAssignments!);
                Assert.Single((await service.GetListAsync(new() { AvailableFrom = VehicleServiceTests.Today.AddDays(3), AvailableTo = VehicleServiceTests.Today.AddDays(3) }, actor)).Items);
                Assert.Equal("VEHICLE_STATUS_LOCKED", (await Assert.ThrowsAsync<ConflictException>(() => service.ChangeStatusAsync(vehicle.VehicleId, VehicleServiceTests.StatusRequest(vehicle.UpdatedAt), actor))).ErrorCode);
                segment.RoutePlan.Status = RoutePlanStatus.COMPLETED;
                await db.SaveChangesAsync();
            }

            // Both contexts deliberately track the old version before the first row lock.
            await using (var first = new AppDbContext(options))
            await using (var second = new AppDbContext(options))
            {
                await first.Vehicles.SingleAsync(x => x.VehicleId == vehicle.VehicleId);
                await second.Vehicles.SingleAsync(x => x.VehicleId == vehicle.VehicleId);
                var outcomes = await Task.WhenAll(
                    Attempt(() => VehicleServiceTests.CreateService(first).ChangeStatusAsync(vehicle.VehicleId, VehicleServiceTests.StatusRequest(vehicle.UpdatedAt, "INACTIVE"), actor)),
                    Attempt(() => VehicleServiceTests.CreateService(second).ChangeStatusAsync(vehicle.VehicleId, VehicleServiceTests.StatusRequest(vehicle.UpdatedAt, "INACTIVE"), actor)));
                Assert.Single(outcomes, x => x is null);
                Assert.Equal("VEHICLE_MODIFIED", Assert.IsType<ConflictException>(Assert.Single(outcomes, x => x is not null)).ErrorCode);
            }

            await using (var first = new AppDbContext(options))
            await using (var second = new AppDbContext(options))
            {
                var outcomes = await Task.WhenAll(
                    Attempt(() => VehicleServiceTests.CreateService(first).CreateAsync(VehicleServiceTests.ValidRequest("RACE", "RACE-1"), actor)),
                    Attempt(() => VehicleServiceTests.CreateService(second).CreateAsync(VehicleServiceTests.ValidRequest("RACE", "RACE-2"), actor)));
                Assert.Single(outcomes, x => x is null);
                Assert.Equal("VEHICLE_CODE_DUPLICATED", Assert.IsType<ConflictException>(Assert.Single(outcomes, x => x is not null)).ErrorCode);
            }
            await using (var first = new AppDbContext(options))
            await using (var second = new AppDbContext(options))
            {
                var outcomes = await Task.WhenAll(
                    Attempt(() => VehicleServiceTests.CreateService(first).CreateAsync(VehicleServiceTests.ValidRequest("PLATE-RACE1", "RACE-PLATE"), actor)),
                    Attempt(() => VehicleServiceTests.CreateService(second).CreateAsync(VehicleServiceTests.ValidRequest("PLATE-RACE2", "RACE-PLATE"), actor)));
                Assert.Single(outcomes, x => x is null);
                Assert.Equal("LICENSE_PLATE_DUPLICATED", Assert.IsType<ConflictException>(Assert.Single(outcomes, x => x is not null)).ErrorCode);
            }

            await using (var db = new AppDbContext(options))
            {
                var service = VehicleServiceTests.CreateService(db);
                var old = await db.Vehicles.SingleAsync(x => x.VehicleCode == "RACE");
                old.IsDeleted = true; await db.SaveChangesAsync();
                await service.CreateAsync(VehicleServiceTests.ValidRequest("RACE", old.LicensePlate!), actor);
                Assert.Equal(2, await db.Vehicles.CountAsync(x => x.VehicleCode == "RACE"));
                // A direct database write must still obey the unique index.
                db.Vehicles.Add(new Vehicle { VehicleId = Guid.NewGuid(), VehicleCode = "VEH-ROAD-005", LicensePlate = "DIRECT", TransportMode = TransportMode.ROAD });
                var unique = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
                Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(unique.InnerException).SqlState);
            }

            Response.VehicleResponse before;
            long auditCount, notificationCount, outboxCount;
            await using (var db = new AppDbContext(options))
            {
                before = await VehicleServiceTests.CreateService(db).GetAsync(vehicle.VehicleId, actor);
                auditCount = await db.CatalogAuditLogs.LongCountAsync();
                notificationCount = await db.Notifications.LongCountAsync();
                outboxCount = await db.OutboxMessages.LongCountAsync();
            }
            await Execute(admin, """
                CREATE FUNCTION reject_vehicle_audit() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'test audit failure'; END $$;
                CREATE TRIGGER reject_vehicle_audit BEFORE INSERT ON "CatalogAuditLog"
                FOR EACH ROW EXECUTE FUNCTION reject_vehicle_audit();
                """);
            await using (var db = new AppDbContext(options))
                await Assert.ThrowsAsync<DbUpdateException>(() => VehicleServiceTests.CreateService(db).ChangeStatusAsync(vehicle.VehicleId, VehicleServiceTests.StatusRequest(before.UpdatedAt, "AVAILABLE"), actor));
            await using (var verify = new AppDbContext(options))
            {
                var after = await VehicleServiceTests.CreateService(verify).GetAsync(vehicle.VehicleId, actor);
                Assert.Equal(before.Status, after.Status); Assert.Equal(before.UpdatedAt, after.UpdatedAt);
                Assert.Equal(auditCount, await verify.CatalogAuditLogs.LongCountAsync());
                Assert.Equal(notificationCount, await verify.Notifications.LongCountAsync());
                Assert.Equal(outboxCount, await verify.OutboxMessages.LongCountAsync());
            }
            await Execute(admin, "DROP TRIGGER reject_vehicle_audit ON \"CatalogAuditLog\"; DROP FUNCTION reject_vehicle_audit()");
            await using var queryDb = new AppDbContext(options);
            Assert.Equal(0, await queryDb.DocumentVersions.Where(item => item.ComplianceDocumentId == Guid.Empty)
                .OrderByDescending(item => item.VersionNo).Take(1)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.DocumentVersionId, item => item.DocumentVersionId)));
            await CheckOutboxClaimsAsync(options);
            await CheckRequirementReferenceRaceAsync(options, actor);
            await CheckVehicleAssignmentRaceAsync(options, actor);
        }
        finally
        {
            await Execute(admin, $"SET search_path TO public; DROP SCHEMA {schema} CASCADE;");
        }
    }

    private static async Task CheckVehicleAssignmentRaceAsync(DbContextOptions<AppDbContext> options, Guid actor)
    {
        await using var seed = new AppDbContext(options);
        var vehicle = await VehicleServiceTests.CreateService(seed).CreateAsync(VehicleServiceTests.ValidRequest("ASSIGN-RACE", "ASSIGN-RACE"), actor);
        var segment = await VehicleServiceTests.AddAssignmentAsync(seed, await seed.Vehicles.SingleAsync(item => item.VehicleId == vehicle.VehicleId));
        seed.RoutePlanRouteVehicles.Remove(await seed.RoutePlanRouteVehicles.SingleAsync(item => item.VehicleId == vehicle.VehicleId));
        await seed.SaveChangesAsync();
        await using var writer = new AppDbContext(options);
        await using var writeTransaction = await writer.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        Assert.True(await writer.Vehicles.AnyAsync(item => item.VehicleId == vehicle.VehicleId && !item.IsDeleted && item.Status == VehicleStatus.AVAILABLE));
        writer.RoutePlanRouteVehicles.Add(new RoutePlanRouteVehicle
        {
            VehicleId = vehicle.VehicleId, RoutePlanRouteId = segment.RoutePlanRouteId,
            AssignedAt = DateTime.SpecifyKind(VehicleServiceTests.TestClock.GetUtcNow().UtcDateTime, DateTimeKind.Unspecified)
        });
        await writer.SaveChangesAsync();
        await using var changer = new AppDbContext(options);
        var changeError = await Attempt(() => VehicleServiceTests.CreateService(changer).ChangeStatusAsync(vehicle.VehicleId,
            VehicleServiceTests.StatusRequest(vehicle.UpdatedAt, "INACTIVE"), actor));
        var writeError = await Attempt(() => writeTransaction.CommitAsync());
        Assert.True(changeError is not null || writeError is not null, "Status change and assignment insertion must not both commit.");
        var conflict = changeError ?? writeError;
        if (conflict is ConflictException applicationConflict)
            Assert.Equal("VEHICLE_MODIFIED", applicationConflict.ErrorCode);
        else
            Assert.Equal(PostgresErrorCodes.SerializationFailure,
                Assert.IsType<PostgresException>(conflict is DbUpdateException update ? update.InnerException : conflict).SqlState);
        await using var check = new AppDbContext(options);
        Assert.False(await check.RoutePlanRouteVehicles.AnyAsync(item => item.VehicleId == vehicle.VehicleId && item.Vehicle.Status == VehicleStatus.INACTIVE));
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
            .AddSingleton<Pegaxus.Service.MailService.IMailService>(mail).BuildServiceProvider();
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

    private sealed class ConcurrentMail : Pegaxus.Service.MailService.IMailService
    {
        public System.Collections.Concurrent.ConcurrentBag<string> Subjects { get; } = [];
        public Task SendMail(Pegaxus.Service.MailService.MailContent mailContent, CancellationToken cancellationToken = default)
        {
            Subjects.Add(mailContent.Subject);
            return Task.CompletedTask;
        }
    }

    private static async Task<Exception?> Attempt(Func<Task> action)
    {
        try { await action(); return null; }
        catch (Exception ex) { return ex; }
    }
    private static async Task Migrate(NpgsqlConnection connection, AppDbContext db, IReadOnlyList<Microsoft.EntityFrameworkCore.Migrations.Operations.MigrationOperation> operations)
    {
        foreach (var command in db.GetService<IMigrationsSqlGenerator>().Generate(operations, db.Model))
            await Execute(connection, command.CommandText);
    }
    private static async Task Execute(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
    private static async Task<long> Scalar(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }
}
