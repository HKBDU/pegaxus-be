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
        ids.AddRange(await db.Vehicles.Select(x => x.VehicleId).ToListAsync());
        ids.AddRange(await db.TransportProviders.Select(x => x.TransportProviderId).ToListAsync());

        Assert.True(ids.Count > 250);
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.All(ids, id => Assert.Matches(Rfc4122, id.ToString()));
        Assert.Equal(Guid.Parse("9a64784f-de25-521a-a9f5-1a663da5a08c"),
            (await db.ComplianceDossiers.SingleAsync(x => x.DossierCode == "CD-20261010-0001")).ComplianceDossierId);
    }

    [Fact]
    public async Task Seed_UsesBusinessCodes_AndPreservesLinkedRowsOnRerun()
    {
        await using var db = TestSupport.CreateDb();
        await AppDbContextSeedData.SeedAsync(db);
        db.ChangeTracker.Clear();

        var bookings = await db.Bookings.OrderBy(item => item.BookingId).ToArrayAsync();
        var transports = await db.Transports.Include(item => item.Booking).OrderBy(item => item.TransportId).ToArrayAsync();
        var horses = await db.Horses.OrderBy(item => item.HorseId).ToArrayAsync();
        var links = await db.BookingHorses.Include(item => item.Booking).Include(item => item.Horse).ToArrayAsync();
        var dossier = Assert.Single(await db.ComplianceDossiers.Include(item => item.DossierTemplate).ToArrayAsync());
        Assert.Equal(6, bookings.Length);
        Assert.Equal(3, transports.Length);
        Assert.Equal(16, horses.Length);
        Assert.All(horses, item => Assert.Matches(@"^\d{15}$", item.MicrochipNumber));
        Assert.Equal(12, links.Length);
        Assert.All(bookings, item => Assert.Matches(@"^REQ-\d{8}-\d{4}$", item.BookingCode));
        Assert.All(transports, item =>
        {
            Assert.Matches(@"^TR-\d{8}-\d{4}$", item.TransportCode);
            Assert.Equal(item.TransportCode.Replace("TR-", "REQ-"), item.Booking.BookingCode);
        });
        Assert.All(links, item => Assert.Equal(item.Booking.CustomerId, item.Horse.CustomerId));
        Assert.Equal(transports.Single(item => item.TransportCode == "TR-20261010-0005").TransportId, dossier.TransportId);
        Assert.Matches(@"^CD-\d{8}-\d{4}$", dossier.DossierCode);
        Assert.Equal("TPL-EU-GB", dossier.DossierTemplate!.TemplateCode);
        var codes = horses.Select(item => item.PassportNumber)
            .Concat(await db.DossierTemplates.Select(item => item.TemplateCode).ToArrayAsync());
        Assert.All(codes, code => Assert.DoesNotContain("DEMO", code, StringComparison.OrdinalIgnoreCase));
        var passportRequirement = await db.DossierRequirements.SingleAsync(item => item.HorseId != null);
        Assert.Contains(links, item => item.BookingId == transports.Single(transport => transport.TransportId == dossier.TransportId).BookingId &&
            item.HorseId == passportRequirement.HorseId);

        db.ChangeTracker.Clear();
        await AppDbContextSeedData.SeedAsync(db);
        Assert.Equal(bookings.Select(item => item.BookingId), await db.Bookings.OrderBy(item => item.BookingId).Select(item => item.BookingId).ToArrayAsync());
        Assert.Equal(transports.Select(item => item.TransportId), await db.Transports.OrderBy(item => item.TransportId).Select(item => item.TransportId).ToArrayAsync());
        Assert.Equal(horses.Select(item => item.HorseId), await db.Horses.OrderBy(item => item.HorseId).Select(item => item.HorseId).ToArrayAsync());
        Assert.Equal(12, await db.BookingHorses.CountAsync());
        Assert.Equal(9, await db.BookingStatusHistories.CountAsync());
        Assert.Equal(2, await db.DossierTemplates.CountAsync());
        Assert.Equal(6, await db.DossierTemplateItems.CountAsync());
        Assert.Equal(dossier.ComplianceDossierId, (await db.ComplianceDossiers.SingleAsync()).ComplianceDossierId);
        Assert.Equal(3, await db.DossierRequirements.CountAsync());
    }
}
