using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;

using static Pegaxus.Service.Common.DbTime;

namespace Pegaxus.Service.BookingService;

/// <summary>
/// A return booking brings back one horse that was left behind mid-trip (BookingHorse.DetachedAt). It goes from the stop where the
/// horse stays to the origin of the original booking and keeps a link to the original transport (Booking.ReturnOfTransportId).
/// </summary>
public sealed partial class Service
{
    private static readonly BookingStatus[] LiveBookingStatuses = [BookingStatus.DRAFT, BookingStatus.PENDING, BookingStatus.APPROVED];

    public async Task<Response.ReturnDraftResponse> GetReturnDraftAsync(Guid accountId, Guid transportId, Guid horseId,
        CancellationToken cancellationToken = default)
    {
        var customer = await GetCustomerAsync(accountId, cancellationToken);
        var link = await DetachedHorseAsync(customer.CustomerId, transportId, horseId, cancellationToken);
        var location = link.DetachedLocation;
        return new Response.ReturnDraftResponse
        {
            ReturnOfTransportId = transportId,
            TransportCode = link.Booking.Transport!.TransportCode,
            BookingCode = link.Booking.BookingCode,
            HorseId = horseId,
            HorseName = link.Horse.HorseName,
            OriginAddress = location is null ? string.Empty : string.Join(", ", new[] { location.LocationName, location.Address, location.City }
                .Where(part => !string.IsNullOrWhiteSpace(part)).Distinct()),
            OriginCountry = location?.Country ?? string.Empty,
            OriginLatitude = location?.Latitude,
            OriginLongitude = location?.Longitude,
            DestinationAddress = link.Booking.OriginAddress,
            DestinationCountry = link.Booking.OriginCountry,
            DestinationLatitude = link.Booking.OriginLatitude,
            DestinationLongitude = link.Booking.OriginLongitude,
            ExistingReturnBookingId = await LiveReturnBookingIdAsync(transportId, horseId, cancellationToken)
        };
    }

    /// <summary>
    /// Checks a return booking before it is created: exactly one horse, left behind by a transport of this customer, with no other
    /// live return booking. The lead-time and same-country rules of a normal booking do not apply: the horse is already waiting.
    /// </summary>
    private async Task ValidateReturnBookingAsync(Customer customer, Request.CreateBookingRequest request, CancellationToken cancellationToken)
    {
        var transportId = request.ReturnOfTransportId!.Value;
        if (request.Horses.Count != 1)
            throw new BadRequestException("A return booking carries exactly the one horse that was left behind", "RETURN_BOOKING_HORSE_INVALID");
        var horseId = request.Horses[0].HorseId;
        await DetachedHorseAsync(customer.CustomerId, transportId, horseId, cancellationToken);
        if (await LiveReturnBookingIdAsync(transportId, horseId, cancellationToken) is not null)
            throw new ConflictException("This horse already has a return booking", "RETURN_BOOKING_EXISTS");
        if (request.RequestedDepartureDate < DateOnly.FromDateTime(DateTime.UtcNow))
            throw new BadRequestException("The departure date cannot be in the past", "DEPARTURE_DATE_OUT_OF_RANGE");
        if (request.RequestedArrivalDate.HasValue && request.RequestedArrivalDate.Value < request.RequestedDepartureDate)
            throw new BadRequestException("The arrival date cannot be before the departure date", "ARRIVAL_DATE_INVALID");
    }

    private async Task<BookingHorse> DetachedHorseAsync(Guid customerId, Guid transportId, Guid horseId, CancellationToken cancellationToken)
    {
        if (transportId == Guid.Empty || horseId == Guid.Empty)
            throw new BadRequestException("Transport ID and horse ID must be non-empty UUIDs.", "VALIDATION_ERROR");
        // Another customer's transport is the same 404 as a missing one.
        return await _db.BookingHorses.AsNoTracking().Include(item => item.Horse).Include(item => item.DetachedLocation)
                   .Include(item => item.Booking).ThenInclude(item => item.Transport)
                   .SingleOrDefaultAsync(item => item.HorseId == horseId && item.Booking.CustomerId == customerId &&
                       item.Booking.Transport != null && item.Booking.Transport.TransportId == transportId && item.DetachedAt != null, cancellationToken)
               ?? throw new NotFoundException("No horse was left behind by this transport", "DETACHED_HORSE_NOT_FOUND");
    }

    private Task<Guid?> LiveReturnBookingIdAsync(Guid transportId, Guid horseId, CancellationToken cancellationToken) =>
        _db.Bookings.AsNoTracking().Where(item => item.ReturnOfTransportId == transportId && LiveBookingStatuses.Contains(item.Status) &&
                item.BookingHorses.Any(link => link.HorseId == horseId))
            .Select(item => (Guid?)item.BookingId).FirstOrDefaultAsync(cancellationToken);

    /// <summary>The original trip of a return booking, for the banner TS, RC and LM see.</summary>
    private async Task<Response.ReturnOfResponse?> ReturnOfAsync(Guid? returnOfTransportId, IEnumerable<Guid> horseIds, CancellationToken cancellationToken)
    {
        if (returnOfTransportId is not { } transportId) return null;
        var ids = horseIds.ToArray();
        var transport = await _db.Transports.AsNoTracking().Where(item => item.TransportId == transportId)
            .Select(item => new { item.TransportId, item.TransportCode, item.BookingId, item.Booking.BookingCode })
            .SingleOrDefaultAsync(cancellationToken);
        if (transport is null) return null;
        var horse = await _db.BookingHorses.AsNoTracking()
            .Where(link => link.BookingId == transport.BookingId && ids.Contains(link.HorseId) && link.DetachedAt != null)
            .Select(link => new { link.HorseId, link.Horse.HorseName, link.DetachedAt, LocationName = link.DetachedLocation == null ? null : link.DetachedLocation.LocationName })
            .FirstOrDefaultAsync(cancellationToken);
        return new Response.ReturnOfResponse
        {
            TransportId = transport.TransportId,
            TransportCode = transport.TransportCode,
            BookingId = transport.BookingId,
            BookingCode = transport.BookingCode,
            Horse = horse is null ? null : new Response.ReturnOfHorseResponse
            {
                HorseId = horse.HorseId,
                HorseName = horse.HorseName,
                DetachedAt = horse.DetachedAt.HasValue ? AsUtc(horse.DetachedAt.Value) : null,
                DetachedLocationName = horse.LocationName
            }
        };
    }

    private static Response.BookingHorseResponse MapHorse(BookingHorse item) => new()
    {
        HorseId = item.HorseId,
        HorseName = item.Horse.HorseName,
        Sex = item.Horse.Sex?.ToString(),
        Breed = item.Horse.Breed,
        MicrochipNumber = item.Horse.MicrochipNumber,
        PassportNumber = item.Horse.PassportNumber,
        SpecialHandlingNotes = item.SpecialHandlingNotes,
        DetachedAt = item.DetachedAt.HasValue ? AsUtc(item.DetachedAt.Value) : null,
        DetachedLocationName = item.DetachedLocation?.LocationName
    };
}
