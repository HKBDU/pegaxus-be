using Microsoft.EntityFrameworkCore;
using Npgsql;
using Npgsql.NameTranslation;
using Pegaxus.Repository;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.OutboxService;

namespace Pegaxus.Service.Test;

public class InvoicePaymentPostgresTests
{
    [PostgresFact]
    public async Task Postgres_DoublePaymentFreshStateAndEmailFailure_PreserveAtomicity()
    {
        var settings = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("PEGAXUS_TEST_CONNECTION_STRING"))
        { Database = "pegaxus_iv8_test_" + Guid.NewGuid().ToString("N") };
        var builder = new NpgsqlDataSourceBuilder(settings.ConnectionString); var translator = new EnumTranslator();
        foreach (var type in typeof(InvoiceStatus).Assembly.GetTypes().Where(x => x.IsEnum && x.Namespace == typeof(InvoiceStatus).Namespace))
            builder.MapEnum(type, translator.TranslateTypeName(type.Name), translator);
        await using var source = builder.Build();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(source).Options;
        await using var seed = new AppDbContext(options);
        try
        {
            await seed.Database.EnsureCreatedAsync();
            var (actor, customer, invoice, plan) = await InvoicePaymentTests.SeedAsync(seed);
            InvoicePaymentTests.AddReceipt(seed, invoice); InvoicePaymentTests.AddReceipt(seed, invoice); await seed.SaveChangesAsync();
            await using (var first = new AppDbContext(options))
            await using (var second = new AppDbContext(options))
            {
                await first.Invoices.SingleAsync(x => x.InvoiceId == invoice.InvoiceId);
                await second.Invoices.SingleAsync(x => x.InvoiceId == invoice.InvoiceId);
                await second.TransportPlans.SingleAsync(x => x.TransportPlanId == plan.TransportPlanId);
                var errors = await Task.WhenAll(
                    Attempt(() => InvoicePaymentTests.CreateService(first).RecordPaymentAsync(invoice.InvoiceId, InvoicePaymentTests.ValidRequest(), actor.AccountId)),
                    Attempt(() => InvoicePaymentTests.CreateService(second).RecordPaymentAsync(invoice.InvoiceId, InvoicePaymentTests.ValidRequest(), actor.AccountId)));
                Assert.Single(errors, x => x is null);
                var failure = Assert.Single(errors, x => x is not null);
                Assert.True(failure is ConflictException, failure?.ToString());
                Assert.Contains(((ConflictException)failure!).ErrorCode, new[] { "INVALID_INVOICE_STATUS", "CONCURRENT_UPDATE" });
            }
            await using (var verify = new AppDbContext(options))
            {
                var paid = await verify.Invoices.SingleAsync(x => x.InvoiceId == invoice.InvoiceId);
                Assert.Equal(InvoiceStatus.PAID, paid.Status); Assert.NotNull(paid.PaidAt);
                var receipts = await verify.Receipts.ToListAsync();
                Assert.Single(receipts, x => x.Status == ReceiptStatus.CONFIRMED); Assert.Equal(2, receipts.Count(x => x.Status == ReceiptStatus.FAILED));
                Assert.Empty(receipts.Where(x => x.Status == ReceiptStatus.PENDING));
                Assert.Single(await verify.InvoiceStatusHistories.ToListAsync()); Assert.Single(await verify.Notifications.ToListAsync());
                Assert.Equal(customer.AccountId, (await verify.AccountNotifications.SingleAsync()).AccountId);
                var outbox = await verify.OutboxMessages.ToListAsync(); Assert.Equal(2, outbox.Count);
                Assert.Equal(customer.Email, OutboxQueue.Deserialize<EmailPayload>(outbox.Single(x => x.MessageType == MessageTypes.Email).Payload).To);
                Assert.Equal(customer.AccountId, OutboxQueue.Deserialize<SignalRAccountPayload>(outbox.Single(x => x.MessageType == MessageTypes.SignalRAccount).Payload).AccountId);
                var approved = await verify.TransportPlans.SingleAsync(x => x.TransportPlanId == plan.TransportPlanId);
                Assert.Equal(TransportPlanStatus.APPROVED, approved.Status); Assert.Equal(actor.EmployeeAccount!.EmployeeId, approved.ApprovedByEmployeeId);
                Assert.Equal(paid.PaidAt, approved.ApprovedAt);
                var service = InvoicePaymentTests.CreateService(verify);
                var details = await service.GetAsync(actor.AccountId, invoice.InvoiceId);
                Assert.Equal(InvoiceStatus.PAID.ToString(), details.Status);
                Assert.Equal(3, details.Receipts.Count);
                Assert.Equal(actor.AccountId, Assert.Single(details.Receipts, x => x.Status == "CONFIRMED").RecordedBy!.AccountId);
                var customerDetails = await service.GetAsync(customer.AccountId, invoice.InvoiceId);
                Assert.Equal(details.Receipts.ToArray(), customerDetails.Receipts.ToArray());
                Assert.Empty(customerDetails.AvailableActions);
            }

            var fresh = await InvoicePaymentTests.SeedAsync(seed);
            await using (var reused = new AppDbContext(options))
            {
                await reused.Invoices.SingleAsync(x => x.InvoiceId == fresh.Invoice.InvoiceId);
                await reused.TransportPlans.SingleAsync(x => x.TransportPlanId == fresh.Plan.TransportPlanId);
                await using (var writer = new AppDbContext(options))
                    await writer.Invoices.Where(x => x.InvoiceId == fresh.Invoice.InvoiceId).ExecuteUpdateAsync(s => s.SetProperty(x => x.TotalAmount, 121m));
                var error = await Assert.ThrowsAsync<UnprocessableEntityException>(() => InvoicePaymentTests.CreateService(reused)
                    .RecordPaymentAsync(fresh.Invoice.InvoiceId, InvoicePaymentTests.ValidRequest(), fresh.Actor.AccountId));
                Assert.Equal("PARTIAL_PAYMENT_NOT_SUPPORTED", error.ErrorCode);
                await using (var writer = new AppDbContext(options))
                {
                    await writer.Invoices.Where(x => x.InvoiceId == fresh.Invoice.InvoiceId).ExecuteUpdateAsync(s => s.SetProperty(x => x.TotalAmount, 120m));
                    await writer.TransportPlans.Where(x => x.TransportPlanId == fresh.Plan.TransportPlanId)
                        .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, TransportPlanStatus.IN_PROGRESS));
                }
                var result = await InvoicePaymentTests.CreateService(reused).RecordPaymentAsync(fresh.Invoice.InvoiceId, InvoicePaymentTests.ValidRequest(), fresh.Actor.AccountId);
                Assert.Null(result.Effects.TransportPlanStatus);
                await using var verify = new AppDbContext(options);
                var after = await verify.TransportPlans.SingleAsync(x => x.TransportPlanId == fresh.Plan.TransportPlanId);
                Assert.Equal(TransportPlanStatus.IN_PROGRESS, after.Status); Assert.Null(after.ApprovedAt);
            }

            var rollback = await InvoicePaymentTests.SeedAsync(seed);
            var pending = InvoicePaymentTests.AddReceipt(seed, rollback.Invoice); await seed.SaveChangesAsync();
            await seed.Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION reject_payment_email() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN IF NEW.message_type = 'EMAIL' THEN RAISE EXCEPTION 'test email failure'; END IF; RETURN NEW; END $$;
                CREATE TRIGGER reject_payment_email BEFORE INSERT ON "OutboxMessage"
                FOR EACH ROW EXECUTE FUNCTION reject_payment_email();
                """);
            await using (var failing = new AppDbContext(options))
                await Assert.ThrowsAsync<DbUpdateException>(() => InvoicePaymentTests.CreateService(failing)
                    .RecordPaymentAsync(rollback.Invoice.InvoiceId, InvoicePaymentTests.ValidRequest(), rollback.Actor.AccountId));
            await using (var verify = new AppDbContext(options))
            {
                var after = await verify.Invoices.SingleAsync(x => x.InvoiceId == rollback.Invoice.InvoiceId);
                Assert.Equal(InvoiceStatus.ISSUED, after.Status); Assert.Null(after.PaidAt); Assert.Equal(rollback.Invoice.UpdatedAt, after.UpdatedAt);
                var receipts = await verify.Receipts.Where(x => x.InvoiceId == after.InvoiceId).ToListAsync();
                Assert.Equal(pending.ReceiptId, Assert.Single(receipts).ReceiptId); Assert.Equal(ReceiptStatus.PENDING, receipts[0].Status);
                Assert.Equal(pending.UpdatedAt, receipts[0].UpdatedAt);
                var afterPlan = await verify.TransportPlans.SingleAsync(x => x.TransportPlanId == rollback.Plan.TransportPlanId);
                Assert.Equal(TransportPlanStatus.PENDING, afterPlan.Status); Assert.Null(afterPlan.ApprovedAt); Assert.Null(afterPlan.ApprovedByEmployeeId);
                Assert.Equal(rollback.Plan.UpdatedAt, afterPlan.UpdatedAt);
                Assert.False(await verify.InvoiceStatusHistories.AnyAsync(x => x.InvoiceId == after.InvoiceId));
                Assert.False(await verify.Notifications.AnyAsync(x => x.ReferenceId == after.InvoiceId));
                Assert.Equal(2, await verify.InvoiceStatusHistories.CountAsync()); Assert.Equal(2, await verify.Notifications.CountAsync());
                Assert.Equal(2, await verify.AccountNotifications.CountAsync()); Assert.Equal(4, await verify.OutboxMessages.CountAsync());
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
