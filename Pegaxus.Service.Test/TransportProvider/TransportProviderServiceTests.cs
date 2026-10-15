using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Npgsql.NameTranslation;
using Pegaxus.API.Controllers;
using Pegaxus.API.Extensions;
using Pegaxus.API.Middleware;
using Pegaxus.API.Services;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Repository.Migrations;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.JwtService;
using Pegaxus.Service.TransportProviderService.Validator;

using Providers = Pegaxus.Service.TransportProviderService;
using Claim = System.Security.Claims.Claim;

namespace Pegaxus.Service.Test;

// These tests create schemas in a shared database; parallel runs make Npgsql see duplicate enum type names.
[Collection("PostgresSharedDatabase")]
public class TransportProviderServiceTests
{
    [Fact]
    public async Task Lifecycle_PreservesMasterDataAuditsAndHistoricalReferences()
    {
        await using var db = TestSupport.CreateDb();
        var (actor, _) = await TestSupport.AddLogisticsManagerAsync(db);
        var (recipient, _) = await TestSupport.AddLogisticsManagerAsync(db);
        var service = CreateService(db);
        var created = await service.CreateAsync(actor.AccountId, CreateRequest());
        Assert.Equal("PRV-LUFTHANSA-01", created.ProviderCode);
        Assert.Equal("ACTIVE", created.Status);
        Assert.Equal(new[] { "AIR", "ROAD" }, created.SupportedModes);
        Assert.Equal("Operations", created.ContactName);
        Assert.Equal("DE", created.Country);
        Assert.Equal(TimeSpan.Zero, created.CreatedAt.Offset);
        Assert.Null(created.GetType().GetProperty("Drivers"));
        Assert.Null(created.GetType().GetProperty("Vehicles"));

        var update = UpdateRequest(created.UpdatedAt);
        var updated = await service.UpdateAsync(actor.AccountId, created.TransportProviderId, update);
        Assert.Equal("Lufthansa Cargo AG", (await service.GetAsync(actor.AccountId, created.TransportProviderId)).ProviderName);
        var inactive = await service.UpdateStatusAsync(actor.AccountId, created.TransportProviderId,
            new() { Status = "INACTIVE", Reason = "Contract ended" });
        var active = await service.UpdateStatusAsync(actor.AccountId, created.TransportProviderId,
            new() { Status = "ACTIVE", LastUpdatedAt = inactive.UpdatedAt });
        Assert.Equal("ACTIVE", active.Status);
        AddSegment(db, created.TransportProviderId, RoutePlanStatus.COMPLETED);
        await db.SaveChangesAsync();
        await service.DeleteAsync(actor.AccountId, created.TransportProviderId);
        var stored = await db.TransportProviders.SingleAsync();
        Assert.True(stored.IsDeleted);
        Assert.NotNull(stored.DeletedAt);
        Assert.Equal(actor.AccountId, stored.DeletedByAccountId);
        Assert.Single(await db.RoutePlanRoutes.ToListAsync());
        Assert.Empty((await service.GetListAsync(actor.AccountId, new())).Items);
        Assert.Equal("PROVIDER_NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(() => service.GetAsync(actor.AccountId, created.TransportProviderId))).ErrorCode);
        var recreated = await service.CreateAsync(actor.AccountId, CreateRequest());
        Assert.NotEqual(created.TransportProviderId, recreated.TransportProviderId);
        var audits = await db.CatalogAuditLogs.OrderBy(item => item.CreatedAt).ToListAsync();
        Assert.Equal(new[] { "CREATE", "UPDATE", "STATUS_CHANGED", "STATUS_CHANGED", "DELETE", "CREATE" }, audits.Select(item => item.ActionType));
        Assert.All(audits, item => Assert.Equal(actor.AccountId, item.ActorAccountId));
        Assert.Equal("Contract ended", audits[2].Summary);
        Assert.True(audits[4].AfterData.RootElement.GetProperty("IsDeleted").GetBoolean());
        Assert.Equal(2, await db.Notifications.CountAsync());
        Assert.All(await db.AccountNotifications.ToListAsync(), item => Assert.Equal(recipient.AccountId, item.AccountId));
    }

    [Theory]
    [InlineData(RoutePlanStatus.DRAFT, false)]
    [InlineData(RoutePlanStatus.CONFIRMED, false)]
    [InlineData(RoutePlanStatus.ACTIVE, false)]
    [InlineData(RoutePlanStatus.COMPLETED, true)]
    [InlineData(RoutePlanStatus.CANCELLED, true)]
    public async Task Delete_RejectsProvidersUsedByUnfinishedRoutes(RoutePlanStatus status, bool allowed)
    {
        await using var db = TestSupport.CreateDb();
        var (actor, _) = await TestSupport.AddLogisticsManagerAsync(db);
        var service = CreateService(db);
        var provider = await service.CreateAsync(actor.AccountId, CreateRequest());
        AddSegment(db, provider.TransportProviderId, status);
        await db.SaveChangesAsync();
        if (allowed) await service.DeleteAsync(actor.AccountId, provider.TransportProviderId);
        else
        {
            Assert.Equal("PROVIDER_IN_USE", (await Assert.ThrowsAsync<ConflictException>(() => service.DeleteAsync(actor.AccountId, provider.TransportProviderId))).ErrorCode);
            Assert.False((await db.TransportProviders.SingleAsync()).IsDeleted);
            Assert.Single(await db.CatalogAuditLogs.ToListAsync());
        }
    }

    [Fact]
    public async Task DuplicateCodesAndStaleUpdates_AreRejectedWithoutAdditionalAudit()
    {
        await using var db = TestSupport.CreateDb();
        var (actor, _) = await TestSupport.AddLogisticsManagerAsync(db);
        var service = CreateService(db);
        var provider = await service.CreateAsync(actor.AccountId, CreateRequest());
        Assert.Equal("PROVIDER_CODE_DUPLICATED", (await Assert.ThrowsAsync<ConflictException>(() => service.CreateAsync(actor.AccountId, CreateRequest()))).ErrorCode);
        var duplicateTax = CreateRequest();
        duplicateTax.ProviderCode = "PRV-OTHER";
        Assert.Equal("TAX_CODE_DUPLICATED", (await Assert.ThrowsAsync<ConflictException>(() => service.CreateAsync(actor.AccountId, duplicateTax))).ErrorCode);
        var changedCode = UpdateRequest(provider.UpdatedAt);
        changedCode.ProviderCode = "PRV-CHANGED";
        await Assert.ThrowsAsync<BadRequestException>(() => service.UpdateAsync(actor.AccountId, provider.TransportProviderId, changedCode));
        await service.UpdateAsync(actor.AccountId, provider.TransportProviderId, UpdateRequest(provider.UpdatedAt));
        Assert.Equal("PROVIDER_MODIFIED", (await Assert.ThrowsAsync<ConflictException>(() => service.UpdateAsync(actor.AccountId, provider.TransportProviderId, UpdateRequest(provider.UpdatedAt)))).ErrorCode);
        Assert.Equal("PROVIDER_MODIFIED", (await Assert.ThrowsAsync<ConflictException>(() => service.UpdateStatusAsync(actor.AccountId, provider.TransportProviderId,
            new() { Status = "SUSPENDED", Reason = "Review", LastUpdatedAt = provider.UpdatedAt }))).ErrorCode);
        Assert.Equal(2, await db.CatalogAuditLogs.CountAsync());
    }

    [Fact]
    public async Task Coordinator_CanOnlyListActiveProvidersAndCannotWrite()
    {
        await using var db = TestSupport.CreateDb();
        var (manager, _) = await TestSupport.AddLogisticsManagerAsync(db);
        var (coordinator, employee) = await TestSupport.AddLogisticsManagerAsync(db);
        employee.Role = EmployeeRole.FLEET_ROUTE_COORDINATOR;
        await db.SaveChangesAsync();
        var service = CreateService(db);
        var provider = await service.CreateAsync(manager.AccountId, CreateRequest());
        Assert.Single((await service.GetListAsync(coordinator.AccountId, new() { SupportedMode = "air", Search = "lufthan", PageSize = 1 })).Items);
        var second = CreateRequest();
        second.ProviderCode = "PRV-SECOND";
        second.TaxCode = "DE-SECOND";
        second.ProviderName = "Second";
        second.SupportedModes = ["SEA"];
        var other = await service.CreateAsync(manager.AccountId, second);
        Assert.Equal(2, (await service.GetListAsync(manager.AccountId, new() { PageSize = 1, PageIndex = 2 })).TotalPages);
        await service.UpdateStatusAsync(manager.AccountId, other.TransportProviderId,
            new() { Status = "SUSPENDED", Reason = "Review", LastUpdatedAt = other.UpdatedAt });
        Assert.Single((await service.GetListAsync(coordinator.AccountId, new() { Status = "SUSPENDED" })).Items);
        Assert.Single((await service.GetListAsync(manager.AccountId, new() { Status = "SUSPENDED", SupportedMode = "SEA", Search = "DE-SECOND" })).Items);
        Assert.Empty((await service.GetListAsync(manager.AccountId, new() { SupportedMode = "ROAD", Search = "missing" })).Items);
        await Assert.ThrowsAsync<ForbiddenException>(() => service.CreateAsync(coordinator.AccountId, CreateRequest()));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.UpdateAsync(coordinator.AccountId, provider.TransportProviderId, UpdateRequest(provider.UpdatedAt)));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.UpdateStatusAsync(coordinator.AccountId, provider.TransportProviderId, new()));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.DeleteAsync(coordinator.AccountId, provider.TransportProviderId));
        var (customer, _) = await TestSupport.AddCustomerAsync(db);
        await Assert.ThrowsAsync<ForbiddenException>(() => service.GetAsync(customer.AccountId, provider.TransportProviderId));
    }

    [Fact]
    public void Validation_RejectsInvalidModesRequiredFieldsEmailAndPagination()
    {
        var create = new CreateRequestValidator();
        Assert.True(create.Validate(CreateRequest()).IsValid);
        Assert.False(create.Validate(new Providers.Request.CreateRequest()).IsValid);
        var request = CreateRequest();
        request.SupportedModes = ["RAIL"];
        Assert.False(create.Validate(request).IsValid);
        request.SupportedModes = null!;
        Assert.False(create.Validate(request).IsValid);
        request.SupportedModes = ["AIR"];
        request.Email = "invalid";
        Assert.False(create.Validate(request).IsValid);
        Assert.False(new UpdateRequestValidator().Validate(UpdateRequest(default)).IsValid);
        Assert.False(new StatusRequestValidator().Validate(new Providers.Request.StatusRequest { Status = "INACTIVE", LastUpdatedAt = DateTimeOffset.UtcNow }).IsValid);
        Assert.False(new StatusRequestValidator().Validate(new Providers.Request.StatusRequest { Status = "123", LastUpdatedAt = DateTimeOffset.UtcNow }).IsValid);
        var list = new ListRequestValidator();
        Assert.False(list.Validate(new Providers.Request.ListRequest { PageSize = 101 }).IsValid);
        Assert.False(list.Validate(new Providers.Request.ListRequest { PageIndex = int.MaxValue }).IsValid);
        Assert.False(list.Validate(new Providers.Request.ListRequest { SupportedMode = "1" }).IsValid);
        Assert.False(list.Validate(new Providers.Request.ListRequest { SortBy = "taxCode:asc" }).IsValid);
    }

    [Fact]
    public async Task HttpContract_EnforcesJwtRolesRoutesPayloadsAndErrorCodes()
    {
        await using var db = TestSupport.CreateDb();
        var (actor, employee) = await TestSupport.AddLogisticsManagerAsync(db);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["JwtOptions:SecretKey"] = new string('x', 32), ["JwtOptions:Issuer"] = "uc41-tests",
            ["JwtOptions:Audience"] = "uc41-tests", ["JwtOptions:ExpirationMinutes"] = "15"
        });
        builder.Services.AddControllers().AddApplicationPart(typeof(TransportProvidersController).Assembly);
        builder.Services.AddJwtServices(builder.Configuration);
        builder.Services.AddSingleton(db);
        builder.Services.AddScoped<Providers.ITransportProviderService>(_ => CreateService(db));
        builder.Services.AddTransient<GlobalExceptionHandlerMiddleware>();
        await using var app = builder.Build();
        app.UseMiddleware<GlobalExceptionHandlerMiddleware>();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        await app.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
        const string path = "/api/v1/transport-providers";
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path)).StatusCode);
        void SignIn(string role) => client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            new JwtServices(builder.Configuration).GenerateAccessToken([new Claim(ClaimTypes.NameIdentifier, actor.AccountId.ToString()),
                new Claim(ClaimTypes.Role, "Employee"), new Claim(ClaimTypes.Role, role)]));
        SignIn("LogisticsManager");
        var created = await client.PostAsJsonAsync(path, CreateRequest());
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("isSuccess").GetBoolean());
        var value = body.GetProperty("value");
        var id = value.GetProperty("transportProviderId").GetGuid();
        var timestamp = value.GetProperty("updatedAt").GetDateTimeOffset();
        Assert.Equal(JsonValueKind.Array, value.GetProperty("supportedModes").ValueKind);
        Assert.EndsWith(id.ToString(), created.Headers.Location!.ToString());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"{path}/{id}")).StatusCode);
        var duplicate = await client.PostAsJsonAsync(path, CreateRequest());
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("PROVIDER_CODE_DUPLICATED", (await duplicate.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errorCode").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(path, new Providers.Request.CreateRequest())).StatusCode);
        var updated = await client.PutAsJsonAsync($"{path}/{id}", UpdateRequest(timestamp));
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        timestamp = (await updated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("value").GetProperty("updatedAt").GetDateTimeOffset();
        Assert.Equal(HttpStatusCode.OK, (await client.PatchAsJsonAsync($"{path}/{id}/status",
            new Providers.Request.StatusRequest { Status = "INACTIVE", Reason = "Contract ended", LastUpdatedAt = timestamp })).StatusCode);
        employee.Role = EmployeeRole.FLEET_ROUTE_COORDINATOR;
        await db.SaveChangesAsync();
        SignIn("FleetRouteCoordinator");
        var list = await client.GetFromJsonAsync<JsonElement>(path + "?status=INACTIVE");
        Assert.Equal(0, list.GetProperty("value").GetProperty("items").GetArrayLength());
        Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync($"{path}/{id}")).StatusCode);
        employee.Role = EmployeeRole.LOGISTICS_MANAGER;
        await db.SaveChangesAsync();
        SignIn("LogisticsManager");
        Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync($"{path}/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"{path}/{id}")).StatusCode);
        await app.StopAsync();
    }

    [PostgreSqlFact]
    public async Task PostgreSql_MigrationUniquenessAndConcurrentUpdates()
    {
        var schema = "uc41_" + Guid.NewGuid().ToString("N");
        var connection = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("PEGAXUS_TEST_CONNECTION_STRING")) { SearchPath = schema };
        await using var admin = new NpgsqlConnection(connection.ConnectionString);
        await admin.OpenAsync();
        await new NpgsqlCommand($"CREATE SCHEMA {schema}", admin).ExecuteNonQueryAsync();
        try
        {
            var sourceBuilder = new NpgsqlDataSourceBuilder(connection.ConnectionString);
            var translator = new EnumTranslator();
            foreach (var type in typeof(ProviderStatus).Assembly.GetTypes().Where(type => type.IsEnum && type.Namespace == typeof(ProviderStatus).Namespace))
                sourceBuilder.MapEnum(type, translator.TranslateTypeName(type.Name), translator);
            await using var source = sourceBuilder.Build();
            var options = new DbContextOptionsBuilder<AppDbContext>().UseTestNpgsql(source).Options;
            await using var db = new AppDbContext(options);
            await new NpgsqlCommand(db.Database.GenerateCreateScript(), admin).ExecuteNonQueryAsync();
            await db.Database.ExecuteSqlRawAsync("DROP INDEX \"TransportProvider_tax_code_key\"; DROP INDEX \"TransportProvider_provider_code_key\"; CREATE UNIQUE INDEX \"TransportProvider_provider_code_key\" ON \"TransportProvider\" (provider_code)");
            var generator = db.GetService<IMigrationsSqlGenerator>();
            foreach (var command in generator.Generate(new AddTransportProviderCatalogConstraints().UpOperations, db.Model))
                await db.Database.ExecuteSqlRawAsync(command.CommandText);
            foreach (var command in generator.Generate(new AddTransportProviderCatalogConstraints().DownOperations, db.Model))
                await db.Database.ExecuteSqlRawAsync(command.CommandText);
            foreach (var command in generator.Generate(new AddTransportProviderCatalogConstraints().UpOperations, db.Model))
                await db.Database.ExecuteSqlRawAsync(command.CommandText);
            var (actor, _) = await TestSupport.AddLogisticsManagerAsync(db);
            await TestSupport.AddLogisticsManagerAsync(db);
            var provider = await CreateService(db).CreateAsync(actor.AccountId, CreateRequest());
            Assert.Single((await CreateService(db).GetListAsync(actor.AccountId, new() { SupportedMode = "AIR" })).Items);
            await using var first = new AppDbContext(options);
            await using var second = new AppDbContext(options);
            async Task<bool> Update(AppDbContext context)
            {
                try { await CreateService(context).UpdateAsync(actor.AccountId, provider.TransportProviderId, UpdateRequest(provider.UpdatedAt)); return true; }
                catch (ConflictException error) { Assert.Equal("PROVIDER_MODIFIED", error.ErrorCode); return false; }
            }
            Assert.Single((await Task.WhenAll(Update(first), Update(second))).Where(success => success));
            var latest = await CreateService(db).GetAsync(actor.AccountId, provider.TransportProviderId);
            var statusRequest = new Providers.Request.StatusRequest { Status = "INACTIVE", Reason = "Contract ended", LastUpdatedAt = latest.UpdatedAt };
            await CreateService(db).UpdateStatusAsync(actor.AccountId, provider.TransportProviderId, statusRequest);
            Assert.Single(await db.Notifications.ToListAsync());
            Assert.Single(await db.OutboxMessages.ToListAsync());
            Assert.Equal("PROVIDER_MODIFIED", (await Assert.ThrowsAsync<ConflictException>(() =>
                CreateService(db).UpdateStatusAsync(actor.AccountId, provider.TransportProviderId, statusRequest))).ErrorCode);
            Assert.Single(await db.OutboxMessages.ToListAsync());
            latest = await CreateService(db).GetAsync(actor.AccountId, provider.TransportProviderId);
            await db.Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION reject_provider_audit() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'UC41 rollback check'; END; $$;
                CREATE TRIGGER reject_provider_audit BEFORE INSERT ON "CatalogAuditLog"
                FOR EACH ROW EXECUTE FUNCTION reject_provider_audit();
                """);
            await using (var failedDb = new AppDbContext(options))
                await Assert.ThrowsAsync<DbUpdateException>(() => CreateService(failedDb).UpdateStatusAsync(actor.AccountId, provider.TransportProviderId,
                    new() { Status = "ACTIVE", LastUpdatedAt = latest.UpdatedAt }));
            Assert.Equal("INACTIVE", (await CreateService(db).GetAsync(actor.AccountId, provider.TransportProviderId)).Status);
            Assert.Equal(3, await db.CatalogAuditLogs.CountAsync());
            Assert.Single(await db.Notifications.ToListAsync());
            Assert.Single(await db.OutboxMessages.ToListAsync());
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_provider_audit ON \"CatalogAuditLog\"; DROP FUNCTION reject_provider_audit()");
            await using var duplicateDb = new AppDbContext(options);
            duplicateDb.TransportProviders.Add(new TransportProvider
            {
                TransportProviderId = Guid.NewGuid(), ProviderCode = "OTHER", ProviderName = "Duplicate tax",
                TaxCode = provider.TaxCode, Status = ProviderStatus.ACTIVE,
                CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
                UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)
            });
            var duplicate = await Assert.ThrowsAsync<DbUpdateException>(() => duplicateDb.SaveChangesAsync());
            Assert.Equal("TransportProvider_tax_code_key", Assert.IsType<PostgresException>(duplicate.InnerException).ConstraintName);
            await CreateService(db).DeleteAsync(actor.AccountId, provider.TransportProviderId);
            await CreateService(db).CreateAsync(actor.AccountId, CreateRequest());
            Assert.Equal(2, await db.TransportProviders.CountAsync());
            // Verify the ordered LINQ update used to lock only the latest document version translates.
            Assert.Equal(0, await db.DocumentVersions.Where(item => item.ComplianceDocumentId == Guid.Empty)
                .OrderByDescending(item => item.VersionNo).Take(1)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.DocumentVersionId, item => item.DocumentVersionId)));
            await CheckOutboxClaimsAsync(options);
            await CheckRequirementReferenceRaceAsync(options, actor.AccountId);
        }
        finally { await new NpgsqlCommand($"DROP SCHEMA {schema} CASCADE", admin).ExecuteNonQueryAsync(); }
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

    private static Providers.Service CreateService(AppDbContext db) => new(db, new ListRequestValidator(), new CreateRequestValidator(),
        new UpdateRequestValidator(), new StatusRequestValidator(),
        new Pegaxus.Service.NotificationService.Service(db, new Pegaxus.Service.NotificationService.NoopNotificationRealtimeService()),
        NullLogger<Providers.Service>.Instance);

    private static Providers.Request.CreateRequest CreateRequest() => new()
    {
        ProviderCode = " prv-lufthansa-01 ", ProviderName = "Lufthansa Cargo", TaxCode = "DE123456789",
        SupportedModes = ["air", "ROAD", "AIR"], ContactName = "Operations", Phone = "+49-000-000",
        Email = "ops@example.com", Address = "Frankfurt", Country = "de", Note = "IATA carrier"
    };

    private static Providers.Request.UpdateRequest UpdateRequest(DateTimeOffset timestamp) => new()
    {
        ProviderName = "Lufthansa Cargo AG", TaxCode = "DE123456789", SupportedModes = ["AIR", "ROAD"], LastUpdatedAt = timestamp
    };

    private static void AddSegment(AppDbContext db, Guid providerId, RoutePlanStatus status) => db.RoutePlanRoutes.Add(new RoutePlanRoute
    {
        RoutePlanRouteId = Guid.NewGuid(), TransportProviderId = providerId, SequenceNo = 1,
        RoutePlan = new RoutePlan { RoutePlanId = Guid.NewGuid(), RoutePlanCode = "RP-TEST", Status = status },
        Route = new Route { RouteId = Guid.NewGuid(), RouteCode = "R-TEST", TransportMode = TransportMode.AIR }
    });

    private sealed class EnumTranslator : INpgsqlNameTranslator
    {
        public string TranslateTypeName(string name) => new NpgsqlSnakeCaseNameTranslator().TranslateTypeName(name);
        public string TranslateMemberName(string name) => name;
    }
}

public sealed class PostgreSqlFactAttribute : FactAttribute
{
    public PostgreSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PEGAXUS_TEST_CONNECTION_STRING")))
            Skip = "Set PEGAXUS_TEST_CONNECTION_STRING to run the isolated PostgreSQL schema check.";
    }
}
