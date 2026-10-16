using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Pegaxus.API;

namespace Pegaxus.Service.Test;

public class SeedIdTests
{
    private static readonly Regex Rfc4122 = new("^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$", RegexOptions.Compiled);

    [Fact]
    public async Task SeededIds_AreStrictRfc4122_AndStayStable()
    {
        await using var db = TestSupport.CreateDb();
        await AppDbContextSeedData.SeedAsync(db);
        db.ChangeTracker.Clear();

        var ids = new List<Guid>();
        ids.AddRange(await db.Accounts.Select(x => x.AccountId).ToListAsync());
        ids.AddRange(await db.Horses.Select(x => x.HorseId).ToListAsync());
        ids.AddRange(await db.Bookings.Select(x => x.BookingId).ToListAsync());
        ids.AddRange(await db.BookingStatusHistories.Select(x => x.BookingStatusHistoryId).ToListAsync());
        ids.AddRange(await db.Transports.Select(x => x.TransportId).ToListAsync());
        ids.AddRange(await db.DossierTemplates.Select(x => x.DossierTemplateId).ToListAsync());
        ids.AddRange(await db.DossierTemplateItems.Select(x => x.DossierTemplateItemId).ToListAsync());
        ids.AddRange(await db.ComplianceRequirements.Select(x => x.ComplianceRequirementId).ToListAsync());
        ids.AddRange(await db.ComplianceDossiers.Select(x => x.ComplianceDossierId).ToListAsync());
        ids.AddRange(await db.DossierRequirements.Select(x => x.DossierRequirementId).ToListAsync());
        ids.AddRange(await db.Locations.Select(x => x.LocationId).ToListAsync());

        Assert.True(ids.Count > 250);
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.All(ids, id => Assert.Matches(Rfc4122, id.ToString()));
        Assert.Equal(Guid.Parse("fe92fe57-fe64-5962-b1d3-6a5d6120e6f3"),
            (await db.ComplianceDossiers.SingleAsync(x => x.DossierCode == "CD-20261010-DEMO-0001")).ComplianceDossierId);
    }
}
