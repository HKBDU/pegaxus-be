using System.Collections.Concurrent;
using System.Data.Common;
using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Npgsql.NameTranslation;
using Pegaxus.API.Middleware;
using Pegaxus.API.Services;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.ComplianceRequirementService.Validator;
using Pegaxus.Service.MailService;
using Pegaxus.Service.OutboxService;

using CatalogService = Pegaxus.Service.ComplianceRequirementService.Service;
using TemplateService = Pegaxus.Service.DossierTemplateService.Service;
using TemplateRequest = Pegaxus.Service.DossierTemplateService.Request;

namespace Pegaxus.Service.Test;

public class LinqConflictResponseTests
{
    [Theory]
    [InlineData(PostgresErrorCodes.SerializationFailure, 0)]
    [InlineData(PostgresErrorCodes.SerializationFailure, 1)]
    [InlineData(PostgresErrorCodes.SerializationFailure, 2)]
    [InlineData(PostgresErrorCodes.SerializationFailure, 3)]
    [InlineData(PostgresErrorCodes.DeadlockDetected, 0)]
    [InlineData(PostgresErrorCodes.DeadlockDetected, 1)]
    [InlineData(PostgresErrorCodes.DeadlockDetected, 2)]
    [InlineData(PostgresErrorCodes.DeadlockDetected, 3)]
    public async Task DatabaseConflict_Returns409WithoutDatabaseDetails(string sqlState, int wrapping)
    {
        var error = new PostgresException("private database details", "ERROR", "ERROR", sqlState);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var middleware = new GlobalExceptionHandlerMiddleware(NullLogger<GlobalExceptionHandlerMiddleware>.Instance);

        Exception failure = wrapping switch
        {
            1 => new DbUpdateException("private update details", error),
            2 => new InvalidOperationException("private execution details", new DbUpdateException("private update details", error)),
            3 => new InvalidOperationException("private execution details", error),
            _ => error
        };
        await middleware.InvokeAsync(context, _ => Task.FromException(failure));

        Assert.Equal(409, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.Contains("CONCURRENT_UPDATE", body);
        Assert.Contains("Reload", body);
        Assert.DoesNotContain("private", body);
    }
}

public class LinqConcurrencyTests : IAsyncLifetime
{
    private NpgsqlDataSource? _source;

    public async Task InitializeAsync()
    {
        var connection = Environment.GetEnvironmentVariable("PEGAXUS_TEST_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connection)) return;
        // Never create or delete the database named in the supplied connection string.
        var settings = new NpgsqlConnectionStringBuilder(connection)
        {
            Database = $"pegaxus_linq_test_{Guid.NewGuid():N}"
        };
        var builder = new NpgsqlDataSourceBuilder(settings.ConnectionString);
        var translator = new EnumNameTranslator();
        foreach (var type in typeof(AccountStatus).Assembly.GetTypes()
                     .Where(type => type.IsEnum && type.Namespace == typeof(AccountStatus).Namespace))
            builder.MapEnum(type, translator.TranslateTypeName(type.Name), translator);
        _source = builder.Build();
        await using var db = CreateDb();
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        if (_source is null) return;
        try
        {
            await using var db = CreateDb();
            await db.Database.EnsureDeletedAsync();
        }
        finally { await _source.DisposeAsync(); }
    }

    [PostgresFact]
    public async Task CompetingOutboxWorkers_SendEligibleMessagesOnce()
    {
        await using var db = CreateDb();
        var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        for (var index = 0; index < 30; index++)
            OutboxQueue.AddEmail(db, new MailContent { To = $"{index}@example.com", Subject = "Test", Body = "Test" }, now);
        OutboxQueue.AddEmail(db, new MailContent { To = "stale@example.com", Subject = "Test", Body = "Test" }, now);
        var stale = db.OutboxMessages.Local.Last();
        stale.Status = "PROCESSING";
        stale.LockedAt = now.AddMinutes(-11);
        OutboxQueue.AddEmail(db, new MailContent { To = "future@example.com", Subject = "Test", Body = "Test" }, now.AddHours(1));
        OutboxQueue.AddEmail(db, new MailContent { To = "locked@example.com", Subject = "Test", Body = "Test" }, now);
        var locked = db.OutboxMessages.Local.Last();
        locked.Status = "PROCESSING";
        locked.LockedAt = now;
        OutboxQueue.AddEmail(db, new MailContent { To = "dead@example.com", Subject = "Test", Body = "Test" }, now);
        db.OutboxMessages.Local.Last().Status = "DEAD";
        await db.SaveChangesAsync();

        var mail = new RecordingMail();
        await using var services = new ServiceCollection()
            .AddScoped(_ => CreateDb())
            .AddSingleton<Pegaxus.Service.MailService.IService>(mail)
            .BuildServiceProvider();
        using var first = new TransactionalOutboxWorker(services.GetRequiredService<IServiceScopeFactory>(), NullLogger<TransactionalOutboxWorker>.Instance);
        using var second = new TransactionalOutboxWorker(services.GetRequiredService<IServiceScopeFactory>(), NullLogger<TransactionalOutboxWorker>.Instance);
        var batch = typeof(TransactionalOutboxWorker).GetMethod("ProcessBatchAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        for (var round = 0; round < 3; round++)
            await Task.WhenAll((Task)batch.Invoke(first, [CancellationToken.None])!, (Task)batch.Invoke(second, [CancellationToken.None])!);

        Assert.Equal(31, mail.Deliveries.Count);
        Assert.All(mail.Deliveries.Values, count => Assert.Equal(1, count));
        Assert.Contains("stale@example.com", mail.Deliveries.Keys);
        Assert.Equal(31, await db.OutboxMessages.CountAsync(message => message.Status == "PROCESSED"));
        Assert.Equal(3, await db.OutboxMessages.CountAsync(message => message.Status != "PROCESSED"));
    }

    [PostgresFact]
    public async Task DocumentReplacement_UpdatesLatestVersionAndMetadataUsingLinq()
    {
        await using var db = CreateDb();
        var (account, dossier, requirement) = await DocumentUploadTests.SeedAsync(db);
        var service = DocumentUploadTests.CreateService(db, new DocumentUploadTests.RecordingStorage());
        var original = await service.UploadDocumentAsync(account.AccountId, dossier.ComplianceDossierId,
            requirement.DossierRequirementId, DocumentUploadTests.ValidRequest(dossier));
        var replacement = DocumentUploadTests.ValidRequest(dossier);
        replacement.Files = [DocumentUploadTests.File("replacement.png", "image/png")];
        var result = await service.SubmitDocumentVersionAsync(account.AccountId, dossier.ComplianceDossierId,
            requirement.DossierRequirementId, replacement);
        Assert.Equal(original.DocumentVersionId, result.DocumentVersionId);
        Assert.False(result.IsNewVersion);
        Assert.Equal("replacement.png", (await db.DocumentVersions.SingleAsync()).FileName);
        await service.UpdateDocumentMetadataAsync(account.AccountId, dossier.ComplianceDossierId,
            requirement.DossierRequirementId, new() { DocumentNumber = "NEW-REFERENCE" });
        Assert.Equal("NEW-REFERENCE", (await db.ComplianceDocuments.SingleAsync()).DocumentNumber);
    }

    [PostgresFact]
    public async Task CatalogDeletion_RacingWithTemplateCreation_CannotLeaveDeletedReference()
    {
        await using var seed = CreateDb();
        var (actor, _) = await TestSupport.AddLogisticsManagerAsync(seed);
        var requirement = new ComplianceRequirement
        {
            ComplianceRequirementId = Guid.NewGuid(), RequirementCode = "CR-RACE", RequirementName = "Race test",
            Status = ComplianceRequirementStatus.ACTIVE, Category = ComplianceRequirementCategory.Quarantine
        };
        seed.ComplianceRequirements.Add(requirement);
        await seed.SaveChangesAsync();
        var gate = new ReferenceReadGate();
        await using var writerDb = CreateDb(gate);
        var creation = new TemplateService(writerDb).CreateAsync(new TemplateRequest.SaveRequest
        {
            TemplateCode = "TPL-RACE", TemplateName = "Race test", Status = "ACTIVE",
            Items = [new() { ComplianceRequirementId = requirement.ComplianceRequirementId, SequenceNo = 1 }]
        });
        try
        {
            await gate.Read.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await using var deletionDb = CreateDb();
            var catalog = new CatalogService(deletionDb, new ListRequestValidator(), new SaveRequestValidator(), new StatusRequestValidator());
            await catalog.DeleteAsync(requirement.ComplianceRequirementId, actor.AccountId).WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally { gate.Resume.TrySetResult(); }
        var error = await Record.ExceptionAsync(async () => await creation);
        Assert.IsType<PostgresException>(error?.GetBaseException());
        Assert.Equal(PostgresErrorCodes.SerializationFailure, ((PostgresException)error!.GetBaseException()).SqlState);
        await using var verification = CreateDb();
        Assert.True((await verification.ComplianceRequirements.SingleAsync()).IsDeleted);
        Assert.Empty(await verification.DossierTemplateItems.ToListAsync());
        Assert.Empty(await verification.DossierTemplates.ToListAsync());
    }

    private AppDbContext CreateDb(params IInterceptor[] interceptors) => new(
        new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_source!)
            .AddInterceptors(interceptors).Options);

    private sealed class EnumNameTranslator : INpgsqlNameTranslator
    {
        private readonly NpgsqlSnakeCaseNameTranslator _snakeCase = new();
        public string TranslateTypeName(string name) => _snakeCase.TranslateTypeName(name);
        public string TranslateMemberName(string name) => name;
    }

    private sealed class RecordingMail : Pegaxus.Service.MailService.IService
    {
        public ConcurrentDictionary<string, int> Deliveries { get; } = new();
        public Task SendMail(MailContent content, CancellationToken cancellationToken = default)
        {
            Deliveries.AddOrUpdate(content.To, 1, (_, count) => count + 1);
            return Task.CompletedTask;
        }
    }

    private sealed class ReferenceReadGate : DbCommandInterceptor
    {
        public TaskCompletionSource Read { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("count(*)", StringComparison.OrdinalIgnoreCase) &&
                command.CommandText.Contains("\"ComplianceRequirement\"", StringComparison.Ordinal))
            {
                Read.TrySetResult();
                await Resume.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }
}

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PEGAXUS_TEST_CONNECTION_STRING")))
            Skip = "Set PEGAXUS_TEST_CONNECTION_STRING to a PostgreSQL server allowing disposable test databases.";
    }
}
