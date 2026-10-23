using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.InvoiceService.Validator;
using Pegaxus.Service.NotificationService;
using Pegaxus.Service.OutboxService;

using InvoiceServiceImpl = Pegaxus.Service.InvoiceService.Service;

namespace Pegaxus.Service.Test;

public class InvoiceOverdueTests
{
    // The seeded invoice is due 2026-11-15 00:00 UTC.
    private static readonly DateTimeOffset DueMoment = new(2026, 11, 15, 0, 0, 0, TimeSpan.Zero);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    internal static InvoiceServiceImpl CreateService(AppDbContext db, DateTimeOffset now)
    {
        var clock = new FixedClock(now);
        return new(db, new InvoiceOnlinePaymentTests.Gateway(), InvoiceOnlinePaymentTests.Logger, clock, new CreateRequestValidator(clock),
            new SuggestionRequestValidator(), new UpdateRequestValidator(clock), new ListRequestValidator(), new BookingListRequestValidator(),
            new IssueRequestValidator(), new RecordPaymentRequestValidator(clock), new CancelRequestValidator(),
            new Pegaxus.Service.NotificationService.Service(db, new NoopNotificationRealtimeService()));
    }

    [Theory]
    [InlineData(InvoiceType.DEPOSIT)]
    [InlineData(InvoiceType.FINAL)]
    [InlineData(InvoiceType.SUPPLEMENTARY)]
    public async Task MarkOverdue_FlipsAnIssuedInvoicePastItsDueMoment_AndTellsTheCustomerAndEveryManager(InvoiceType type)
    {
        await using var db = TestSupport.CreateDb();
        var (_, customer, invoice, _) = await InvoicePaymentTests.SeedAsync(db, type);
        var now = DueMoment.AddSeconds(1);

        var marked = await CreateService(db, now).MarkOverdueAsync();

        Assert.Equal(1, marked);
        Assert.Equal(InvoiceStatus.OVERDUE, invoice.Status);
        Assert.Equal(now.UtcDateTime, invoice.UpdatedAt);
        var history = Assert.Single(db.InvoiceStatusHistories);
        Assert.Equal(InvoiceHistoryType.MARK_OVERDUE, history.Type);
        Assert.Equal(InvoiceStatus.ISSUED, history.PreviousStatus); Assert.Equal(InvoiceStatus.OVERDUE, history.NewStatus);
        Assert.Null(history.ChangedByAccountId);
        var managers = await db.Employees.Where(x => x.Role == EmployeeRole.LOGISTICS_MANAGER).Select(x => x.AccountId).ToArrayAsync();
        Assert.Equal(managers.Append(customer.AccountId).Order(), (await db.AccountNotifications.Select(x => x.AccountId).ToArrayAsync()).Order());
        Assert.All(await db.Notifications.ToArrayAsync(), item =>
        {
            Assert.Equal(NotificationType.PAYMENT, item.NotificationType); Assert.Equal(NotificationPriority.HIGH, item.Priority);
            Assert.Equal("Invoice overdue", item.Title); Assert.Equal(invoice.InvoiceId, item.ReferenceId);
            Assert.Contains("2026-11-15 00:00 UTC", item.Message);
        });
        var email = OutboxQueue.Deserialize<EmailPayload>(Assert.Single(await db.OutboxMessages.ToListAsync()).Payload);
        Assert.Equal(customer.Email, email.To); Assert.Contains(invoice.InvoiceCode, email.Subject); Assert.Contains("15/11/2026 00:00 UTC", email.Body);
    }

    [Fact]
    public async Task MarkOverdue_AtTheDueMomentItselfTheInvoiceIsStillOnTime()
    {
        await using var db = TestSupport.CreateDb();
        var (_, _, invoice, _) = await InvoicePaymentTests.SeedAsync(db, InvoiceType.FINAL);

        Assert.Equal(0, await CreateService(db, DueMoment).MarkOverdueAsync());
        Assert.Equal(0, await CreateService(db, DueMoment.AddSeconds(-1)).MarkOverdueAsync());

        Assert.Equal(InvoiceStatus.ISSUED, invoice.Status); Assert.Empty(db.InvoiceStatusHistories); Assert.Empty(db.Notifications);
    }

    [Theory]
    [InlineData(InvoiceType.QUOTATION, InvoiceStatus.ISSUED)]
    [InlineData(InvoiceType.FINAL, InvoiceStatus.DRAFT)]
    [InlineData(InvoiceType.FINAL, InvoiceStatus.PAID)]
    [InlineData(InvoiceType.FINAL, InvoiceStatus.CANCELLED)]
    [InlineData(InvoiceType.FINAL, InvoiceStatus.OVERDUE)]
    public async Task MarkOverdue_LeavesQuotationsAndEveryStatusOtherThanIssuedAlone(InvoiceType type, InvoiceStatus status)
    {
        await using var db = TestSupport.CreateDb();
        var (_, _, invoice, _) = await InvoicePaymentTests.SeedAsync(db, type);
        invoice.Status = status; await db.SaveChangesAsync();

        Assert.Equal(0, await CreateService(db, DueMoment.AddDays(30)).MarkOverdueAsync());

        Assert.Equal(status, invoice.Status); Assert.Empty(db.InvoiceStatusHistories);
        Assert.Empty(db.Notifications); Assert.Empty(db.OutboxMessages);
    }

    [Fact]
    public async Task MarkOverdue_RunningTwiceAddsNothingTheSecondTime()
    {
        await using var db = TestSupport.CreateDb();
        await InvoicePaymentTests.SeedAsync(db, InvoiceType.FINAL);
        var service = CreateService(db, DueMoment.AddHours(1));

        Assert.Equal(1, await service.MarkOverdueAsync());
        Assert.Equal(0, await service.MarkOverdueAsync());

        Assert.Single(db.InvoiceStatusHistories); Assert.Equal(3, await db.Notifications.CountAsync()); Assert.Single(db.OutboxMessages);
    }

    [Fact]
    public async Task MarkOverdue_ACreditNoteIsAPayoutSoOnlyTheManagersAreTold()
    {
        await using var db = TestSupport.CreateDb();
        var (_, customer, invoice, _) = await InvoicePaymentTests.SeedAsync(db, InvoiceType.CREDIT_NOTE);

        Assert.Equal(1, await CreateService(db, DueMoment.AddHours(1)).MarkOverdueAsync());

        Assert.Equal(InvoiceStatus.OVERDUE, invoice.Status);
        Assert.DoesNotContain(customer.AccountId, await db.AccountNotifications.Select(x => x.AccountId).ToArrayAsync());
        Assert.Equal(2, await db.Notifications.CountAsync()); Assert.Empty(db.OutboxMessages);
    }
}
