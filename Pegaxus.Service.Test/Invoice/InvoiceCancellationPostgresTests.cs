using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Npgsql.NameTranslation;
using Pegaxus.Repository;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.OutboxService;

namespace Pegaxus.Service.Test;

public class InvoiceCancellationPostgresTests
{
    [PostgresFact]
    public async Task Postgres_DoubleCancellationFreshPaymentStateAndFailures_PreserveAtomicity()
    {
        var settings = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("PEGAXUS_TEST_CONNECTION_STRING"))
        { Database = "pegaxus_iv9_test_" + Guid.NewGuid().ToString("N") };
        var builder = new NpgsqlDataSourceBuilder(settings.ConnectionString); var translator = new EnumTranslator();
        foreach (var type in typeof(InvoiceStatus).Assembly.GetTypes().Where(x => x.IsEnum && x.Namespace == typeof(InvoiceStatus).Namespace))
            builder.MapEnum(type, translator.TranslateTypeName(type.Name), translator);
        await using var source = builder.Build();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(source).Options;
        await using var seed = new AppDbContext(options);
        try
        {
            await seed.Database.EnsureCreatedAsync();
            var (actor, customer, invoice, _) = await InvoiceCancellationTests.SeedAsync(seed);
            await using (var first = new AppDbContext(options))
            await using (var second = new AppDbContext(options))
            {
                await first.Invoices.SingleAsync(x => x.InvoiceId == invoice.InvoiceId);
                await second.Invoices.SingleAsync(x => x.InvoiceId == invoice.InvoiceId);
                var errors = await Task.WhenAll(
                    Attempt(() => InvoiceCancellationTests.CreateService(first).CancelAsync(invoice.InvoiceId, InvoiceCancellationTests.ValidRequest(), actor.AccountId)),
                    Attempt(() => InvoiceCancellationTests.CreateService(second).CancelAsync(invoice.InvoiceId, InvoiceCancellationTests.ValidRequest(), actor.AccountId)));
                Assert.Single(errors, x => x is null); var failure = Assert.Single(errors, x => x is not null);
                Assert.True(failure is ConflictException, failure?.ToString());
                Assert.Contains(((ConflictException)failure!).ErrorCode, new[] { "INVALID_INVOICE_STATUS", "CONCURRENT_UPDATE" });
            }
            await using (var verify = new AppDbContext(options))
            {
                var cancelled = await verify.Invoices.SingleAsync(x => x.InvoiceId == invoice.InvoiceId);
                Assert.Equal(InvoiceStatus.CANCELLED, cancelled.Status);
                Assert.Equal(InvoiceCancellationTests.TestClock.GetUtcNow().UtcDateTime.Ticks, cancelled.CancelledAt!.Value.Ticks);
                Assert.Equal(InvoiceCancellationTests.ValidRequest().CancellationReason.Trim(), cancelled.CancellationReason);
                Assert.Single(await verify.InvoiceStatusHistories.ToListAsync()); Assert.Single(await verify.Notifications.ToListAsync());
                Assert.Equal(customer.AccountId, (await verify.AccountNotifications.SingleAsync()).AccountId);
                var outbox = await verify.OutboxMessages.ToListAsync(); Assert.Equal(2, outbox.Count);
                Assert.Equal(customer.Email, OutboxQueue.Deserialize<EmailPayload>(outbox.Single(x => x.MessageType == MessageTypes.Email).Payload).To);
                Assert.Equal(customer.AccountId, OutboxQueue.Deserialize<SignalRAccountPayload>(outbox.Single(x => x.MessageType == MessageTypes.SignalRAccount).Payload).AccountId);
                var details = await InvoiceCancellationTests.CreateService(verify).GetAsync(customer.AccountId, invoice.InvoiceId);
                Assert.Equal("CANCELLED", details.Status);
                Assert.Equal(cancelled.CancellationReason, details.CancellationReason);
                Assert.Empty(details.AvailableActions);
            }

            var fresh = await InvoiceCancellationTests.SeedAsync(seed);
            var receipt = InvoiceCancellationTests.AddReceipt(seed, fresh.Invoice, ReceiptStatus.FAILED); await seed.SaveChangesAsync();
            await using (var reused = new AppDbContext(options))
            {
                await reused.Invoices.Include(x => x.Receipts).SingleAsync(x => x.InvoiceId == fresh.Invoice.InvoiceId);
                await using (var writer = new AppDbContext(options))
                    await writer.Receipts.Where(x => x.ReceiptId == receipt.ReceiptId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, ReceiptStatus.CONFIRMED));
                var confirmed = await Assert.ThrowsAsync<UnprocessableEntityException>(() => InvoiceCancellationTests.CreateService(reused)
                    .CancelAsync(fresh.Invoice.InvoiceId, InvoiceCancellationTests.ValidRequest(), fresh.Actor.AccountId));
                Assert.Equal("INVOICE_HAS_PAYMENT", confirmed.ErrorCode);
                await using (var writer = new AppDbContext(options))
                    await writer.Receipts.Where(x => x.ReceiptId == receipt.ReceiptId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, ReceiptStatus.PENDING));
                var pending = await Assert.ThrowsAsync<ConflictException>(() => InvoiceCancellationTests.CreateService(reused)
                    .CancelAsync(fresh.Invoice.InvoiceId, InvoiceCancellationTests.ValidRequest(), fresh.Actor.AccountId));
                Assert.Equal("PAYMENT_IN_PROGRESS", pending.ErrorCode);
                await using (var writer = new AppDbContext(options))
                {
                    await writer.Receipts.Where(x => x.ReceiptId == receipt.ReceiptId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, ReceiptStatus.FAILED));
                    await writer.Invoices.Where(x => x.InvoiceId == fresh.Invoice.InvoiceId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, InvoiceStatus.PAID));
                }
                Assert.Equal("INVOICE_HAS_PAYMENT", (await Assert.ThrowsAsync<UnprocessableEntityException>(() => InvoiceCancellationTests.CreateService(reused)
                    .CancelAsync(fresh.Invoice.InvoiceId, InvoiceCancellationTests.ValidRequest(), fresh.Actor.AccountId))).ErrorCode);
            }

            var race = await InvoiceCancellationTests.SeedAsync(seed);
            await using (var payer = new AppDbContext(options))
            await using (var canceller = new AppDbContext(options))
            {
                await canceller.Invoices.SingleAsync(x => x.InvoiceId == race.Invoice.InvoiceId);
                await using var payment = await payer.Database.BeginTransactionAsync(IsolationLevel.Serializable);
                await payer.Invoices.Where(x => x.InvoiceId == race.Invoice.InvoiceId)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.InvoiceId, x => x.InvoiceId));
                var paid = await payer.Invoices.SingleAsync(x => x.InvoiceId == race.Invoice.InvoiceId);
                paid.Status = InvoiceStatus.PAID;
                InvoiceCancellationTests.AddReceipt(payer, paid, ReceiptStatus.CONFIRMED); await payer.SaveChangesAsync();
                var cancel = Attempt(() => InvoiceCancellationTests.CreateService(canceller)
                    .CancelAsync(race.Invoice.InvoiceId, InvoiceCancellationTests.ValidRequest(), race.Actor.AccountId));
                await payment.CommitAsync();
                var error = await cancel;
                Assert.True(error is ConflictException or UnprocessableEntityException, error?.ToString());
                Assert.Contains(((Pegaxus.Service.Exceptions.Exceptions)error!).ErrorCode, new[] { "CONCURRENT_UPDATE", "INVOICE_HAS_PAYMENT" });
            }

            var rollback = await InvoiceCancellationTests.SeedAsync(seed);
            await seed.Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION reject_cancel_email() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN IF NEW.message_type = 'EMAIL' THEN RAISE EXCEPTION 'test email failure'; END IF; RETURN NEW; END $$;
                CREATE TRIGGER reject_cancel_email BEFORE INSERT ON "OutboxMessage"
                FOR EACH ROW EXECUTE FUNCTION reject_cancel_email();
                """);
            await using (var failing = new AppDbContext(options))
                await Assert.ThrowsAsync<DbUpdateException>(() => InvoiceCancellationTests.CreateService(failing)
                    .CancelAsync(rollback.Invoice.InvoiceId, InvoiceCancellationTests.ValidRequest(), rollback.Actor.AccountId));
            var draft = await InvoiceCancellationTests.SeedAsync(seed, status: InvoiceStatus.DRAFT);
            await seed.Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION reject_cancel_history() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'test history failure'; END $$;
                CREATE TRIGGER reject_cancel_history BEFORE INSERT ON "InvoiceStatusHistory"
                FOR EACH ROW EXECUTE FUNCTION reject_cancel_history();
                """);
            await using (var failing = new AppDbContext(options))
                await Assert.ThrowsAsync<DbUpdateException>(() => InvoiceCancellationTests.CreateService(failing)
                    .CancelAsync(draft.Invoice.InvoiceId, InvoiceCancellationTests.ValidRequest(), draft.Actor.AccountId));
            await using (var verify = new AppDbContext(options))
            {
                foreach (var before in new[] { rollback.Invoice, draft.Invoice })
                {
                    var after = await verify.Invoices.SingleAsync(x => x.InvoiceId == before.InvoiceId);
                    Assert.Equal(before.Status, after.Status); Assert.Null(after.CancelledAt); Assert.Null(after.CancellationReason);
                    Assert.Equal(before.UpdatedAt, after.UpdatedAt); Assert.Equal(before.IssuedAt, after.IssuedAt);
                }
                Assert.Equal(InvoiceStatus.PAID, (await verify.Invoices.SingleAsync(x => x.InvoiceId == race.Invoice.InvoiceId)).Status);
                Assert.Single(await verify.InvoiceStatusHistories.ToListAsync()); Assert.Single(await verify.Notifications.ToListAsync());
                Assert.Single(await verify.AccountNotifications.ToListAsync()); Assert.Equal(2, await verify.OutboxMessages.CountAsync());
            }
        }
        finally { await seed.Database.EnsureDeletedAsync(); }
    }
    private static async Task<Exception?> Attempt(Func<Task> action)
    { try { await action(); return null; } catch (Exception error) { return error; } }
    private sealed class EnumTranslator : INpgsqlNameTranslator
    {
        public string TranslateTypeName(string name) => new NpgsqlSnakeCaseNameTranslator().TranslateTypeName(name);
        public string TranslateMemberName(string name) => name;
    }
}
