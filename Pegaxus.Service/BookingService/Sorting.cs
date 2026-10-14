using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Repository.Extensions;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;

using BookingEntity = Pegaxus.Repository.Entities.Booking;

namespace Pegaxus.Service.BookingService;

public sealed partial class Service
{
    private static IQueryable<BookingEntity> ApplySorting(
        IQueryable<BookingEntity> query,
        IReadOnlyCollection<string>? sortBy)
    {
        var sortParameters = sortBy is null || sortBy.Count == 0
            ? ["createdAt:desc"]
            : sortBy.SelectMany(value => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        IOrderedQueryable<BookingEntity>? orderedQuery = null;
        foreach (var sortParameter in sortParameters)
        {
            var parts = sortParameter.Split(':', StringSplitOptions.TrimEntries);
            if (parts.Length != 2 ||
                (parts[1].ToLowerInvariant() != "asc" && parts[1].ToLowerInvariant() != "desc") ||
                (parts[0].ToLowerInvariant() != "createdat" &&
                 parts[0].ToLowerInvariant() != "requesteddeparturedate"))
                throw new BadRequestException("Sort parameter is invalid", "INVALID_SORT_PARAMETER");

            var ascending = parts[1].Equals("asc", StringComparison.OrdinalIgnoreCase);
            var field = parts[0].ToLowerInvariant();
            orderedQuery = orderedQuery is null
                ? ApplyPrimarySort(query, field, ascending)
                : ApplySecondarySort(orderedQuery, field, ascending);
        }

        return orderedQuery ?? query.OrderByDescending(booking => booking.CreatedAt);
    }

    private static IOrderedQueryable<BookingEntity> ApplyPrimarySort(
        IQueryable<BookingEntity> query,
        string field,
        bool ascending) => field switch
        {
            "createdat" when ascending => query.OrderBy(booking => booking.CreatedAt),
            "createdat" => query.OrderByDescending(booking => booking.CreatedAt),
            "requesteddeparturedate" when ascending => query.OrderBy(booking => booking.RequestedDepartureDate),
            _ => query.OrderByDescending(booking => booking.RequestedDepartureDate)
        };

    private static IOrderedQueryable<BookingEntity> ApplySecondarySort(
        IOrderedQueryable<BookingEntity> query,
        string field,
        bool ascending) => field switch
        {
            "createdat" when ascending => query.ThenBy(booking => booking.CreatedAt),
            "createdat" => query.ThenByDescending(booking => booking.CreatedAt),
            "requesteddeparturedate" when ascending => query.ThenBy(booking => booking.RequestedDepartureDate),
            _ => query.ThenByDescending(booking => booking.RequestedDepartureDate)
        };
}
