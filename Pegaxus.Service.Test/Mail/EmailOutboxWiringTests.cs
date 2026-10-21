using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Entities;
using Pegaxus.Service.MailService;
using Pegaxus.Service.OutboxService;

using EmployeeServiceImpl = Pegaxus.Service.EmployeeService.Service;

namespace Pegaxus.Service.Test;

public class EmailOutboxWiringTests
{
    internal static async Task<List<EmailPayload>> EmailsAsync(Pegaxus.Repository.AppDbContext db) =>
        (await db.OutboxMessages.Where(item => item.MessageType == MessageTypes.Email).OrderBy(item => item.CreatedAt).ToListAsync())
        .Select(item => JsonSerializer.Deserialize<EmailPayload>(item.Payload, new JsonSerializerOptions(JsonSerializerDefaults.Web))!).ToList();

    [Fact]
    public async Task NewEmployee_GetsAWelcomeEmail_WithALoginLink_AndNoPassword()
    {
        await using var db = TestSupport.CreateDb();
        var (manager, _) = await TestSupport.AddLogisticsManagerAsync(db);
        var service = new EmployeeServiceImpl(db, new PasswordHasher<Account>());

        var created = await service.CreateAsync(new() { Role = "DRIVER", Email = "New.Driver@Example.com", Phone = "+33123456789", FullName = "New Driver" }, manager.AccountId);

        var mail = Assert.Single(await EmailsAsync(db));
        Assert.Equal("new.driver@example.com", mail.To);
        Assert.Equal($"Welcome to Pegaxus — {created.EmployeeCode}", mail.Subject);
        Assert.Contains("New Driver", mail.Body);
        Assert.Contains("Driver", mail.Body);
        Assert.Contains("{{PX_FRONTEND_URL}}/login", mail.Body);
        Assert.DoesNotContain("pegaxusDemo", mail.Body);
    }

    [Fact]
    public async Task QueueAsync_SkipsInactiveAndDeletedAccounts_AndGivesEachRecipientTheirRole()
    {
        await using var db = TestSupport.CreateDb();
        var (activeCustomer, _) = await TestSupport.AddCustomerAsync(db);
        var (deletedCustomer, _) = await TestSupport.AddCustomerAsync(db);
        deletedCustomer.IsDeleted = true;
        var (manager, _) = await TestSupport.AddLogisticsManagerAsync(db);
        var (disabledManager, disabledEmployee) = await TestSupport.AddLogisticsManagerAsync(db);
        disabledEmployee.IsActive = false;
        await db.SaveChangesAsync();

        await MailRecipients.QueueAsync(db, [activeCustomer.AccountId, deletedCustomer.AccountId, manager.AccountId, disabledManager.AccountId, manager.AccountId],
            DateTime.UtcNow, recipient => MailRecipients.Mail(recipient, "Subject", recipient.Role is null ? "customer" : recipient.Role.ToString()!));
        await db.SaveChangesAsync();

        var mails = await EmailsAsync(db);
        Assert.Equal(2, mails.Count);
        Assert.Contains(mails, mail => mail.To == activeCustomer.Email && mail.Body == "customer");
        Assert.Contains(mails, mail => mail.To == manager.Email && mail.Body == "LOGISTICS_MANAGER");
    }

    [Fact]
    public async Task QueueAsync_LetsTheBuilderSkipARecipient()
    {
        await using var db = TestSupport.CreateDb();
        var (customer, _) = await TestSupport.AddCustomerAsync(db);
        var (manager, _) = await TestSupport.AddLogisticsManagerAsync(db);

        await MailRecipients.QueueAsync(db, [customer.AccountId, manager.AccountId], DateTime.UtcNow,
            recipient => recipient.Role is null ? null : MailRecipients.Mail(recipient, "Staff only", "body"));
        await db.SaveChangesAsync();

        Assert.Equal(manager.Email, Assert.Single(await EmailsAsync(db)).To);
    }
}
