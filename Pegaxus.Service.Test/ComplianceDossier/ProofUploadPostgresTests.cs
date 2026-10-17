using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Npgsql.NameTranslation;
using Pegaxus.Repository;
using Pegaxus.Repository.Enum;
using Pegaxus.Repository.Extensions;
using Pegaxus.Service.Exceptions;

using DossierService = Pegaxus.Service.ComplianceDossierService.Service;

namespace Pegaxus.Service.Test;

[Collection("PostgresSharedDatabase")]
public class ProofUploadPostgresTests
{
    [PostgresFact]
    public async Task Submit_ReloadsDossier_AndCommitsOrRollsBackEvidenceAndOutboxTogether()
    {
        foreach (var customs in new[] { false, true })
        {
            await WithDatabaseAsync(async options =>
            {
                await using var seed = new AppDbContext(options);
                var (accountId, dossierId) = await ProofUploadTests.SeedAsync(seed);
                var (_, specialist) = await TestSupport.AddLogisticsManagerAsync(seed);
                specialist.Role = EmployeeRole.TRANSPORT_SPECIALIST;
                await seed.SaveChangesAsync();

                await using (var stale = new AppDbContext(options))
                {
                    await stale.ComplianceDossiers.SingleAsync(item => item.ComplianceDossierId == dossierId);
                    await seed.ComplianceDossiers.Where(item => item.ComplianceDossierId == dossierId)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Status, ComplianceDossierStatus.CLOSED));
                    var storage = new DocumentUploadTests.RecordingStorage();
                    var error = await Assert.ThrowsAsync<ConflictException>(() => SubmitAsync(
                        DocumentUploadTests.CreateService(stale, storage), customs, accountId, dossierId));
                    Assert.Equal("DOSSIER_NOT_APPROVED", error.ErrorCode);
                    Assert.Empty(storage.Uploaded);
                }
                await seed.ComplianceDossiers.Where(item => item.ComplianceDossierId == dossierId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Status, ComplianceDossierStatus.APPROVED));

                var retained = new DocumentUploadTests.RecordingStorage();
                var failingOptions = new DbContextOptionsBuilder<AppDbContext>(options).AddInterceptors(new FailAfterSave()).Options;
                await using (var writer = new AppDbContext(failingOptions))
                    await Assert.ThrowsAsync<IOException>(() => SubmitAsync(
                        DocumentUploadTests.CreateService(writer, retained), customs, accountId, dossierId));
                Assert.Single(retained.Uploaded);
                Assert.Empty(retained.Deleted);
                await using (var verify = new AppDbContext(options))
                {
                    Assert.Equal(1, await verify.QuarantineProofs.CountAsync());
                    Assert.Empty(await verify.CustomsClearanceProofs.ToListAsync());
                    Assert.Empty(await verify.ProofAttachments.ToListAsync());
                    Assert.Empty(await verify.AuditDossiers.ToListAsync());
                    Assert.Empty(await verify.Notifications.ToListAsync());
                    Assert.Empty(await verify.AccountNotifications.ToListAsync());
                    Assert.Empty(await verify.OutboxMessages.ToListAsync());
                }

                Guid proofId;
                await using (var writer = new AppDbContext(options))
                    proofId = await SubmitAsync(DocumentUploadTests.CreateService(writer, retained), customs, accountId, dossierId);
                await using (var verify = new AppDbContext(options))
                {
                    var attachment = await verify.ProofAttachments.SingleAsync();
                    Assert.Equal(proofId, customs ? attachment.CustomsClearanceProofId : attachment.QuarantineProofId);
                    Assert.Equal(proofId, (await verify.AuditDossiers.SingleAsync()).TargetId);
                    Assert.Single(await verify.Notifications.ToListAsync());
                    Assert.Single(await verify.AccountNotifications.ToListAsync());
                    Assert.Equal(2, await verify.OutboxMessages.CountAsync());
                    Assert.Equal(ComplianceDossierStatus.APPROVED, (await verify.ComplianceDossiers.SingleAsync()).Status);
                }
            });
        }
    }

    [PostgresFact]
    public async Task Submit_ConcurrentDossierClosureRejectsEvidenceWithoutConfirmingAssets()
    {
        foreach (var customs in new[] { false, true })
        {
            await WithDatabaseAsync(async options =>
            {
                await using var seed = new AppDbContext(options);
                var (accountId, dossierId) = await ProofUploadTests.SeedAsync(seed);
                await using var closing = new AppDbContext(options);
                await using var transaction = await closing.Database.BeginSerializableAsync();
                await closing.LockRowsAsync(closing.ComplianceDossiers.Where(item => item.ComplianceDossierId == dossierId),
                    item => item.ComplianceDossierId);
                await closing.ComplianceDossiers.Where(item => item.ComplianceDossierId == dossierId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Status, ComplianceDossierStatus.CLOSED));

                var locking = new CompetingLock();
                var competingOptions = new DbContextOptionsBuilder<AppDbContext>(options).AddInterceptors(locking).Options;
                await using var submitting = new AppDbContext(competingOptions);
                var storage = new DocumentUploadTests.RecordingStorage();
                var competitor = Record.ExceptionAsync(() => SubmitAsync(
                    DocumentUploadTests.CreateService(submitting, storage), customs, accountId, dossierId));
                try
                {
                    await locking.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
                    await transaction.CommitAsync();
                    var error = await competitor;
                    Assert.NotNull(error);
                    Assert.True(PostgresErrors.IsWriteConflict(error), error.ToString());
                }
                finally
                {
                    await transaction.DisposeAsync();
                    await competitor;
                }
                Assert.Empty(storage.Uploaded);
                Assert.Empty(storage.Deleted);
                await using var verify = new AppDbContext(options);
                Assert.Equal(ComplianceDossierStatus.CLOSED, (await verify.ComplianceDossiers.SingleAsync()).Status);
                Assert.Equal(1, await verify.QuarantineProofs.CountAsync());
                Assert.Empty(await verify.CustomsClearanceProofs.ToListAsync());
                Assert.Empty(await verify.ProofAttachments.ToListAsync());
                Assert.Empty(await verify.AuditDossiers.ToListAsync());
                Assert.Empty(await verify.OutboxMessages.ToListAsync());
            });
        }
    }

    private static Task<Guid> SubmitAsync(DossierService service, bool customs, Guid accountId, Guid dossierId) => customs
        ? service.SubmitCustomsClearanceProofAsync(accountId, dossierId, new()
        {
            CustomsPortName = "Port", IsHeld = true, QuickNote = "Inspection required",
            Files = [DocumentUploadTests.File("stamp.png", "image/png")]
        })
        : service.SubmitQuarantineProofAsync(accountId, dossierId, new()
        {
            BcpName = "BCP", IsHoldback = true, QuickNote = "Inspection required",
            Files = [DocumentUploadTests.File("stamp.png", "image/png")]
        });

    private static async Task WithDatabaseAsync(Func<DbContextOptions<AppDbContext>, Task> test)
    {
        var settings = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("PEGAXUS_TEST_CONNECTION_STRING"))
            { Database = "pegaxus_proof_test_" + Guid.NewGuid().ToString("N") };
        var builder = new NpgsqlDataSourceBuilder(settings.ConnectionString);
        var translator = new EnumTranslator();
        foreach (var type in typeof(ComplianceDossierStatus).Assembly.GetTypes()
                     .Where(type => type.IsEnum && type.Namespace == typeof(ComplianceDossierStatus).Namespace))
            builder.MapEnum(type, translator.TranslateTypeName(type.Name), translator);
        await using var source = builder.Build();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseTestNpgsql(source).Options;
        await using var db = new AppDbContext(options);
        try { await db.Database.EnsureCreatedAsync(); await test(options); }
        finally { await db.Database.EnsureDeletedAsync(); }
    }

    private sealed class EnumTranslator : INpgsqlNameTranslator
    {
        public string TranslateTypeName(string name) => new NpgsqlSnakeCaseNameTranslator().TranslateTypeName(name);
        public string TranslateMemberName(string name) => name;
    }

    private sealed class FailAfterSave : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            var db = Assert.IsType<AppDbContext>(eventData.Context);
            var transaction = db.Database.CurrentTransaction;
            Assert.NotNull(transaction);
            Assert.Equal(IsolationLevel.Serializable, transaction.GetDbTransaction().IsolationLevel);
            throw new IOException("Planned failure after database writes");
        }
    }

    private sealed class CompetingLock : DbCommandInterceptor
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("UPDATE \"ComplianceDossier\"", StringComparison.Ordinal))
            {
                // Pin the snapshot before releasing the competing dossier lock.
                var db = Assert.IsType<AppDbContext>(eventData.Context);
                await db.ComplianceDossiers.AnyAsync(cancellationToken);
                Entered.TrySetResult();
            }
            return result;
        }
    }
}
