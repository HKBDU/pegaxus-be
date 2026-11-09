using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Common;

namespace Pegaxus.Service.ComplianceDossierService;

/// <summary>
/// A dossier is submitted to the authorities of the countries its horses actually pass: the booking's origin and destination and every
/// stop of the transport's route plans (so the EU country of entry, where the border control post is, is included).
/// </summary>
public sealed partial class Service
{
    private static readonly RoutePlanStatus[] DeadRoutePlanStatuses = [RoutePlanStatus.CANCELLED, RoutePlanStatus.REJECTED_BY_MANAGER];

    /// <summary>ISO codes of the countries on the transport's route, origin first.</summary>
    private async Task<IReadOnlyList<string>> RouteCountriesAsync(Guid transportId, CancellationToken cancellationToken)
    {
        var booking = await _db.Transports.AsNoTracking().Where(item => item.TransportId == transportId)
            .Select(item => new { item.Booking.OriginCountry, item.Booking.DestinationCountry }).SingleAsync(cancellationToken);
        var stops = await _db.RoutePlanRoutes.AsNoTracking()
            .Where(item => item.RoutePlan.TransportPlan.TransportId == transportId && !DeadRoutePlanStatuses.Contains(item.RoutePlan.Status))
            .OrderBy(item => item.RoutePlan.CreatedAt).ThenBy(item => item.SequenceNo)
            .Select(item => new { From = item.Route.FromLocation.Country, To = item.Route.ToLocation.Country })
            .ToListAsync(cancellationToken);
        return new[] { booking.OriginCountry }.Concat(stops.SelectMany(item => new[] { item.From, item.To })).Append(booking.DestinationCountry)
            .Select(CountryUtils.IsoCode).OfType<string>().Distinct().ToList();
    }

    private static Response.RouteAuthoritiesResponse RouteAuthorities(IReadOnlyList<string> countries) => new()
    {
        RouteCountries = countries,
        AllowedAuthorities = CompetentAuthorities.ForCountries(countries.ToArray())
            .Select(item => new Response.CompetentAuthorityResponse { Name = item.Name, CountryCode = item.CountryCode }).ToList()
    };
}
