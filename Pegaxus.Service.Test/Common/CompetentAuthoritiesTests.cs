using Pegaxus.Service.Common;

namespace Pegaxus.Service.Test;

public class CompetentAuthoritiesTests
{
    /// <summary>A booking may start or end in any EU member or the United Kingdom, so each of them needs an authority to submit to.</summary>
    [Fact]
    public void EveryCountryABookingAccepts_HasACompetentAuthority()
    {
        var codes = CountryUtils.EuCountries.Append(CountryUtils.UnitedKingdom).Select(CountryUtils.IsoCode).OfType<string>().Distinct().ToList();

        Assert.Equal(28, codes.Count);
        Assert.Empty(codes.Where(code => CompetentAuthorities.ForCountries([code]).Count == 0));
    }

    [Fact]
    public void AuthorityNames_AreUnique_AndBelongToAcceptedCountries()
    {
        Assert.Equal(CompetentAuthorities.All.Count, CompetentAuthorities.All.Select(item => item.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(CompetentAuthorities.All, item => Assert.Equal(item.CountryCode, CountryUtils.IsoCode(item.CountryCode)));
    }
}
