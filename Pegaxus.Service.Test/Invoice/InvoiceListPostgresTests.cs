using System.Data.Common;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Npgsql.NameTranslation;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.JwtService;
using Xunit.Abstractions;

namespace Pegaxus.Service.Test;

public class InvoiceListPostgresTests(ITestOutputHelper output)
{
    [PostgresFact]
    public async Task PostgreSql_ProjectsFiltersSortsAndPagesTenThousandInvoicesWithoutWrites()
    {
        // Always own a disposable database; never modify the supplied database.
        var settings = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("PEGAXUS_TEST_CONNECTION_STRING")!)
        {
            Database = $"pegaxus_invoice_list_test_{Guid.NewGuid():N}"
        };
        var builder = new NpgsqlDataSourceBuilder(settings.ConnectionString);
        var translator = new EnumTranslator();
        foreach (var type in typeof(AccountStatus).Assembly.GetTypes().Where(type => type.IsEnum && type.Namespace == typeof(AccountStatus).Namespace))
            builder.MapEnum(type, translator.TranslateTypeName(type.Name), translator);
        await using var source = builder.Build();
        var commands = new ReadCommands();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseTestNpgsql(source).AddInterceptors(commands).Options);
        try
        {
            await db.Database.EnsureCreatedAsync();
            var f = await InvoiceListTests.Fixture.CreateAsync(db);
            db.ChangeTracker.Clear();
            commands.Sql.Clear();
            var result = await f.Service.GetListAsync(f.Account.AccountId, new() { TransportPlanId = f.OldPlan.TransportPlanId, PageIndex = 2, PageSize = 2 });
            Assert.Equal(4, result.TotalItems);
            Assert.Equal(new[] { f.Invoices[1].InvoiceId, f.Invoices[0].InvoiceId }, result.Items.Select(item => item.InvoiceId));
            Assert.All(result.Items, item => Assert.Equal(f.LatestPlan.TransportPlanId, item.TransportPlanId));
            Assert.Equal(3, commands.Sql.Count);
            Assert.Contains("LIMIT", commands.Sql[^1]);
            Assert.Contains("OFFSET", commands.Sql[^1]);
            Assert.All(commands.Sql, sql => {
                Assert.StartsWith("SELECT", sql);
                Assert.DoesNotContain("\"InvoiceItem\"", sql);
                Assert.DoesNotContain("\"Receipt\"", sql);
            });
            Assert.Empty(db.ChangeTracker.Entries());
            result = await f.Service.GetListAsync(f.Account.AccountId, new() { TransportPlanId = f.CancelledPlan.TransportPlanId });
            Assert.Equal(4, result.TotalItems);
            result = await f.Service.GetListAsync(f.Account.AccountId, new() { IssuedFrom = new(2026, 10, 5), IssuedTo = new(2026, 10, 5) });
            Assert.Equal(new[] { f.Invoices[2].InvoiceId, f.Invoices[1].InvoiceId }, result.Items.Select(item => item.InvoiceId));
            result = await f.Service.GetListAsync(f.Account.AccountId, new() { IssuedTo = DateOnly.MaxValue });
            Assert.Equal(3, result.TotalItems);
            result = await f.Service.GetListAsync(f.Account.AccountId, new() { DueFrom = new(2026, 10, 20), DueTo = new(2026, 10, 25) });
            Assert.Equal(2, result.TotalItems);
            foreach (var (sort, indexes) in new[] { ("issuedAt:asc", new[] { 1, 2, 3, 0, 4 }), ("issuedAt:desc", new[] { 3, 2, 1, 0, 4 }),
                         ("dueDate:asc", new[] { 4, 0, 1, 2, 3 }), ("dueDate:desc", new[] { 2, 1, 0, 4, 3 }) })
            {
                result = await f.Service.GetListAsync(f.Account.AccountId, new() { SortBy = sort });
                Assert.Equal(indexes.Select(index => f.Invoices[index].InvoiceId), result.Items.Select(item => item.InvoiceId));
            }
            db.Attach(f.Invoices[0]); f.Invoices[0].InvoiceCode += "%";
            db.Attach(f.Booking); f.Booking.BookingCode += "_";
            db.Attach(f.OtherBooking.Customer); f.OtherBooking.Customer.FullName += "\\";
            await db.SaveChangesAsync();
            foreach (var (search, count) in new[] { ("%", 1), ("_", 4), ("\\", 1), (" club alpha ", 4), (" inv-list-0003 ", 1) })
                Assert.Equal(count, (await f.Service.GetListAsync(f.Account.AccountId, new() { Search = search })).TotalItems);

            for (var index = 0; index < 9995; index++)
                db.Invoices.Add(new Invoice { InvoiceId = Guid.NewGuid(), InvoiceCode = $"INV-PERF-{index:D5}",
                    BookingId = f.Booking.BookingId, EmployeeId = f.Employee.EmployeeId, Type = InvoiceType.FINAL,
                    Status = InvoiceStatus.ISSUED, Currency = "VND", TotalAmount = 100 + index,
                    IssuedAt = InvoiceListTests.Fixture.Day(5), DueDate = new(2026, 10, 25),
                    CreatedAt = InvoiceListTests.Fixture.Day(1).AddSeconds(index), UpdatedAt = InvoiceListTests.Fixture.Day(1).AddSeconds(index) });
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            await using var host = await InvoiceListHttpTests.StartAsync(db);
            using var client = InvoiceListHttpTests.Client(host, f.Account.AccountId, JwtServices.GetRoleName(EmployeeRole.LOGISTICS_MANAGER));
            var urls = new[] { "/api/v1/invoices", $"/api/v1/invoices?transportPlanId={f.OldPlan.TransportPlanId}&search=club%20alpha&sortBy=dueDate:desc&pageIndex=50&pageSize=100" };
            foreach (var url in urls)
            {
                await client.GetFromJsonAsync<JsonElement>(url);
                var timings = new List<double>();
                for (var run = 0; run < 3; run++)
                {
                    var timer = Stopwatch.StartNew();
                    var body = await client.GetFromJsonAsync<JsonElement>(url);
                    timer.Stop();
                    Assert.Equal(url == urls[0] ? 10000 : 9999, body.GetProperty("value").GetProperty("totalItems").GetInt32());
                    Assert.Equal(url == urls[0] ? 20 : 100, body.GetProperty("value").GetProperty("items").GetArrayLength());
                    timings.Add(timer.Elapsed.TotalMilliseconds);
                }
                output.WriteLine($"Local PostgreSQL HTTP benchmark, 10000 invoices: {string.Join(", ", timings.Select(ms => $"{ms:F1} ms"))}; {url}");
                Assert.True(timings.Max() <= 800, $"Warmed localhost response exceeded 800 ms: {timings.Max():F1} ms.");
            }
            Assert.Empty(db.ChangeTracker.Entries());
            Assert.Equal(0, await db.InvoiceStatusHistories.CountAsync());
            Assert.Equal(0, await db.OutboxMessages.CountAsync());
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
