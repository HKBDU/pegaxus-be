using System.Data.Common;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Npgsql.NameTranslation;
using Pegaxus.Repository;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;

namespace Pegaxus.Service.Test;

public class InvoiceDetailPostgresTests
{
    [PostgresFact]
    public async Task PostgreSql_MapsDetailsHistoricalRecorderAndOwnershipWithoutWritesOrTracking()
    {
        // Always own a disposable database; never modify the supplied database.
        var settings = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("PEGAXUS_TEST_CONNECTION_STRING")!)
        {
            Database = $"pegaxus_invoice_detail_test_{Guid.NewGuid():N}"
        };
        var builder = new NpgsqlDataSourceBuilder(settings.ConnectionString);
        var translator = new EnumTranslator();
        foreach (var type in typeof(AccountStatus).Assembly.GetTypes().Where(type => type.IsEnum && type.Namespace == typeof(AccountStatus).Namespace))
            builder.MapEnum(type, translator.TranslateTypeName(type.Name), translator);
        await using var source = builder.Build();
        var commands = new ReadCommands();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(source).AddInterceptors(commands).Options);
        try
        {
            await db.Database.EnsureCreatedAsync();
            var f = await InvoiceDetailTests.Fixture.CreateAsync(db);
            db.ChangeTracker.Clear();
            commands.Sql.Clear();
            var owner = f.Booking.Customer.AccountId;
            var result = await f.Service.GetAsync(owner, f.Invoice.InvoiceId);
            Assert.Equal(-40, result.Summary.DepositDeduction);
            Assert.Equal(86, result.Summary.GrandTotal);
            Assert.Equal(f.Items.Select(item => item.InvoiceItemId), result.Items.Select(item => item.InvoiceItemId));
            Assert.Equal(f.Receipts.Select(item => item.ReceiptId), result.Receipts.Select(item => item.ReceiptId));
            Assert.Equal(f.LatestPlan.TransportPlanId, result.TransportPlan!.TransportPlanId);
            var receipt = result.Receipts.Single(item => item.Status == "CONFIRMED");
            Assert.Equal(f.Recorder.AccountId, receipt.RecordedBy!.AccountId);
            Assert.Equal(f.Recorder.FullName, receipt.RecordedBy.FullName);
            Assert.Equal(InvoiceDetailTests.Fixture.Offset(InvoiceDetailTests.Fixture.Day(6)), receipt.PaidAt);
            Assert.Equal(InvoiceDetailTests.Fixture.Offset(InvoiceDetailTests.Fixture.Day(8)), receipt.ConfirmedAt);
            Assert.All(result.Receipts.Where(item => item.Status != "CONFIRMED"), item => Assert.Null(item.RecordedBy));
            Assert.Equal(5, commands.Sql.Count);
            Assert.Contains("\"InvoiceStatusHistory\"", commands.Sql[3]);
            Assert.All(commands.Sql, sql => Assert.StartsWith("SELECT", sql));
            Assert.Empty(db.ChangeTracker.Entries());
            Assert.Equal(2, await db.Invoices.CountAsync());
            Assert.Equal(4, await db.InvoiceItems.CountAsync());
            Assert.Equal(3, await db.Receipts.CountAsync());
            Assert.Equal(3, await db.InvoiceStatusHistories.CountAsync());
            Assert.Equal(0, await db.Notifications.CountAsync());
            Assert.Equal(0, await db.OutboxMessages.CountAsync());
            await Assert.ThrowsAsync<NotFoundException>(() => f.Service.GetAsync(owner, f.OtherInvoice.InvoiceId));
            await Assert.ThrowsAsync<NotFoundException>(() => f.Service.GetAsync(owner, Guid.NewGuid()));
            result = await f.Service.GetAsync(f.Account.AccountId, f.OtherInvoice.InvoiceId);
            Assert.Empty(result.Items);
            Assert.Empty(result.Receipts);
            Assert.Null(result.Transport);
            Assert.Null(result.TransportPlan);
            await db.InvoiceStatusHistories.Where(item => item.InvoiceId == f.Invoice.InvoiceId && item.Type == InvoiceHistoryType.RECORD_PAYMENT)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.ChangedByAccountId, (Guid?)null));
            result = await f.Service.GetAsync(owner, f.Invoice.InvoiceId);
            Assert.Null(result.Receipts.Single(item => item.Status == "CONFIRMED").RecordedBy);
            await using var host = await InvoiceDetailHttpTests.StartAsync(db);
            using var client = InvoiceDetailHttpTests.Client(host, owner, "Customer");
            var body = (await client.GetFromJsonAsync<JsonElement>($"/api/v1/invoices/{f.Invoice.InvoiceId}")).GetProperty("value");
            Assert.Equal(86, body.GetProperty("summary").GetProperty("grandTotal").GetDecimal());
            Assert.Equal(JsonValueKind.Null, body.GetProperty("payment").ValueKind);
            Assert.Equal(JsonValueKind.Null, body.GetProperty("receipts")[2].GetProperty("recordedBy").ValueKind);
            await db.Invoices.Where(item => item.InvoiceId == f.Invoice.InvoiceId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Status, InvoiceStatus.DRAFT).SetProperty(item => item.IssuedAt, (DateTime?)null));
            var error = await Assert.ThrowsAsync<ForbiddenException>(() => f.Service.GetAsync(owner, f.Invoice.InvoiceId));
            Assert.Equal("Customer cannot view draft invoices", error.Message);
            await db.TransportPlans.Where(item => item.Transport.BookingId == f.Booking.BookingId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Status, TransportPlanStatus.CANCELLED));
            result = await f.Service.GetAsync(f.Account.AccountId, f.Invoice.InvoiceId);
            Assert.Null(result.TransportPlan);
            Assert.Equal(InvoiceDetailTests.Fixture.Offset(f.Invoice.CreatedAt), result.CreatedAt);
            Assert.Empty(db.ChangeTracker.Entries());
        }
        finally { await db.Database.EnsureDeletedAsync(); }
    }

    private sealed class EnumTranslator : INpgsqlNameTranslator
    {
        public string TranslateTypeName(string name) => new NpgsqlSnakeCaseNameTranslator().TranslateTypeName(name);
        public string TranslateMemberName(string name) => name;
    }

    private sealed class ReadCommands : DbCommandInterceptor
    {
        internal List<string> Sql { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Sql.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
