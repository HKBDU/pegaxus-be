using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.InvoiceService.Validator;
using DetailService = Pegaxus.Service.InvoiceService.Service;

namespace Pegaxus.Service.Test;

public class InvoiceDetailTests
{
    [Fact]
    public async Task Detail_MapsContractFieldsDeductionsReceiptsAndHistoricalRecorderWithoutWrites()
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        db.ChangeTracker.Clear();
        var result = await f.Service.GetAsync(f.Booking.Customer.AccountId, f.Invoice.InvoiceId);
        Assert.Equal(f.Invoice.InvoiceId, result.InvoiceId);
        Assert.Equal(f.Invoice.InvoiceCode, result.InvoiceNumber);
        Assert.Equal("FINAL", result.Type);
        Assert.Equal("PAID", result.Status);
        Assert.True(result.IsPaymentRequired);
        Assert.Equal(new DateOnly(2026, 10, 5), result.InvoiceDate);
        Assert.Equal(f.Invoice.DueDate, result.DueDate);
        Assert.Equal("EUR", result.Currency);
        Assert.Equal("Final settlement", result.Note);
        Assert.Null(result.Payment);
        Assert.Equal(f.Booking.Customer.CustomerId, result.Customer.CustomerId);
        Assert.Equal(f.Booking.Customer.CustomerCode, result.Customer.CustomerCode);
        Assert.Equal("Club Alpha", result.Customer.Name);
        Assert.Equal("+33123456789", result.Customer.Phone);
        Assert.Equal("Paris", result.Customer.Address);
        Assert.Equal(f.Booking.Customer.Account.Email, result.Customer.Email);
        Assert.Equal(f.Creator.FullName, result.ContactPerson.Name);
        Assert.Equal(f.Account.Email, result.ContactPerson.Email);
        Assert.Equal(f.Creator.Phone, result.ContactPerson.Phone);
        Assert.Equal(f.Items.Select(item => item.InvoiceItemId), result.Items.Select(item => item.InvoiceItemId));
        Assert.Equal(2, result.Items.Count(item => item.IsDepositDeduction));
        Assert.Equal(f.Horse.HorseId, result.Items[0].Horse!.HorseId);
        Assert.Equal(f.Horse.HorseName, result.Items[0].Horse!.HorseName);
        Assert.Equal(2, result.Items[0].Quantity);
        Assert.Equal(50, result.Items[0].Price);
        Assert.Equal(100, result.Items[0].Total);
        Assert.Equal(-30, result.Items[1].Price);
        Assert.Equal(-30, result.Items[1].Total);
        Assert.Null(result.Items[1].Horse);
        Assert.All(result.Items, item => { Assert.Null(item.ItemDate); Assert.Null(item.EmergencyCostRequestId); });
        Assert.Equal(105, result.Summary.Subtotal);
        Assert.Equal(0, result.Summary.Discount);
        Assert.Equal(21, result.Summary.Tax);
        Assert.Equal(20, result.Summary.TaxRatePercent);
        Assert.Equal(-40, result.Summary.DepositDeduction);
        Assert.Equal(86, result.Summary.GrandTotal);
        Assert.Equal(f.Booking.BookingId, result.Booking.BookingId);
        Assert.Equal(f.Booking.BookingCode, result.Booking.BookingCode);
        Assert.Equal("APPROVED", result.Booking.Status);
        Assert.Equal(f.Booking.OriginAddress, result.Booking.OriginAddress);
        Assert.Equal(f.Booking.OriginCountry, result.Booking.OriginCountry);
        Assert.Equal(f.Booking.DestinationAddress, result.Booking.DestinationAddress);
        Assert.Equal(f.Booking.DestinationCountry, result.Booking.DestinationCountry);
        Assert.Equal(f.Booking.RequestedDepartureDate, result.Booking.RequestedDepartureDate);
        Assert.Equal(f.Transport.TransportId, result.Transport!.TransportId);
        Assert.Equal(f.Transport.TransportCode, result.Transport.TransportCode);
        Assert.Equal("COMPLETED", result.Transport.Status);
        Assert.Equal(f.LatestPlan.TransportPlanId, result.TransportPlan!.TransportPlanId);
        Assert.Equal(f.LatestPlan.PlanCode, result.TransportPlan.PlanCode);
        Assert.Equal("APPROVED", result.TransportPlan.Status);
        Assert.Equal(f.Receipts.Select(item => item.ReceiptId), result.Receipts.Select(item => item.ReceiptId));
        var receipt = result.Receipts[2];
        Assert.Equal(86, receipt.Amount);
        Assert.Equal("EUR", receipt.Currency);
        Assert.Equal("BANK_TRANSFER", receipt.PaymentMethod);
        Assert.Equal("CONFIRMED", receipt.Status);
        Assert.Equal("BANK-123", receipt.TransactionReference);
        Assert.Equal("Matched with statement", receipt.Note);
        Assert.Equal(Fixture.Offset(Fixture.Day(6)), receipt.PaidAt);
        Assert.Equal(Fixture.Offset(Fixture.Day(8)), receipt.ConfirmedAt);
        Assert.Equal(f.Recorder.AccountId, receipt.RecordedBy!.AccountId);
        Assert.Equal(f.Recorder.FullName, receipt.RecordedBy.FullName);
        Assert.All(result.Receipts.Take(2), item => Assert.Null(item.RecordedBy));
        Assert.Equal(Fixture.Offset(f.Invoice.IssuedAt!.Value), result.IssuedAt);
        Assert.Equal(Fixture.Offset(f.Invoice.PaidAt!.Value), result.PaidAt);
        Assert.Null(result.CancelledAt);
        Assert.Null(result.CancellationReason);
        Assert.Equal(Fixture.Offset(f.Invoice.CreatedAt), result.CreatedAt);
        Assert.Equal(Fixture.Offset(f.Invoice.UpdatedAt), result.UpdatedAt);
        Assert.Empty(result.AvailableActions);
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Equal(2, await db.Invoices.CountAsync());
        Assert.Equal(3, await db.InvoiceStatusHistories.CountAsync());
        Assert.Empty(await db.Notifications.ToListAsync());
        Assert.Empty(await db.OutboxMessages.ToListAsync());
    }

    [Fact]
    public async Task Ownership_Returns404ForMissingOrForeignDraftAnd403OnlyForOwnedUnissuedInvoice()
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        foreach (var id in new[] { Guid.NewGuid(), f.OtherInvoice.InvoiceId })
        {
            var error = await Assert.ThrowsAsync<NotFoundException>(() => f.Service.GetAsync(f.Booking.Customer.AccountId, id));
            Assert.Equal("INVOICE_NOT_FOUND", error.ErrorCode);
            Assert.Equal("Invoice not found", error.Message);
        }
        foreach (var status in new[] { InvoiceStatus.DRAFT, InvoiceStatus.CANCELLED })
        {
            f.Invoice.Status = status; f.Invoice.IssuedAt = null;
            await db.SaveChangesAsync();
            var error = await Assert.ThrowsAsync<ForbiddenException>(() => f.Service.GetAsync(f.Booking.Customer.AccountId, f.Invoice.InvoiceId));
            Assert.Equal("Customer cannot view draft invoices", error.Message);
            Assert.Equal(status.ToString(), (await f.Service.GetAsync(f.Account.AccountId, f.Invoice.InvoiceId)).Status);
        }
        f.Invoice.IssuedAt = Fixture.Day(5); f.Invoice.CancelledAt = Fixture.Day(9); f.Invoice.CancellationReason = "Booking cancelled";
        await db.SaveChangesAsync();
        var result = await f.Service.GetAsync(f.Booking.Customer.AccountId, f.Invoice.InvoiceId);
        Assert.Equal("CANCELLED", result.Status);
        Assert.Equal(Fixture.Offset(Fixture.Day(9)), result.CancelledAt);
        Assert.Equal("Booking cancelled", result.CancellationReason);
        f.Invoice.Status = InvoiceStatus.DRAFT;
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ForbiddenException>(() => f.Service.GetAsync(f.Booking.Customer.AccountId, f.Invoice.InvoiceId));
        var invalid = await Assert.ThrowsAsync<FieldValidationException>(() => f.Service.GetAsync(f.Account.AccountId, Guid.Empty));
        Assert.Contains(invalid.Errors, item => item.Field == "invoiceId");
    }

    [Fact]
    public async Task OptionalDetails_UseCreationDateEmptyArraysNullTransportAndGatewayRecorder()
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        var result = await f.Service.GetAsync(f.Account.AccountId, f.OtherInvoice.InvoiceId);
        Assert.Equal(DateOnly.FromDateTime(f.OtherInvoice.CreatedAt), result.InvoiceDate);
        Assert.Empty(result.Items);
        Assert.Empty(result.Receipts);
        Assert.Null(result.Transport);
        Assert.Null(result.TransportPlan);
        Assert.Null(result.DueDate);
        Assert.Null(result.Customer.Phone);
        Assert.Null(result.Customer.Address);
        Assert.Equal(0, result.Summary.DepositDeduction);
        var payment = await db.InvoiceStatusHistories.SingleAsync(item => item.InvoiceId == f.Invoice.InvoiceId && item.Type == InvoiceHistoryType.RECORD_PAYMENT);
        payment.ChangedByAccountId = null;
        await db.SaveChangesAsync();
        result = await f.Service.GetAsync(f.Booking.Customer.AccountId, f.Invoice.InvoiceId);
        Assert.Null(result.Receipts.Single(item => item.Status == "CONFIRMED").RecordedBy);
        db.InvoiceStatusHistories.Remove(payment);
        await db.SaveChangesAsync();
        Assert.Null((await f.Service.GetAsync(f.Account.AccountId, f.Invoice.InvoiceId)).Receipts.Single(item => item.Status == "CONFIRMED").RecordedBy);
    }

    [Theory]
    [InlineData(true, InvoiceStatus.DRAFT, InvoiceType.FINAL, false, new[] { "EDIT", "ISSUE", "CANCEL", "CREATE_ANOTHER" })]
    [InlineData(true, InvoiceStatus.ISSUED, InvoiceType.FINAL, false, new[] { "RECORD_PAYMENT", "CANCEL", "CREATE_ANOTHER" })]
    [InlineData(true, InvoiceStatus.OVERDUE, InvoiceType.CREDIT_NOTE, false, new[] { "RECORD_PAYMENT", "CANCEL", "CREATE_ANOTHER" })]
    [InlineData(true, InvoiceStatus.ISSUED, InvoiceType.QUOTATION, false, new[] { "CANCEL", "CREATE_ANOTHER" })]
    [InlineData(true, InvoiceStatus.PAID, InvoiceType.FINAL, true, new[] { "CREATE_ANOTHER" })]
    [InlineData(true, InvoiceStatus.CANCELLED, InvoiceType.FINAL, false, new[] { "CREATE_ANOTHER" })]
    [InlineData(true, InvoiceStatus.ISSUED, InvoiceType.FINAL, true, new[] { "CREATE_ANOTHER" })]
    [InlineData(false, InvoiceStatus.ISSUED, InvoiceType.DEPOSIT, false, new[] { "PAY_NOW" })]
    [InlineData(false, InvoiceStatus.ISSUED, InvoiceType.FINAL, false, new[] { "PAY_NOW" })]
    [InlineData(false, InvoiceStatus.OVERDUE, InvoiceType.SUPPLEMENTARY, false, new[] { "PAY_NOW" })]
    [InlineData(false, InvoiceStatus.ISSUED, InvoiceType.QUOTATION, false, new string[0])]
    [InlineData(false, InvoiceStatus.ISSUED, InvoiceType.CREDIT_NOTE, false, new string[0])]
    [InlineData(false, InvoiceStatus.PAID, InvoiceType.FINAL, true, new string[0])]
    [InlineData(false, InvoiceStatus.CANCELLED, InvoiceType.FINAL, false, new string[0])]
    [InlineData(false, InvoiceStatus.ISSUED, InvoiceType.FINAL, true, new string[0])]
    public async Task AvailableActions_FollowRoleTypeStatusAndConfirmedReceipt(bool manager, InvoiceStatus status, InvoiceType type, bool confirmed, string[] expected)
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        f.Invoice.Status = status; f.Invoice.Type = type;
        if (!confirmed) db.Receipts.Remove(f.Receipts[2]);
        await db.SaveChangesAsync();
        var result = await f.Service.GetAsync(manager ? f.Account.AccountId : f.Booking.Customer.AccountId, f.Invoice.InvoiceId);
        Assert.Equal(expected, result.AvailableActions);
        Assert.Equal(type != InvoiceType.QUOTATION, result.IsPaymentRequired);
        Assert.Equal(type == InvoiceType.FINAL ? -40 : 0, result.Summary.DepositDeduction);
    }

    [Theory]
    [InlineData("role")]
    [InlineData("employeeInactive")]
    [InlineData("employeeDeleted")]
    [InlineData("accountInactive")]
    [InlineData("accountDeleted")]
    [InlineData("customerDeleted")]
    [InlineData("unknownAccount")]
    public async Task Authorization_RejectsInactiveProfilesAndStaleManagerRole(string problem)
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        switch (problem)
        {
            case "role": f.Creator.Role = EmployeeRole.DRIVER; break;
            case "employeeInactive": f.Creator.IsActive = false; break;
            case "employeeDeleted": f.Creator.IsDeleted = true; break;
            case "accountInactive": f.Account.Status = AccountStatus.INACTIVE; break;
            case "accountDeleted": f.Account.IsDeleted = true; break;
            case "customerDeleted": f.Booking.Customer.IsDeleted = true; break;
        }
        await db.SaveChangesAsync();
        var id = problem == "customerDeleted" ? f.Booking.Customer.AccountId : problem == "unknownAccount" ? Guid.NewGuid() : f.Account.AccountId;
        await Assert.ThrowsAsync<ForbiddenException>(() => f.Service.GetAsync(id, f.Invoice.InvoiceId));
    }

    internal sealed record Fixture(Account Account, Employee Creator, Employee Recorder, Booking Booking, Horse Horse, Transport Transport,
        TransportPlan LatestPlan, Invoice Invoice, Invoice OtherInvoice, List<InvoiceItem> Items, List<Receipt> Receipts, DetailService Service)
    {
        internal static async Task<Fixture> CreateAsync(AppDbContext db)
        {
            var (account, creator) = await TestSupport.AddLogisticsManagerAsync(db);
            var (_, recorder) = await TestSupport.AddLogisticsManagerAsync(db);
            creator.FullName = "Invoice Contact"; creator.Phone = "+84901234567"; recorder.FullName = "Payment Recorder";
            var (_, alpha) = await TestSupport.AddCustomerAsync(db);
            var (_, bravo) = await TestSupport.AddCustomerAsync(db);
            alpha.FullName = "Club Alpha"; alpha.Phone = "+33123456789"; alpha.Address = "Paris";
            var (booking, horse) = await TestSupport.AddPendingBookingAsync(db, alpha, true);
            var (other, _) = await TestSupport.AddPendingBookingAsync(db, bravo);
            booking.Status = BookingStatus.APPROVED;
            var transport = new Transport { TransportId = Guid.NewGuid(), TransportCode = "TR-DETAIL", BookingId = booking.BookingId,
                Status = TransportStatus.COMPLETED, CreatedAt = Day(1), UpdatedAt = Day(1) };
            var latest = Plan(transport.TransportId, 2, TransportPlanStatus.APPROVED);
            var invoice = new Invoice { InvoiceId = Guid.NewGuid(), InvoiceCode = "INV-DETAIL-0001", BookingId = booking.BookingId,
                EmployeeId = creator.EmployeeId, Type = InvoiceType.FINAL, Status = InvoiceStatus.PAID, Currency = "EUR", Subtotal = 105,
                TaxAmount = 21, TotalAmount = 86, BillingNote = "Final settlement", DueDate = new(2026, 10, 15), IssuedAt = Day(5), PaidAt = Day(8),
                CreatedAt = Day(1), UpdatedAt = Day(9) };
            var otherInvoice = new Invoice { InvoiceId = Guid.NewGuid(), InvoiceCode = "INV-DETAIL-0002", BookingId = other.BookingId,
                EmployeeId = creator.EmployeeId, Type = InvoiceType.QUOTATION, Status = InvoiceStatus.DRAFT, Currency = "VND", CreatedAt = Day(2), UpdatedAt = Day(2) };
            var items = new List<InvoiceItem>();
            for (var index = 0; index < 4; index++)
                items.Add(new InvoiceItem { InvoiceItemId = Guid.Parse($"00000000-0000-0000-0000-{index + 1:D12}"), InvoiceId = invoice.InvoiceId,
                    ItemType = index == 0 ? InvoiceItemType.HORSE_SERVICE_FEE : InvoiceItemType.OTHER,
                    Description = index == 0 ? "Horse care" : "Adjustment", HorseId = index == 0 ? horse!.HorseId : null,
                    Quantity = index == 0 ? 2 : 1, UnitPrice = index == 0 ? 50 : index == 1 ? -30 : index == 2 ? -10 : 5,
                    LineAmount = index == 0 ? 100 : index == 1 ? -30 : index == 2 ? -10 : 5,
                    Note = index is 1 or 2 ? "Deposit deduction" : null, CreatedAt = Day(2).AddSeconds(index), UpdatedAt = Day(2) });
            var receipts = new List<Receipt>();
            for (var index = 0; index < 3; index++)
                receipts.Add(new Receipt { ReceiptId = Guid.NewGuid(), ReceiptCode = $"RCP-DETAIL-{index + 1:D4}", InvoiceId = invoice.InvoiceId,
                    PaymentMethod = PaymentMethod.BANK_TRANSFER, Status = index == 0 ? ReceiptStatus.PENDING : index == 1 ? ReceiptStatus.FAILED : ReceiptStatus.CONFIRMED,
                    Amount = 86, Currency = "EUR", TransactionReference = index == 2 ? "BANK-123" : null,
                    PaidAt = index == 2 ? Day(6) : null, ConfirmedAt = index == 2 ? Day(8) : null,
                    Note = index == 2 ? "Matched with statement" : null, CreatedAt = Day(index + 6), UpdatedAt = Day(8) });
            db.AddRange(transport, Plan(transport.TransportId, 1, TransportPlanStatus.COMPLETED), latest, Plan(transport.TransportId, 3, TransportPlanStatus.CANCELLED), invoice, otherInvoice);
            db.InvoiceItems.AddRange(items.AsEnumerable().Reverse());
            db.Receipts.AddRange(receipts.AsEnumerable().Reverse());
            db.InvoiceStatusHistories.AddRange(
                History(invoice.InvoiceId, InvoiceHistoryType.RECORD_PAYMENT, recorder.AccountId, 8),
                History(invoice.InvoiceId, InvoiceHistoryType.ISSUE, creator.AccountId, 9),
                History(otherInvoice.InvoiceId, InvoiceHistoryType.RECORD_PAYMENT, creator.AccountId, 10));
            await db.SaveChangesAsync();
            return new(account, creator, recorder, booking, horse!, transport, latest, invoice, otherInvoice, items, receipts,
                new DetailService(db, TimeProvider.System, new CreateRequestValidator(TimeProvider.System), new SuggestionRequestValidator(),
                    new UpdateRequestValidator(TimeProvider.System), new ListRequestValidator(), new BookingListRequestValidator(),
                    new IssueRequestValidator(), new RecordPaymentRequestValidator(TimeProvider.System), new CancelRequestValidator(), new NotificationService.Service(db, new NotificationService.NoopNotificationRealtimeService())));
        }
        internal static DateTime Day(int day) => new(2026, 10, day, 0, 0, 0, DateTimeKind.Unspecified);
        internal static DateTimeOffset Offset(DateTime date) => new(DateTime.SpecifyKind(date, DateTimeKind.Utc));
        private static TransportPlan Plan(Guid transportId, int day, TransportPlanStatus status) => new()
        {
            TransportPlanId = Guid.NewGuid(), TransportId = transportId, PlanCode = $"TP-DETAIL-{day}", Status = status, CreatedAt = Day(day), UpdatedAt = Day(day)
        };
        private static InvoiceStatusHistory History(Guid invoiceId, InvoiceHistoryType type, Guid accountId, int day) => new()
        {
            InvoiceStatusHistoryId = Guid.NewGuid(), InvoiceId = invoiceId, Type = type, NewStatus = InvoiceStatus.PAID,
            ChangedByAccountId = accountId, ChangedAt = Day(day), CreatedAt = Day(day)
        };
    }
}
