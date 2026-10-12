using Microsoft.EntityFrameworkCore;
using Npgsql;
using Npgsql.NameTranslation;
using Pegaxus.Repository;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;

namespace Pegaxus.Service.Test;

public class InvoiceHistoryPostgresTests
{
    [PostgresFact]
    public async Task Postgres_HistoryProjectionFilteringAndFreshState_AreReadOnly()
    {
        var settings = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("PEGAXUS_TEST_CONNECTION_STRING"))
        { Database = "pegaxus_iv10_test_" + Guid.NewGuid().ToString("N") };
        var builder = new NpgsqlDataSourceBuilder(settings.ConnectionString); var translator = new EnumTranslator();
        foreach (var type in typeof(InvoiceStatus).Assembly.GetTypes().Where(x => x.IsEnum && x.Namespace == typeof(InvoiceStatus).Namespace))
            builder.MapEnum(type, translator.TranslateTypeName(type.Name), translator);
        await using var source = builder.Build();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(source).Options;
        await using var seed = new AppDbContext(options);
        try
        {
            await seed.Database.EnsureCreatedAsync();
            var (manager, customer, invoice, histories) = await InvoiceHistoryTests.SeedAsync(seed);
            await using var reader = new AppDbContext(options);
            // Any accidental write would fail even though the connection normally has write access.
            await using var transaction = await reader.Database.BeginTransactionAsync();
            await reader.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY");
            var service = InvoiceHistoryTests.CreateService(reader);
            var all = await service.GetStatusHistoryAsync(invoice.InvoiceId, manager.AccountId);
            Assert.Equal(histories.Select(x => x.InvoiceStatusHistoryId), all.Select(x => x.HistoryId));
            Assert.Equal("Le Hoang Phuc", all[0].ChangedBy.FullName); Assert.Equal("LOGISTICS_MANAGER", all[0].ChangedBy.Role);
            Assert.Equal("System", all[2].ChangedBy.FullName); Assert.Null(all[2].ChangedBy.AccountId); Assert.Null(all[2].ChangedBy.Role);
            Assert.Equal(new DateTimeOffset(DateTime.SpecifyKind(histories[0].ChangedAt, DateTimeKind.Utc)), all[0].ChangedAt);
            Assert.Equal(3, (await service.GetStatusHistoryAsync(invoice.InvoiceId, customer.AccountId)).Count);
            Assert.Empty(reader.ChangeTracker.Entries());
            await transaction.CommitAsync();

            await reader.Invoices.Include(x => x.Booking).ThenInclude(x => x.Customer).SingleAsync(x => x.InvoiceId == invoice.InvoiceId);
            await using (var writer = new AppDbContext(options))
                await writer.Invoices.Where(x => x.InvoiceId == invoice.InvoiceId).ExecuteUpdateAsync(s => s.SetProperty(x => x.IssuedAt, (DateTime?)null));
            Assert.Equal("FORBIDDEN", (await Assert.ThrowsAsync<ForbiddenException>(() => service.GetStatusHistoryAsync(invoice.InvoiceId, customer.AccountId))).ErrorCode);
            var stranger = await TestSupport.AddCustomerAsync(seed);
            Assert.Equal("INVOICE_NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(() => service.GetStatusHistoryAsync(invoice.InvoiceId, stranger.Account.AccountId))).ErrorCode);
            Assert.Equal(4, (await service.GetStatusHistoryAsync(invoice.InvoiceId, manager.AccountId)).Count);
            await using var verify = new AppDbContext(options);
            Assert.Equal(4, await verify.InvoiceStatusHistories.CountAsync()); Assert.Empty(await verify.Notifications.ToListAsync());
            Assert.Empty(await verify.OutboxMessages.ToListAsync());
        }
        finally { await seed.Database.EnsureDeletedAsync(); }
    }
    private sealed class EnumTranslator : INpgsqlNameTranslator
    {
        public string TranslateTypeName(string name) => new NpgsqlSnakeCaseNameTranslator().TranslateTypeName(name);
        public string TranslateMemberName(string name) => name;
    }
}
