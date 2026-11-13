namespace Pegaxus.Service.Common;

/// <summary>
/// The competent authorities a compliance dossier can be submitted to, one or more for every country that has locations in the system.
/// A dossier may only be submitted to an authority of a country on its route (origin, destination or a stop of its legs); the front end
/// offers the same fixed list, so a resubmission to the same authority always matches its earlier submission by name.
/// Keep this list in sync with the front end (src/shared/constants/competent-authorities.ts).
/// </summary>
public static class CompetentAuthorities
{
    public sealed record Authority(string Name, string CountryCode);

    public static readonly IReadOnlyList<Authority> All =
    [
        new("DEFRA - Department for Environment, Food & Rural Affairs (UK)", "GB"),
        new("APHA - Animal and Plant Health Agency (UK)", "GB"),
        new("HMRC - HM Revenue & Customs (UK)", "GB"),
        // Locations only carry a country code, so Northern Ireland stops are GB as well.
        new("DAERA - Department of Agriculture, Environment and Rural Affairs (Northern Ireland)", "GB"),
        new("DAFM - Department of Agriculture, Food and the Marine (Ireland)", "IE"),
        new("DGAL - Direction générale de l'alimentation (France)", "FR"),
        new("DGDDI - Direction générale des douanes et droits indirects (France)", "FR"),
        new("BMEL - Federal Ministry of Food and Agriculture (Germany)", "DE"),
        new("NVWA - Netherlands Food and Consumer Product Safety Authority (Netherlands)", "NL"),
        new("FASFC - Federal Agency for the Safety of the Food Chain (Belgium)", "BE"),
        new("Ministero della Salute - Ministry of Health (Italy)", "IT"),
        new("MAPA - Ministry of Agriculture, Fisheries and Food (Spain)", "ES"),
        new("BMSGPK - Federal Ministry of Social Affairs, Health, Care and Consumer Protection (Austria)", "AT"),
        new("GIW - General Veterinary Inspectorate (Poland)", "PL"),
        new("Jordbruksverket - Swedish Board of Agriculture (Sweden)", "SE"),
        new("DVFA - Danish Veterinary and Food Administration (Denmark)", "DK"),
        new("SVS - State Veterinary Administration (Czech Republic)", "CZ"),
        new("DGAV - Direção-Geral de Alimentação e Veterinária (Portugal)", "PT")
    ];

    /// <summary>The listed authority named <paramref name="name"/> (any case), or null when it is not listed.</summary>
    public static Authority? Find(string? name) =>
        All.FirstOrDefault(item => string.Equals(item.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>The authorities of the given countries, in list order.</summary>
    public static IReadOnlyList<Authority> ForCountries(IReadOnlyCollection<string> countryCodes) =>
        All.Where(item => countryCodes.Contains(item.CountryCode, StringComparer.OrdinalIgnoreCase)).ToList();
}
