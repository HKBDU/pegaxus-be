namespace Pegaxus.Service.Common;

public static class CountryUtils
{
    public const string UnitedKingdom = "United Kingdom";

    public static readonly HashSet<string> EuCountries = new(StringComparer.OrdinalIgnoreCase)
    {
        "Austria",
        "Belgium",
        "Bulgaria",
        "Croatia",
        "Cyprus",
        "Czech Republic",
        "Czechia",
        "Denmark",
        "Estonia",
        "Finland",
        "France",
        "Germany",
        "Greece",
        "Hungary",
        "Ireland",
        "Italy",
        "Latvia",
        "Lithuania",
        "Luxembourg",
        "Malta",
        "Netherlands",
        "Poland",
        "Portugal",
        "Romania",
        "Slovakia",
        "Slovenia",
        "Spain",
        "Sweden"
    };

    public static bool IsEuCountry(string? country)
    {
        if (string.IsNullOrWhiteSpace(country)) return false;
        return EuCountries.Contains(country.Trim());
    }

    private static readonly Dictionary<string, string> IsoByName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Austria"] = "AT", ["Belgium"] = "BE", ["Bulgaria"] = "BG", ["Croatia"] = "HR", ["Cyprus"] = "CY", ["Czech Republic"] = "CZ", ["Czechia"] = "CZ",
        ["Denmark"] = "DK", ["Estonia"] = "EE", ["Finland"] = "FI", ["France"] = "FR", ["Germany"] = "DE", ["Greece"] = "GR", ["Hungary"] = "HU",
        ["Ireland"] = "IE", ["Italy"] = "IT", ["Latvia"] = "LV", ["Lithuania"] = "LT", ["Luxembourg"] = "LU", ["Malta"] = "MT", ["Netherlands"] = "NL",
        ["Poland"] = "PL", ["Portugal"] = "PT", ["Romania"] = "RO", ["Slovakia"] = "SK", ["Slovenia"] = "SI", ["Spain"] = "ES", ["Sweden"] = "SE",
        [UnitedKingdom] = "GB"
    };

    private static readonly HashSet<string> EuIsoCodes = new(IsoByName.Values.Where(code => code != "GB"), StringComparer.OrdinalIgnoreCase);

    /// <summary>The ISO code of an accepted country, from its name or from the code itself; null for any other country.</summary>
    public static string? IsoCode(string? country)
    {
        var value = country?.Trim();
        if (string.IsNullOrEmpty(value)) return null;
        if (IsoByName.TryGetValue(value, out var code)) return code;
        return value.Length == 2 && (EuIsoCodes.Contains(value) || value.Equals("GB", StringComparison.OrdinalIgnoreCase)) ? value.ToUpperInvariant() : null;
    }

    /// <summary>
    /// The country name a booking stores ("France", "United Kingdom") for an accepted ISO code or name; null for any other
    /// country. Bookings hold names while locations and employees hold ISO codes.
    /// </summary>
    public static string? CountryName(string? country) =>
        IsoCode(country) is { } code ? IsoByName.First(pair => pair.Value == code).Key : null;

    /// <summary>
    /// The country a booking request carries, in the stored name format: an accepted 2-letter ISO code ("FR", "gb") becomes its name
    /// ("France", "United Kingdom"); a name or any other value is kept as sent, so the validators still reject an unknown country.
    /// Locations hold ISO codes, so a return booking from a detach stop arrives with a code.
    /// </summary>
    public static string? BookingCountry(string? country) =>
        country?.Trim() is { Length: 2 } code && CountryName(code) is { } name ? name : country;

    /// <summary>The codes a requirement country can use to apply to this country: its own ISO code, plus EU for a member state.</summary>
    public static IReadOnlyList<string> RegulatoryCodes(string? country) =>
        IsoCode(country) is { } code ? (EuIsoCodes.Contains(code) ? [code, "EU"] : [code]) : [];

    public static bool IsUnitedKingdom(string? country)
    {
        if (string.IsNullOrWhiteSpace(country)) return false;
        return string.Equals(country.Trim(), UnitedKingdom, StringComparison.OrdinalIgnoreCase);
    }

    public static (bool IsValid, string ErrorMessage) ValidateRoute(string? originCountry, string? destinationCountry)
    {
        var origin = originCountry?.Trim();
        var destination = destinationCountry?.Trim();

        if (string.IsNullOrEmpty(origin) || string.IsNullOrEmpty(destination))
        {
            return (true, string.Empty);
        }

        if (string.Equals(origin, destination, StringComparison.OrdinalIgnoreCase))
        {
            if (!IsUnitedKingdom(origin))
            {
                return (false, "When origin and destination countries are the same, the country must be United Kingdom.");
            }
            return (true, string.Empty);
        }

        // The United Kingdom is served as a route end like an EU country (EU to UK and UK to EU are the EXTRA_EU import and export routes).
        if (!IsEuCountry(origin) && !IsUnitedKingdom(origin))
        {
            return (false, $"Origin country '{origin}' is not an accepted EU member country or United Kingdom.");
        }

        if (!IsEuCountry(destination) && !IsUnitedKingdom(destination))
        {
            return (false, $"Destination country '{destination}' is not an accepted EU member country or United Kingdom.");
        }

        return (true, string.Empty);
    }
}
