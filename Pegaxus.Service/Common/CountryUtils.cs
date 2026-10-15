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

        if (!IsEuCountry(origin))
        {
            return (false, $"Origin country '{origin}' is not an accepted EU member country.");
        }

        if (!IsEuCountry(destination))
        {
            return (false, $"Destination country '{destination}' is not an accepted EU member country.");
        }

        return (true, string.Empty);
    }
}
