using Pegaxus.Service.Common;

namespace Pegaxus.Service.DossierTemplateService;

/// <summary>
/// Ranks a template for a booking route by the countries of its requirements. A requirement with no country applies to every route.
/// A template that asks for a country outside the route (a GB requirement on a France to Germany trip) is never suggested.
/// </summary>
public static class Recommendation
{
    public sealed record Result(int Group, IReadOnlyList<string> MatchedCountries, string Reason);

    public static Result Evaluate(string? originCountry, string? destinationCountry, IEnumerable<string?> requirementCountries)
    {
        var origin = CountryUtils.RegulatoryCodes(originCountry);
        var destination = CountryUtils.RegulatoryCodes(destinationCountry);
        var originCode = CountryUtils.IsoCode(originCountry) ?? originCountry?.Trim() ?? "?";
        var destinationCode = CountryUtils.IsoCode(destinationCountry) ?? destinationCountry?.Trim() ?? "?";
        var countries = requirementCountries.Where(country => !string.IsNullOrWhiteSpace(country)).Select(country => country!.Trim().ToUpperInvariant()).Distinct().Order().ToList();
        if (countries.Count == 0)
            return new(3, [], "This template only holds shared requirements that apply to every route");

        var foreign = countries.Where(country => !origin.Contains(country) && !destination.Contains(country)).ToList();
        var matched = countries.Except(foreign).ToList();
        if (foreign.Count > 0)
            return new(4, matched, $"This template also requires {string.Join(", ", foreign)}, which is not on the booking route");
        var atOrigin = countries.Any(origin.Contains);
        var atDestination = countries.Any(destination.Contains);
        if (atOrigin && atDestination)
            return new(1, matched, $"Requirements in this template match both {originCode} and {destinationCode} on the booking route");
        if (atOrigin || atDestination)
            return new(2, matched, $"Requirements in this template match {(atOrigin ? originCode : destinationCode)} on the booking route");
        return new(4, matched, "Active template without a country match for this route");
    }
}
