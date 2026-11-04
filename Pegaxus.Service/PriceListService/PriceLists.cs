using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Extensions;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;

using static Pegaxus.Service.Common.DbTime;
using static Pegaxus.Service.Common.Validation;

namespace Pegaxus.Service.PriceListService;

public sealed partial class Service
{
    public async Task<Response.ListResponse> GetListAsync(Request.ListRequest request, CancellationToken cancellationToken = default)
    {
        ValidateFields(request, listValidator);
        var query = db.PriceLists.AsNoTracking();
        if (request.IsActive is { } active) query = query.Where(item => item.IsActive == active);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToLowerInvariant();
            query = query.Where(item => item.Name.ToLower().Contains(search));
        }
        var ordered = request.SortBy.Trim().ToLowerInvariant() switch
        {
            "createdat:asc" => query.OrderBy(item => item.CreatedAt),
            "updatedat:asc" => query.OrderBy(item => item.UpdatedAt),
            "updatedat:desc" => query.OrderByDescending(item => item.UpdatedAt),
            "name:asc" => query.OrderBy(item => item.Name),
            "name:desc" => query.OrderByDescending(item => item.Name),
            _ => query.OrderByDescending(item => item.CreatedAt)
        };
        var total = await query.CountAsync(cancellationToken);
        var offset = ((long)request.PageIndex - 1) * request.PageSize;
        var rows = offset >= total ? [] : await ordered.ThenBy(item => item.PriceListId).Skip((int)offset).Take(request.PageSize)
            .Select(item => new { List = item, Count = item.PriceListItems.Count }).ToListAsync(cancellationToken);
        return new Response.ListResponse
        {
            Items = rows.Select(row => new Response.SummaryResponse
            {
                PriceListId = row.List.PriceListId, Name = row.List.Name, Currency = row.List.Currency, TaxRate = row.List.TaxRate,
                DepositRate = row.List.DepositRate, IsActive = row.List.IsActive, ItemCount = row.Count,
                CreatedAt = AsUtc(row.List.CreatedAt), UpdatedAt = AsUtc(row.List.UpdatedAt)
            }).ToList(),
            PageIndex = request.PageIndex, PageSize = request.PageSize, TotalItems = total, TotalPages = Paging.TotalPages(total, request.PageSize)
        };
    }

    public async Task<Response.DetailResponse> GetAsync(Guid priceListId, CancellationToken cancellationToken = default)
    {
        var list = await db.PriceLists.AsNoTracking().Include(item => item.PriceListItems)
            .SingleOrDefaultAsync(item => item.PriceListId == priceListId, cancellationToken) ?? throw NotFound();
        return ToResponse(list, list.PriceListItems);
    }

    public async Task<Response.DetailResponse> CreateAsync(Request.CreateRequest request, Guid actorAccountId, CancellationToken cancellationToken = default)
    {
        ValidateFields(request, createValidator);
        var now = UtcNowTruncated();
        var list = new PriceList
        {
            PriceListId = Guid.NewGuid(), Name = request.Name.Trim(), Currency = request.Currency.Trim().ToUpperInvariant(),
            TaxRate = request.TaxRate!.Value, DepositRate = request.DepositRate!.Value, IsActive = false,
            CreatedAt = now, UpdatedAt = now, CreatedByAccountId = actorAccountId, UpdatedByAccountId = actorAccountId
        };
        db.PriceLists.Add(list);
        Audit(nameof(PriceList), list.PriceListId, "CREATE", actorAccountId, null, Empty, Snapshot(list), now);
        await db.SaveChangesAsync(cancellationToken);
        return ToResponse(list, []);
    }

    public async Task<Response.DetailResponse> UpdateAsync(Guid priceListId, Request.UpdateRequest request, Guid actorAccountId, CancellationToken cancellationToken = default)
    {
        ValidateFields(request, updateValidator);
        await using var transaction = await db.Database.BeginSerializableAsync(cancellationToken);
        var list = await LockListAsync(priceListId, cancellationToken);
        if (list.IsActive) throw Locked();
        if (Truncate(request.LastUpdatedAt!.Value) != Truncate(list.UpdatedAt))
            throw new ConflictException("The price list was just modified. Please reload the page", "PRICE_LIST_MODIFIED");

        var before = Snapshot(list);
        list.Name = request.Name.Trim();
        list.Currency = request.Currency.Trim().ToUpperInvariant();
        list.TaxRate = request.TaxRate!.Value;
        list.DepositRate = request.DepositRate!.Value;
        var after = Snapshot(list);
        if (before.RootElement.GetRawText() != after.RootElement.GetRawText())
        {
            list.UpdatedAt = NextVersion(list.UpdatedAt);
            list.UpdatedByAccountId = actorAccountId;
            Audit(nameof(PriceList), list.PriceListId, "UPDATE", actorAccountId, null, before, after, list.UpdatedAt);
            await db.SaveChangesAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(priceListId, cancellationToken);
    }

    public async Task<Response.DetailResponse> ActivateAsync(Guid priceListId, Guid actorAccountId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginSerializableAsync(cancellationToken);
        // The table is tiny; locking every list keeps two concurrent activations from both succeeding (BR-010).
        await db.LockRowsAsync(db.PriceLists, item => item.PriceListId, cancellationToken);
        var list = await db.PriceLists.Include(item => item.PriceListItems).SingleOrDefaultAsync(item => item.PriceListId == priceListId, cancellationToken)
            ?? throw NotFound();
        if (list.IsActive) return ToResponse(list, list.PriceListItems);
        EnsureUsable(list, list.PriceListItems);

        var now = UtcNowTruncated();
        foreach (var old in await db.PriceLists.Where(item => item.IsActive).ToListAsync(cancellationToken))
        {
            var before = Snapshot(old);
            old.IsActive = false;
            old.UpdatedAt = NextVersion(old.UpdatedAt);
            old.UpdatedByAccountId = actorAccountId;
            Audit(nameof(PriceList), old.PriceListId, "DEACTIVATE", actorAccountId, $"Replaced by price list {list.PriceListId}", before, Snapshot(old), now);
        }
        // Two saves: the unique index on the active flag must see the old list switched off before the new one is switched on.
        await db.SaveChangesAsync(cancellationToken);

        var beforeActivation = Snapshot(list);
        list.IsActive = true;
        list.UpdatedAt = NextVersion(list.UpdatedAt);
        list.UpdatedByAccountId = actorAccountId;
        Audit(nameof(PriceList), list.PriceListId, "ACTIVATE", actorAccountId, null, beforeActivation, Snapshot(list), now);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToResponse(list, list.PriceListItems);
    }

    public async Task<Response.DetailResponse> CloneAsync(Guid priceListId, Guid actorAccountId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginSerializableAsync(cancellationToken);
        var source = await LockListAsync(priceListId, cancellationToken);
        var items = await db.PriceListItems.AsNoTracking().Where(item => item.PriceListId == priceListId).ToListAsync(cancellationToken);

        const string suffix = " (Copy)";
        var now = UtcNowTruncated();
        var copy = new PriceList
        {
            PriceListId = Guid.NewGuid(), Name = (source.Name.Length > 200 - suffix.Length ? source.Name[..(200 - suffix.Length)] : source.Name) + suffix,
            Currency = source.Currency, TaxRate = source.TaxRate, DepositRate = source.DepositRate, IsActive = false,
            CreatedAt = now, UpdatedAt = now, CreatedByAccountId = actorAccountId, UpdatedByAccountId = actorAccountId
        };
        db.PriceLists.Add(copy);
        foreach (var item in items)
            db.PriceListItems.Add(new PriceListItem
            {
                PriceListItemId = Guid.NewGuid(), PriceListId = copy.PriceListId, ItemCode = item.ItemCode, ItemName = item.ItemName,
                ItemType = item.ItemType, Unit = item.Unit, UnitPrice = item.UnitPrice, VehicleTier = item.VehicleTier,
                SortOrder = item.SortOrder, CreatedAt = now, UpdatedAt = now
            });
        Audit(nameof(PriceList), copy.PriceListId, "CLONE", actorAccountId, $"Cloned from price list {source.PriceListId} with {items.Count} lines", Empty, Snapshot(copy), now);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(copy.PriceListId, cancellationToken);
    }

    // Row lock on the list header serializes every change to the list and its lines.
    private async Task<PriceList> LockListAsync(Guid priceListId, CancellationToken cancellationToken)
    {
        await db.LockRowsAsync(db.PriceLists.Where(item => item.PriceListId == priceListId), item => item.PriceListId, cancellationToken);
        var list = await db.PriceLists.SingleOrDefaultAsync(item => item.PriceListId == priceListId, cancellationToken) ?? throw NotFound();
        await db.Entry(list).ReloadAsync(cancellationToken);
        return list;
    }
}
