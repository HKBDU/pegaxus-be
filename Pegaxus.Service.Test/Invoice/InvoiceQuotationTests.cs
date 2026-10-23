using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.InvoiceService;

namespace Pegaxus.Service.Test;

public class QuotationCatalogTests
{
    // Rows of the 2-9 horse table in COST_ESTIMATION_PROPOSAL.md (2 transit days): subtotal, deposit at 40%, remaining.
    [Theory]
    [InlineData(2, "S", 3_040, 1_459, 2_189)]
    [InlineData(3, "S", 3_560, 1_709, 2_563)]
    [InlineData(4, "M", 4_880, 2_342, 3_514)]
    [InlineData(5, "M", 5_400, 2_592, 3_888)]
    [InlineData(6, "M", 5_920, 2_842, 4_262)]
    [InlineData(7, "L", 7_440, 3_571, 5_357)]
    [InlineData(8, "L", 7_960, 3_821, 5_731)]
    [InlineData(9, "L", 8_480, 4_070, 6_106)]
    public void Table_MatchesTheDocumentedSubtotalDepositAndRemaining(int horses, string tier, long subtotal, long depositTotal, long remaining)
    {
        var lines = QuotationCatalog.Generate(new QuotationOptions(), horses, 2);
        Assert.Equal(tier, QuotationCatalog.Tier(horses));
        Assert.Equal(9, lines.Count);
        Assert.Equal(subtotal, lines.Sum(line => line.Quantity * line.UnitPrice));
        var depositLine = QuotationCatalog.Deposit(subtotal, 40);
        var depositTotalWithVat = depositLine + (long)Vnd.Tax(depositLine);
        Assert.Equal(depositTotal, depositTotalWithVat);
        var grand = subtotal + (long)Vnd.Tax(subtotal);
        Assert.Equal(remaining, grand - depositTotalWithVat);
    }

    [Fact]
    public void OneHorse_UsesTierSWithRealHorseCount()
    {
        var lines = QuotationCatalog.Generate(new QuotationOptions(), 1, 2);
        Assert.Equal("Base vehicle fare, Tier S", lines[0].Description);
        Assert.Equal(2_520, lines.Sum(line => line.Quantity * line.UnitPrice));
    }

    [Fact]
    public void TransitDays_OnlyScaleTheFeedLine()
    {
        var two = QuotationCatalog.Generate(new QuotationOptions(), 4, 2);
        var five = QuotationCatalog.Generate(new QuotationOptions(), 4, 5);
        Assert.Equal(4 * 3 * 35, five.Sum(line => line.Quantity * line.UnitPrice) - two.Sum(line => line.Quantity * line.UnitPrice));
        Assert.Equal(4 * 5, five.Single(line => line.Description == "Feed and electrolyte water").Quantity);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    public void UnsupportedHorseCount_IsRejected(int horses) =>
        Assert.Equal("HORSE_COUNT_UNSUPPORTED", Assert.Throws<UnprocessableEntityException>(() => QuotationCatalog.Generate(new QuotationOptions(), horses, 2)).ErrorCode);

    [Fact]
    public void Deposit_RoundsHalfUpToWholeVnd()
    {
        Assert.Equal(1_952, QuotationCatalog.Deposit(4_880, 40));
        Assert.Equal(1_216, QuotationCatalog.Deposit(3_040, 40));
        Assert.Equal(2, QuotationCatalog.Deposit(5, 40));
        Assert.Equal(1, QuotationCatalog.Deposit(3, 30));
    }

    [Fact]
    public void Options_ValidateRatesAndPrices()
    {
        Assert.True(new QuotationOptions().IsValid());
        Assert.False(new QuotationOptions { DepositRatePercent = 29 }.IsValid());
        Assert.False(new QuotationOptions { DepositRatePercent = 51 }.IsValid());
        Assert.False(new QuotationOptions { FeedPerHorseDay = 0 }.IsValid());
        Assert.False(new QuotationOptions { DefaultTransitDays = 61 }.IsValid());
    }
}

public class InvoiceQuotationTests
{
    private static async Task AddHorsesAsync(AppDbContext db, InvoiceTests.Fixture f, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var horse = new Horse { HorseId = Guid.NewGuid(), CustomerId = f.Booking.CustomerId, HorseName = $"Horse {i}",
                CreatedAt = f.Plan.CreatedAt, UpdatedAt = f.Plan.CreatedAt };
            db.Horses.Add(horse);
            db.BookingHorses.Add(new BookingHorse { BookingId = f.Booking.BookingId, HorseId = horse.HorseId, CreatedAt = f.Plan.CreatedAt });
        }
        await db.SaveChangesAsync();
    }

    private static Request.CreateRequest Create(string type, int? transitDays = null, string? note = null) =>
        new() { Type = type, DueDate = DateOnly.FromDateTime(InvoiceTests.Fixture.Clock.GetUtcNow().UtcDateTime), TransitDays = transitDays, Note = note };

    [Fact]
    public async Task Quotation_IsGeneratedFromTheBookingHorsesAndTransitDays()
    {
        await using var db = TestSupport.CreateDb();
        var f = await InvoiceTests.Fixture.CreateAsync(db);
        var result = await f.Service.CreateAsync(f.Account.AccountId, f.Booking.BookingId, Create("QUOTATION", 2, "Customer asked for a quiet route"));
        Assert.Equal(9, result.Items.Count);
        Assert.Equal(new Response.SummaryResponse(2_520, 504, 0, 3_024), result.Summary);
        Assert.False(result.IsPaymentRequired);
        Assert.Equal("Additional costs may arise during the trip and will be added to the final invoice. Customer asked for a quiet route", result.Note);
        Assert.Equal("Base vehicle fare, Tier S", result.Items[0].Description);
        Assert.All(result.Items, item => Assert.Null(item.EmergencyCostRequestId));
    }

    [Fact]
    public async Task Quotation_DerivesTransitDaysFromTheBookingDatesUnlessTheClientSendsThem()
    {
        await using var db = TestSupport.CreateDb();
        var f = await InvoiceTests.Fixture.CreateAsync(db);
        f.Booking.RequestedArrivalDate = f.Booking.RequestedDepartureDate.AddDays(4);
        await db.SaveChangesAsync();
        var suggestion = await f.Service.GetSuggestionAsync(f.Account.AccountId, f.Booking.BookingId, new() { Type = "QUOTATION" });
        Assert.Equal(5, suggestion.Pricing!.TransitDays);
        Assert.Equal(5, suggestion.Items.Single(item => item.Description == "Feed and electrolyte water").Quantity);
        var overridden = await f.Service.GetSuggestionAsync(f.Account.AccountId, f.Booking.BookingId, new() { Type = "QUOTATION", TransitDays = 3 });
        Assert.Equal(3, overridden.Pricing!.TransitDays);
    }

    [Fact]
    public async Task Suggestion_ForQuotationReturnsLockedLinesAndPricing()
    {
        await using var db = TestSupport.CreateDb();
        var f = await InvoiceTests.Fixture.CreateAsync(db);
        var result = await f.Service.GetSuggestionAsync(f.Account.AccountId, f.Booking.BookingId, new() { Type = "QUOTATION" });
        Assert.True(result.IsLocked);
        Assert.Equal(new Response.PricingResponse("QUOTATION_V1", 1, 2, "S", 40, 20), result.Pricing);
        Assert.Equal(9, result.Items.Count);
        Assert.False(result.IsPaymentRequired);
        Assert.Empty(await db.Invoices.ToListAsync());
    }

    [Theory]
    [InlineData("QUOTATION")]
    [InlineData("DEPOSIT")]
    [InlineData("FINAL")]
    public async Task GeneratedTypes_RejectClientItems(string type)
    {
        await using var db = TestSupport.CreateDb();
        var f = await InvoiceTests.Fixture.CreateAsync(db);
        if (type == "DEPOSIT") f.Transport.Status = TransportStatus.READY;
        if (type != "QUOTATION") db.Invoices.Add(f.IssuedQuotation());
        await db.SaveChangesAsync();
        var request = Create(type);
        request.Items = [InvoiceTests.Fixture.Item(1, 100)];
        var error = await Assert.ThrowsAsync<UnprocessableEntityException>(() => f.Service.CreateAsync(f.Account.AccountId, f.Booking.BookingId, request));
        Assert.Equal("INVOICE_ITEMS_GENERATED", error.ErrorCode);
        Assert.Empty(await db.InvoiceStatusHistories.ToListAsync());
    }

    [Fact]
    public async Task EchoedDepositDeductionRows_AreIgnoredForFinal()
    {
        await using var db = TestSupport.CreateDb();
        var f = await InvoiceTests.Fixture.CreateAsync(db);
        db.Invoices.Add(f.IssuedQuotation());
        await db.SaveChangesAsync();
        var request = Create("FINAL");
        request.Items = [new() { IsDepositDeduction = true, Price = -1 }];
        Assert.Equal("FINAL", (await f.Service.CreateAsync(f.Account.AccountId, f.Booking.BookingId, request)).Type);
    }

    [Fact]
    public async Task TenHorses_NeedManualLinesWithANote()
    {
        await using var db = TestSupport.CreateDb();
        var f = await InvoiceTests.Fixture.CreateAsync(db);
        await AddHorsesAsync(db, f, 9);
        var suggestion = await f.Service.GetSuggestionAsync(f.Account.AccountId, f.Booking.BookingId, new() { Type = "QUOTATION" });
        Assert.False(suggestion.IsLocked);
        Assert.Empty(suggestion.Items);
        Assert.Equal(10, suggestion.Pricing!.HorseCount);

        Assert.Equal("HORSE_COUNT_UNSUPPORTED", (await Assert.ThrowsAsync<UnprocessableEntityException>(() =>
            f.Service.CreateAsync(f.Account.AccountId, f.Booking.BookingId, Create("QUOTATION")))).ErrorCode);

        var manual = Create("QUOTATION");
        manual.Items = [InvoiceTests.Fixture.Item(1, 9_000)];
        var noNote = await Assert.ThrowsAsync<FieldValidationException>(() => f.Service.CreateAsync(f.Account.AccountId, f.Booking.BookingId, manual));
        Assert.Contains(noNote.Errors, error => error.Field == "note");

        manual.Note = "Charter of three trucks";
        var result = await f.Service.CreateAsync(f.Account.AccountId, f.Booking.BookingId, manual);
        Assert.Equal("Charter of three trucks", result.Note);
        Assert.Equal(9_000, result.Summary.Subtotal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(61)]
    public async Task TransitDays_MustBeBetweenOneAndSixty(int days)
    {
        await using var db = TestSupport.CreateDb();
        var f = await InvoiceTests.Fixture.CreateAsync(db);
        var error = await Assert.ThrowsAsync<FieldValidationException>(() => f.Service.CreateAsync(f.Account.AccountId, f.Booking.BookingId, Create("QUOTATION", days)));
        Assert.Contains(error.Errors, item => item.Field == "transitDays");
        error = await Assert.ThrowsAsync<FieldValidationException>(() => f.Service.GetSuggestionAsync(f.Account.AccountId, f.Booking.BookingId, new() { Type = "QUOTATION", TransitDays = days }));
        Assert.Contains(error.Errors, item => item.Field == "transitDays");
        Assert.Empty(await db.Invoices.ToListAsync());
    }

    [Theory]
    [InlineData(InvoiceStatus.DRAFT)]
    [InlineData(InvoiceStatus.CANCELLED)]
    public async Task Deposit_NeedsAnIssuedQuotation(InvoiceStatus quotationStatus)
    {
        await using var db = TestSupport.CreateDb();
        var f = await InvoiceTests.Fixture.CreateAsync(db);
        f.Transport.Status = TransportStatus.READY;
        db.Invoices.Add(f.IssuedQuotation(status: quotationStatus));
        await db.SaveChangesAsync();
        Assert.Equal("QUOTATION_REQUIRED", (await Assert.ThrowsAsync<ConflictException>(() =>
            f.Service.CreateAsync(f.Account.AccountId, f.Booking.BookingId, Create("DEPOSIT")))).ErrorCode);
        Assert.Equal("QUOTATION_REQUIRED", (await Assert.ThrowsAsync<ConflictException>(() =>
            f.Service.GetSuggestionAsync(f.Account.AccountId, f.Booking.BookingId, new() { Type = "DEPOSIT" }))).ErrorCode);
    }

    [Fact]
    public async Task Deposit_IsOneLineOfFortyPercentOfTheQuotationSubtotal()
    {
        await using var db = TestSupport.CreateDb();
        var f = await InvoiceTests.Fixture.CreateAsync(db);
        f.Transport.Status = TransportStatus.READY;
        var quotation = f.IssuedQuotation(4_880);
        db.Invoices.Add(quotation);
        await db.SaveChangesAsync();
        var result = await f.Service.CreateAsync(f.Account.AccountId, f.Booking.BookingId, Create("DEPOSIT"));
        var line = Assert.Single(result.Items);
        Assert.Equal($"Deposit 40% of quotation {quotation.InvoiceCode}", line.Description);
        Assert.Equal(new Response.SummaryResponse(1_952, 390, 0, 2_342), result.Summary);
        Assert.True(result.IsPaymentRequired);
    }

    [Fact]
    public async Task Final_AddsApprovedCostsToTheQuotationAndDeductsThePaidDeposit()
    {
        await using var db = TestSupport.CreateDb();
        var f = await InvoiceTests.Fixture.CreateAsync(db);
        var cost = f.Cost(EmergencyCostStatus.APPROVED, 500);
        db.Invoices.AddRange(f.IssuedQuotation(4_880), f.Invoice(InvoiceType.DEPOSIT, InvoiceStatus.PAID, 2_342));
        db.EmergencyCostRequests.Add(cost);
        await db.SaveChangesAsync();
        var result = await f.Service.CreateAsync(f.Account.AccountId, f.Booking.BookingId, Create("FINAL"));
        Assert.Equal(new Response.SummaryResponse(5_380, 1_076, -2_342, 4_114), result.Summary);
        Assert.Equal(cost.EmergencyCostRequestId, Assert.Single(result.Items, item => item.ItemType == "EMERGENCY_COST").EmergencyCostRequestId);
        var detail = await db.InvoiceItems.AsNoTracking().SingleAsync(item => item.EmergencyCostRequestId == cost.EmergencyCostRequestId);
        Assert.Equal(result.InvoiceId, detail.InvoiceId);
    }

    [Fact]
    public async Task Final_IsBlockedWhileAnEmergencyCostIsPending()
    {
        await using var db = TestSupport.CreateDb();
        var f = await InvoiceTests.Fixture.CreateAsync(db);
        db.Invoices.Add(f.IssuedQuotation());
        db.EmergencyCostRequests.Add(f.Cost(EmergencyCostStatus.PENDING));
        await db.SaveChangesAsync();
        Assert.Equal("PENDING_EMERGENCY_COSTS", (await Assert.ThrowsAsync<UnprocessableEntityException>(() =>
            f.Service.CreateAsync(f.Account.AccountId, f.Booking.BookingId, Create("FINAL")))).ErrorCode);
        Assert.Equal("PENDING_EMERGENCY_COSTS", (await Assert.ThrowsAsync<UnprocessableEntityException>(() =>
            f.Service.GetSuggestionAsync(f.Account.AccountId, f.Booking.BookingId, new() { Type = "FINAL" }))).ErrorCode);
    }

    [Fact]
    public async Task CancellingAFinal_ReleasesItsCostsSoTheNextFinalBillsThemAgain()
    {
        await using var db = TestSupport.CreateDb();
        var f = await InvoiceTests.Fixture.CreateAsync(db);
        var cost = f.Cost(EmergencyCostStatus.APPROVED, 500);
        db.Invoices.Add(f.IssuedQuotation(4_880));
        db.EmergencyCostRequests.Add(cost);
        await db.SaveChangesAsync();
        var first = await f.Service.CreateAsync(f.Account.AccountId, f.Booking.BookingId, Create("FINAL"));
        await f.Service.CancelAsync(first.InvoiceId, new() { CancellationReason = "Wrong due date" }, f.Account.AccountId);
        Assert.Null((await db.InvoiceItems.AsNoTracking().SingleAsync(item => item.ItemType == InvoiceItemType.EMERGENCY_COST)).EmergencyCostRequestId);
        var second = await f.Service.CreateAsync(f.Account.AccountId, f.Booking.BookingId, Create("FINAL"));
        Assert.Equal(cost.EmergencyCostRequestId, Assert.Single(second.Items, item => item.ItemType == "EMERGENCY_COST").EmergencyCostRequestId);
    }

    [Fact]
    public async Task Issue_RejectsADraftWhoseSourcesChanged()
    {
        await using var db = TestSupport.CreateDb();
        var f = await InvoiceTests.Fixture.CreateAsync(db);
        db.Invoices.Add(f.IssuedQuotation(4_880));
        await db.SaveChangesAsync();
        var draft = await f.Service.CreateAsync(f.Account.AccountId, f.Booking.BookingId, Create("FINAL"));
        var issue = new Request.IssueRequest { LastUpdatedAt = draft.UpdatedAt };

        var late = f.Cost(EmergencyCostStatus.PENDING);
        db.EmergencyCostRequests.Add(late);
        await db.SaveChangesAsync();
        Assert.Equal("PENDING_EMERGENCY_COSTS", (await Assert.ThrowsAsync<UnprocessableEntityException>(() =>
            f.Service.IssueAsync(draft.InvoiceId, issue, f.Account.AccountId))).ErrorCode);

        late.Status = EmergencyCostStatus.APPROVED;
        late.ApprovedAmount = 500;
        await db.SaveChangesAsync();
        Assert.Equal("INVOICE_SOURCE_CHANGED", (await Assert.ThrowsAsync<ConflictException>(() =>
            f.Service.IssueAsync(draft.InvoiceId, issue, f.Account.AccountId))).ErrorCode);

        var saved = await f.Service.UpdateAsync(f.Account.AccountId, draft.InvoiceId,
            new() { LastUpdatedAt = draft.UpdatedAt, DueDate = draft.DueDate });
        Assert.Equal(5_380, saved.Summary.Subtotal);
        var issued = await f.Service.IssueAsync(draft.InvoiceId, new() { LastUpdatedAt = saved.UpdatedAt }, f.Account.AccountId);
        Assert.Equal("ISSUED", issued.Status);
    }

    [Fact]
    public async Task Issue_DepositNeedsTheQuotationToStillBeValid()
    {
        await using var db = TestSupport.CreateDb();
        var f = await InvoiceTests.Fixture.CreateAsync(db);
        f.Transport.Status = TransportStatus.READY;
        var quotation = f.IssuedQuotation(4_880);
        db.Invoices.Add(quotation);
        await db.SaveChangesAsync();
        var draft = await f.Service.CreateAsync(f.Account.AccountId, f.Booking.BookingId, Create("DEPOSIT"));
        quotation.Status = InvoiceStatus.CANCELLED;
        await db.SaveChangesAsync();
        Assert.Equal("QUOTATION_REQUIRED", (await Assert.ThrowsAsync<ConflictException>(() =>
            f.Service.IssueAsync(draft.InvoiceId, new() { LastUpdatedAt = draft.UpdatedAt }, f.Account.AccountId))).ErrorCode);
    }
}
