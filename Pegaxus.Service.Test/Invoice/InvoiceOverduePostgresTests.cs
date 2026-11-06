using Microsoft.EntityFrameworkCore;
using Npgsql;
using Npgsql.NameTranslation;
using Pegaxus.Repository;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.OutboxService;

namespace Pegaxus.Service.Test;

[Collection("PostgresSharedDatabase")]
public class InvoiceOverduePostgresTests
{
    [PostgresFact]
    public async Task Postgres_MarkOverdue_FlipsOnceKeepsHistoryAndQueuesTheEmailWithTheInvoice()
    {
        var settings = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("PEGAXUS_TEST_CONNECTION_STRING"))
        { Database = "pegaxus_overdue_test_" + Guid.NewGuid().ToString("N") };
        var builder = new NpgsqlDataSourceBuilder(settings.ConnectionString); var translator = new EnumTranslator();
        foreach (var type in typeof(InvoiceStatus).Assembly.GetTypes().Where(x => x.IsEnum && x.Namespace == typeof(InvoiceStatus).Namespace))
            builder.MapEnum(type, translator.TranslateTypeName(type.Name), translator);
        await using var source = builder.Build();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseTestNpgsql(source).Options;
        await using var seed = new AppDbContext(options);
        try
        {
            await seed.Database.EnsureCreatedAsync();
            var (_, customer, invoice, _) = await InvoicePaymentTests.SeedAsync(seed, InvoiceType.FINAL);
            var now = new DateTimeOffset(2026, 11, 15, 8, 0, 0, TimeSpan.Zero);
            await using (var db = new AppDbContext(options))
            {
                // Not yet due: nothing happens, and the date comparison runs as SQL.
                Assert.Equal(0, await InvoiceOverdueTests.CreateService(db, now.AddDays(-1)).MarkOverdueAsync());
                Assert.Equal(1, await InvoiceOverdueTests.CreateService(db, now).MarkOverdueAsync());
                Assert.Equal(0, await InvoiceOverdueTests.CreateService(db, now.AddHours(1)).MarkOverdueAsync());
            }
            await using var verify = new AppDbContext(options);
            Assert.Equal(InvoiceStatus.OVERDUE, (await verify.Invoices.SingleAsync()).Status);
            var history = await verify.InvoiceStatusHistories.SingleAsync();
            Assert.Equal(InvoiceHistoryType.MARK_OVERDUE, history.Type); Assert.Null(history.ChangedByAccountId);
            Assert.Equal(3, await verify.Notifications.CountAsync());
            var email = Assert.Single(await verify.OutboxMessages.Where(x => x.MessageType == MessageTypes.Email).ToListAsync());
            Assert.Equal(customer.Email, OutboxQueue.Deserialize<EmailPayload>(email.Payload).To);
        }
        finally { await seed.Database.EnsureDeletedAsync(); }
    }

    private sealed class EnumTranslator : INpgsqlNameTranslator
    {
        public string TranslateTypeName(string name) => new NpgsqlSnakeCaseNameTranslator().TranslateTypeName(name);
        public string TranslateMemberName(string name) => name;
    }
}
