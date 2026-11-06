using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.InvoiceService;
using Pegaxus.Service.PriceListService.Validator;

using PriceListRequest = Pegaxus.Service.PriceListService.Request;
using PriceListSvc = Pegaxus.Service.PriceListService.Service;

namespace Pegaxus.Service.Test;

internal static class PriceListSupport
{
    internal static PriceListSvc CreateService(AppDbContext db) => new(db, new ListRequestValidator(), new CreateRequestValidator(),
        new UpdateRequestValidator(), new CreateItemRequestValidator(), new UpdateItemRequestValidator());

    /// <summary>A price list holding the 11 default lines. Active lists are what the invoice engine reads.</summary>
    internal static async Task<PriceList> AddListAsync(AppDbContext db, bool active, string name = "Default", decimal tax = 20, decimal deposit = 40, bool withItems = true)
    {
        var at = new DateTime(2026, 11, 4, 0, 0, 0, DateTimeKind.Unspecified);
        var list = new PriceList
        {
            PriceListId = Guid.NewGuid(), Name = name, Currency = "VND", TaxRate = tax, DepositRate = deposit, IsActive = active, CreatedAt = at, UpdatedAt = at
        };
        db.PriceLists.Add(list);
        if (withItems)
            foreach (var item in PriceListSnapshot.Default.Items)
                db.PriceListItems.Add(new PriceListItem
                {
                    PriceListItemId = Guid.NewGuid(), PriceListId = list.PriceListId, ItemCode = item.ItemCode, ItemName = item.ItemName,
                    ItemType = item.ItemType, Unit = item.Unit, UnitPrice = item.UnitPrice, VehicleTier = item.VehicleTier,
                    SortOrder = item.SortOrder, CreatedAt = at, UpdatedAt = at
                });
        await db.SaveChangesAsync();
        return list;
    }

    internal static DateTimeOffset Version(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    internal static PriceListRequest.CreateItemRequest NewItem(string code = "STRAW", decimal price = 10) => new()
    {
        ItemCode = code, ItemName = "Extra straw", ItemType = "OTHER", Unit = "HORSE_DAY", UnitPrice = price
    };
}

public class PriceListServiceTests
{
    [Fact]
    public async Task Create_MakesAnInactiveEmptyListAndAudits()
    {
        await using var db = TestSupport.CreateDb();
        var (actor, _) = await TestSupport.AddLogisticsManagerAsync(db);
        var result = await PriceListSupport.CreateService(db).CreateAsync(
            new() { Name = "  Q1-2027  ", Currency = "vnd", TaxRate = 10, DepositRate = 35.5m }, actor.AccountId);

        Assert.Equal("Q1-2027", result.Name);
        Assert.Equal("VND", result.Currency);
        Assert.Equal((10m, 35.5m, false), (result.TaxRate, result.DepositRate, result.IsActive));
        Assert.Empty(result.Items);
        Assert.Equal(actor.AccountId, result.CreatedByAccountId);
        var log = await db.CatalogAuditLogs.SingleAsync();
        Assert.Equal((nameof(PriceList), "CREATE", actor.AccountId), (log.EntityType, log.ActionType, log.ActorAccountId));
    }

    [Theory]
    [InlineData("", "VND", 20.0, 40.0, "name")]
    [InlineData("A", "VN", 20.0, 40.0, "currency")]
    [InlineData("A", "VND", 150.0, 40.0, "taxRate")]
    [InlineData("A", "VND", -1.0, 40.0, "taxRate")]
    [InlineData("A", "VND", 20.0, 100.5, "depositRate")]
    [InlineData("A", "VND", 20.0, 12.345, "depositRate")]
    [InlineData("A", "VND", null, 40.0, "taxRate")]
    public async Task Create_RejectsInvalidFields(string name, string currency, double? tax, double deposit, string field)
    {
        await using var db = TestSupport.CreateDb();
        var error = await Assert.ThrowsAsync<FieldValidationException>(() => PriceListSupport.CreateService(db).CreateAsync(
            new() { Name = name, Currency = currency, TaxRate = (decimal?)tax, DepositRate = (decimal)deposit }, Guid.NewGuid()));
        Assert.Contains(error.Errors, item => item.Field == field);
        Assert.Empty(await db.PriceLists.ToListAsync());
    }

    [Fact]
    public async Task Update_ChangesAnInactiveList_AndRejectsStaleVersionsAndActiveLists()
    {
        await using var db = TestSupport.CreateDb();
        var list = await PriceListSupport.AddListAsync(db, active: false);
        var active = await PriceListSupport.AddListAsync(db, active: true, name: "Live");
        var service = PriceListSupport.CreateService(db);
        var request = new PriceListRequest.UpdateRequest
        {
            Name = "Renamed", Currency = "VND", TaxRate = 8, DepositRate = 30, LastUpdatedAt = PriceListSupport.Version(list.UpdatedAt)
        };

        var previous = list.UpdatedAt;
        var result = await service.UpdateAsync(list.PriceListId, request, Guid.NewGuid());
        Assert.Equal(("Renamed", 8m, 30m), (result.Name, result.TaxRate, result.DepositRate));
        Assert.True(result.UpdatedAt.UtcDateTime > previous);
        Assert.Equal("UPDATE", (await db.CatalogAuditLogs.SingleAsync()).ActionType);

        Assert.Equal("PRICE_LIST_MODIFIED", (await Assert.ThrowsAsync<ConflictException>(() => service.UpdateAsync(list.PriceListId, request, Guid.NewGuid()))).ErrorCode);

        request.LastUpdatedAt = PriceListSupport.Version(active.UpdatedAt);
        Assert.Equal("ACTIVE_PRICELIST_LOCKED", (await Assert.ThrowsAsync<UnprocessableEntityException>(() => service.UpdateAsync(active.PriceListId, request, Guid.NewGuid()))).ErrorCode);
        Assert.Equal("PRICE_LIST_NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(() => service.UpdateAsync(Guid.NewGuid(), request, Guid.NewGuid()))).ErrorCode);
    }

    [Fact]
    public async Task Update_WithNoChange_KeepsTheVersionAndWritesNoAudit()
    {
        await using var db = TestSupport.CreateDb();
        var list = await PriceListSupport.AddListAsync(db, active: false);
        var result = await PriceListSupport.CreateService(db).UpdateAsync(list.PriceListId, new()
        {
            Name = list.Name, Currency = "VND", TaxRate = list.TaxRate, DepositRate = list.DepositRate, LastUpdatedAt = PriceListSupport.Version(list.UpdatedAt)
        }, Guid.NewGuid());
        Assert.Equal(list.UpdatedAt, result.UpdatedAt.UtcDateTime);
        Assert.Empty(await db.CatalogAuditLogs.ToListAsync());
    }

    [Fact]
    public async Task Activate_SwitchesTheActiveListAtomically_AndIsIdempotent()
    {
        await using var db = TestSupport.CreateDb();
        var (actor, _) = await TestSupport.AddLogisticsManagerAsync(db);
        var old = await PriceListSupport.AddListAsync(db, active: true, name: "Old");
        var next = await PriceListSupport.AddListAsync(db, active: false, name: "Next");
        var service = PriceListSupport.CreateService(db);

        var result = await service.ActivateAsync(next.PriceListId, actor.AccountId);

        Assert.True(result.IsActive);
        Assert.Equal(next.PriceListId, (await db.PriceLists.SingleAsync(item => item.IsActive)).PriceListId);
        Assert.False((await db.PriceLists.SingleAsync(item => item.PriceListId == old.PriceListId)).IsActive);
        Assert.Equal(new[] { "ACTIVATE", "DEACTIVATE" }, (await db.CatalogAuditLogs.ToListAsync()).Select(log => log.ActionType).Order().ToArray());

        db.CatalogAuditLogs.RemoveRange(db.CatalogAuditLogs);
        await db.SaveChangesAsync();
        await service.ActivateAsync(next.PriceListId, actor.AccountId);
        Assert.Empty(await db.CatalogAuditLogs.ToListAsync());
    }

    [Fact]
    public async Task Activate_RefusesAnEmptyListAMissingTierAndANonVndCurrency()
    {
        await using var db = TestSupport.CreateDb();
        var service = PriceListSupport.CreateService(db);
        var empty = await PriceListSupport.AddListAsync(db, active: false, withItems: false);
        Assert.Equal("PRICE_LIST_EMPTY", (await Assert.ThrowsAsync<UnprocessableEntityException>(() => service.ActivateAsync(empty.PriceListId, Guid.NewGuid()))).ErrorCode);

        var noTierL = await PriceListSupport.AddListAsync(db, active: false);
        db.PriceListItems.Remove(await db.PriceListItems.SingleAsync(item => item.PriceListId == noTierL.PriceListId && item.VehicleTier == "L"));
        await db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<UnprocessableEntityException>(() => service.ActivateAsync(noTierL.PriceListId, Guid.NewGuid()));
        Assert.Equal("PRICE_LIST_TIER_MISSING", error.ErrorCode);
        Assert.Contains("L", error.Message);

        var usd = await PriceListSupport.AddListAsync(db, active: false);
        usd.Currency = "USD";
        await db.SaveChangesAsync();
        Assert.Equal("PRICE_LIST_CURRENCY_UNSUPPORTED", (await Assert.ThrowsAsync<UnprocessableEntityException>(() => service.ActivateAsync(usd.PriceListId, Guid.NewGuid()))).ErrorCode);
        Assert.Empty(await db.PriceLists.Where(item => item.IsActive).ToListAsync());
    }

    [Fact]
    public async Task Clone_CopiesEveryLineIntoAnInactiveList()
    {
        await using var db = TestSupport.CreateDb();
        var source = await PriceListSupport.AddListAsync(db, active: true, name: "Live", tax: 12, deposit: 33);
        var result = await PriceListSupport.CreateService(db).CloneAsync(source.PriceListId, Guid.NewGuid());

        Assert.Equal(("Live (Copy)", false, 12m, 33m), (result.Name, result.IsActive, result.TaxRate, result.DepositRate));
        Assert.Equal(11, result.Items.Count);
        var sourceIds = await db.PriceListItems.Where(item => item.PriceListId == source.PriceListId).Select(item => item.PriceListItemId).ToListAsync();
        Assert.Empty(result.Items.Select(item => item.PriceListItemId).Intersect(sourceIds));
        Assert.Equal(sourceIds.Count, await db.PriceListItems.CountAsync(item => item.PriceListId == source.PriceListId));
        Assert.True((await db.PriceLists.SingleAsync(item => item.PriceListId == source.PriceListId)).IsActive);
        Assert.Equal("CLONE", (await db.CatalogAuditLogs.SingleAsync()).ActionType);
    }

    [Fact]
    public async Task List_FiltersSearchesAndPages()
    {
        await using var db = TestSupport.CreateDb();
        await PriceListSupport.AddListAsync(db, active: true, name: "Alpha");
        await PriceListSupport.AddListAsync(db, active: false, name: "Beta", withItems: false);
        await PriceListSupport.AddListAsync(db, active: false, name: "Gamma");
        var service = PriceListSupport.CreateService(db);

        var all = await service.GetListAsync(new() { SortBy = "name:asc", PageSize = 2 });
        Assert.Equal((3, 2, 2), (all.TotalItems, all.TotalPages, all.Items.Count));
        Assert.Equal(new[] { "Alpha", "Beta" }, all.Items.Select(item => item.Name));
        Assert.Equal(new[] { 11, 0 }, all.Items.Select(item => item.ItemCount));
        Assert.Equal("Alpha", Assert.Single((await service.GetListAsync(new() { IsActive = true })).Items).Name);
        Assert.Equal("Gamma", Assert.Single((await service.GetListAsync(new() { Search = " gam " })).Items).Name);
        Assert.Contains("SortBy", (await Assert.ThrowsAsync<FieldValidationException>(() => service.GetListAsync(new() { SortBy = "bogus" }))).Errors[0].Message);
    }

    [Fact]
    public async Task AddItem_AppendsAndRejectsDuplicatesAndInvalidFields()
    {
        await using var db = TestSupport.CreateDb();
        var list = await PriceListSupport.AddListAsync(db, active: false);
        var service = PriceListSupport.CreateService(db);

        var item = await service.AddItemAsync(list.PriceListId, PriceListSupport.NewItem(), Guid.NewGuid());
        Assert.Equal((100, "HORSE_DAY", "OTHER", 10L, null), (item.SortOrder, item.Unit, item.ItemType, item.UnitPrice, item.VehicleTier));
        Assert.Equal("STRAW", (await service.GetItemsAsync(list.PriceListId)).Last().ItemCode);
        Assert.Equal("DUPLICATE_ITEM_CODE", (await Assert.ThrowsAsync<ConflictException>(() => service.AddItemAsync(list.PriceListId, PriceListSupport.NewItem(), Guid.NewGuid()))).ErrorCode);

        (string Field, Action<PriceListRequest.CreateItemRequest> Break)[] cases =
        [
            ("itemCode", request => request.ItemCode = "straw"),
            ("itemCode", request => request.ItemCode = new string('A', 51)),
            ("itemName", request => request.ItemName = " "),
            ("itemType", request => request.ItemType = "NOPE"),
            ("itemType", request => request.ItemType = "1"),
            ("unit", request => request.Unit = "PALLET"),
            ("unitPrice", request => request.UnitPrice = 0),
            ("unitPrice", request => request.UnitPrice = 10.5m),
            ("unitPrice", request => request.UnitPrice = null),
            ("vehicleTier", request => request.VehicleTier = "XL")
        ];
        foreach (var (field, mutate) in cases)
        {
            var request = PriceListSupport.NewItem("OTHER_CODE");
            mutate(request);
            var error = await Assert.ThrowsAsync<FieldValidationException>(() => service.AddItemAsync(list.PriceListId, request, Guid.NewGuid()));
            Assert.Contains(error.Errors, entry => entry.Field == field);
        }
        Assert.Equal(12, await db.PriceListItems.CountAsync());
    }

    [Fact]
    public async Task ItemCode_MayRepeatAcrossLists()
    {
        await using var db = TestSupport.CreateDb();
        var first = await PriceListSupport.AddListAsync(db, active: false);
        var second = await PriceListSupport.AddListAsync(db, active: false, withItems: false);
        var service = PriceListSupport.CreateService(db);
        await service.AddItemAsync(first.PriceListId, PriceListSupport.NewItem("TRUCK_X"), Guid.NewGuid());
        await service.AddItemAsync(second.PriceListId, PriceListSupport.NewItem("TRUCK_X"), Guid.NewGuid());
        Assert.Equal(2, await db.PriceListItems.CountAsync(item => item.ItemCode == "TRUCK_X"));
    }

    [Fact]
    public async Task ActiveList_LocksAddUpdateAndDelete()
    {
        await using var db = TestSupport.CreateDb();
        var list = await PriceListSupport.AddListAsync(db, active: true);
        var service = PriceListSupport.CreateService(db);
        var feeding = await db.PriceListItems.SingleAsync(item => item.PriceListId == list.PriceListId && item.ItemCode == "FEEDING");
        var unitPrice = feeding.UnitPrice;
        var version = feeding.UpdatedAt;

        Assert.Equal("ACTIVE_PRICELIST_LOCKED", (await Assert.ThrowsAsync<UnprocessableEntityException>(() => service.AddItemAsync(list.PriceListId, PriceListSupport.NewItem(), Guid.NewGuid()))).ErrorCode);
        Assert.Equal("ACTIVE_PRICELIST_LOCKED", (await Assert.ThrowsAsync<UnprocessableEntityException>(() => service.DeleteItemAsync(list.PriceListId, feeding.PriceListItemId, Guid.NewGuid()))).ErrorCode);

        // Not even a new name or price is allowed, and the line stays exactly as it was.
        var request = new PriceListRequest.UpdateItemRequest
        {
            ItemName = "Feed (new)", ItemType = "HORSE_SERVICE_FEE", Unit = "HORSE_DAY", UnitPrice = unitPrice + 1, SortOrder = feeding.SortOrder,
            LastUpdatedAt = PriceListSupport.Version(version)
        };
        Assert.Equal("ACTIVE_PRICELIST_LOCKED", (await Assert.ThrowsAsync<UnprocessableEntityException>(() => service.UpdateItemAsync(list.PriceListId, feeding.PriceListItemId, request, Guid.NewGuid()))).ErrorCode);
        var stored = await db.PriceListItems.AsNoTracking().SingleAsync(item => item.PriceListItemId == feeding.PriceListItemId);
        Assert.Equal((unitPrice, version), (stored.UnitPrice, stored.UpdatedAt));
        Assert.NotEqual("Feed (new)", stored.ItemName);
    }

    [Fact]
    public async Task UpdateAndDeleteItem_OnAnInactiveList_AuditAndRejectStaleVersions()
    {
        await using var db = TestSupport.CreateDb();
        var list = await PriceListSupport.AddListAsync(db, active: false);
        var service = PriceListSupport.CreateService(db);
        var item = await db.PriceListItems.SingleAsync(entry => entry.PriceListId == list.PriceListId && entry.ItemCode == "INSURANCE");
        var request = new PriceListRequest.UpdateItemRequest
        {
            ItemName = "Insurance", ItemType = "OTHER", Unit = "TRIP", UnitPrice = 99, VehicleTier = " m ", SortOrder = 5,
            LastUpdatedAt = PriceListSupport.Version(item.UpdatedAt)
        };

        var updated = await service.UpdateItemAsync(list.PriceListId, item.PriceListItemId, request, Guid.NewGuid());
        Assert.Equal(("TRIP", 99L, "M", 5), (updated.Unit, updated.UnitPrice, updated.VehicleTier, updated.SortOrder));
        Assert.Equal("PRICE_LIST_ITEM_MODIFIED", (await Assert.ThrowsAsync<ConflictException>(() => service.UpdateItemAsync(list.PriceListId, item.PriceListItemId, request, Guid.NewGuid()))).ErrorCode);
        Assert.Equal("PRICE_LIST_ITEM_NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(() => service.UpdateItemAsync(list.PriceListId, Guid.NewGuid(), request, Guid.NewGuid()))).ErrorCode);

        await service.DeleteItemAsync(list.PriceListId, item.PriceListItemId, Guid.NewGuid());
        Assert.Equal(10, await db.PriceListItems.CountAsync());
        Assert.Equal(new[] { "DELETE", "UPDATE" }, (await db.CatalogAuditLogs.ToListAsync()).Select(log => log.ActionType).Order().ToArray());
        Assert.Equal("PRICE_LIST_ITEM_NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(() => service.DeleteItemAsync(list.PriceListId, item.PriceListItemId, Guid.NewGuid()))).ErrorCode);
    }
}

public class PriceListInvoiceTests
{
    private static Request.CreateRequest Create(string type, int? transitDays = 2) =>
        new() { Type = type, DueDate = InvoiceTests.Fixture.Clock.GetUtcNow().AddDays(1), TransitDays = transitDays };

    [Fact]
    public async Task Quotation_UsesTheActiveList_RecordsTheSourceLines_AndItsTaxRate()
    {
        await using var db = TestSupport.CreateDb();
        var f = await InvoiceTests.Fixture.CreateAsync(db);
        var list = await PriceListSupport.AddListAsync(db, active: true, tax: 10);
        var truckS = await db.PriceListItems.SingleAsync(item => item.PriceListId == list.PriceListId && item.ItemCode == "TRUCK_S");
        truckS.UnitPrice = 2_000;
        await db.SaveChangesAsync();

        var result = await f.Service.CreateAsync(f.Account.AccountId, f.Booking.BookingId, Create("QUOTATION"));

        Assert.Equal(new Response.SummaryResponse(2_920, 292, 0, 3_212) { TaxRatePercent = 10 }, result.Summary);
        var stored = await db.InvoiceItems.Where(item => item.InvoiceId == result.InvoiceId).ToListAsync();
        Assert.Equal(9, stored.Count);
        Assert.All(stored, item => Assert.NotNull(item.PriceListItemId));
        Assert.Equal(truckS.PriceListItemId, stored.Single(item => item.UnitPrice == 2_000).PriceListItemId);
        Assert.Equal(10m, (await db.Invoices.SingleAsync()).TaxRate);
    }

    [Fact]
    public async Task SavedDraft_KeepsItsTaxRate_UntilItIsSavedAgain()
    {
        await using var db = TestSupport.CreateDb();
        var f = await InvoiceTests.Fixture.CreateAsync(db);
        var old = await PriceListSupport.AddListAsync(db, active: true, tax: 10);
        var draft = await f.Service.CreateAsync(f.Account.AccountId, f.Booking.BookingId, Create("QUOTATION"));
        var next = await PriceListSupport.AddListAsync(db, active: false, name: "Next", tax: 5);
        await PriceListSupport.CreateService(db).ActivateAsync(next.PriceListId, f.Account.AccountId);

        var unchanged = await f.Service.GetAsync(f.Account.AccountId, draft.InvoiceId);
        Assert.Equal(10m, unchanged.Summary.TaxRatePercent);
        Assert.Equal(252, unchanged.Summary.Tax);

        var saved = await f.Service.UpdateAsync(f.Account.AccountId, draft.InvoiceId,
            new() { LastUpdatedAt = draft.UpdatedAt, Type = "QUOTATION", DueDate = draft.DueDate, TransitDays = 2 });
        Assert.Equal(5m, saved.Summary.TaxRatePercent);
        Assert.Equal(126, saved.Summary.Tax);
        Assert.False(old.IsActive && next.IsActive);
    }

    [Fact]
    public async Task Deposit_UsesTheActiveListDepositRate()
    {
        await using var db = TestSupport.CreateDb();
        var f = await InvoiceTests.Fixture.CreateAsync(db);
        f.Transport.Status = TransportStatus.READY;
        await PriceListSupport.AddListAsync(db, active: true, deposit: 37.5m);
        var quotation = f.IssuedQuotation(4_880);
        db.Invoices.Add(quotation);
        await db.SaveChangesAsync();

        var result = await f.Service.CreateAsync(f.Account.AccountId, f.Booking.BookingId, Create("DEPOSIT"));

        var line = Assert.Single(result.Items);
        Assert.Equal($"Deposit 37.5% of quotation {quotation.InvoiceCode}", line.Description);
        Assert.Equal(1_830, line.Price);
    }

    [Fact]
    public async Task NoPriceList_FallsBackToTheDefaults_ButListsWithoutAnActiveOneBlockQuoting()
    {
        await using var db = TestSupport.CreateDb();
        var f = await InvoiceTests.Fixture.CreateAsync(db);
        var fallback = await f.Service.GetSuggestionAsync(f.Account.AccountId, f.Booking.BookingId, new() { Type = "QUOTATION" });
        Assert.Equal(2_520, fallback.Items.Sum(item => item.Total));

        await PriceListSupport.AddListAsync(db, active: false);
        var error = await Assert.ThrowsAsync<UnprocessableEntityException>(() =>
            f.Service.GetSuggestionAsync(f.Account.AccountId, f.Booking.BookingId, new() { Type = "QUOTATION" }));
        Assert.Equal("NO_ACTIVE_PRICE_LIST", error.ErrorCode);
        Assert.Empty(await db.Invoices.ToListAsync());
    }
}
