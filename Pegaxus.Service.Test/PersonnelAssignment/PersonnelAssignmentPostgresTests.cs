using System.Text.Json;
using Microsoft.EntityFrameworkCore;
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
using Pegaxus.Service.PersonnelAssignmentService;

namespace Pegaxus.Service.Test;

public class PersonnelAssignmentPostgresTests
{
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
                var driverEmployee = await PersonnelAssignmentTests.EmployeeAsync(db, EmployeeRole.DRIVER);
                driverEmployee.Account.AvatarUrl = "https://example.com/driver.png";
                driver = driverEmployee.EmployeeId;
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
                await AssertConflict(Assert.Single(results, x => x is not null)!, "SCHEDULE_CONFLICT");
            }
            Guid winner, winningSegment, winningEscort;
            await using (var db = new AppDbContext(options))
            {
                var assigned = await db.RoutePlanRoutes.SingleAsync(x => x.DriverEmployeeId == driver);
                winner = assigned.RoutePlanId; winningSegment = assigned.RoutePlanRouteId; winningEscort = assigned.EscortEmployeeId!.Value;
                var route = await db.RoutePlans.SingleAsync(x => x.RoutePlanId == winner);
                var blockingPlan = await db.TransportPlans.SingleAsync(x => x.TransportPlanId == route.TransportPlanId);
                var employee = await db.Employees.Include(x => x.Account).SingleAsync(x => x.EmployeeId == driver);
                var error = await Assert.ThrowsAsync<ConflictException>(() => PersonnelAssignmentTests.CreateService(db).AssignAsync(
                    winner == route1 ? route2 : route1, Assign(version, winner == route1 ? segment2 : segment1,
                        driver, winner == route1 ? escort2 : escort1), actor));
                Assert.Equal("SCHEDULE_CONFLICT", error.ErrorCode);
                var scheduleConflict = Assert.IsType<Response.ScheduleConflictResponse>(error.Value);
                Assert.Equal(driver, scheduleConflict.EmployeeId); Assert.Equal(employee.EmployeeCode, scheduleConflict.EmployeeCode);
                Assert.Equal("DRIVER", scheduleConflict.Role);
                Assert.Equal(winner == route1 ? segment2 : segment1, scheduleConflict.RoutePlanRouteId);
                Assert.Equal(blockingPlan.PlanCode, scheduleConflict.ConflictingTransportPlanCode);
                Assert.Equal("Stable", scheduleConflict.ConflictingFromLocationName); Assert.Equal("Airport", scheduleConflict.ConflictingToLocationName);
                Assert.Equal(new DateTimeOffset(DateTime.SpecifyKind(assigned.PlannedDepartureAt!.Value, DateTimeKind.Utc)), scheduleConflict.ConflictingPlannedDepartureAt);
                Assert.Equal(new DateTimeOffset(DateTime.SpecifyKind(assigned.PlannedArrivalAt!.Value, DateTimeKind.Utc)), scheduleConflict.ConflictingPlannedArrivalAt);
                version = PersonnelAssignmentTests.Version(route);
                Assert.Equal(2, await db.EmployeeTransportPlans.CountAsync());
                Assert.Single(await db.CatalogAuditLogs.ToListAsync());
                Assert.Equal(4, await db.OutboxMessages.CountAsync()); // Two emails, two SignalR messages.
                var view = await PersonnelAssignmentTests.CreateService(db).GetAsync(route.TransportPlanId, actor);
                Assert.True(view.Phase2.IsPersonnelComplete);
                Assert.Equal(employee.Account.Email, view.Phase2.Segments[0].Driver!.Email);
                Assert.Equal(employee.Account.AvatarUrl, view.Phase2.Segments[0].Driver!.AvatarUrl);
                var from = new DateTimeOffset(DateTime.SpecifyKind(assigned.PlannedDepartureAt!.Value, DateTimeKind.Utc));
                var to = new DateTimeOffset(DateTime.SpecifyKind(assigned.PlannedArrivalAt!.Value, DateTimeKind.Utc));
                foreach (var offset in new[] { TimeSpan.Zero, TimeSpan.FromHours(7) })
                {
                    var drivers = await PersonnelAssignmentTests.CreateService(db).GetAssignableAsync(new() {
                        Role = "DRIVER", AvailableFrom = from.ToOffset(offset), AvailableTo = to.ToOffset(offset) }, actor);
                    Assert.Equal(2, drivers.TotalItems);
                    Assert.Equal(new[] { replacement1, replacement2 }.OrderBy(x => x), drivers.Items.Select(x => x.EmployeeId).OrderBy(x => x));
                    var escorts = await PersonnelAssignmentTests.CreateService(db).GetAssignableAsync(new() {
                        Role = "ESCORT", AvailableFrom = from.ToOffset(offset), AvailableTo = to.ToOffset(offset) }, actor);
                    Assert.Equal(winningEscort == escort1 ? escort2 : escort1, Assert.Single(escorts.Items).EmployeeId);
                }
                var retry = await Assert.ThrowsAsync<ConflictException>(() => PersonnelAssignmentTests.CreateService(db)
                    .AssignAsync(winner, Assign(version, winningSegment, driver, winningEscort), actor));
                Assert.Equal("SEGMENT_ALREADY_ASSIGNED", retry.ErrorCode);
                Assert.Equal(version, PersonnelAssignmentTests.Version(await db.RoutePlans.AsNoTracking().SingleAsync(x => x.RoutePlanId == winner)));
                Assert.Equal(4, await db.OutboxMessages.CountAsync());
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
                await AssertConflict(Assert.Single(results, x => x is not null)!, "ROUTE_PLAN_MODIFIED");
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
            await Execute(admin, "DROP TRIGGER reject_personnel_audit ON \"CatalogAuditLog\"; DROP FUNCTION reject_personnel_audit()");
            await using var queryDb = new AppDbContext(options);
            Assert.Equal(0, await queryDb.DocumentVersions.Where(item => item.ComplianceDocumentId == Guid.Empty)
                .OrderByDescending(item => item.VersionNo).Take(1)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.DocumentVersionId, item => item.DocumentVersionId)));
            await CheckOutboxClaimsAsync(options);
            await CheckRequirementReferenceRaceAsync(options, actor);
        }
        finally { await Execute(admin, $"SET search_path TO public; DROP SCHEMA {schema} CASCADE;"); }
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

    private static Request.AssignRequest Assign(DateTimeOffset version, Guid segment, Guid driver, Guid escort) => new()
    { LastUpdatedAt = version, Segments = [new() { RoutePlanRouteId = segment, DriverEmployeeId = driver, EscortEmployeeId = escort }] };
    private static async Task<Exception?> Attempt(Func<Task> action)
    {
        try { await action(); return null; } catch (Exception ex) { return ex; }
    }
    private static async Task AssertConflict(Exception error, string errorCode)
    {
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        await new GlobalExceptionHandlerMiddleware(NullLogger<GlobalExceptionHandlerMiddleware>.Instance)
            .InvokeAsync(context, _ => Task.FromException(error));
        Assert.Equal(409, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        using var body = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Contains(body.RootElement.GetProperty("errorCode").GetString(), new[] { errorCode, "CONCURRENT_UPDATE" });
    }
    private static async Task Execute(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection); await command.ExecuteNonQueryAsync();
    }
}
