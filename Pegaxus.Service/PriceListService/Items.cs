using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Extensions;
using Pegaxus.Service.Exceptions;

using static Pegaxus.Service.Common.DbTime;
using static Pegaxus.Service.Common.Validation;

namespace Pegaxus.Service.PriceListService;

public sealed partial class Service
{
    private const string DuplicateMessage = "An item with this code already exists in this price list. Use another code or edit the existing line";

    public async Task<IReadOnlyList<Response.ItemResponse>> GetItemsAsync(Guid priceListId, CancellationToken cancellationToken = default)
    {
        if (!await db.PriceLists.AsNoTracking().AnyAsync(item => item.PriceListId == priceListId, cancellationToken)) throw NotFound();
        var items = await db.PriceListItems.AsNoTracking().Where(item => item.PriceListId == priceListId).ToListAsync(cancellationToken);
        return items.OrderBy(item => item.SortOrder).ThenBy(item => item.ItemCode, StringComparer.Ordinal).Select(ToResponse).ToList();
    }

    public async Task<Response.ItemResponse> AddItemAsync(Guid priceListId, Request.CreateItemRequest request, Guid actorAccountId, CancellationToken cancellationToken = default)
    {
        ValidateFields(request, createItemValidator);
        await using var transaction = await db.Database.BeginSerializableAsync(cancellationToken);
        var list = await LockListAsync(priceListId, cancellationToken);
        if (list.IsActive) throw Locked();
        if (await db.PriceListItems.AnyAsync(item => item.PriceListId == priceListId && item.ItemCode == request.ItemCode, cancellationToken))
            throw new ConflictException(DuplicateMessage, "DUPLICATE_ITEM_CODE");

        var now = UtcNowTruncated();
        var sortOrder = request.SortOrder ?? (await db.PriceListItems.Where(item => item.PriceListId == priceListId)
            .Select(item => (int?)item.SortOrder).MaxAsync(cancellationToken) ?? 0) + 10;
        var item = new PriceListItem
        {
            PriceListItemId = Guid.NewGuid(), PriceListId = priceListId, ItemCode = request.ItemCode, ItemName = request.ItemName.Trim(),
            ItemType = PriceListService.Validator.PriceListRules.ParseType(request.ItemType)!.Value, Unit = request.Unit.Trim().ToUpperInvariant(),
            UnitPrice = request.UnitPrice!.Value, VehicleTier = CleanTier(request.VehicleTier), SortOrder = sortOrder, CreatedAt = now, UpdatedAt = now
        };
        db.PriceListItems.Add(item);
        Audit(nameof(PriceListItem), item.PriceListItemId, "CREATE", actorAccountId, $"Added {item.ItemCode} to price list {priceListId}", Empty, Snapshot(item), now);
        await SaveItemsAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToResponse(item);
    }

    public async Task<Response.ItemResponse> UpdateItemAsync(Guid priceListId, Guid priceListItemId, Request.UpdateItemRequest request,
        Guid actorAccountId, CancellationToken cancellationToken = default)
    {
        ValidateFields(request, updateItemValidator);
        await using var transaction = await db.Database.BeginSerializableAsync(cancellationToken);
        var list = await LockListAsync(priceListId, cancellationToken);
        var item = await db.PriceListItems.SingleOrDefaultAsync(entry => entry.PriceListId == priceListId && entry.PriceListItemId == priceListItemId, cancellationToken)
            ?? throw ItemNotFound();
        await db.Entry(item).ReloadAsync(cancellationToken);
        if (Truncate(request.LastUpdatedAt!.Value) != Truncate(item.UpdatedAt))
            throw new ConflictException("The price list item was just modified. Please reload the page", "PRICE_LIST_ITEM_MODIFIED");

        var type = PriceListService.Validator.PriceListRules.ParseType(request.ItemType)!.Value;
        var unit = request.Unit.Trim().ToUpperInvariant();
        var sortOrder = request.SortOrder ?? item.SortOrder;
        // An active list may change what a line is called and costs, but not what the line is (unit, type, order).
        if (list.IsActive && (type != item.ItemType || unit != item.Unit || sortOrder != item.SortOrder)) throw Locked();

        var before = Snapshot(item);
        item.ItemName = request.ItemName.Trim();
        item.ItemType = type;
        item.Unit = unit;
        item.UnitPrice = request.UnitPrice!.Value;
        item.VehicleTier = CleanTier(request.VehicleTier);
        item.SortOrder = sortOrder;
        var after = Snapshot(item);
        if (before.RootElement.GetRawText() != after.RootElement.GetRawText())
        {
            if (list.IsActive)
                EnsureTiers((await db.PriceListItems.AsNoTracking().Where(entry => entry.PriceListId == priceListId && entry.PriceListItemId != priceListItemId)
                    .ToListAsync(cancellationToken)).Append(item));
            item.UpdatedAt = NextVersion(item.UpdatedAt);
            Audit(nameof(PriceListItem), item.PriceListItemId, "UPDATE", actorAccountId, $"Changed {item.ItemCode} in price list {priceListId}", before, after, item.UpdatedAt);
            await SaveItemsAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return ToResponse(item);
    }

    public async Task DeleteItemAsync(Guid priceListId, Guid priceListItemId, Guid actorAccountId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginSerializableAsync(cancellationToken);
        var list = await LockListAsync(priceListId, cancellationToken);
        if (list.IsActive) throw Locked();
        var item = await db.PriceListItems.SingleOrDefaultAsync(entry => entry.PriceListId == priceListId && entry.PriceListItemId == priceListItemId, cancellationToken)
            ?? throw ItemNotFound();
        // InvoiceItem rows keep their price_list_item_id: they are snapshots and the id may legitimately outlive the line.
        db.PriceListItems.Remove(item);
        Audit(nameof(PriceListItem), item.PriceListItemId, "DELETE", actorAccountId, $"Removed {item.ItemCode} from price list {priceListId}", Snapshot(item), Empty, UtcNowTruncated());
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task SaveItemsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "PriceListItem_price_list_id_item_code_key" })
        {
            throw new ConflictException(DuplicateMessage, "DUPLICATE_ITEM_CODE");
        }
    }
}
