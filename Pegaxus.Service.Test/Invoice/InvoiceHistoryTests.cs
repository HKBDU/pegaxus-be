using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;

using HistoryService = Pegaxus.Service.InvoiceService.Service;

namespace Pegaxus.Service.Test;

public class InvoiceHistoryTests
{
    internal static HistoryService CreateService(AppDbContext db) => InvoiceIssueTests.CreateService(db);
    internal static async Task<(Account Manager, Account Customer, Invoice Invoice, InvoiceStatusHistory[] Histories)> SeedAsync(AppDbContext db)
    {
        var (managerAccount, _) = await TestSupport.AddLogisticsManagerAsync(db);
        var (actorAccount, actor) = await TestSupport.AddLogisticsManagerAsync(db); actor.FullName = "Le Hoang Phuc";
        var (customerAccount, customer) = await TestSupport.AddCustomerAsync(db);
        var (booking, _) = await TestSupport.AddPendingBookingAsync(db, customer);
        var start = new DateTime(2026, 11, 5, 8, 0, 0);
        var invoice = new Invoice { InvoiceId = Guid.NewGuid(), InvoiceCode = "INV-" + Guid.NewGuid().ToString("N"),
            BookingId = booking.BookingId, EmployeeId = actor.EmployeeId, Type = InvoiceType.FINAL, Status = InvoiceStatus.PAID,
            Currency = "EUR", TotalAmount = 120, CreatedAt = start, UpdatedAt = start.AddDays(12),
            IssuedAt = start.AddHours(1), PaidAt = start.AddDays(12) };
        var histories = new[]
        {
            History(invoice, InvoiceHistoryType.CREATE, null, InvoiceStatus.DRAFT, actorAccount.AccountId, start),
            History(invoice, InvoiceHistoryType.ISSUE, InvoiceStatus.DRAFT, InvoiceStatus.ISSUED, actorAccount.AccountId, start.AddHours(1)),
            History(invoice, InvoiceHistoryType.MARK_OVERDUE, InvoiceStatus.ISSUED, InvoiceStatus.OVERDUE, null, start.AddDays(11)),
            History(invoice, InvoiceHistoryType.RECORD_PAYMENT, InvoiceStatus.OVERDUE, InvoiceStatus.PAID, null, start.AddDays(12))
        };
        db.Invoices.Add(invoice); db.InvoiceStatusHistories.AddRange(histories.Reverse()); await db.SaveChangesAsync();
        return (managerAccount, customerAccount, invoice, histories);
    }
    internal static InvoiceStatusHistory History(Invoice invoice, InvoiceHistoryType type, InvoiceStatus? previous,
        InvoiceStatus next, Guid? actor, DateTime changedAt) => new()
    {
        InvoiceStatusHistoryId = Guid.NewGuid(), InvoiceId = invoice.InvoiceId, Type = type, PreviousStatus = previous,
        NewStatus = next, ChangedByAccountId = actor, ChangedAt = changedAt, CreatedAt = changedAt
    };

    [Fact]
    public async Task History_ProjectsAllEventsChronologically_WithEmployeeAndSystemActors_WithoutWrites()
    {
        await using var db = TestSupport.CreateDb();
        var (manager, customer, invoice, histories) = await SeedAsync(db);
        var service = CreateService(db);
        var all = await service.GetStatusHistoryAsync(invoice.InvoiceId, manager.AccountId);
        Assert.Equal(histories.Select(x => x.InvoiceStatusHistoryId), all.Select(x => x.HistoryId));
        Assert.Equal(new[] { "CREATE", "ISSUE", "MARK_OVERDUE", "RECORD_PAYMENT" }, all.Select(x => x.Type));
        Assert.Null(all[0].PreviousStatus); Assert.Equal("DRAFT", all[0].NewStatus); Assert.Null(all[0].Reason);
        Assert.Equal(histories[0].ChangedByAccountId, all[0].ChangedBy.AccountId);
        Assert.Equal("Le Hoang Phuc", all[0].ChangedBy.FullName); Assert.Equal("LOGISTICS_MANAGER", all[0].ChangedBy.Role);
        foreach (var system in all.Skip(2))
        { Assert.Null(system.ChangedBy.AccountId); Assert.Equal("System", system.ChangedBy.FullName); Assert.Null(system.ChangedBy.Role); }
        Assert.Equal("OVERDUE", all[3].PreviousStatus); Assert.Equal("PAID", all[3].NewStatus);
        Assert.All(all, x => Assert.Equal(TimeSpan.Zero, x.ChangedAt.Offset));
        Assert.Equal(new DateTimeOffset(DateTime.SpecifyKind(histories[0].ChangedAt, DateTimeKind.Utc)), all[0].ChangedAt);
        var own = await service.GetStatusHistoryAsync(invoice.InvoiceId, customer.AccountId);
        Assert.Equal(histories.Skip(1).Select(x => x.InvoiceStatusHistoryId), own.Select(x => x.HistoryId));
        Assert.Equal("DRAFT", own[0].PreviousStatus); Assert.Equal("ISSUE", own[0].Type);
        Assert.All(db.ChangeTracker.Entries(), x => Assert.Equal(EntityState.Unchanged, x.State));
        Assert.Equal(4, db.InvoiceStatusHistories.Count()); Assert.Empty(db.Notifications); Assert.Empty(db.OutboxMessages);
    }

    [Fact]
    public async Task Customer_FiltersCreateAndNewDraftIndependently_AndOnlyRequestedInvoice()
    {
        await using var db = TestSupport.CreateDb();
        var (manager, customer, invoice, histories) = await SeedAsync(db);
        var other = await SeedAsync(db);
        db.InvoiceStatusHistories.Add(History(invoice, InvoiceHistoryType.CREATE, null, InvoiceStatus.ISSUED, manager.AccountId, invoice.CreatedAt));
        db.InvoiceStatusHistories.Add(History(invoice, InvoiceHistoryType.ISSUE, InvoiceStatus.ISSUED, InvoiceStatus.DRAFT, manager.AccountId, invoice.CreatedAt));
        await db.SaveChangesAsync();
        var result = await CreateService(db).GetStatusHistoryAsync(invoice.InvoiceId, customer.AccountId);
        Assert.Equal(histories.Skip(1).Select(x => x.InvoiceStatusHistoryId), result.Select(x => x.HistoryId));
        Assert.DoesNotContain(result, x => other.Histories.Any(h => h.InvoiceStatusHistoryId == x.HistoryId));
        Assert.Equal(6, (await CreateService(db).GetStatusHistoryAsync(invoice.InvoiceId, manager.AccountId)).Count);
    }

    [Theory]
    [InlineData(InvoiceStatus.DRAFT, false, 403)]
    [InlineData(InvoiceStatus.CANCELLED, false, 403)]
    [InlineData(InvoiceStatus.ISSUED, false, 403)]
    [InlineData(InvoiceStatus.ISSUED, true, 200)]
    [InlineData(InvoiceStatus.OVERDUE, true, 200)]
    [InlineData(InvoiceStatus.PAID, true, 200)]
    [InlineData(InvoiceStatus.CANCELLED, true, 200)]
    public async Task Customer_VisibilityUsesIssuedAt_ManagersSeeAll_AndOwnershipIsCheckedFirst(InvoiceStatus status, bool issued, int expected)
    {
        await using var db = TestSupport.CreateDb(); var (manager, customer, invoice, _) = await SeedAsync(db);
        invoice.Status = status; if (!issued) invoice.IssuedAt = null; await db.SaveChangesAsync();
        var service = CreateService(db);
        Assert.Equal(4, (await service.GetStatusHistoryAsync(invoice.InvoiceId, manager.AccountId)).Count);
        if (expected == 200) Assert.Equal(3, (await service.GetStatusHistoryAsync(invoice.InvoiceId, customer.AccountId)).Count);
        else Assert.Equal("FORBIDDEN", (await Assert.ThrowsAsync<ForbiddenException>(() => service.GetStatusHistoryAsync(invoice.InvoiceId, customer.AccountId))).ErrorCode);
        var stranger = await TestSupport.AddCustomerAsync(db);
        var error = await Assert.ThrowsAsync<NotFoundException>(() => service.GetStatusHistoryAsync(invoice.InvoiceId, stranger.Account.AccountId));
        Assert.Equal("INVOICE_NOT_FOUND", error.ErrorCode); Assert.Equal("Invoice not found", error.Message);
    }

    [Theory]
    [InlineData(EmployeeRole.FLEET_ROUTE_COORDINATOR)]
    [InlineData(EmployeeRole.TRANSPORT_SPECIALIST)]
    [InlineData(EmployeeRole.DRIVER)]
    [InlineData(EmployeeRole.ESCORT)]
    public async Task OtherEmployees_AreForbiddenBeforeInvoiceLookup(EmployeeRole role)
    {
        await using var db = TestSupport.CreateDb(); var (account, employee) = await TestSupport.AddLogisticsManagerAsync(db);
        employee.Role = role; await db.SaveChangesAsync();
        Assert.Equal("FORBIDDEN", (await Assert.ThrowsAsync<ForbiddenException>(() =>
            CreateService(db).GetStatusHistoryAsync(Guid.NewGuid(), account.AccountId))).ErrorCode);
    }

    [Fact]
    public async Task CancelReasonAndInactiveHistoricalActor_ArePreserved_UnknownActorsAreNotSystem()
    {
        await using var db = TestSupport.CreateDb(); var (manager, customer, invoice, histories) = await SeedAsync(db);
        db.InvoiceStatusHistories.Remove(histories[3]); invoice.Status = InvoiceStatus.CANCELLED; invoice.PaidAt = null;
        var actor = await db.Employees.SingleAsync(x => x.AccountId == histories[0].ChangedByAccountId);
        actor.IsActive = false; actor.IsDeleted = true;
        var cancelled = History(invoice, InvoiceHistoryType.CANCEL, InvoiceStatus.OVERDUE, InvoiceStatus.CANCELLED, actor.AccountId, invoice.UpdatedAt);
        cancelled.Reason = "  Wrong unit price <keep exactly>  "; db.InvoiceStatusHistories.Add(cancelled);
        var unknown = History(invoice, InvoiceHistoryType.ISSUE, InvoiceStatus.DRAFT, InvoiceStatus.ISSUED, customer.AccountId, invoice.UpdatedAt.AddHours(1));
        db.InvoiceStatusHistories.Add(unknown); await db.SaveChangesAsync();
        var result = await CreateService(db).GetStatusHistoryAsync(invoice.InvoiceId, manager.AccountId);
        var cancel = Assert.Single(result, x => x.Type == "CANCEL");
        Assert.Equal(cancelled.Reason, cancel.Reason); Assert.Equal("Le Hoang Phuc", cancel.ChangedBy.FullName);
        Assert.Equal("LOGISTICS_MANAGER", cancel.ChangedBy.Role);
        Assert.Equal(customer.AccountId, result[^1].ChangedBy.AccountId); Assert.Equal("Unknown", result[^1].ChangedBy.FullName); Assert.Null(result[^1].ChangedBy.Role);
    }

    [Fact]
    public async Task EmptyHistoryAndMissingInvoice_HaveDifferentResponses_OrderingBreaksTiesById()
    {
        await using var db = TestSupport.CreateDb(); var (manager, customer, invoice, histories) = await SeedAsync(db);
        db.InvoiceStatusHistories.RemoveRange(histories); await db.SaveChangesAsync();
        Assert.Empty(await CreateService(db).GetStatusHistoryAsync(invoice.InvoiceId, manager.AccountId));
        Assert.Empty(await CreateService(db).GetStatusHistoryAsync(invoice.InvoiceId, customer.AccountId));
        foreach (var caller in new[] { manager.AccountId, customer.AccountId })
            Assert.Equal("INVOICE_NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(() =>
                CreateService(db).GetStatusHistoryAsync(Guid.NewGuid(), caller))).ErrorCode);
        var first = History(invoice, InvoiceHistoryType.ISSUE, InvoiceStatus.DRAFT, InvoiceStatus.ISSUED, manager.AccountId, invoice.CreatedAt);
        var second = History(invoice, InvoiceHistoryType.MARK_OVERDUE, InvoiceStatus.ISSUED, InvoiceStatus.OVERDUE, null, invoice.CreatedAt);
        first.InvoiceStatusHistoryId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        second.InvoiceStatusHistoryId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        db.InvoiceStatusHistories.AddRange(second, first); await db.SaveChangesAsync();
        Assert.Equal(new[] { first.InvoiceStatusHistoryId, second.InvoiceStatusHistoryId },
            (await CreateService(db).GetStatusHistoryAsync(invoice.InvoiceId, manager.AccountId)).Select(x => x.HistoryId));
    }
}
