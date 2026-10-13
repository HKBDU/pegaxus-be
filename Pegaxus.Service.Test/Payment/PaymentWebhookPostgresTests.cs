using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Npgsql.NameTranslation;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.OutboxService;

namespace Pegaxus.Service.Test;

public class PaymentWebhookPostgresTests : IAsyncLifetime
{
    private NpgsqlDataSource? source;
    public async Task InitializeAsync()
    {
        var connection = Environment.GetEnvironmentVariable("PEGAXUS_TEST_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var settings = new NpgsqlConnectionStringBuilder(connection) { Database = $"pegaxus_payment_test_{Guid.NewGuid():N}" };
        var builder = new NpgsqlDataSourceBuilder(settings.ConnectionString);
        var translator = new EnumTranslator();
        foreach (var type in typeof(AccountStatus).Assembly.GetTypes().Where(x => x.IsEnum && x.Namespace == typeof(AccountStatus).Namespace))
            builder.MapEnum(type, translator.TranslateTypeName(type.Name), translator);
        source = builder.Build();
        await using var db = CreateDb();
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        if (source is null) return;
        try { await using var db = CreateDb(); await db.Database.EnsureDeletedAsync(); }
        finally { await source.DisposeAsync(); }
    }

    [PostgresFact]
    public async Task ConcurrentPayments_ConfirmOnce_ReplayAfterConflict_AndOutboxIsDurable()
    {
        Receipt receipt;
        await using (var db = CreateDb()) receipt = await PaymentWebhookTests.SeedAsync(db);
        await Race(receipt, receipt, expectedConfirmed: 1);
        await using var verify = CreateDb();
        Assert.Equal(InvoiceStatus.PAID, (await verify.Invoices.SingleAsync()).Status);
        Assert.Equal(TransportPlanStatus.APPROVED, (await verify.TransportPlans.SingleAsync()).Status);
        Assert.Single(await verify.InvoiceStatusHistories.ToListAsync());
        Assert.Single(await verify.Notifications.ToListAsync()); Assert.Single(await verify.AccountNotifications.ToListAsync());
        Assert.Single(await verify.OutboxMessages.Where(x => x.MessageType == MessageTypes.Email).ToListAsync());
        Assert.Single(await verify.OutboxMessages.Where(x => x.MessageType == MessageTypes.SignalRAccount).ToListAsync());
    }

    [PostgresFact]
    public async Task DifferentReceiptsForSameInvoice_CannotBothConfirm()
    {
        Receipt first, second;
        await using (var db = CreateDb())
        {
            first = await PaymentWebhookTests.SeedAsync(db);
            second = new Receipt
            {
                ReceiptId = Guid.NewGuid(), ReceiptCode = "RCP-" + Guid.NewGuid().ToString("N"), InvoiceId = first.InvoiceId,
                Amount = first.Amount, Currency = first.Currency, PaymentMethod = PaymentMethod.BANK_TRANSFER,
                Status = ReceiptStatus.PENDING, CreatedAt = PaymentWebhookTests.Now, UpdatedAt = PaymentWebhookTests.Now
            };
            db.Receipts.Add(second); await db.SaveChangesAsync();
        }
        await Race(first, second, expectedConfirmed: 1);
    }

    [PostgresFact]
    public async Task OutboxWriteFailure_RollsBackReceiptInvoiceHistoryPlanAndNotifications()
    {
        Receipt receipt;
        await using (var db = CreateDb()) receipt = await PaymentWebhookTests.SeedAsync(db);
        await using (var connection = await source!.OpenConnectionAsync())
        await using (var command = new NpgsqlCommand("""
            CREATE FUNCTION reject_payment_outbox() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'test outbox failure'; END $$;
            CREATE TRIGGER reject_payment_outbox BEFORE INSERT ON "OutboxMessage"
            FOR EACH ROW EXECUTE FUNCTION reject_payment_outbox();
            """, connection))
            await command.ExecuteNonQueryAsync();
        await using (var db = CreateDb())
            await Assert.ThrowsAsync<DbUpdateException>(() => PaymentWebhookTests.CreateService(db)
                .HandleWebhookAsync(PaymentWebhookTests.Payload(receipt), PaymentWebhookTests.Secret));
        await using var verify = CreateDb();
        var unchanged = await verify.Receipts.SingleAsync();
        Assert.Equal(ReceiptStatus.PENDING, unchanged.Status); Assert.Null(unchanged.TransactionReference);
        Assert.Null(unchanged.PaidAt); Assert.Null(unchanged.ConfirmedAt);
        Assert.Equal(InvoiceStatus.ISSUED, (await verify.Invoices.SingleAsync()).Status);
        Assert.Null((await verify.Invoices.SingleAsync()).PaidAt);
        Assert.Equal(TransportPlanStatus.PENDING, (await verify.TransportPlans.SingleAsync()).Status);
        Assert.Empty(await verify.InvoiceStatusHistories.ToListAsync()); Assert.Empty(await verify.Notifications.ToListAsync());
        Assert.Empty(await verify.AccountNotifications.ToListAsync()); Assert.Empty(await verify.OutboxMessages.ToListAsync());
    }

    private async Task Race(Receipt firstReceipt, Receipt secondReceipt, int expectedConfirmed)
    {
        var gate = new ReceiptReadGate();
        await using var first = CreateDb(gate);
        await using var second = CreateDb(gate);
        var receipts = new[] { firstReceipt, secondReceipt };
        var outcomes = await Task.WhenAll(new[] { first, second }.Select((db, i) => Attempt(() =>
            PaymentWebhookTests.CreateService(db).HandleWebhookAsync(PaymentWebhookTests.Payload(receipts[i]), PaymentWebhookTests.Secret))));
        foreach (var error in outcomes.Where(x => x is not null))
            Assert.Equal(PostgresErrorCodes.SerializationFailure, FindPostgres(error!).SqlState);
        foreach (var receipt in receipts)
        {
            await using var retry = CreateDb();
            await PaymentWebhookTests.CreateService(retry).HandleWebhookAsync(PaymentWebhookTests.Payload(receipt), PaymentWebhookTests.Secret);
        }
        await using var verify = CreateDb();
        Assert.Equal(expectedConfirmed, await verify.Receipts.CountAsync(x => x.Status == ReceiptStatus.CONFIRMED));
        Assert.Equal(expectedConfirmed, await verify.InvoiceStatusHistories.CountAsync());
        Assert.Equal(expectedConfirmed, await verify.Notifications.CountAsync());
        Assert.Equal(expectedConfirmed, await verify.OutboxMessages.CountAsync(x => x.MessageType == MessageTypes.Email));
    }

    private AppDbContext CreateDb(DbCommandInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>().UseTestNpgsql(source!);
        if (interceptor is not null) builder.AddInterceptors(interceptor);
        return new(builder.Options);
    }

    private static async Task<Exception?> Attempt(Func<Task> action)
    { try { await action(); return null; } catch (Exception error) { return error; } }
    private static PostgresException FindPostgres(Exception error) =>
        error as PostgresException ?? (error.InnerException is not null ? FindPostgres(error.InnerException) : throw error);
    private sealed class EnumTranslator : INpgsqlNameTranslator
    {
        public string TranslateTypeName(string name) => new NpgsqlSnakeCaseNameTranslator().TranslateTypeName(name);
        public string TranslateMemberName(string name) => name;
    }

    private sealed class ReceiptReadGate : DbCommandInterceptor
    {
        private int reads;
        private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("SELECT r.invoice_id", StringComparison.Ordinal))
            {
                if (Interlocked.Increment(ref reads) == 2) ready.TrySetResult();
                await ready.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            }
            return result;
        }
    }
}
