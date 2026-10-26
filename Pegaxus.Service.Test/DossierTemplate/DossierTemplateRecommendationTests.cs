using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.DossierTemplateService;
using Pegaxus.Service.DossierTemplateService.Validator;
using Pegaxus.Service.Exceptions;

using TemplateRequest = Pegaxus.Service.DossierTemplateService.Request;
using TemplateService = Pegaxus.Service.DossierTemplateService.Service;

namespace Pegaxus.Service.Test;

public class DossierTemplateRecommendationTests
{
    [Theory]
    [InlineData("France", "Germany", new[] { "EU" }, 1, "FR and DE")]
    [InlineData("France", "United Kingdom", new[] { "EU", "GB" }, 1, "FR and GB")]
    [InlineData("United Kingdom", "France", new[] { "EU", "GB" }, 1, "GB and FR")]
    [InlineData("FR", "GB", new[] { "EU", "GB" }, 1, "FR and GB")]
    [InlineData("France", "United Kingdom", new[] { "GB" }, 2, "match GB on")]
    [InlineData("France", "Germany", new[] { "FR" }, 2, "match FR on")]
    [InlineData("France", "Germany", new[] { "EU", "GB" }, 4, "also requires GB")]
    [InlineData("France", "Germany", new string[0], 3, "shared requirements")]
    [InlineData("France", "Germany", new string?[] { null, " " }, 3, "shared requirements")]
    public void Evaluate_GroupsATemplateByTheCountriesOfItsRequirements(string origin, string destination, string?[] countries, int group, string reason)
    {
        var result = Recommendation.Evaluate(origin, destination, countries);

        Assert.Equal(group, result.Group); Assert.Contains(reason, result.Reason);
    }

    [Fact]
    public void Evaluate_ListsTheRequirementCountriesThatMatchTheRoute()
    {
        var result = Recommendation.Evaluate("France", "United Kingdom", ["gb", "EU", "GB", null]);

        Assert.Equal(["EU", "GB"], result.MatchedCountries);
    }

    private sealed record Seed(Booking Booking, DossierTemplate EuGb, DossierTemplate EuOnly, DossierTemplate Generic);

    private static async Task<Seed> SeedAsync(AppDbContext db, string origin, string destination, BookingStatus status = BookingStatus.APPROVED)
    {
        var (_, customer) = await TestSupport.AddCustomerAsync(db);
        var (booking, _) = await TestSupport.AddPendingBookingAsync(db, customer, true);
        booking.Status = status; booking.OriginCountry = origin; booking.DestinationCountry = destination;
        var eu = Requirement("EU"); var gb = Requirement("GB"); var shared = Requirement(null);
        var euGb = Template("TPL-EU-GB", eu, gb); var euOnly = Template("TPL-EU", eu); var generic = Template("TPL-GENERIC", shared);
        db.AddRange(eu, gb, shared, euGb, euOnly, generic);
        await db.SaveChangesAsync();
        return new(booking, euGb, euOnly, generic);
    }

    private static ComplianceRequirement Requirement(string? country) => new()
    {
        ComplianceRequirementId = Guid.NewGuid(), RequirementCode = "REQ-" + Guid.NewGuid().ToString("N")[..8], RequirementName = "Requirement " + country, Country = country
    };

    private static DossierTemplate Template(string code, params ComplianceRequirement[] requirements) => new()
    {
        DossierTemplateId = Guid.NewGuid(), TemplateCode = code, TemplateName = code, Status = DossierTemplateStatus.ACTIVE, VersionNo = 1,
        DossierTemplateItems = requirements.Select((item, index) => new DossierTemplateItem
            { DossierTemplateItemId = Guid.NewGuid(), ComplianceRequirement = item, SequenceNo = index + 1, IsMandatory = true }).ToList()
    };

    private static TemplateRequest.ListRequest Ranked(Guid bookingId) => new() { BookingId = bookingId, SortBy = "recommendation:desc", PageSize = 20 };

    [Fact]
    public async Task List_ForACrossBorderRoute_RecommendsTheTemplateThatMatchesBothEnds()
    {
        await using var db = TestSupport.CreateDb(); var seed = await SeedAsync(db, "France", "United Kingdom");

        var list = await new TemplateService(db).GetListAsync(Ranked(seed.Booking.BookingId));

        Assert.Equal(["TPL-EU-GB", "TPL-EU", "TPL-GENERIC"], list.Items.Select(item => item.TemplateCode));
        Assert.Equal([1, 2, 3], list.Items.Select(item => item.RecommendationRank));
        Assert.Equal([true, false, false], list.Items.Select(item => item.IsRecommended));
        Assert.Equal(["EU", "GB"], list.Items[0].MatchedCountries); Assert.Contains("FR and GB", list.Items[0].RecommendationReason);
        Assert.Equal(3, list.TotalItems);
    }

    [Fact]
    public async Task List_ForAnIntraEuRoute_DoesNotRecommendATemplateThatAsksForTheUnitedKingdom()
    {
        await using var db = TestSupport.CreateDb(); var seed = await SeedAsync(db, "France", "Germany");

        var list = await new TemplateService(db).GetListAsync(Ranked(seed.Booking.BookingId));

        Assert.Equal(["TPL-EU", "TPL-GENERIC", "TPL-EU-GB"], list.Items.Select(item => item.TemplateCode));
        Assert.Equal(seed.EuOnly.DossierTemplateId, Assert.Single(list.Items, item => item.IsRecommended == true).DossierTemplateId);
        Assert.Contains("GB", list.Items[2].RecommendationReason);
    }

    [Fact]
    public async Task List_FlagsNoTemplateWhenTheBestOneDoesNotMatchTheRoute()
    {
        await using var db = TestSupport.CreateDb(); var seed = await SeedAsync(db, "France", "Germany");
        db.RemoveRange(seed.EuOnly.DossierTemplateItems); db.Remove(seed.EuOnly); db.RemoveRange(seed.Generic.DossierTemplateItems); db.Remove(seed.Generic); await db.SaveChangesAsync();

        var list = await new TemplateService(db).GetListAsync(Ranked(seed.Booking.BookingId));

        Assert.Equal("TPL-EU-GB", Assert.Single(list.Items).TemplateCode); Assert.False(list.Items[0].IsRecommended);
    }

    [Fact]
    public async Task List_OnlyIncludesTemplatesThatCanBeApplied()
    {
        await using var db = TestSupport.CreateDb(); var seed = await SeedAsync(db, "France", "United Kingdom");
        seed.EuGb.Status = DossierTemplateStatus.DRAFT;
        seed.EuOnly.EffectiveTo = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        seed.Generic.DossierTemplateItems.Single().ComplianceRequirement.Status = ComplianceRequirementStatus.INACTIVE;
        db.Add(Template("TPL-EMPTY")); await db.SaveChangesAsync();

        var list = await new TemplateService(db).GetListAsync(Ranked(seed.Booking.BookingId));

        Assert.Empty(list.Items); Assert.Equal(0, list.TotalItems);
    }

    [Fact]
    public async Task List_WithABookingButWithoutTheRecommendationSort_KeepsTheCodeOrderAndStillAddsTheFields()
    {
        await using var db = TestSupport.CreateDb(); var seed = await SeedAsync(db, "France", "United Kingdom");

        var list = await new TemplateService(db).GetListAsync(new() { BookingId = seed.Booking.BookingId, PageSize = 20 });

        Assert.Equal(["TPL-EU", "TPL-EU-GB", "TPL-GENERIC"], list.Items.Select(item => item.TemplateCode));
        Assert.Equal(1, list.Items.Single(item => item.TemplateCode == "TPL-EU-GB").RecommendationRank);
    }

    [Fact]
    public async Task List_PagesTheRankedResult()
    {
        await using var db = TestSupport.CreateDb(); var seed = await SeedAsync(db, "France", "United Kingdom");
        var request = Ranked(seed.Booking.BookingId); request.PageSize = 2; request.PageIndex = 2;

        var list = await new TemplateService(db).GetListAsync(request);

        Assert.Equal("TPL-GENERIC", Assert.Single(list.Items).TemplateCode); Assert.Equal(3, list.Items[0].RecommendationRank); Assert.Equal(2, list.TotalPages);
    }

    [Fact]
    public async Task List_WithoutABooking_HasNoRecommendationFields()
    {
        await using var db = TestSupport.CreateDb(); await SeedAsync(db, "France", "Germany");

        var list = await new TemplateService(db).GetListAsync(new());

        Assert.All(list.Items, item => { Assert.Null(item.IsRecommended); Assert.Null(item.RecommendationRank); Assert.Null(item.MatchedCountries); Assert.Null(item.RecommendationReason); });
    }

    [Fact]
    public async Task List_RefusesAnUnknownOrUnapprovedBooking()
    {
        await using var db = TestSupport.CreateDb(); var seed = await SeedAsync(db, "France", "Germany", BookingStatus.PENDING);

        var missing = await Assert.ThrowsAsync<NotFoundException>(() => new TemplateService(db).GetListAsync(Ranked(Guid.NewGuid())));
        var pending = await Assert.ThrowsAsync<ConflictException>(() => new TemplateService(db).GetListAsync(Ranked(seed.Booking.BookingId)));

        Assert.Equal("BOOKING_NOT_FOUND", missing.ErrorCode); Assert.Equal("BOOKING_NOT_APPROVED", pending.ErrorCode);
    }

    [Fact]
    public void Validator_NeedsABookingForTheRecommendationSort()
    {
        var validator = new ListRequestValidator();

        Assert.False(validator.Validate(new TemplateRequest.ListRequest { SortBy = "recommendation:desc" }).IsValid);
        Assert.True(validator.Validate(new TemplateRequest.ListRequest { SortBy = "recommendation:desc", BookingId = Guid.NewGuid() }).IsValid);
        Assert.True(validator.Validate(new TemplateRequest.ListRequest { SortBy = "updatedAt:desc" }).IsValid);
        Assert.False(validator.Validate(new TemplateRequest.ListRequest { BookingId = Guid.Empty }).IsValid);
    }
}
