using System.Data;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;
using AppException = Pegaxus.Service.Exceptions.Exceptions;

namespace Pegaxus.Service.InvoiceService;

public sealed class Service(AppDbContext db, PaymentGatewayService.IService gateway, TimeProvider clock,
    ILogger<Service> logger) : IService
{
    public async Task<Response.OnlinePaymentResponse> InitiateOnlinePaymentAsync(Guid invoiceId, Guid accountId,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : await db.Database.BeginTransactionAsync(cancellationToken);
        var account = await db.Accounts.AsNoTracking().Include(x => x.CustomerAccount)
            .SingleOrDefaultAsync(x => x.AccountId == accountId, cancellationToken);
        if (account is null || account.IsDeleted || account.Status != AccountStatus.ACTIVE)
            throw new UnauthorizedException("The account is inactive or unavailable");
        if (account.AccountType != AccountType.CUSTOMER)
            throw new ForbiddenException("Only Customers can initiate online payments");
        if (account.CustomerAccount is not { IsDeleted: false } customer)
            throw new UnauthorizedException("The customer profile is unavailable");

        var invoices = db.Invoices.AsNoTracking().Where(x => x.InvoiceId == invoiceId &&
            x.Booking.CustomerId == customer.CustomerId && x.IssuedAt != null);
        // Serialize payment starts before preparing checkout; keep invoice values unchanged.
        if (db.Database.IsRelational())
            await invoices.ExecuteUpdateAsync(s => s.SetProperty(x => x.InvoiceId, x => x.InvoiceId), cancellationToken);
        var invoice = await invoices.SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Invoice not found", "INVOICE_NOT_FOUND");
        if (invoice.Type is not (InvoiceType.DEPOSIT or InvoiceType.FINAL or InvoiceType.SUPPLEMENTARY))
            throw new UnprocessableEntityException("This invoice type cannot be paid", "UNPAYABLE_INVOICE_TYPE");
        if (invoice.Status is not (InvoiceStatus.ISSUED or InvoiceStatus.OVERDUE))
            throw new ConflictException($"Cannot pay an invoice in status {invoice.Status}", "INVALID_INVOICE_STATUS");
        if (await db.Receipts.AnyAsync(x => x.InvoiceId == invoiceId && x.Status == ReceiptStatus.CONFIRMED, cancellationToken))
            throw new ConflictException("Cannot pay an invoice that already has a confirmed payment", "INVALID_INVOICE_STATUS");
        if (invoice.TotalAmount <= 0)
            throw new UnprocessableEntityException("Invoice total must be greater than 0", "INVOICE_TOTAL_NOT_POSITIVE");

        var now = DateTime.SpecifyKind(clock.GetUtcNow().UtcDateTime, DateTimeKind.Unspecified);
        var pending = await db.Receipts.Where(x => x.InvoiceId == invoiceId && x.Status == ReceiptStatus.PENDING)
            .ToListAsync(cancellationToken);
        foreach (var previous in pending) { previous.Status = ReceiptStatus.FAILED; previous.UpdatedAt = now; }
        var receipt = new Receipt
        {
            ReceiptId = Guid.NewGuid(), InvoiceId = invoiceId, Amount = invoice.TotalAmount, Currency = invoice.Currency,
            PaymentMethod = PaymentMethod.BANK_TRANSFER, Status = ReceiptStatus.PENDING, CreatedAt = now, UpdatedAt = now
        };
        await SaveReceiptAsync(receipt, now, cancellationToken);

        PaymentGatewayService.Checkout checkout;
        try
        {
            checkout = await gateway.CreateCheckoutAsync(invoiceId, receipt.ReceiptCode, receipt.Amount, receipt.Currency, cancellationToken);
            if (!Uri.TryCreate(checkout.PaymentUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length != 0)
                throw new InvalidDataException("The gateway returned an invalid payment URL");
        }
        catch (Exception exception) when (exception is not UnprocessableEntityException &&
            (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested))
        {
            logger.LogWarning(exception, "Payment gateway failed for receipt {ReceiptCode}", receipt.ReceiptCode);
            throw new AppException("Payment gateway is unavailable. Please try again later", 502, "PAYMENT_GATEWAY_ERROR");
        }
        await transaction.CommitAsync(cancellationToken);
        return new(new(receipt.ReceiptId, receipt.ReceiptCode, receipt.Amount, receipt.Currency,
            receipt.PaymentMethod.ToString(), receipt.Status.ToString(), null, null, null, null, null), checkout.PaymentUrl, checkout.Fields);
    }

    private async Task SaveReceiptAsync(Receipt receipt, DateTime now, CancellationToken token)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            receipt.ReceiptCode = $"RCP-{now:yyyyMMdd}-{RandomNumberGenerator.GetInt32(10_000):D4}";
            if (await db.Receipts.AnyAsync(x => x.ReceiptCode == receipt.ReceiptCode, token)) continue;
            db.Receipts.Add(receipt);
            try { await db.SaveChangesAsync(token); return; }
            catch (DbUpdateException exception) when (exception.InnerException is PostgresException
                { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "Receipt_receipt_code_key" })
            { db.Entry(receipt).State = EntityState.Detached; }
        }
        throw new ConflictException("Could not generate a unique receipt code", "RECEIPT_CODE_GENERATION_FAILED");
    }
}
