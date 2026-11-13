using System.Text.Json;
using Pegaxus.Service.Exceptions;

using BookingRequest = Pegaxus.Service.BookingService.Request;

namespace Pegaxus.Service.Assistant.Tools;

public sealed class CustomerListMyBookingsTool(Pegaxus.Service.BookingService.IBookingService bookings) : IAssistantTool
{
    private sealed class Arguments
    {
        public int PageIndex { get; init; } = 1;
        public int PageSize { get; init; } = 10;
    }

    public string Name => "customer_list_my_bookings";
    public string Description => "List the authenticated customer's bookings, newest first.";
    public JsonElement ParametersSchema => AssistantToolJson.Schema("""
        {"type":"object","properties":{"pageIndex":{"type":"integer","minimum":1,"maximum":1000},"pageSize":{"type":"integer","minimum":1,"maximum":20}},"additionalProperties":false}
        """);

    public async Task<AssistantToolResult> RunAsync(JsonElement arguments, AssistantToolContext context)
    {
        AssistantToolJson.ValidateContext(context);
        var input = AssistantToolJson.Parse<Arguments>(arguments);
        if (input.PageIndex is < 1 or > 1000 || input.PageSize is < 1 or > 20)
            throw new BadRequestException("pageIndex must be between 1 and 1000 and pageSize between 1 and 20.", "VALIDATION_ERROR");

        var result = await bookings.GetListAsync(context.AccountId, new BookingRequest.BookingListRequest
        {
            PageIndex = input.PageIndex,
            PageSize = input.PageSize,
            SortBy = ["createdAt:desc"]
        }, context.CancellationToken);
        var data = new BookingListToolData(result.Items.Select(item => new BookingListToolItem(
            item.BookingId, item.BookingCode, item.Status, item.OriginCountry, item.DestinationCountry,
            item.RequestedDepartureDate, item.TotalHorses, item.CreatedAt)).ToArray(),
            result.PageIndex, result.PageSize, result.TotalItems, result.TotalPages);
        var references = result.Items.Select(item => new AssistantReferenceCandidate(
            "booking", item.BookingId.ToString(), item.BookingCode, item.CreatedAt)).ToArray();
        return new(Name, data, references);
    }
}

public sealed class CustomerGetBookingTool(Pegaxus.Service.BookingService.IBookingService bookings) : IAssistantTool
{
    private sealed class Arguments
    {
        public Guid? BookingId { get; init; }
        public string? BookingCode { get; init; }
    }

    public string Name => "customer_get_booking";
    public string Description => "Get a booking owned by the authenticated customer, by its bookingId or by the booking code the customer typed. Pass exactly one of them.";
    public JsonElement ParametersSchema => AssistantToolJson.Schema("""
        {"type":"object","properties":{"bookingId":{"type":"string","format":"uuid"},"bookingCode":{"type":"string","maxLength":50}},"additionalProperties":false}
        """);

    public async Task<AssistantToolResult> RunAsync(JsonElement arguments, AssistantToolContext context)
    {
        AssistantToolJson.ValidateContext(context);
        var input = AssistantToolJson.Parse<Arguments>(arguments);
        var code = input.BookingCode?.Trim();
        if ((input.BookingId is not null) == !string.IsNullOrEmpty(code) || input.BookingId == Guid.Empty || code?.Length > 50)
            throw new BadRequestException("Pass either a non-empty bookingId UUID or a bookingCode.", "VALIDATION_ERROR");

        var bookingId = input.BookingId ?? await IdByCodeAsync(code!, context);
        var booking = await bookings.GetAsync(context.AccountId, bookingId, context.CancellationToken);
        var data = new BookingDetailToolData(booking.BookingId, booking.BookingCode, booking.Status,
            booking.OriginAddress, booking.OriginCountry, booking.DestinationAddress, booking.DestinationCountry,
            booking.RequestedDepartureDate, booking.RequestedArrivalDate, booking.TotalHorses,
            booking.Horses.Select(horse => new BookingHorseToolItem(horse.HorseId, horse.HorseName)).ToArray(),
            booking.Transport is null ? null : new BookingTransportToolItem(booking.Transport.TransportId,
                booking.Transport.TransportCode, booking.Transport.Status), booking.CreatedAt, booking.UpdatedAt);
        var reference = new AssistantReferenceCandidate("booking", booking.BookingId.ToString(), booking.BookingCode, booking.UpdatedAt);
        return new(Name, data, [reference]);
    }

    /// <summary>Only an exact code match among the customer's own bookings counts; a partial match is never substituted.</summary>
    private async Task<Guid> IdByCodeAsync(string code, AssistantToolContext context)
    {
        var matches = await bookings.GetListAsync(context.AccountId, new BookingRequest.BookingListRequest
        {
            PageIndex = 1, PageSize = 20, Search = code, SortBy = ["createdAt:desc"]
        }, context.CancellationToken);
        return matches.Items.FirstOrDefault(item => string.Equals(item.BookingCode, code, StringComparison.OrdinalIgnoreCase))?.BookingId
            ?? throw new NotFoundException("Booking not found", "BOOKING_NOT_FOUND");
    }
}

public sealed class CustomerGetBookingHistoryTool(Pegaxus.Service.BookingService.IBookingService bookings) : IAssistantTool
{
    private sealed class Arguments
    {
        public Guid BookingId { get; init; }
    }

    public string Name => "customer_get_booking_history";
    public string Description => "Get status history for a booking owned by the authenticated customer.";
    public JsonElement ParametersSchema => AssistantToolJson.Schema("""
        {"type":"object","properties":{"bookingId":{"type":"string","format":"uuid"}},"required":["bookingId"],"additionalProperties":false}
        """);

    public async Task<AssistantToolResult> RunAsync(JsonElement arguments, AssistantToolContext context)
    {
        AssistantToolJson.ValidateContext(context);
        var input = AssistantToolJson.Parse<Arguments>(arguments);
        if (input.BookingId == Guid.Empty)
            throw new BadRequestException("bookingId must be a non-empty UUID.", "VALIDATION_ERROR");

        var history = await bookings.GetHistoriesAsync(context.AccountId, input.BookingId, context.CancellationToken);
        var recent = history.TakeLast(20).Select(item => new BookingHistoryToolItem(
            item.Action, item.PreviousStatus, item.NewStatus, item.ChangedAt)).ToArray();
        var updatedAt = recent.LastOrDefault()?.ChangedAt;
        var data = new BookingHistoryToolData(input.BookingId, recent, history.Count > recent.Length);
        var reference = new AssistantReferenceCandidate("booking", input.BookingId.ToString(), "Booking history", updatedAt);
        return new(Name, data, [reference]);
    }
}

public sealed record BookingListToolData(IReadOnlyList<BookingListToolItem> Items, int PageIndex, int PageSize, int TotalItems, int TotalPages);
public sealed record BookingListToolItem(Guid BookingId, string BookingCode, string Status, string OriginCountry,
    string DestinationCountry, DateOnly RequestedDepartureDate, int TotalHorses, DateTimeOffset CreatedAt);
public sealed record BookingDetailToolData(Guid BookingId, string BookingCode, string Status, string OriginAddress,
    string OriginCountry, string DestinationAddress, string DestinationCountry, DateOnly RequestedDepartureDate,
    DateOnly? RequestedArrivalDate, int TotalHorses, IReadOnlyList<BookingHorseToolItem> Horses,
    BookingTransportToolItem? Transport, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record BookingHorseToolItem(Guid HorseId, string HorseName);
public sealed record BookingTransportToolItem(Guid TransportId, string TransportCode, string Status);
public sealed record BookingHistoryToolData(Guid BookingId, IReadOnlyList<BookingHistoryToolItem> History, bool HasEarlierHistory);
public sealed record BookingHistoryToolItem(string Action, string? PreviousStatus, string NewStatus, DateTimeOffset ChangedAt);
