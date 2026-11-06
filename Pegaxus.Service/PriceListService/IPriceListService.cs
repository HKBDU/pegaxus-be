namespace Pegaxus.Service.PriceListService;

public interface IPriceListService
{
    Task<Response.ListResponse> GetListAsync(Request.ListRequest request, CancellationToken cancellationToken = default);
    Task<Response.DetailResponse> GetAsync(Guid priceListId, CancellationToken cancellationToken = default);
    Task<Response.DetailResponse> CreateAsync(Request.CreateRequest request, Guid actorAccountId, CancellationToken cancellationToken = default);
    Task<Response.DetailResponse> UpdateAsync(Guid priceListId, Request.UpdateRequest request, Guid actorAccountId, CancellationToken cancellationToken = default);
    Task<Response.DetailResponse> ActivateAsync(Guid priceListId, Guid actorAccountId, CancellationToken cancellationToken = default);
    Task<Response.DetailResponse> CloneAsync(Guid priceListId, Guid actorAccountId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Response.ItemResponse>> GetItemsAsync(Guid priceListId, CancellationToken cancellationToken = default);
    Task<Response.ItemResponse> AddItemAsync(Guid priceListId, Request.CreateItemRequest request, Guid actorAccountId, CancellationToken cancellationToken = default);
    Task<Response.ItemResponse> UpdateItemAsync(Guid priceListId, Guid priceListItemId, Request.UpdateItemRequest request, Guid actorAccountId, CancellationToken cancellationToken = default);
    Task DeleteItemAsync(Guid priceListId, Guid priceListItemId, Guid actorAccountId, CancellationToken cancellationToken = default);
}
