using Microsoft.EntityFrameworkCore;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.InvoiceService.Validator;

using InvoiceRequest = Pegaxus.Service.InvoiceService.Request;
using RouteChangeRequest = Pegaxus.Service.RouteChangeService.Request;

namespace Pegaxus.Service.Test;

public class VndTests
{
    [Theory]
    [InlineData("1000", true)]
    [InlineData("151800000", true)]
    [InlineData("1000.0", false)]
    [InlineData("1000.00", false)]
    [InlineData("1000.5", false)]
    public void IsWhole_RejectsAnyNumberSentWithADecimalPart(string text, bool expected) =>
        Assert.Equal(expected, Vnd.IsWhole(decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public void ToLong_DropsTheStoredZeroScale_ButNeverCutsARealFraction()
    {
        Assert.Equal(151_800_000L, Vnd.ToLong(151_800_000.00m));
        Assert.Equal(-30_000_000L, Vnd.ToLong(-30_000_000.00m));
        var error = Assert.Throws<UnprocessableEntityException>(() => Vnd.ToLong(123_456.50m));
        Assert.Equal("AMOUNT_NOT_WHOLE_VND", error.ErrorCode);
        Assert.Equal("AMOUNT_NOT_WHOLE_VND", Assert.Throws<UnprocessableEntityException>(() => Vnd.ToInt(1.5m)).ErrorCode);
    }

    [Theory]
    [InlineData(100, 20)]
    [InlineData(3, 1)]
    [InlineData(2, 0)]
    [InlineData(12.5, 3)]
    [InlineData(2.5, 1)]
    [InlineData(0, 0)]
    public void Tax_IsTwentyPercentRoundedHalfUpToAWholeVnd(double subtotal, int expected) =>
        Assert.Equal(expected, Vnd.Tax((decimal)subtotal));

    [Fact]
    public void Format_UsesThousandsSeparatorsAndNoDecimals() =>
        Assert.Equal("151,800,000 VND", Vnd.Format(151_800_000.00m));

    private static InvoiceRequest.ItemRequest Item(decimal quantity, decimal price) => new()
    {
        ItemType = "TRANSPORT_FEE", Description = "Transport fee", Quantity = quantity, Price = price
    };

    private static decimal Number(string text) => decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);

    [Theory]
    [InlineData("1", "1000", true)]
    [InlineData("1", "1000.0", false)]
    [InlineData("1", "1000.5", false)]
    [InlineData("1.5", "1000", false)]
    [InlineData("1.0", "1000", false)]
    [InlineData("0", "1000", false)]
    [InlineData("1", "0", false)]
    public void InvoiceItems_NeedWholeVndPriceAndWholeQuantity(string quantity, string price, bool valid)
    {
        var clock = TimeProvider.System;
        var create = new CreateRequestValidator(clock).Validate(new InvoiceRequest.CreateRequest
        {
            Type = "QUOTATION", DueDate = DateOnly.FromDateTime(DateTime.UtcNow), Items = [Item(Number(quantity), Number(price))]
        });
        Assert.Equal(valid, create.IsValid);
    }

    [Fact]
    public void WholeNumberMessages_NameTheProblem()
    {
        var clock = TimeProvider.System;
        var result = new CreateRequestValidator(clock).Validate(new InvoiceRequest.CreateRequest
        {
            Type = "QUOTATION", DueDate = DateOnly.FromDateTime(DateTime.UtcNow), Items = [Item(1, 1000.50m)]
        });
        Assert.Contains(result.Errors, error => error.ErrorMessage == "Amount must be a whole number of VND");
        var update = new UpdateRequestValidator(clock).Validate(new InvoiceRequest.UpdateRequest
        {
            LastUpdatedAt = DateTimeOffset.UtcNow, DueDate = DateOnly.FromDateTime(DateTime.UtcNow), Items = [Item(1.5m, 1000)]
        });
        Assert.Contains(update.Errors, error => error.ErrorMessage == "Quantity must be a whole number");
    }

    [Theory]
    [InlineData("151800000", true)]
    [InlineData("1", true)]
    [InlineData("0", false)]
    [InlineData("100.0", false)]
    [InlineData("100.25", false)]
    public void ReceiptAmount_MustBeAWholeVnd(string amount, bool valid)
    {
        var result = new RecordPaymentRequestValidator(TimeProvider.System).Validate(new InvoiceRequest.RecordPaymentRequest
        {
            Amount = Number(amount), PaymentMethod = "BANK_TRANSFER"
        });
        Assert.Equal(valid, result.IsValid);
    }

    [Theory]
    [InlineData("0", true)]
    [InlineData("30000000", true)]
    [InlineData("1000.0", false)]
    [InlineData("1000.5", false)]
    [InlineData("-1", false)]
    public void EmergencyExtraCost_MustBeAWholeVnd(string cost, bool valid)
    {
        var result = new Pegaxus.Service.RouteChangeService.Validator.CreateRequestValidator().Validate(new RouteChangeRequest.CreateRequest
        {
            Reason = "Snow closed the primary road", NewEstimatedArrivalAt = DateTimeOffset.UtcNow.AddHours(5), EstimatedExtraCost = Number(cost)
        });
        Assert.Equal(valid, result.IsValid);
    }

    [Fact]
    public async Task Detail_RefusesAnOldInvoiceWhoseStoredAmountHasADecimalPart()
    {
        await using var db = TestSupport.CreateDb();
        var (_, customer, invoice, _) = await InvoicePaymentTests.SeedAsync(db);
        invoice.TotalAmount = 120.50m;
        await db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<UnprocessableEntityException>(() =>
            InvoicePaymentTests.CreateService(db).GetAsync(customer.AccountId, invoice.InvoiceId));

        Assert.Equal("AMOUNT_NOT_WHOLE_VND", error.ErrorCode);
        Assert.Equal(120.50m, (await db.Invoices.AsNoTracking().SingleAsync()).TotalAmount);
    }
}
