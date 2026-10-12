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
using Pegaxus.Service.Exceptions;
using Xunit.Abstractions;

namespace Pegaxus.Service.Test;

public class BookingInvoiceListPostgresTests(ITestOutputHelper output)
{
    [PostgresFact]
    public async Task PostgreSql_EnforcesVisibilityProjectsAndPagesTenThousandInvoicesWithoutWrites()
    {
        // Always own a disposable database; never modify the supplied database.
        var settings = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("PEGAXUS_TEST_CONNECTION_STRING")!)
        {
            Database = $"pegaxus_booking_invoice_list_test_{Guid.NewGuid():N}"
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
            var f = await BookingInvoiceListTests.Fixture.CreateAsync(db);
            db.ChangeTracker.Clear();
            commands.Sql.Clear();
            var owner = f.Booking.Customer.AccountId;
            var result = await f.Service.GetByBookingAsync(owner, f.Booking.BookingId, new() { PageIndex = 2, PageSize = 2 });
            Assert.Equal(4, result.TotalItems);
            Assert.Equal(2, result.TotalPages);
            Assert.Equal(new[] { f.Invoices[2].InvoiceId, f.Invoices[1].InvoiceId }, result.Items.Select(item => item.InvoiceId));
            Assert.All(result.Items, item => Assert.Equal(f.LatestPlan.TransportPlanId, item.TransportPlanId));
            Assert.Equal(4, commands.Sql.Count);
            Assert.Contains("LIMIT", commands.Sql[^1]);
            Assert.Contains("OFFSET", commands.Sql[^1]);
            Assert.Contains("issued_at IS NOT NULL", commands.Sql[^1]);
            Assert.Contains("issued_at IS NOT NULL", commands.Sql[^2]);
            Assert.All(commands.Sql, sql => {
                Assert.StartsWith("SELECT", sql);
                Assert.DoesNotContain("\"InvoiceItem\"", sql);
                Assert.DoesNotContain("\"Receipt\"", sql);
            });
            Assert.Empty(db.ChangeTracker.Entries());
            result = await f.Service.GetByBookingAsync(owner, f.Booking.BookingId, new() { Status = "CANCELLED" });
            Assert.Equal(f.Invoices[4].InvoiceId, Assert.Single(result.Items).InvoiceId);
            result = await f.Service.GetByBookingAsync(owner, f.Booking.BookingId, new() { Status = "DRAFT" });
            Assert.Empty(result.Items);
            Assert.Equal(0, result.TotalItems);
            await Assert.ThrowsAsync<NotFoundException>(() => f.Service.GetByBookingAsync(owner, f.OtherBooking.BookingId, new()));
            result = await f.Service.GetByBookingAsync(owner, f.EmptyBooking.BookingId, new());
            Assert.Empty(result.Items);
            foreach (var (sort, indexes) in new[] { ("issuedAt:asc", new[] { 1, 2, 3, 4, 6, 0, 5 }), ("issuedAt:desc", new[] { 6, 4, 3, 2, 1, 0, 5 }),
                         ("dueDate:asc", new[] { 4, 0, 1, 2, 5, 3, 6 }), ("dueDate:desc", new[] { 5, 2, 1, 0, 4, 3, 6 }) })
            {
                result = await f.Service.GetByBookingAsync(f.Account.AccountId, f.Booking.BookingId, new() { SortBy = sort });
                Assert.Equal(indexes.Select(index => f.Invoices[index].InvoiceId), result.Items.Select(item => item.InvoiceId));
            }
            result = await f.Service.GetByBookingAsync(f.Account.AccountId, f.OtherBooking.BookingId, new());
            Assert.Null(Assert.Single(result.Items).TransportPlanId);

            for (var index = 0; index < 9992; index++)
                db.Invoices.Add(new Invoice { InvoiceId = Guid.NewGuid(), InvoiceCode = $"INV-PERF-{index:D5}",
                    BookingId = f.Booking.BookingId, EmployeeId = f.Employee.EmployeeId, Type = InvoiceType.FINAL,
                    Status = InvoiceStatus.ISSUED, Currency = "VND", TotalAmount = 100 + index,
                    IssuedAt = BookingInvoiceListTests.Fixture.Day(5), DueDate = new(2026, 10, 25),
                    CreatedAt = BookingInvoiceListTests.Fixture.Day(1).AddSeconds(index), UpdatedAt = BookingInvoiceListTests.Fixture.Day(1).AddSeconds(index) });
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            await using var host = await BookingInvoiceListHttpTests.StartAsync(db);
            using var client = BookingInvoiceListHttpTests.Client(host, owner, "Customer");
            var path = $"/api/v1/bookings/{f.Booking.BookingId}/invoices";
            foreach (var (url, total, size) in new[] { (path, 9996, 20), (path + "?type=FINAL&status=ISSUED&sortBy=dueDate:desc&pageIndex=50&pageSize=100", 9992, 100) })
            {
                await client.GetFromJsonAsync<JsonElement>(url);
                var timings = new List<double>();
                for (var run = 0; run < 3; run++)
                {
                    var timer = Stopwatch.StartNew();
                    var body = await client.GetFromJsonAsync<JsonElement>(url);
                    timer.Stop();
                    var value = body.GetProperty("value");
                    Assert.Equal(total, value.GetProperty("totalItems").GetInt32());
                    Assert.Equal(size, value.GetProperty("items").GetArrayLength());
                    Assert.All(value.GetProperty("items").EnumerateArray(), item => {
                        Assert.Equal(f.Booking.BookingId, item.GetProperty("bookingId").GetGuid());
                        Assert.NotEqual("DRAFT", item.GetProperty("status").GetString());
                        Assert.NotEqual(JsonValueKind.Null, item.GetProperty("issuedAt").ValueKind);
                    });
                    timings.Add(timer.Elapsed.TotalMilliseconds);
                }
                output.WriteLine($"Local PostgreSQL HTTP benchmark, 10000 invoices: {string.Join(", ", timings.Select(ms => $"{ms:F1} ms"))}; {url}");
                Assert.True(timings.Max() <= 800, $"Warmed localhost response exceeded 800 ms: {timings.Max():F1} ms.");
            }
            Assert.Empty(db.ChangeTracker.Entries());
            Assert.Equal(10000, await db.Invoices.CountAsync());
            Assert.Equal(0, await db.InvoiceStatusHistories.CountAsync());
            Assert.Equal(0, await db.Notifications.CountAsync());
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
