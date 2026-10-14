using Microsoft.EntityFrameworkCore;
using Npgsql;
using Npgsql.NameTranslation;
using Pegaxus.Repository;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.OutboxService;

namespace Pegaxus.Service.Test;

[Collection("PostgresSharedDatabase")]
public class InvoiceIssuePostgresTests
{
    [PostgresFact]
    public async Task Postgres_DoubleIssueFreshTrackedStateAndOutboxFailure_PreserveAtomicity()
    {
        var settings = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("PEGAXUS_TEST_CONNECTION_STRING"))
        { Database = "pegaxus_iv7_test_" + Guid.NewGuid().ToString("N") };
        var builder = new NpgsqlDataSourceBuilder(settings.ConnectionString);
        var translator = new EnumTranslator();
        foreach (var type in typeof(InvoiceStatus).Assembly.GetTypes().Where(x => x.IsEnum && x.Namespace == typeof(InvoiceStatus).Namespace))
            builder.MapEnum(type, translator.TranslateTypeName(type.Name), translator);
        await using var source = builder.Build();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseTestNpgsql(source).Options;
        await using var seed = new AppDbContext(options);
        try
        {
            await seed.Database.EnsureCreatedAsync();
            var (actor, customer, invoice, _) = await InvoiceIssueTests.SeedAsync(seed);
            var request = InvoiceIssueTests.Version(invoice);
            await using (var first = new AppDbContext(options))
            await using (var second = new AppDbContext(options))
            {
                await first.Invoices.SingleAsync(x => x.InvoiceId == invoice.InvoiceId);
                await second.Invoices.SingleAsync(x => x.InvoiceId == invoice.InvoiceId);
                var errors = await Task.WhenAll(
                    Attempt(() => InvoiceIssueTests.CreateService(first).IssueAsync(invoice.InvoiceId, request, actor.AccountId)),
                    Attempt(() => InvoiceIssueTests.CreateService(second).IssueAsync(invoice.InvoiceId, request, actor.AccountId)));
                Assert.Single(errors, x => x is null);
                var conflict = Assert.IsType<ConflictException>(Assert.Single(errors, x => x is not null));
                Assert.Contains(conflict.ErrorCode, new[] { "INVOICE_MODIFIED", "INVALID_INVOICE_STATUS" });
            }
            await using (var verify = new AppDbContext(options))
            {
                var issued = await verify.Invoices.SingleAsync(x => x.InvoiceId == invoice.InvoiceId);
                Assert.Equal(InvoiceStatus.ISSUED, issued.Status);
                Assert.Equal(InvoiceIssueTests.TestClock.GetUtcNow().UtcDateTime.Ticks, issued.IssuedAt!.Value.Ticks);
                Assert.Single(await verify.InvoiceStatusHistories.ToListAsync());
                Assert.Single(await verify.Notifications.ToListAsync());
                Assert.Equal(customer.AccountId, (await verify.AccountNotifications.SingleAsync()).AccountId);
                var outbox = await verify.OutboxMessages.ToListAsync(); Assert.Equal(2, outbox.Count);
                Assert.Equal(customer.Email, OutboxQueue.Deserialize<EmailPayload>(outbox.Single(x => x.MessageType == MessageTypes.Email).Payload).To);
                Assert.Equal(customer.AccountId, OutboxQueue.Deserialize<SignalRAccountPayload>(outbox.Single(x => x.MessageType == MessageTypes.SignalRAccount).Payload).AccountId);
            }

            var finalSeed = await InvoiceIssueTests.SeedAsync(seed, InvoiceType.FINAL);
            var deposit = InvoiceIssueTests.AddDeposit(seed, finalSeed.Invoice, 30); await seed.SaveChangesAsync();
            await using (var reused = new AppDbContext(options))
            {
                await reused.Invoices.SingleAsync(x => x.InvoiceId == deposit.InvoiceId);
                await reused.InvoiceItems.Where(x => x.InvoiceId == finalSeed.Invoice.InvoiceId).ToListAsync();
                await using (var writer = new AppDbContext(options))
                    await writer.Invoices.Where(x => x.InvoiceId == deposit.InvoiceId)
                        .ExecuteUpdateAsync(s => s.SetProperty(x => x.TotalAmount, 31m));
                var error = await Assert.ThrowsAsync<ConflictException>(() => InvoiceIssueTests.CreateService(reused)
                    .IssueAsync(finalSeed.Invoice.InvoiceId, InvoiceIssueTests.Version(finalSeed.Invoice), finalSeed.Actor.AccountId));
                Assert.Equal("DEPOSIT_DEDUCTION_CHANGED", error.ErrorCode);
            }
            await using (var reused = new AppDbContext(options))
            {
                await reused.Invoices.SingleAsync(x => x.InvoiceId == finalSeed.Invoice.InvoiceId);
                await using (var writer = new AppDbContext(options))
                    await writer.Invoices.Where(x => x.InvoiceId == finalSeed.Invoice.InvoiceId)
                        .ExecuteUpdateAsync(s => s.SetProperty(x => x.UpdatedAt, finalSeed.Invoice.UpdatedAt.AddTicks(10)));
                Assert.Equal("INVOICE_MODIFIED", (await Assert.ThrowsAsync<ConflictException>(() => InvoiceIssueTests.CreateService(reused)
                    .IssueAsync(finalSeed.Invoice.InvoiceId, InvoiceIssueTests.Version(finalSeed.Invoice), finalSeed.Actor.AccountId))).ErrorCode);
            }

            var rollback = await InvoiceIssueTests.SeedAsync(seed);
            await seed.Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION reject_invoice_email() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN IF NEW.message_type = 'EMAIL' THEN RAISE EXCEPTION 'test email failure'; END IF; RETURN NEW; END $$;
                CREATE TRIGGER reject_invoice_email BEFORE INSERT ON "OutboxMessage"
                FOR EACH ROW EXECUTE FUNCTION reject_invoice_email();
                """);
            await using (var failing = new AppDbContext(options))
                await Assert.ThrowsAsync<DbUpdateException>(() => InvoiceIssueTests.CreateService(failing)
                    .IssueAsync(rollback.Invoice.InvoiceId, InvoiceIssueTests.Version(rollback.Invoice), rollback.Actor.AccountId));
            await using (var verify = new AppDbContext(options))
            {
                var after = await verify.Invoices.SingleAsync(x => x.InvoiceId == rollback.Invoice.InvoiceId);
                Assert.Equal(InvoiceStatus.DRAFT, after.Status); Assert.Null(after.IssuedAt);
                Assert.Equal(rollback.Invoice.UpdatedAt, after.UpdatedAt); Assert.Equal(rollback.Invoice.EmployeeId, after.EmployeeId);
                Assert.Single(await verify.InvoiceStatusHistories.ToListAsync());
                Assert.Single(await verify.Notifications.ToListAsync()); Assert.Single(await verify.AccountNotifications.ToListAsync());
                Assert.Equal(2, await verify.OutboxMessages.CountAsync()); Assert.Empty(await verify.Receipts.ToListAsync());
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
