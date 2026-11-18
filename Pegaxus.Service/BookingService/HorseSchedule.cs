using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;

namespace Pegaxus.Service.BookingService;

/// <summary>
/// A horse can be on one trip at a time. A booking holds its horses from its departure date to its arrival date (only the departure
/// day when it has no arrival date), and a booking whose transport is on the road holds them until that transport completes.
/// </summary>
public sealed partial class Service
{
    private static readonly BookingStatus[] HoldingStatuses = [BookingStatus.DRAFT, BookingStatus.PENDING, BookingStatus.APPROVED];

    /// <summary>
    /// Refuses when one of <paramref name="horseIds"/> is already held by another live booking whose dates overlap. On approval
    /// (<paramref name="approvedOnly"/>) only approved bookings count, so of two requests waiting for the same horse the first one approved wins.
    /// </summary>
    private async Task EnsureHorsesFreeAsync(Guid? bookingId, IReadOnlyCollection<Guid> horseIds, DateOnly departure, DateOnly? arrival,
        CancellationToken cancellationToken, bool approvedOnly = false)
    {
        if (horseIds.Count == 0) return;
        var end = arrival ?? departure;
        BookingStatus[] holding = approvedOnly ? [BookingStatus.APPROVED] : HoldingStatuses;
        var clash = await _db.BookingHorses.AsNoTracking()
            .Where(link => horseIds.Contains(link.HorseId) && link.DetachedAt == null && link.BookingId != bookingId &&
                holding.Contains(link.Booking.Status) &&
                (link.Booking.Transport == null ||
                    link.Booking.Transport.Status != TransportStatus.COMPLETED && link.Booking.Transport.Status != TransportStatus.CANCELLED) &&
                link.Booking.RequestedDepartureDate <= end &&
                // A transport on the road keeps its horses until it completes, whatever its planned arrival.
                ((link.Booking.Transport != null && link.Booking.Transport.Status == TransportStatus.IN_TRANSIT) ||
                    (link.Booking.RequestedArrivalDate ?? link.Booking.RequestedDepartureDate) >= departure))
            .OrderBy(link => link.Booking.RequestedDepartureDate).ThenBy(link => link.Booking.BookingCode)
            .Select(link => new
            {
                link.Horse.HorseName, link.Booking.BookingCode, link.Booking.RequestedDepartureDate, link.Booking.RequestedArrivalDate,
                OnTheRoad = link.Booking.Transport != null && link.Booking.Transport.Status == TransportStatus.IN_TRANSIT
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (clash is null) return;
        var when = clash.OnTheRoad
            ? "is on the road right now and has not arrived yet"
            : clash.RequestedArrivalDate is { } until && until != clash.RequestedDepartureDate
                ? $"is already booked from {Day(clash.RequestedDepartureDate)} to {Day(until)}"
                : $"is already booked on {Day(clash.RequestedDepartureDate)}";
        throw new ConflictException($"{clash.HorseName} {when} (booking {clash.BookingCode}). Choose other dates or another horse.",
            "HORSE_SCHEDULE_CONFLICT");
    }

    private static string Day(DateOnly date) => date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    /// <summary>The horses that still travel with <paramref name="bookingId"/>.</summary>
    private Task<List<Guid>> BookingHorseIdsAsync(Guid bookingId, CancellationToken cancellationToken) =>
        _db.BookingHorses.AsNoTracking().Where(link => link.BookingId == bookingId && link.DetachedAt == null)
            .Select(link => link.HorseId).ToListAsync(cancellationToken);
}
