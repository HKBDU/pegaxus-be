using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;

namespace Pegaxus.Service.Test;

/// <summary>A paid invoice is told to the customer and, in the app only, to the manager who approved the booking.</summary>
public class PaymentManagerNotificationTests
{
    private static async Task<(Receipt Receipt, Account Manager, Employee ManagerEmployee)> SeedAssignedAsync(AppDbContext db, InvoiceType type = InvoiceType.DEPOSIT)
    {
        var receipt = await PaymentWebhookTests.SeedAsync(db, type);
        var (account, employee) = await TestSupport.AddLogisticsManagerAsync(db);
        (await db.Bookings.SingleAsync()).AssignedEmployeeId = employee.EmployeeId;
        await db.SaveChangesAsync();
        return (receipt, account, employee);
    }

    private static Task PayAsync(AppDbContext db, Receipt receipt) => PaymentWebhookTests.CreateService(db).HandleWebhookAsync(PaymentWebhookTests.Payload(receipt), PaymentWebhookTests.Secret);

    [Fact]
    public async Task Payment_TellsTheManagerWhoApprovedTheBooking_InTheAppOnly()
    {
        await using var db = TestSupport.CreateDb(); var (receipt, manager, _) = await SeedAssignedAsync(db);

        await PayAsync(db, receipt);

        var received = await db.AccountNotifications.Include(item => item.Notification).Where(item => item.AccountId == manager.AccountId).ToListAsync();
        var notification = Assert.Single(received).Notification;
        Assert.Equal("Payment received", notification.Title); Assert.Equal(NotificationType.PAYMENT, notification.NotificationType);
        Assert.Contains(receipt.Invoice.InvoiceCode, notification.Message); Assert.Equal(receipt.Invoice.InvoiceId, notification.ReferenceId);
        Assert.Single(await db.OutboxMessages.Where(item => item.MessageType == "EMAIL").ToListAsync());
    }

    [Fact]
    public async Task DepositPayment_IsHighPriority_AndSaysThePlanWasApproved()
    {
        await using var db = TestSupport.CreateDb(); var (receipt, _, _) = await SeedAssignedAsync(db);

        await PayAsync(db, receipt);

        var notification = await db.Notifications.SingleAsync(item => item.Title == "Payment received");
        Assert.Equal(NotificationPriority.HIGH, notification.Priority); Assert.Contains("approved automatically", notification.Message);
    }

    [Fact]
    public async Task OtherInvoicePayment_IsNormalPriority()
    {
        await using var db = TestSupport.CreateDb(); var (receipt, _, _) = await SeedAssignedAsync(db, InvoiceType.FINAL);

        await PayAsync(db, receipt);

        var notification = await db.Notifications.SingleAsync(item => item.Title == "Payment received");
        Assert.Equal(NotificationPriority.NORMAL, notification.Priority); Assert.DoesNotContain("approved automatically", notification.Message);
    }

    [Fact]
    public async Task Payment_DoesNotTellOtherManagers_ButStillTellsTheCustomer()
    {
        await using var db = TestSupport.CreateDb(); var (receipt, manager, _) = await SeedAssignedAsync(db);
        var (other, _) = await TestSupport.AddLogisticsManagerAsync(db);

        await PayAsync(db, receipt);

        var recipients = (await db.AccountNotifications.Include(item => item.Notification).ToListAsync()).ToDictionary(item => item.AccountId, item => item.Notification.Title);
        Assert.DoesNotContain(other.AccountId, recipients.Keys);
        Assert.Equal("Payment received", recipients[manager.AccountId]);
        Assert.Contains("Payment confirmed", recipients.Values);
    }

    [Fact]
    public async Task Payment_TellsNoManager_WhenTheAssignedOneIsNoLongerActive()
    {
        await using var db = TestSupport.CreateDb(); var (receipt, _, employee) = await SeedAssignedAsync(db);
        employee.IsActive = false; await db.SaveChangesAsync();

        await PayAsync(db, receipt);

        Assert.DoesNotContain(await db.Notifications.ToListAsync(), item => item.Title == "Payment received");
        Assert.Contains(await db.Notifications.ToListAsync(), item => item.Title == "Payment confirmed");
    }

    [Fact]
    public async Task ARepeatedWebhook_DoesNotNotifyTheManagerTwice()
    {
        await using var db = TestSupport.CreateDb(); var (receipt, _, _) = await SeedAssignedAsync(db);

        await PayAsync(db, receipt); await PayAsync(db, receipt);

        Assert.Single(await db.Notifications.Where(item => item.Title == "Payment received").ToListAsync());
    }
}
