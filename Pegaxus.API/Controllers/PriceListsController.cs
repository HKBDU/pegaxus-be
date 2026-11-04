using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Pegaxus.API.Extensions;
using Pegaxus.API.Models;
using Pegaxus.Service.Exceptions;

using PriceLists = Pegaxus.Service.PriceListService;

namespace Pegaxus.API.Controllers;

[ApiController]
[Authorize(Policy = JwtExtensions.LogisticsManagerPolicy)]
[Route("api/v1/price-lists")]
public sealed class PriceListsController(PriceLists.IPriceListService service) : ApiControllerBase
{
    /// <summary>
    /// List price lists.
    /// </summary>
    /// <remarks>Logistics Managers page through the price lists, optionally filtered by isActive or name search. The list also shows how many lines each price list holds.</remarks>
    /// <param name="request">The filters, sort order and pagination parameters.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with a paginated list of price lists.</returns>
    /// <exception cref="BadRequestException">The query parameters are invalid.</exception>
    [HttpGet]
    [EnableRateLimiting(RateLimitingExtensions.PriceListPolicy)]
    public async Task<IActionResult> GetList([FromQuery] PriceLists.Request.ListRequest request, CancellationToken cancellationToken) =>
        Success(await service.GetListAsync(request, cancellationToken), "Price lists retrieved successfully.");

    /// <summary>
    /// Get a price list.
    /// </summary>
    /// <remarks>Returns the header (name, currency, taxRate, depositRate, isActive) and every line. Send updatedAt back as lastUpdatedAt when editing the header.</remarks>
    /// <param name="priceListId">The price list identifier (UUID).</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the price list and its lines.</returns>
    /// <exception cref="NotFoundException">The price list does not exist.</exception>
    [HttpGet("{priceListId:guid}")]
    [EnableRateLimiting(RateLimitingExtensions.PriceListPolicy)]
    public async Task<IActionResult> Get(Guid priceListId, CancellationToken cancellationToken) =>
        Success(await service.GetAsync(priceListId, cancellationToken), "Price list retrieved successfully.");

    /// <summary>
    /// Create a price list.
    /// </summary>
    /// <remarks>The new price list is inactive and has no lines. Add lines (or clone an existing list), then activate it. taxRate and depositRate are percentages from 0 to 100 with at most 2 decimals.</remarks>
    /// <param name="request">The name, currency, taxRate and depositRate.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>201 Created with the new price list.</returns>
    /// <exception cref="BadRequestException">The request fields are invalid.</exception>
    [HttpPost]
    [EnableRateLimiting(RateLimitingExtensions.PriceListPolicy)]
    public async Task<IActionResult> Create([FromBody] PriceLists.Request.CreateRequest request, CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(request, AccountId, cancellationToken);
        return CreatedAtAction(nameof(Get), new { priceListId = result.PriceListId }, ResponseBuilder.SuccessResponse(
            result, "Price list created successfully.", StatusCodes.Status201Created, HttpContext.TraceIdentifier));
    }

    /// <summary>
    /// Update a price list header.
    /// </summary>
    /// <remarks>Only inactive price lists can be edited; an active one returns 422 ACTIVE_PRICELIST_LOCKED. A stale lastUpdatedAt returns 409 PRICE_LIST_MODIFIED.</remarks>
    /// <param name="priceListId">The price list identifier (UUID).</param>
    /// <param name="request">The replacement header and the lastUpdatedAt version from the last read.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the updated price list.</returns>
    /// <exception cref="BadRequestException">The request fields are invalid.</exception>
    /// <exception cref="NotFoundException">The price list does not exist.</exception>
    /// <exception cref="ConflictException">The price list was changed since it was read.</exception>
    /// <exception cref="UnprocessableEntityException">The price list is active and locked.</exception>
    [HttpPut("{priceListId:guid}")]
    [EnableRateLimiting(RateLimitingExtensions.PriceListPolicy)]
    public async Task<IActionResult> Update(Guid priceListId, [FromBody] PriceLists.Request.UpdateRequest request, CancellationToken cancellationToken) =>
        Success(await service.UpdateAsync(priceListId, request, AccountId, cancellationToken), "Price list updated successfully.");

    /// <summary>
    /// Activate a price list.
    /// </summary>
    /// <remarks>
    /// Makes this price list the one used for new quotations, deposits and tax, and deactivates the current one in the same transaction.
    /// The list needs at least one line, a line for each vehicle tier S, M and L, and the VND currency.
    /// Activating the already active list does nothing.
    /// </remarks>
    /// <param name="priceListId">The price list identifier (UUID).</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the activated price list.</returns>
    /// <exception cref="NotFoundException">The price list does not exist.</exception>
    /// <exception cref="UnprocessableEntityException">PRICE_LIST_EMPTY, PRICE_LIST_TIER_MISSING or PRICE_LIST_CURRENCY_UNSUPPORTED.</exception>
    [HttpPost("{priceListId:guid}/activate")]
    [EnableRateLimiting(RateLimitingExtensions.PriceListPolicy)]
    public async Task<IActionResult> Activate(Guid priceListId, CancellationToken cancellationToken) =>
        Success(await service.ActivateAsync(priceListId, AccountId, cancellationToken), "Price list activated successfully.");

    /// <summary>
    /// Clone a price list.
    /// </summary>
    /// <remarks>Creates an inactive copy named "[name] (Copy)" with every line copied. Edit the copy and activate it to change prices without touching the active list.</remarks>
    /// <param name="priceListId">The price list to copy (UUID).</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>201 Created with the new price list.</returns>
    /// <exception cref="NotFoundException">The price list does not exist.</exception>
    [HttpPost("{priceListId:guid}/clone")]
    [EnableRateLimiting(RateLimitingExtensions.PriceListPolicy)]
    public async Task<IActionResult> Clone(Guid priceListId, CancellationToken cancellationToken)
    {
        var result = await service.CloneAsync(priceListId, AccountId, cancellationToken);
        return CreatedAtAction(nameof(Get), new { priceListId = result.PriceListId }, ResponseBuilder.SuccessResponse(
            result, "Price list cloned successfully.", StatusCodes.Status201Created, HttpContext.TraceIdentifier));
    }

    /// <summary>
    /// List the lines of a price list.
    /// </summary>
    /// <remarks>Lines are ordered by sortOrder, then itemCode.</remarks>
    /// <param name="priceListId">The price list identifier (UUID).</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the lines.</returns>
    /// <exception cref="NotFoundException">The price list does not exist.</exception>
    [HttpGet("{priceListId:guid}/items")]
    [EnableRateLimiting(RateLimitingExtensions.PriceListItemPolicy)]
    public async Task<IActionResult> GetItems(Guid priceListId, CancellationToken cancellationToken) =>
        Success(await service.GetItemsAsync(priceListId, cancellationToken), "Price list items retrieved successfully.");

    /// <summary>
    /// Add a line to a price list.
    /// </summary>
    /// <remarks>
    /// itemCode is unique inside the list (A-Z, 0-9, underscore). unit is TRIP (quantity 1), HORSE (per horse) or HORSE_DAY (per horse per transit day).
    /// unitPrice is a whole number of VND. vehicleTier S, M or L limits the line to that tier. sortOrder defaults to the end of the list.
    /// Lines cannot be added to an active list (422 ACTIVE_PRICELIST_LOCKED).
    /// </remarks>
    /// <param name="priceListId">The price list identifier (UUID).</param>
    /// <param name="request">The line to add.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>201 Created with the new line.</returns>
    /// <exception cref="BadRequestException">The request fields are invalid.</exception>
    /// <exception cref="NotFoundException">The price list does not exist.</exception>
    /// <exception cref="ConflictException">DUPLICATE_ITEM_CODE.</exception>
    /// <exception cref="UnprocessableEntityException">The price list is active and locked.</exception>
    [HttpPost("{priceListId:guid}/items")]
    [EnableRateLimiting(RateLimitingExtensions.PriceListItemPolicy)]
    public async Task<IActionResult> AddItem(Guid priceListId, [FromBody] PriceLists.Request.CreateItemRequest request, CancellationToken cancellationToken)
    {
        var result = await service.AddItemAsync(priceListId, request, AccountId, cancellationToken);
        return SuccessCreated(result, "Price list item created successfully.");
    }

    /// <summary>
    /// Update a line of a price list.
    /// </summary>
    /// <remarks>
    /// itemCode cannot change. On an active list only itemName, unitPrice and vehicleTier may change (itemType, unit and sortOrder return 422 ACTIVE_PRICELIST_LOCKED),
    /// and the list must still have a line for each tier. Invoices already created keep their prices. A stale lastUpdatedAt returns 409 PRICE_LIST_ITEM_MODIFIED.
    /// </remarks>
    /// <param name="priceListId">The price list identifier (UUID).</param>
    /// <param name="priceListItemId">The line identifier (UUID).</param>
    /// <param name="request">The replacement line and the lastUpdatedAt version from the last read.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the updated line.</returns>
    /// <exception cref="BadRequestException">The request fields are invalid.</exception>
    /// <exception cref="NotFoundException">The price list or line does not exist.</exception>
    /// <exception cref="ConflictException">The line was changed since it was read.</exception>
    /// <exception cref="UnprocessableEntityException">ACTIVE_PRICELIST_LOCKED or PRICE_LIST_TIER_MISSING.</exception>
    [HttpPut("{priceListId:guid}/items/{priceListItemId:guid}")]
    [EnableRateLimiting(RateLimitingExtensions.PriceListItemPolicy)]
    public async Task<IActionResult> UpdateItem(Guid priceListId, Guid priceListItemId, [FromBody] PriceLists.Request.UpdateItemRequest request,
        CancellationToken cancellationToken) =>
        Success(await service.UpdateItemAsync(priceListId, priceListItemId, request, AccountId, cancellationToken), "Price list item updated successfully.");

    /// <summary>
    /// Delete a line from a price list.
    /// </summary>
    /// <remarks>Not allowed on an active list (422 ACTIVE_PRICELIST_LOCKED). Invoice lines generated from the deleted line keep their snapshot.</remarks>
    /// <param name="priceListId">The price list identifier (UUID).</param>
    /// <param name="priceListItemId">The line identifier (UUID).</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK after the line is deleted.</returns>
    /// <exception cref="NotFoundException">The price list or line does not exist.</exception>
    /// <exception cref="UnprocessableEntityException">The price list is active and locked.</exception>
    [HttpDelete("{priceListId:guid}/items/{priceListItemId:guid}")]
    [EnableRateLimiting(RateLimitingExtensions.PriceListItemPolicy)]
    public async Task<IActionResult> DeleteItem(Guid priceListId, Guid priceListItemId, CancellationToken cancellationToken)
    {
        await service.DeleteItemAsync(priceListId, priceListItemId, AccountId, cancellationToken);
        return Success(null, "Price list item deleted successfully.");
    }
}
