using Microsoft.EntityFrameworkCore;
using Pegaxus.Service.Exceptions;

using static Pegaxus.Service.Common.DbTime;

namespace Pegaxus.Service.ClaimService;

public sealed partial class Service
{
    public async Task<Response.Eligibility> GetEligibilityAsync(Guid accountId, Guid bookingId, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(accountId, cancellationToken);
        var booking = await db.Bookings.AsNoTracking().Where(item => item.BookingId == bookingId && (actor.IsManager || item.Customer.AccountId == accountId))
            .Select(item => new { item.BookingId, item.BookingCode }).SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Booking not found", "BOOKING_NOT_FOUND");
        var transport = await db.Transports.AsNoTracking().Where(item => item.BookingId == bookingId)
            .Select(item => new { item.TransportId, item.TransportCode, item.Status }).SingleOrDefaultAsync(cancellationToken);
        var delivery = await DeliveryAsync(bookingId, cancellationToken);
        var claimCount = await db.Claims.CountAsync(item => item.Status != Repository.Enum.ClaimStatus.CANCELLED &&
            item.HandoverRecord.Transport.BookingId == bookingId, cancellationToken);
        var now = clock.GetUtcNow();
        var brief = new Response.BookingBrief(booking.BookingId, booking.BookingCode);
        var transportBrief = transport is null ? null : new Response.TransportBrief(transport.TransportId, transport.TransportCode, transport.Status.ToString());

        if (delivery is null)
            return new(now, brief, transportBrief, null, null, null, false, "HANDOVER_NOT_COMPLETED", claimCount);
        var completedAt = AsUtc(delivery.CompletedAt!.Value);
        var deadline = completedAt + ClaimWindow;
        var remaining = (int)Math.Max(0, Math.Floor((deadline - now).TotalSeconds));
        var expired = now > deadline;
        return new(now, brief, transportBrief,
            new(delivery.HandoverRecordId, delivery.HandoverCode, delivery.HandoverType.ToString(), completedAt),
            deadline, remaining, !expired, expired ? "CLAIM_WINDOW_EXPIRED" : null, claimCount);
    }
}
