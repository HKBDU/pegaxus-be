using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;

using static Pegaxus.Service.Common.DbTime;

namespace Pegaxus.Service.PriceListService;

public sealed partial class Service(AppDbContext db,
    IValidator<Request.ListRequest> listValidator,
    IValidator<Request.CreateRequest> createValidator,
    IValidator<Request.UpdateRequest> updateValidator,
    IValidator<Request.CreateItemRequest> createItemValidator,
    IValidator<Request.UpdateItemRequest> updateItemValidator) : IPriceListService
{
    private static readonly string[] Tiers = ["S", "M", "L"];

    private static NotFoundException NotFound() => new("Price list not found", "PRICE_LIST_NOT_FOUND");

    private static NotFoundException ItemNotFound() => new("Price list item not found", "PRICE_LIST_ITEM_NOT_FOUND");

    private static UnprocessableEntityException Locked() => new(
        "The price list is active and locked. Clone it to change its lines, then activate the copy", "ACTIVE_PRICELIST_LOCKED");

    // The tiers are chosen by horse count, so an active list without a line for one of them could not quote that booking.
    private static void EnsureUsable(PriceList list, ICollection<PriceListItem> items)
    {
        if (items.Count == 0)
            throw new UnprocessableEntityException("A price list without any line cannot be activated. Add at least one line first", "PRICE_LIST_EMPTY");
        if (!string.Equals(list.Currency, Vnd.Currency, StringComparison.OrdinalIgnoreCase))
            throw new UnprocessableEntityException($"Only {Vnd.Currency} price lists can be activated", "PRICE_LIST_CURRENCY_UNSUPPORTED");
        EnsureTiers(items);
    }

    private static void EnsureTiers(IEnumerable<PriceListItem> items)
    {
        var missing = Tiers.Where(tier => !items.Any(item => item.VehicleTier == tier)).ToArray();
        if (missing.Length > 0)
            throw new UnprocessableEntityException($"The price list needs a line for vehicle tier {string.Join(", ", missing)}", "PRICE_LIST_TIER_MISSING");
    }

    private static DateTime NextVersion(DateTime previous)
    {
        var now = UtcNowTruncated();
        return now > previous ? now : Truncate(previous).AddTicks(10);
    }

    private static string? CleanTier(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    private static JsonDocument Snapshot(PriceList list) => JsonSerializer.SerializeToDocument(new
    {
        list.PriceListId, list.Name, list.Currency, list.TaxRate, list.DepositRate, list.IsActive
    });

    private static JsonDocument Snapshot(PriceListItem item) => JsonSerializer.SerializeToDocument(new
    {
        item.PriceListItemId, item.PriceListId, item.ItemCode, item.ItemName, ItemType = item.ItemType.ToString(),
        item.Unit, item.UnitPrice, item.VehicleTier, item.SortOrder
    });

    private static readonly JsonDocument Empty = JsonSerializer.SerializeToDocument(new { });

    private void Audit(string entityType, Guid entityId, string action, Guid actor, string? summary, JsonDocument before, JsonDocument after, DateTime at) =>
        db.CatalogAuditLogs.Add(new CatalogAuditLog
        {
            CatalogAuditLogId = Guid.NewGuid(), EntityType = entityType, EntityId = entityId, ActionType = action,
            ActorAccountId = actor, Summary = summary is { Length: > 500 } ? summary[..500] : summary,
            BeforeData = before, AfterData = after, CreatedAt = at
        });

    private static Response.ItemResponse ToResponse(PriceListItem item) => new()
    {
        PriceListItemId = item.PriceListItemId, PriceListId = item.PriceListId, ItemCode = item.ItemCode, ItemName = item.ItemName,
        ItemType = item.ItemType.ToString(), Unit = item.Unit, UnitPrice = Vnd.ToLong(item.UnitPrice), VehicleTier = item.VehicleTier,
        SortOrder = item.SortOrder, CreatedAt = AsUtc(item.CreatedAt), UpdatedAt = AsUtc(item.UpdatedAt)
    };

    private static Response.DetailResponse ToResponse(PriceList list, IEnumerable<PriceListItem> items) => new()
    {
        PriceListId = list.PriceListId, Name = list.Name, Currency = list.Currency, TaxRate = list.TaxRate, DepositRate = list.DepositRate,
        IsActive = list.IsActive, CreatedAt = AsUtc(list.CreatedAt), UpdatedAt = AsUtc(list.UpdatedAt),
        CreatedByAccountId = list.CreatedByAccountId, UpdatedByAccountId = list.UpdatedByAccountId,
        Items = items.OrderBy(item => item.SortOrder).ThenBy(item => item.ItemCode, StringComparer.Ordinal).Select(ToResponse).ToList()
    };
}
