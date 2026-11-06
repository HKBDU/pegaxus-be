using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Npgsql;
using Npgsql.NameTranslation;
using Pegaxus.Repository;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;

using AppException = Pegaxus.Service.Exceptions.Exceptions;

namespace Pegaxus.Service.Test;

[Collection("PostgresSharedDatabase")]
public class InvoiceOnlinePaymentPostgresTests
{
    [PostgresFact]
    public async Task PostgreSql_GatewayRollbackCancellationRaceAndFreshInvoiceState()
    {
        var settings = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("PEGAXUS_TEST_CONNECTION_STRING"))
        { Database = "pegaxus_iv11_test_" + Guid.NewGuid().ToString("N") };
        var builder = new NpgsqlDataSourceBuilder(settings.ConnectionString); var translator = new EnumTranslator();
        foreach (var type in typeof(InvoiceStatus).Assembly.GetTypes().Where(x => x.IsEnum && x.Namespace == typeof(InvoiceStatus).Namespace))
            builder.MapEnum(type, translator.TranslateTypeName(type.Name), translator);
        await using var source = builder.Build();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseTestNpgsql(source).Options;
        await using var seed = new AppDbContext(options);
        try
        {
            await seed.Database.EnsureCreatedAsync();
            var (customer, manager, invoice) = await InvoiceOnlinePaymentTests.SeedAsync(seed);
            var old = InvoiceOnlinePaymentTests.Receipt(invoice, ReceiptStatus.PENDING, "RCP-20261109-0001");
            seed.Receipts.Add(old); await seed.SaveChangesAsync();
            await using (var db = new AppDbContext(options))
            {
                var gateway = new Pegaxus.Service.PaymentGatewayService.Service(Options.Create(new Pegaxus.Service.PaymentGatewayService.SePayOptions()));
                Assert.Equal("PAYMENT_GATEWAY_NOT_CONFIGURED", (await Assert.ThrowsAsync<AppException>(() => InvoiceOnlinePaymentTests.CreateService(db, gateway)
                    .InitiateOnlinePaymentAsync(invoice.InvoiceId, customer.AccountId))).ErrorCode);
            }
            invoice.Currency = "EUR"; await seed.SaveChangesAsync();
            await using (var db = new AppDbContext(options))
            {
                var gateway = new Pegaxus.Service.PaymentGatewayService.Service(Options.Create(SePayCheckoutTests.Settings()));
                Assert.Equal("UNSUPPORTED_PAYMENT_CURRENCY", (await Assert.ThrowsAsync<UnprocessableEntityException>(() => InvoiceOnlinePaymentTests.CreateService(db, gateway)
                    .InitiateOnlinePaymentAsync(invoice.InvoiceId, customer.AccountId))).ErrorCode);
            }
            invoice.Currency = "VND"; invoice.TotalAmount += 0.5m; await seed.SaveChangesAsync();
            await using (var db = new AppDbContext(options))
            {
                var gateway = new Pegaxus.Service.PaymentGatewayService.Service(Options.Create(SePayCheckoutTests.Settings()));
                Assert.Equal("UNSUPPORTED_PAYMENT_AMOUNT", (await Assert.ThrowsAsync<UnprocessableEntityException>(() => InvoiceOnlinePaymentTests.CreateService(db, gateway)
                    .InitiateOnlinePaymentAsync(invoice.InvoiceId, customer.AccountId))).ErrorCode);
            }
            invoice.TotalAmount -= 0.5m; await seed.SaveChangesAsync();
            await using (var verify = new AppDbContext(options))
                Assert.Equal(ReceiptStatus.PENDING, Assert.Single(await verify.Receipts.ToListAsync()).Status);
            await using (var db = new AppDbContext(options))
            {
                var gateway = new InvoiceOnlinePaymentTests.Gateway
                {
                    OnCreate = async token =>
                    {
                        Assert.Equal(ReceiptStatus.PENDING, (await db.Receipts.SingleAsync(x => x.ReceiptId == old.ReceiptId, token)).Status);
                        Assert.Equal(1, await db.Receipts.CountAsync(x => x.Status == ReceiptStatus.PENDING, token));
                        throw new HttpRequestException("Upstream unavailable");
                    }
                };
                var error = await Assert.ThrowsAsync<AppException>(() => InvoiceOnlinePaymentTests.CreateService(db, gateway)
                    .InitiateOnlinePaymentAsync(invoice.InvoiceId, customer.AccountId));
                Assert.Equal(502, error.StatusCode); Assert.Equal("PAYMENT_GATEWAY_ERROR", error.ErrorCode);
            }
            await using (var verify = new AppDbContext(options))
            {
                var receipt = Assert.Single(await verify.Receipts.ToListAsync());
                Assert.Equal(old.ReceiptId, receipt.ReceiptId); Assert.Equal(ReceiptStatus.PENDING, receipt.Status);
                Assert.Equal(old.UpdatedAt, receipt.UpdatedAt);
            }
            await using (var db = new AppDbContext(options))
            {
                var timeout = new InvoiceOnlinePaymentTests.Gateway { Failure = new TaskCanceledException("Gateway timeout") };
                Assert.Equal("PAYMENT_GATEWAY_ERROR", (await Assert.ThrowsAsync<AppException>(() => InvoiceOnlinePaymentTests.CreateService(db, timeout)
                    .InitiateOnlinePaymentAsync(invoice.InvoiceId, customer.AccountId))).ErrorCode);
            }
            await using (var db = new AppDbContext(options))
            using (var cancelled = new CancellationTokenSource())
            {
                var gateway = new InvoiceOnlinePaymentTests.Gateway
                { OnCreate = token => { cancelled.Cancel(); throw new OperationCanceledException(token); } };
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => InvoiceOnlinePaymentTests.CreateService(db, gateway)
                    .InitiateOnlinePaymentAsync(invoice.InvoiceId, customer.AccountId, cancelled.Token));
            }
            await using (var verify = new AppDbContext(options))
                Assert.Equal(ReceiptStatus.PENDING, Assert.Single(await verify.Receipts.ToListAsync()).Status);

            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var locking = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var competingOptions = new DbContextOptionsBuilder<AppDbContext>().UseTestNpgsql(source)
                .AddInterceptors(new InvoiceUpdateSignal(locking)).Options;
            await using (var first = new AppDbContext(options))
            await using (var second = new AppDbContext(competingOptions))
            {
                var gateway = new InvoiceOnlinePaymentTests.Gateway
                { OnCreate = async _ => { entered.SetResult(); await release.Task.WaitAsync(TimeSpan.FromSeconds(10)); return "https://gateway.example/pay/winner"; } };
                var competingGateway = new InvoiceOnlinePaymentTests.Gateway();
                var winner = InvoiceOnlinePaymentTests.CreateService(first, gateway).InitiateOnlinePaymentAsync(invoice.InvoiceId, customer.AccountId);
                try
                {
                    await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                    var competitor = InvoiceOnlinePaymentTests.CreateService(second, competingGateway).InitiateOnlinePaymentAsync(invoice.InvoiceId, customer.AccountId);
                    await locking.Task.WaitAsync(TimeSpan.FromSeconds(10)); release.SetResult();
                    var result = await winner;
                    Assert.Equal(invoice.TotalAmount, result.Receipt.Amount); Assert.True(result.Reused);
                    var failure = await Record.ExceptionAsync(() => competitor);
                    var error = Assert.IsType<ConflictException>(failure);
                    Assert.Equal("CONCURRENT_UPDATE", error.ErrorCode);
                    Assert.Equal(1, gateway.Calls); Assert.Equal(0, competingGateway.Calls);
                }
                finally { release.TrySetResult(); }
            }
            await using (var verify = new AppDbContext(options))
            {
                Assert.Equal(1, await verify.Receipts.CountAsync());
                Assert.Equal(ReceiptStatus.PENDING, (await verify.Receipts.SingleAsync(x => x.ReceiptId == old.ReceiptId)).Status);
                var unchanged = await verify.Invoices.SingleAsync();
                Assert.Equal(InvoiceStatus.ISSUED, unchanged.Status); Assert.Equal(invoice.UpdatedAt, unchanged.UpdatedAt);
                Assert.Null(unchanged.PaidAt); Assert.Empty(await verify.InvoiceStatusHistories.ToListAsync());
                Assert.Empty(await verify.Notifications.ToListAsync()); Assert.Empty(await verify.OutboxMessages.ToListAsync());
                var details = await InvoicePaymentTests.CreateService(verify).GetAsync(customer.AccountId, invoice.InvoiceId);
                Assert.Equal("PENDING", Assert.Single(details.Receipts, x => x.Status == "PENDING").Status);
            }
            await using (var db = new AppDbContext(options))
            {
                var error = await Assert.ThrowsAsync<ConflictException>(() => InvoiceCancellationTests.CreateService(db)
                    .CancelAsync(invoice.InvoiceId, InvoiceCancellationTests.ValidRequest(), manager.AccountId));
                Assert.Equal("PAYMENT_IN_PROGRESS", error.ErrorCode);
            }
            await using (var reader = new AppDbContext(options))
            {
                await reader.Invoices.Include(x => x.Booking).SingleAsync();
                var gateway = new InvoiceOnlinePaymentTests.Gateway(); var service = InvoiceOnlinePaymentTests.CreateService(reader, gateway);
                await using var writer = new AppDbContext(options);
                await writer.Invoices.Where(x => x.InvoiceId == invoice.InvoiceId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, InvoiceStatus.CANCELLED));
                Assert.Equal("INVALID_INVOICE_STATUS", (await Assert.ThrowsAsync<ConflictException>(() => service
                    .InitiateOnlinePaymentAsync(invoice.InvoiceId, customer.AccountId))).ErrorCode);
                await writer.Invoices.Where(x => x.InvoiceId == invoice.InvoiceId).ExecuteUpdateAsync(s => s.SetProperty(x => x.IssuedAt, (DateTime?)null));
                Assert.Equal("INVOICE_NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(() => service
                    .InitiateOnlinePaymentAsync(invoice.InvoiceId, customer.AccountId))).ErrorCode);
                await writer.Invoices.Where(x => x.InvoiceId == invoice.InvoiceId).ExecuteUpdateAsync(s => s.SetProperty(x => x.IssuedAt, InvoiceOnlinePaymentTests.Now));
                var stranger = await TestSupport.AddCustomerAsync(writer);
                await writer.Bookings.Where(x => x.BookingId == invoice.BookingId).ExecuteUpdateAsync(s => s.SetProperty(x => x.CustomerId, stranger.Customer.CustomerId));
                Assert.Equal("INVOICE_NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(() => service
                    .InitiateOnlinePaymentAsync(invoice.InvoiceId, customer.AccountId))).ErrorCode);
                Assert.Equal(0, gateway.Calls);
            }
            await using (var writer = new AppDbContext(options))
            {
                await writer.Bookings.Where(x => x.BookingId == invoice.BookingId).ExecuteUpdateAsync(s => s.SetProperty(x => x.CustomerId, customer.CustomerAccount!.CustomerId));
                await writer.Invoices.Where(x => x.InvoiceId == invoice.InvoiceId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, InvoiceStatus.ISSUED));
            }
            await using (var db = new AppDbContext(options))
            {
                var gateway = new InvoiceOnlinePaymentTests.Gateway();
                var service = InvoiceOnlinePaymentTests.CreateService(db, gateway);
                await service.RecordPaymentAsync(invoice.InvoiceId, new()
                { Amount = (long)invoice.TotalAmount, PaymentMethod = "BANK_TRANSFER" }, manager.AccountId);
                Assert.Equal("INVALID_INVOICE_STATUS", (await Assert.ThrowsAsync<ConflictException>(() => service
                    .InitiateOnlinePaymentAsync(invoice.InvoiceId, customer.AccountId))).ErrorCode);
                Assert.Equal(0, gateway.Calls);
            }
            await using (var verify = new AppDbContext(options))
            {
                Assert.Equal(1, await verify.Receipts.CountAsync(x => x.Status == ReceiptStatus.PENDING));
                Assert.Equal(0, await verify.Receipts.CountAsync(x => x.Status == ReceiptStatus.FAILED));
                var details = await InvoicePaymentTests.CreateService(verify).GetAsync(customer.AccountId, invoice.InvoiceId);
                Assert.Equal("PAID", details.Status);
                Assert.Equal(manager.AccountId, Assert.Single(details.Receipts, x => x.Status == "CONFIRMED").RecordedBy!.AccountId);
                Assert.Equal(InvoiceHistoryType.RECORD_PAYMENT, Assert.Single(await verify.InvoiceStatusHistories.ToListAsync()).Type);
            }
        }
        finally { await seed.Database.EnsureDeletedAsync(); }
    }

    private sealed class InvoiceUpdateSignal(TaskCompletionSource started) : DbCommandInterceptor
    {
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("UPDATE \"Invoice\""))
            {
                // Pin the competing transaction's snapshot before the first checkout can commit.
                await Assert.IsType<AppDbContext>(eventData.Context).Invoices.AnyAsync(cancellationToken);
                started.TrySetResult();
            }
            return result;
        }
    }
    private sealed class EnumTranslator : INpgsqlNameTranslator
    {
        public string TranslateTypeName(string name) => new NpgsqlSnakeCaseNameTranslator().TranslateTypeName(name);
        public string TranslateMemberName(string name) => name;
    }
}
