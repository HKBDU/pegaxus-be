using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.InvoiceService;

namespace Pegaxus.Service.Test;

public class InvoiceDueDateOrderTests
{
    private static DateTimeOffset Clock => InvoiceTests.Fixture.Clock.GetUtcNow();

    private static DateTime Stored(int days) => DateTime.SpecifyKind(Clock.UtcDateTime.AddDays(days), DateTimeKind.Unspecified);

    private static Request.CreateRequest Create(string type, int dueInDays) => new() { Type = type, DueDate = Clock.AddDays(dueInDays), Items = [] };

    private static Invoice Existing(InvoiceTests.Fixture f, InvoiceType type, InvoiceStatus status, int dueInDays)
    {
        var invoice = type == InvoiceType.QUOTATION ? f.IssuedQuotation(status: status) : f.Invoice(type, status);
        invoice.DueDate = Stored(dueInDays);
        return invoice;
    }

    private static async Task<string> RejectedAsync(InvoiceTests.Fixture f, string type, int dueInDays)
    {
        var error = await Assert.ThrowsAsync<FieldValidationException>(() => f.Service.CreateAsync(f.Account.AccountId, f.Booking.BookingId, Create(type, dueInDays)));
        return Assert.Single(error.Errors, item => item.Field == "dueDate").Message;
    }

    [Fact]
    public async Task Deposit_MustFallDueOnALaterDayThanTheQuotation()
    {
        await using var db = TestSupport.CreateDb();
        var f = await InvoiceTests.Fixture.CreateAsync(db);
        f.Transport.Status = TransportStatus.READY;
        var quotation = Existing(f, InvoiceType.QUOTATION, InvoiceStatus.ISSUED, 5);
        db.Invoices.Add(quotation);
        await db.SaveChangesAsync();

        Assert.Contains($"after the QUOTATION invoice due date ({quotation.InvoiceCode}, {Stored(5):yyyy-MM-dd})", await RejectedAsync(f, "DEPOSIT", 5));
        await RejectedAsync(f, "DEPOSIT", 2);
        Assert.Empty(await db.Invoices.Where(item => item.Type == InvoiceType.DEPOSIT).ToListAsync());
        await f.Service.CreateAsync(f.Account.AccountId, f.Booking.BookingId, Create("DEPOSIT", 6));
    }

    [Fact]
    public async Task Final_MustFallDueAfterTheDeposit()
    {
        await using var db = TestSupport.CreateDb();
        var f = await InvoiceTests.Fixture.CreateAsync(db);
        db.Invoices.AddRange(Existing(f, InvoiceType.QUOTATION, InvoiceStatus.ISSUED, 5), Existing(f, InvoiceType.DEPOSIT, InvoiceStatus.ISSUED, 10));
        await db.SaveChangesAsync();

        Assert.Contains("after the DEPOSIT invoice due date", await RejectedAsync(f, "FINAL", 10));
        Assert.Contains("after the DEPOSIT invoice due date", await RejectedAsync(f, "FINAL", 7));
        await f.Service.CreateAsync(f.Account.AccountId, f.Booking.BookingId, Create("FINAL", 11));
    }

    [Fact]
    public async Task Supplementary_MustFollowTheLatestOfQuotationAndDeposit()
    {
        await using var db = TestSupport.CreateDb();
        var f = await InvoiceTests.Fixture.CreateAsync(db);
        db.Invoices.AddRange(Existing(f, InvoiceType.QUOTATION, InvoiceStatus.ISSUED, 5), Existing(f, InvoiceType.DEPOSIT, InvoiceStatus.PAID, 8));
        await db.SaveChangesAsync();

        Assert.Contains("after the DEPOSIT invoice due date", await RejectedAsync(f, "SUPPLEMENTARY", 8));
        await f.Service.CreateAsync(f.Account.AccountId, f.Booking.BookingId, Create("SUPPLEMENTARY", 9));
    }

    [Fact]
    public async Task CreditNote_MustFollowTheFinalInvoice()
    {
        await using var db = TestSupport.CreateDb();
        var f = await InvoiceTests.Fixture.CreateAsync(db);
        db.Invoices.Add(Existing(f, InvoiceType.FINAL, InvoiceStatus.PAID, 3));
        await db.SaveChangesAsync();

        Assert.Contains("after the FINAL invoice due date", await RejectedAsync(f, "CREDIT_NOTE", 3));
        await f.Service.CreateAsync(f.Account.AccountId, f.Booking.BookingId, Create("CREDIT_NOTE", 4));
    }

    [Fact]
    public async Task Quotation_MustFallDueBeforeAnExistingDeposit()
    {
        await using var db = TestSupport.CreateDb();
        var f = await InvoiceTests.Fixture.CreateAsync(db);
        db.Invoices.Add(Existing(f, InvoiceType.DEPOSIT, InvoiceStatus.ISSUED, 5));
        await db.SaveChangesAsync();

        Assert.Contains("before the DEPOSIT invoice due date", await RejectedAsync(f, "QUOTATION", 5));
        await f.Service.CreateAsync(f.Account.AccountId, f.Booking.BookingId, Create("QUOTATION", 4));
    }

    [Fact]
    public async Task CancelledInvoicesAndInvoicesWithoutADueDate_AreIgnored()
    {
        await using var db = TestSupport.CreateDb();
        var f = await InvoiceTests.Fixture.CreateAsync(db);
        f.Transport.Status = TransportStatus.READY;
        var open = Existing(f, InvoiceType.QUOTATION, InvoiceStatus.ISSUED, 5);
        open.DueDate = null;
        db.Invoices.AddRange(Existing(f, InvoiceType.QUOTATION, InvoiceStatus.CANCELLED, 20), open);
        await db.SaveChangesAsync();

        await f.Service.CreateAsync(f.Account.AccountId, f.Booking.BookingId, Create("DEPOSIT", 2));
    }

    [Fact]
    public async Task Update_AppliesTheSameOrder()
    {
        await using var db = TestSupport.CreateDb();
        var f = await InvoiceTests.Fixture.CreateAsync(db);
        db.Invoices.Add(Existing(f, InvoiceType.DEPOSIT, InvoiceStatus.ISSUED, 8));
        await db.SaveChangesAsync();
        var supplementary = await f.Service.CreateAsync(f.Account.AccountId, f.Booking.BookingId, Create("SUPPLEMENTARY", 9));

        var error = await Assert.ThrowsAsync<FieldValidationException>(() => f.Service.UpdateAsync(f.Account.AccountId, supplementary.InvoiceId,
            new() { LastUpdatedAt = supplementary.UpdatedAt, DueDate = Clock.AddDays(7), Items = [] }));

        Assert.Contains(error.Errors, item => item.Field == "dueDate" && item.Message.Contains("after the DEPOSIT invoice due date"));
    }
}
