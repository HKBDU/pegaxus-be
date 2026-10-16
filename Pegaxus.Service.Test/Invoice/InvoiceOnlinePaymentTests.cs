using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;

using Invoices = Pegaxus.Service.InvoiceService;
using AppException = Pegaxus.Service.Exceptions.Exceptions;

namespace Pegaxus.Service.Test;

public class InvoiceOnlinePaymentTests
{
    internal static readonly DateTime Now = new(2026, 11, 10, 4, 0, 0);
    internal static readonly NullLogger<Invoices.Service> Logger = NullLogger<Invoices.Service>.Instance;
    internal sealed class Gateway : Pegaxus.Service.PaymentGatewayService.IPaymentGatewayService
    {
        internal int Calls;
        internal string? ReceiptCode;
        internal decimal Amount;
        internal string? Currency;
        internal Guid? BookingId;
        internal string PaymentUrl = "https://gateway.example/pay/test";
        internal Exception? Failure;
        internal Func<CancellationToken, Task<string>>? OnCreate;
        public async Task<Pegaxus.Service.PaymentGatewayService.Checkout> CreateCheckoutAsync(Guid invoiceId, string code,
            decimal amount, string currency, Guid? bookingId = null, CancellationToken token = default)
        {
            Calls++; ReceiptCode = code; Amount = amount; Currency = currency; BookingId = bookingId;
            if (Failure is not null) throw Failure;
            var url = OnCreate is null ? PaymentUrl : await OnCreate(token);
            return new(url, new Dictionary<string, string>
            {
                ["order_invoice_number"] = code, ["order_amount"] = amount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["currency"] = currency
            });
        }
    }
    internal static Invoices.Service CreateService(AppDbContext db, Pegaxus.Service.PaymentGatewayService.IPaymentGatewayService gateway) =>
        InvoicePaymentTests.CreateService(db, gateway: gateway);

    internal static async Task<(Account Customer, Account Manager, Invoice Invoice)> SeedAsync(AppDbContext db)
    {
        var (customer, profile) = await TestSupport.AddCustomerAsync(db);
        var (manager, employee) = await TestSupport.AddLogisticsManagerAsync(db);
        var (booking, _) = await TestSupport.AddPendingBookingAsync(db, profile);
        booking.Status = BookingStatus.APPROVED;
        var invoice = new Invoice
        {
            InvoiceId = Guid.NewGuid(), InvoiceCode = "INV-20261105-0001", BookingId = booking.BookingId,
            Booking = booking, EmployeeId = employee.EmployeeId, Employee = employee, Type = InvoiceType.FINAL,
            Status = InvoiceStatus.ISSUED, Currency = "VND", Subtotal = 126500000, TotalAmount = 151800000,
            IssuedAt = Now.AddDays(-5), CreatedAt = Now.AddDays(-5), UpdatedAt = Now.AddDays(-5)
        };
        db.Invoices.Add(invoice); await db.SaveChangesAsync();
        return (customer, manager, invoice);
    }

    internal static Receipt Receipt(Invoice invoice, ReceiptStatus status, string code) => new()
    {
        ReceiptId = Guid.NewGuid(), InvoiceId = invoice.InvoiceId, ReceiptCode = code, Status = status,
        Amount = invoice.TotalAmount, Currency = invoice.Currency, PaymentMethod = PaymentMethod.BANK_TRANSFER,
        CreatedAt = Now.AddDays(-1), UpdatedAt = Now.AddDays(-1)
    };

    [Theory]
    [InlineData(InvoiceType.DEPOSIT, InvoiceStatus.ISSUED, "VND")]
    [InlineData(InvoiceType.DEPOSIT, InvoiceStatus.OVERDUE, "EUR")]
    [InlineData(InvoiceType.FINAL, InvoiceStatus.ISSUED, "EUR")]
    [InlineData(InvoiceType.FINAL, InvoiceStatus.OVERDUE, "VND")]
    [InlineData(InvoiceType.SUPPLEMENTARY, InvoiceStatus.ISSUED, "VND")]
    [InlineData(InvoiceType.SUPPLEMENTARY, InvoiceStatus.OVERDUE, "EUR")]
    public async Task Initiation_UsesFullInvoiceAmount_ReplacesPending_WithoutConfirmingPayment(
        InvoiceType type, InvoiceStatus status, string currency)
    {
        await using var db = TestSupport.CreateDb();
        var (customer, _, invoice) = await SeedAsync(db);
        invoice.Type = type; invoice.Status = status; invoice.Currency = currency; invoice.TotalAmount = 12345;
        var previous = Receipt(invoice, ReceiptStatus.PENDING, "RCP-20261109-0001");
        var failed = Receipt(invoice, ReceiptStatus.FAILED, "RCP-20261109-0002");
        db.Receipts.AddRange(previous, failed); await db.SaveChangesAsync();
        var gateway = new Gateway();
        var result = await CreateService(db, gateway).InitiateOnlinePaymentAsync(invoice.InvoiceId, customer.AccountId);
        Assert.Equal(ReceiptStatus.FAILED, previous.Status); Assert.Equal(Now, previous.UpdatedAt);
        Assert.Equal(Now.AddDays(-1), failed.UpdatedAt);
        var receipt = await db.Receipts.SingleAsync(x => x.ReceiptId == result.Receipt.ReceiptId);
        Assert.Equal(ReceiptStatus.PENDING, receipt.Status); Assert.Equal(PaymentMethod.BANK_TRANSFER, receipt.PaymentMethod);
        Assert.Equal(12345, result.Receipt.Amount); Assert.Equal(currency, result.Receipt.Currency);
        Assert.Matches("^RCP-20261110-[0-9]{4}$", result.Receipt.ReceiptCode);
        Assert.Equal(receipt.ReceiptCode, gateway.ReceiptCode); Assert.Equal(12345m, gateway.Amount); Assert.Equal(currency, gateway.Currency);
        Assert.Equal(gateway.PaymentUrl, result.PaymentUrl); Assert.Equal(1, gateway.Calls);
        Assert.Equal(invoice.BookingId, gateway.BookingId);
        Assert.Null(result.Receipt.TransactionReference); Assert.Null(result.Receipt.PaidAt);
        Assert.Null(result.Receipt.ConfirmedAt); Assert.Null(result.Receipt.RecordedBy); Assert.Null(result.Receipt.Note);
        Assert.Equal(status, invoice.Status); Assert.Null(invoice.PaidAt); Assert.Equal(Now.AddDays(-5), invoice.UpdatedAt);
        Assert.Empty(await db.InvoiceStatusHistories.ToListAsync()); Assert.Empty(await db.Notifications.ToListAsync());
        Assert.Empty(await db.OutboxMessages.ToListAsync());
    }

    [Theory]
    [InlineData(InvoiceType.QUOTATION, InvoiceStatus.PAID, "UNPAYABLE_INVOICE_TYPE", 422)]
    [InlineData(InvoiceType.CREDIT_NOTE, InvoiceStatus.CANCELLED, "UNPAYABLE_INVOICE_TYPE", 422)]
    [InlineData(InvoiceType.FINAL, InvoiceStatus.DRAFT, "INVALID_INVOICE_STATUS", 409)]
    [InlineData(InvoiceType.FINAL, InvoiceStatus.PAID, "INVALID_INVOICE_STATUS", 409)]
    [InlineData(InvoiceType.DEPOSIT, InvoiceStatus.CANCELLED, "INVALID_INVOICE_STATUS", 409)]
    [InlineData(InvoiceType.SUPPLEMENTARY, InvoiceStatus.PARTIALLY_PAID, "INVALID_INVOICE_STATUS", 409)]
    public async Task InvalidType_PrecedesStatus_WithoutCallingGateway(InvoiceType type, InvoiceStatus status, string code, int http)
    {
        await using var db = TestSupport.CreateDb();
        var (customer, _, invoice) = await SeedAsync(db);
        invoice.Type = type; invoice.Status = status; await db.SaveChangesAsync();
        var gateway = new Gateway();
        var error = await Assert.ThrowsAnyAsync<AppException>(() => CreateService(db, gateway).InitiateOnlinePaymentAsync(invoice.InvoiceId, customer.AccountId));
        Assert.Equal(code, error.ErrorCode); Assert.Equal(http, error.StatusCode);
        Assert.Equal(0, gateway.Calls); Assert.Empty(await db.Receipts.ToListAsync());
    }

    [Fact]
    public async Task MissingForeignAndUnissuedInvoices_AreHidden_BeforeTypeAndStatusChecks()
    {
        await using var db = TestSupport.CreateDb();
        var (customer, _, invoice) = await SeedAsync(db);
        var stranger = await TestSupport.AddCustomerAsync(db);
        invoice.Type = InvoiceType.QUOTATION; invoice.Status = InvoiceStatus.DRAFT; invoice.IssuedAt = null;
        await db.SaveChangesAsync(); var gateway = new Gateway(); var service = CreateService(db, gateway);
        foreach (var (id, actor) in new[] { (Guid.NewGuid(), customer.AccountId), (invoice.InvoiceId, customer.AccountId), (invoice.InvoiceId, stranger.Account.AccountId) })
        {
            var error = await Assert.ThrowsAsync<NotFoundException>(() => service.InitiateOnlinePaymentAsync(id, actor));
            Assert.Equal("INVOICE_NOT_FOUND", error.ErrorCode); Assert.Equal("Invoice not found", error.Message);
        }
        Assert.Equal(0, gateway.Calls); Assert.Empty(await db.Receipts.ToListAsync());
    }

    [Fact]
    public async Task ExistingConfirmationAndNonPositiveTotals_AreRejected()
    {
        await using var db = TestSupport.CreateDb();
        var (customer, _, invoice) = await SeedAsync(db);
        var confirmed = Receipt(invoice, ReceiptStatus.CONFIRMED, "RCP-20261109-0001");
        db.Receipts.Add(confirmed); await db.SaveChangesAsync(); var gateway = new Gateway(); var service = CreateService(db, gateway);
        Assert.Equal("INVALID_INVOICE_STATUS", (await Assert.ThrowsAsync<ConflictException>(() => service.InitiateOnlinePaymentAsync(invoice.InvoiceId, customer.AccountId))).ErrorCode);
        db.Receipts.Remove(confirmed); invoice.TotalAmount = 0; await db.SaveChangesAsync();
        Assert.Equal("INVOICE_TOTAL_NOT_POSITIVE", (await Assert.ThrowsAsync<UnprocessableEntityException>(() => service.InitiateOnlinePaymentAsync(invoice.InvoiceId, customer.AccountId))).ErrorCode);
        Assert.Equal(0, gateway.Calls);
    }

    [Fact]
    public async Task AccountStateAndDatabaseRole_AreCheckedIndependentlyOfClaims()
    {
        await using var db = TestSupport.CreateDb();
        var (customer, manager, invoice) = await SeedAsync(db); var gateway = new Gateway(); var service = CreateService(db, gateway);
        await Assert.ThrowsAsync<ForbiddenException>(() => service.InitiateOnlinePaymentAsync(invoice.InvoiceId, manager.AccountId));
        await Assert.ThrowsAsync<UnauthorizedException>(() => service.InitiateOnlinePaymentAsync(invoice.InvoiceId, Guid.NewGuid()));
        customer.Status = AccountStatus.SUSPENDED; await db.SaveChangesAsync();
        await Assert.ThrowsAsync<UnauthorizedException>(() => service.InitiateOnlinePaymentAsync(invoice.InvoiceId, customer.AccountId));
        customer.Status = AccountStatus.ACTIVE; customer.CustomerAccount!.IsDeleted = true; await db.SaveChangesAsync();
        await Assert.ThrowsAsync<UnauthorizedException>(() => service.InitiateOnlinePaymentAsync(invoice.InvoiceId, customer.AccountId));
        Assert.Equal(0, gateway.Calls);
    }

    [Theory]
    [InlineData("http://gateway.example/pay/test")]
    [InlineData("javascript:alert(1)")]
    [InlineData("/relative/payment")]
    [InlineData("https://user:password@gateway.example/pay/test")]
    public async Task InvalidGatewayUrl_ReturnsGatewayError(string paymentUrl)
    {
        await using var db = TestSupport.CreateDb();
        var (customer, _, invoice) = await SeedAsync(db); var gateway = new Gateway { PaymentUrl = paymentUrl };
        var error = await Assert.ThrowsAsync<AppException>(() => CreateService(db, gateway).InitiateOnlinePaymentAsync(invoice.InvoiceId, customer.AccountId));
        Assert.Equal(502, error.StatusCode); Assert.Equal("PAYMENT_GATEWAY_ERROR", error.ErrorCode);
    }
}
