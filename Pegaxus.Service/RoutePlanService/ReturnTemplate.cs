using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;

namespace Pegaxus.Service.RoutePlanService;

/// <summary>
/// For a return booking (a horse left behind mid-trip), the legs the original transport drove up to the stop where the horse stays,
/// reversed: a starting point the coordinator edits and saves as an ordinary PRIMARY route plan. Nothing is written here.
/// </summary>
public sealed partial class Service
{
    private static readonly RoutePlanStatus[] DrivenPlanStatuses = [RoutePlanStatus.CONFIRMED, RoutePlanStatus.ACTIVE, RoutePlanStatus.COMPLETED];

    public async Task<Response.ReturnTemplateResponse> GetReturnTemplateAsync(Guid accountId, Guid transportPlanId, CancellationToken cancellationToken = default)
    {
        await RequireEmployeeAsync(accountId, cancellationToken);
        var target = await db.TransportPlans.AsNoTracking().Where(item => item.TransportPlanId == transportPlanId)
            .Select(item => new { item.Transport.Booking.ReturnOfTransportId, HorseIds = item.Transport.Booking.BookingHorses.Select(link => link.HorseId).ToList() })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Transport plan not found", "TRANSPORT_PLAN_NOT_FOUND");
        if (target.ReturnOfTransportId is not { } sourceTransportId)
            throw new UnprocessableEntityException("Only a return booking can reuse the route of its original trip", "NOT_RETURN_BOOKING");

        var source = await db.Transports.AsNoTracking().Where(item => item.TransportId == sourceTransportId)
            .Select(item => new
            {
                item.TransportCode,
                DetachedLocationId = item.Booking.BookingHorses.Where(link => target.HorseIds.Contains(link.HorseId) && link.DetachedAt != null)
                    .Select(link => link.DetachedLocationId).FirstOrDefault()
            }).SingleAsync(cancellationToken);
        var plans = await db.RoutePlans.AsNoTracking().AsSplitQuery()
            .Include(plan => plan.RoutePlanRoutes).ThenInclude(route => route.Route).ThenInclude(route => route.FromLocation)
            .Include(plan => plan.RoutePlanRoutes).ThenInclude(route => route.Route).ThenInclude(route => route.ToLocation)
            .Where(plan => plan.TransportPlan.TransportId == sourceTransportId && DrivenPlanStatuses.Contains(plan.Status)).ToArrayAsync(cancellationToken);
        var driven = TransportItinerary.Effective(plans).Select(segment => segment.Route).ToList();

        // Keep the legs up to the stop: through the leg that ends there, or before the leg that starts there.
        var end = driven.FindIndex(route => route.Route.ToLocationId == source.DetachedLocationId);
        var start = driven.FindIndex(route => route.Route.FromLocationId == source.DetachedLocationId);
        var kept = end >= 0 ? driven.Take(end + 1) : start >= 0 ? driven.Take(start) : [];
        var segments = kept.Reverse().Select((route, index) => new Response.ReturnTemplateSegment(index + 1,
            LocationBrief.From(route.Route.ToLocation)!, LocationBrief.From(route.Route.FromLocation)!, route.Route.TransportMode.ToString(),
            route.Route.DistanceKm, route.Route.EstimatedDurationMinutes)).ToList();
        return new(sourceTransportId, source.TransportCode, segments);
    }
}
