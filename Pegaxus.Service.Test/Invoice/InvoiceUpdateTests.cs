using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.InvoiceService;
using Pegaxus.Service.InvoiceService.Validator;

using DraftInvoiceService = Pegaxus.Service.InvoiceService.Service;

namespace Pegaxus.Service.Test;

public class InvoiceUpdateTests
{
    [Fact]
    public async Task Replacement_KeepsEditedRowAddsNewRowDeletesOmissionsAndPreservesIdentity()
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        var keep = f.Invoice.InvoiceItems.First();
        var remove = f.Invoice.InvoiceItems.Last();
        var (editor, _) = await TestSupport.AddLogisticsManagerAsync(db);
        var request = f.Request();
        request.Type = " quotation ";
        request.Note = "Updated billing note";
        request.Items = [Fixture.Item(2, 5, keep.InvoiceItemId), Fixture.Item(3, 5)];
        request.Items[0].HorseId = f.Horse.HorseId;
        request.Items[0].Note = "Horse care";
        request.Items[1].ItemType = "EMERGENCY_COST";
        request.Items[1].ItemDate = JsonSerializer.SerializeToElement("ignored-invalid-date");
        request.Payment = JsonSerializer.SerializeToElement(new { method = "BANKING" });

        var result = await f.Service.UpdateAsync(editor.AccountId, f.Invoice.InvoiceId, request);

        Assert.Equal(f.Invoice.InvoiceCode, result.InvoiceNumber);
        Assert.Equal(f.Invoice.InvoiceId, result.InvoiceId);
        Assert.Equal("QUOTATION", result.Type);
        Assert.False(result.IsPaymentRequired);
        Assert.Equal("DRAFT", result.Status);
        Assert.Equal("VND", result.Currency);
        Assert.Equal(request.Note, result.Note);
        Assert.Equal(keep.InvoiceItemId, result.Items[0].InvoiceItemId);
        Assert.Equal(f.Horse.HorseName, result.Items[0].Horse!.HorseName);
        Assert.Equal("Horse care", result.Items[0].Note);
        Assert.NotEqual(remove.InvoiceItemId, result.Items[1].InvoiceItemId);
        Assert.Equal("EMERGENCY_COST", result.Items[1].ItemType);
        Assert.Equal(new Response.SummaryResponse(25, 5, 0, 30), result.Summary);
        Assert.Null(result.Payment);
        Assert.All(result.Items, item => { Assert.Null(item.ItemDate); Assert.Null(item.EmergencyCostRequestId); });
        Assert.Equal(f.Account.Email, result.ContactPerson.Email);
        Assert.Equal(f.Booking.Customer.Account.Email, result.Customer.Email);
        Assert.Equal(f.Plan.TransportPlanId, result.TransportPlan!.TransportPlanId);
        Assert.Equal(["EDIT", "ISSUE", "CANCEL", "CREATE_ANOTHER"], result.AvailableActions);
        Assert.Equal(Fixture.Version(f.Invoice.CreatedAt), result.CreatedAt);
        Assert.Equal(Fixture.Clock.GetUtcNow(), result.UpdatedAt);
        db.ChangeTracker.Clear();
        var saved = await db.Invoices.Include(item => item.InvoiceItems).SingleAsync();
        Assert.Equal(f.Employee.EmployeeId, saved.EmployeeId);
        Assert.Equal(f.Invoice.CreatedAt, saved.CreatedAt);
        Assert.Equal(InvoiceType.QUOTATION, saved.Type);
        Assert.Equal(InvoiceStatus.DRAFT, saved.Status);
        Assert.Null(saved.IssuedAt);
        Assert.Equal(keep.CreatedAt, saved.InvoiceItems.Single(item => item.InvoiceItemId == keep.InvoiceItemId).CreatedAt);
        Assert.Equal(2, saved.InvoiceItems.Count);
        Assert.DoesNotContain(saved.InvoiceItems, item => item.InvoiceItemId == remove.InvoiceItemId);
        Assert.Equal(result.Summary.GrandTotal, saved.TotalAmount);
        Assert.Equal(0, saved.DiscountAmount);
        var history = await db.InvoiceStatusHistories.SingleAsync();
        Assert.Equal(f.History.InvoiceStatusHistoryId, history.InvoiceStatusHistoryId);
        Assert.Equal(InvoiceHistoryType.CREATE, history.Type);
        Assert.Equal(f.History.ChangedAt, history.ChangedAt);
        Assert.Empty(await db.Receipts.ToListAsync());
        Assert.Empty(await db.Notifications.ToListAsync());
        Assert.Empty(await db.OutboxMessages.ToListAsync());
        Assert.Equal(TransportPlanStatus.DRAFT, (await db.TransportPlans.SingleAsync()).Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Final_RegeneratesDeductionsFromCurrentPaidDepositsIgnoringClientRows(bool sendOldIdWithoutFlag)
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db, InvoiceType.FINAL);
        var old = Fixture.Row(f.Invoice.InvoiceId, -999, f.Invoice.CreatedAt);
        db.InvoiceItems.Add(old);
        var deposit1 = f.Deposit(30_000_000);
        var deposit2 = f.Deposit(5_000_000);
        var cancelled = f.Deposit(99_000_000); cancelled.Status = InvoiceStatus.CANCELLED;
        db.Invoices.AddRange(deposit1, deposit2, cancelled);
        await db.SaveChangesAsync();
        var request = f.Request();
        request.Items = [Fixture.Item(1, 120_000_000), Fixture.Item(4, 5_000_000), Fixture.Item(1, 8_000_000), Fixture.Item(1, 3_500_000)];
        request.Items.Add(sendOldIdWithoutFlag ? Fixture.Item(1, 777, old.InvoiceItemId)
            : new() { InvoiceItemId = old.InvoiceItemId, IsDepositDeduction = true, Price = -999, HorseId = Guid.NewGuid() });
        var result = await f.Service.UpdateAsync(f.Account.AccountId, f.Invoice.InvoiceId, request);
        Assert.Equal(new Response.SummaryResponse(151_500_000, 30_300_000, -35_000_000, 146_800_000), result.Summary);
        Assert.Equal(2, result.Items.Count(item => item.IsDepositDeduction));
        Assert.DoesNotContain(result.Items, item => item.InvoiceItemId == old.InvoiceItemId);
        Assert.Contains(result.Items, item => item.Description == $"Deposit deduction ({deposit1.InvoiceCode})" && item.Total == -30_000_000);
        Assert.Contains(result.Items, item => item.Description == $"Deposit deduction ({deposit2.InvoiceCode})" && item.Total == -5_000_000);
        Assert.All(result.Items.Where(item => item.IsDepositDeduction), item => { Assert.Equal(1, item.Quantity); Assert.Null(item.Horse); });
        Assert.False(await db.InvoiceItems.AnyAsync(item => item.InvoiceItemId == old.InvoiceItemId));
        Assert.Equal(6, await db.InvoiceItems.CountAsync(item => item.InvoiceId == f.Invoice.InvoiceId));
        Assert.Single(await db.InvoiceStatusHistories.ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmptyDraft_AllowsZeroOrNegativeTotal(bool withPaidDeposit)
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db, withPaidDeposit ? InvoiceType.FINAL : InvoiceType.QUOTATION);
        if (withPaidDeposit) { db.Invoices.Add(f.Deposit(25)); await db.SaveChangesAsync(); }
        var result = await f.Service.UpdateAsync(f.Account.AccountId, f.Invoice.InvoiceId, f.Request());
        Assert.Equal(withPaidDeposit ? -25 : 0, result.Summary.GrandTotal);
        Assert.Equal(withPaidDeposit ? 1 : 0, result.Items.Count);
    }

    [Fact]
    public async Task Version_UsesUtcInstantAdvancesEvenWhenClockIsOlderAndRejectsStaleSave()
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        f.Invoice.UpdatedAt = Fixture.Clock.GetUtcNow().UtcDateTime.AddHours(1);
        f.Invoice.UpdatedAt = DateTime.SpecifyKind(f.Invoice.UpdatedAt, DateTimeKind.Unspecified);
        await db.SaveChangesAsync();
        var request = f.Request();
        request.LastUpdatedAt = request.LastUpdatedAt!.Value.ToOffset(TimeSpan.FromHours(7));
        var result = await f.Service.UpdateAsync(f.Account.AccountId, f.Invoice.InvoiceId, request);
        Assert.Equal(f.Invoice.UpdatedAt.Ticks + 10, result.UpdatedAt.UtcTicks);
        Assert.Equal("INVOICE_MODIFIED", (await Assert.ThrowsAsync<ConflictException>(() =>
            f.Service.UpdateAsync(f.Account.AccountId, f.Invoice.InvoiceId, request))).ErrorCode);
    }

    [Theory]
    [InlineData(InvoiceStatus.ISSUED)]
    [InlineData(InvoiceStatus.PAID)]
    [InlineData(InvoiceStatus.OVERDUE)]
    [InlineData(InvoiceStatus.CANCELLED)]
    [InlineData(InvoiceStatus.PARTIALLY_PAID)]
    public async Task NonDraft_IsLockedWithoutChanges(InvoiceStatus status)
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        f.Invoice.Status = status; await db.SaveChangesAsync();
        Assert.Equal("INVOICE_LOCKED", (await Assert.ThrowsAsync<ConflictException>(() =>
            f.Service.UpdateAsync(f.Account.AccountId, f.Invoice.InvoiceId, f.Request()))).ErrorCode);
        Assert.Equal(2, await db.InvoiceItems.CountAsync());
    }

    [Theory]
    [InlineData("role")]
    [InlineData("employeeInactive")]
    [InlineData("employeeDeleted")]
    [InlineData("accountInactive")]
    [InlineData("accountDeleted")]
    [InlineData("accountType")]
    [InlineData("unknownAccount")]
    public async Task Authorization_UsesLiveAccountAndEmployee(string problem)
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        switch (problem)
        {
            case "role": f.Employee.Role = EmployeeRole.DRIVER; break;
            case "employeeInactive": f.Employee.IsActive = false; break;
            case "employeeDeleted": f.Employee.IsDeleted = true; break;
            case "accountInactive": f.Account.Status = AccountStatus.INACTIVE; break;
            case "accountDeleted": f.Account.IsDeleted = true; break;
            case "accountType": f.Account.AccountType = AccountType.CUSTOMER; break;
        }
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ForbiddenException>(() => f.Service.UpdateAsync(
            problem == "unknownAccount" ? Guid.NewGuid() : f.Account.AccountId, f.Invoice.InvoiceId, f.Request()));
        Assert.Equal(2, await db.InvoiceItems.CountAsync());
    }

    [Fact]
    public async Task MissingInvoice_ReturnsNotFound()
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        Assert.Equal("INVOICE_NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(() =>
            f.Service.UpdateAsync(f.Account.AccountId, Guid.NewGuid(), f.Request()))).ErrorCode);
    }

    [Fact]
    public async Task ExistingForeignItemAndHorse_CannotBeReassignedToThisInvoice()
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        var other = f.Deposit(10);
        var foreignRow = Fixture.Row(other.InvoiceId, 10, f.Invoice.CreatedAt);
        db.AddRange(other, foreignRow);
        var (_, foreignHorse) = await TestSupport.AddPendingBookingAsync(db, f.Booking.Customer, withHorse: true);
        var request = f.Request();
        request.Items = [Fixture.Item(1, 100, foreignRow.InvoiceItemId)];
        var error = await Assert.ThrowsAsync<FieldValidationException>(() =>
            f.Service.UpdateAsync(f.Account.AccountId, f.Invoice.InvoiceId, request));
        Assert.Contains(error.Errors, item => item.Field == "items[0].invoiceItemId");
        request.Items[0].InvoiceItemId = f.Invoice.InvoiceItems.First().InvoiceItemId;
        request.Items[0].HorseId = foreignHorse!.HorseId;
        error = await Assert.ThrowsAsync<FieldValidationException>(() =>
            f.Service.UpdateAsync(f.Account.AccountId, f.Invoice.InvoiceId, request));
        Assert.Contains(error.Errors, item => item.Field == "items[0].horseId");
        Assert.Equal(other.InvoiceId, (await db.InvoiceItems.AsNoTracking().SingleAsync(item => item.InvoiceItemId == foreignRow.InvoiceItemId)).InvoiceId);
        Assert.Equal(2, await db.InvoiceItems.CountAsync(item => item.InvoiceId == f.Invoice.InvoiceId));
    }

    [Fact]
    public async Task Edit_DoesNotReapplyCreationGatesAndReturnsNullTransportWhenAbsent()
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db, InvoiceType.CREDIT_NOTE);
        f.Booking.Status = BookingStatus.CANCELLED;
        db.TransportPlans.Remove(f.Plan);
        db.Transports.Remove(f.Transport);
        await db.SaveChangesAsync();
        var result = await f.Service.UpdateAsync(f.Account.AccountId, f.Invoice.InvoiceId, f.Request());
        Assert.Equal("CREDIT_NOTE", result.Type);
        Assert.Equal("CANCELLED", result.Booking.Status);
        Assert.Null(result.Transport);
        Assert.Null(result.TransportPlan);
    }

    [Theory]
    [InlineData("version", "lastUpdatedAt")]
    [InlineData("type", "type")]
    [InlineData("dueDate", "dueDate")]
    [InlineData("missingDueDate", "dueDate")]
    [InlineData("missingItems", "items")]
    [InlineData("nullItem", "items[0]")]
    [InlineData("itemType", "items[0].itemType")]
    [InlineData("description", "items[0].description")]
    [InlineData("longDescription", "items[0].description")]
    [InlineData("quantity", "items[0].quantity")]
    [InlineData("quantityPrecision", "items[0].quantity")]
    [InlineData("quantityScale", "items[0].quantity")]
    [InlineData("price", "items[0].price")]
    [InlineData("pricePrecision", "items[0].price")]
    [InlineData("priceScale", "items[0].price")]
    [InlineData("horse", "items[0].horseId")]
    [InlineData("itemId", "items[0].invoiceItemId")]
    [InlineData("emptyItemId", "items[0].invoiceItemId")]
    [InlineData("duplicateItemId", "items")]
    [InlineData("overflow", "items")]
    [InlineData("taxOverflow", "items")]
    public async Task InvalidInput_ReportsCorrectFieldAndLeavesStoredRowsUnchanged(string problem, string field)
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        var request = f.Request();
        request.Items = [Fixture.Item(1, 100, f.Invoice.InvoiceItems.First().InvoiceItemId)];
        switch (problem)
        {
            case "version": request.LastUpdatedAt = null; break;
            case "type": request.Type = "FINAL"; break;
            case "dueDate": request.DueDate = request.DueDate!.Value.AddDays(-1); break;
            case "missingDueDate": request.DueDate = null; break;
            case "missingItems": request.Items = null; break;
            case "nullItem": request.Items[0] = null!; break;
            case "itemType": request.Items[0].ItemType = "1"; break;
            case "description": request.Items[0].Description = "  "; break;
            case "longDescription": request.Items[0].Description = new string('x', 501); break;
            case "quantity": request.Items[0].Quantity = 0; break;
            case "quantityPrecision": request.Items[0].Quantity = 10_000_000_000; break;
            case "quantityScale": request.Items[0].Quantity = 1.001m; break;
            case "price": request.Items[0].Price = -1; break;
            case "pricePrecision": request.Items[0].Price = 10_000_000_000_000_000; break;
            case "priceScale": request.Items[0].Price = 1.001m; break;
            case "horse": request.Items[0].HorseId = Guid.NewGuid(); break;
            case "itemId": request.Items[0].InvoiceItemId = Guid.NewGuid(); break;
            case "emptyItemId": request.Items[0].InvoiceItemId = Guid.Empty; break;
            case "duplicateItemId": request.Items.Add(request.Items[0]); break;
            case "overflow": request.Items[0].Quantity = 5000; request.Items[0].Price = 9_999_999_999_999_999; break;
            case "taxOverflow": request.Items[0].Price = 9_999_999_999_999_999; break;
        }
        var error = await Assert.ThrowsAsync<FieldValidationException>(() =>
            f.Service.UpdateAsync(f.Account.AccountId, f.Invoice.InvoiceId, request));
        Assert.Contains(error.Errors, item => item.Field == field);
        Assert.Equal(2, await db.InvoiceItems.CountAsync());
        Assert.Single(await db.InvoiceStatusHistories.ToListAsync());
        Assert.Equal(120, (await db.Invoices.AsNoTracking().SingleAsync()).TotalAmount);
    }

    internal sealed record Fixture(Account Account, Employee Employee, Booking Booking, Horse Horse,
        Transport Transport, TransportPlan Plan, Invoice Invoice, InvoiceStatusHistory History, DraftInvoiceService Service)
    {
        internal static TimeProvider Clock { get; } = new FixedClock();
        internal static async Task<Fixture> CreateAsync(AppDbContext db, InvoiceType type = InvoiceType.QUOTATION)
        {
            var (account, employee) = await TestSupport.AddLogisticsManagerAsync(db);
            var (_, customer) = await TestSupport.AddCustomerAsync(db);
            var (booking, horse) = await TestSupport.AddPendingBookingAsync(db, customer, withHorse: true);
            booking.Status = BookingStatus.APPROVED;
            var now = DateTime.SpecifyKind(Clock.GetUtcNow().UtcDateTime.AddDays(-1), DateTimeKind.Unspecified);
            var transport = new Transport { TransportId = Guid.NewGuid(), TransportCode = $"TR-{Guid.NewGuid():N}",
                BookingId = booking.BookingId, Status = TransportStatus.COMPLETED, CreatedAt = now, UpdatedAt = now };
            var plan = new TransportPlan { TransportPlanId = Guid.NewGuid(), PlanCode = $"TP-{Guid.NewGuid():N}",
                TransportId = transport.TransportId, Status = TransportPlanStatus.DRAFT, CreatedAt = now, UpdatedAt = now };
            var invoice = new Invoice { InvoiceId = Guid.NewGuid(), InvoiceCode = $"INV-{Guid.NewGuid():N}", BookingId = booking.BookingId,
                EmployeeId = employee.EmployeeId, Type = type, Currency = "VND", Subtotal = 100, TaxAmount = 20, TotalAmount = 120,
                DueDate = DateOnly.FromDateTime(Clock.GetUtcNow().UtcDateTime), CreatedAt = now, UpdatedAt = now };
            invoice.InvoiceItems = [Row(invoice.InvoiceId, 60, now), Row(invoice.InvoiceId, 40, now.AddHours(1))];
            var history = new InvoiceStatusHistory { InvoiceStatusHistoryId = Guid.NewGuid(), InvoiceId = invoice.InvoiceId,
                Type = InvoiceHistoryType.CREATE, NewStatus = InvoiceStatus.DRAFT, ChangedByAccountId = account.AccountId,
                CreatedAt = now, ChangedAt = now };
            db.AddRange(transport, plan, invoice, history);
            await db.SaveChangesAsync();
            return new(account, employee, booking, horse!, transport, plan, invoice, history,
                new DraftInvoiceService(db, new InvoiceOnlinePaymentTests.Gateway(), InvoiceOnlinePaymentTests.Logger,
                    Clock, new CreateRequestValidator(Clock), new SuggestionRequestValidator(), new UpdateRequestValidator(Clock),
                    new ListRequestValidator(), new BookingListRequestValidator(), new IssueRequestValidator(), new RecordPaymentRequestValidator(Clock), new CancelRequestValidator(),
                    new NotificationService.Service(db, new NotificationService.NoopNotificationRealtimeService())));
        }
        internal Request.UpdateRequest Request() => new()
        {
            LastUpdatedAt = Version(Invoice.UpdatedAt), DueDate = DateOnly.FromDateTime(Clock.GetUtcNow().UtcDateTime), Items = []
        };
        internal static Request.ItemRequest Item(decimal quantity, decimal price, Guid? id = null) => new()
        {
            InvoiceItemId = id, ItemType = "OTHER", Description = "Manual fee", Quantity = quantity, Price = price
        };
        internal static InvoiceItem Row(Guid invoiceId, decimal amount, DateTime created) => new()
        {
            InvoiceItemId = Guid.NewGuid(), InvoiceId = invoiceId, ItemType = InvoiceItemType.OTHER,
            Description = "Original fee", Quantity = 1, UnitPrice = amount, LineAmount = amount, CreatedAt = created, UpdatedAt = created
        };
        internal Invoice Deposit(decimal total) => new()
        {
            InvoiceId = Guid.NewGuid(), InvoiceCode = $"INV-{Guid.NewGuid():N}", BookingId = Booking.BookingId,
            EmployeeId = Employee.EmployeeId, Type = InvoiceType.DEPOSIT, Status = InvoiceStatus.PAID,
            Currency = "VND", TotalAmount = total, CreatedAt = Invoice.CreatedAt, UpdatedAt = Invoice.CreatedAt
        };
        internal static DateTimeOffset Version(DateTime time) => new(DateTime.SpecifyKind(time, DateTimeKind.Utc));
        private sealed class FixedClock : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => new(2026, 10, 12, 8, 0, 0, TimeSpan.Zero);
        }
    }
}
