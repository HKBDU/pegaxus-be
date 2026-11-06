using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;

using Audit = Pegaxus.Service.AuditService;
using static Pegaxus.Service.Test.AuditSupport;

namespace Pegaxus.Service.Test;

public class AuditTests
{
    private static async Task AssertFieldsAsync(Task task, params string[] fields)
    {
        var error = await Assert.ThrowsAsync<FieldValidationException>(() => task);
        foreach (var field in fields) Assert.Contains(error.Errors, item => item.Field == field);
    }

    /// <summary>The rich transport plus a second finished one (to Germany), a cancelled one and one still on the road.</summary>
    private static async Task<(Fixture F, Transport Other, Transport Cancelled)> ArchiveAsync(AppDbContext db)
    {
        var f = await Fixture.CreateAsync(db);
        var other = f.R.AddTransport("2026-10-20 10:00", "2026-10-20 09:00", "2026-10-20 10:00", destination: "Germany");
        var cancelled = f.R.AddTransport(null, status: TransportStatus.CANCELLED); cancelled.CancelledAt = D("2026-10-18 10:00");
        f.R.AddTransport("2026-10-25 10:00", status: TransportStatus.IN_TRANSIT);
        await db.SaveChangesAsync();
        return (f, other, cancelled);
    }

    // ---------------------------------------------------------------- AU1

    [Fact]
    public async Task Archive_ListsFinishedTransportsNewestFirst_WithHorsesAndCounts()
    {
        await using var db = TestSupport.CreateDb(); var (f, other, _) = await ArchiveAsync(db);

        var page = await f.Service.GetArchiveAsync(f.ManagerId, new());

        Assert.Equal((2, 1), (page.TotalItems, page.TotalPages));
        Assert.Equal(new[] { other.TransportCode, f.Transport.TransportCode }, page.Items.Select(item => item.TransportCode));
        var item = page.Items[1];
        Assert.Equal(("COMPLETED", "Test Customer", "France", "United Kingdom"), (item.Status, item.CustomerName, item.OriginCountry, item.DestinationCountry));
        Assert.Equal(new DateTimeOffset(2026, 10, 14, 16, 5, 0, TimeSpan.Zero), item.CompletedAt);
        var horse = Assert.Single(item.Horses);
        Assert.Equal(("Pegasus King", "985120012345678", "FR-2020-0001"), (horse.Name, horse.MicrochipNumber, horse.PassportNumber));
        // The cancelled claim and the cancelled incident do not count.
        Assert.Equal((1, 2), (item.ClaimCount, item.IncidentCount));
        Assert.Equal((0, 0), (page.Items[0].ClaimCount, page.Items[0].IncidentCount)); Assert.Empty(page.Items[0].Horses);
    }

    [Fact]
    public async Task Archive_SearchMatchesTheSixFieldsAnywhereInTheTextWithoutCase()
    {
        await using var db = TestSupport.CreateDb(); var (f, _, _) = await ArchiveAsync(db);
        async Task<IEnumerable<string>> Find(string text) => (await f.Service.GetArchiveAsync(f.ManagerId, new() { Search = text })).Items.Select(item => item.TransportCode);

        foreach (var text in new[] { "985120", "345678", "fr-2020-0001", "PEGASUS", "king", f.Transport.TransportCode.ToLower(), f.Transport.Booking.BookingCode[3..12] })
            Assert.Equal(new[] { f.Transport.TransportCode }, await Find(text));
        Assert.Equal(2, (await Find("test customer")).Count());
        Assert.Empty(await Find("no such horse"));
    }

    [Fact]
    public async Task Archive_CancelledTransportsOnlyWhenAskedAndNeverTheOnesOnTheRoad()
    {
        await using var db = TestSupport.CreateDb(); var (f, other, cancelled) = await ArchiveAsync(db);

        var withCancelled = await f.Service.GetArchiveAsync(f.ManagerId, new() { IncludeCancelled = true });

        Assert.Equal(new[] { other.TransportCode, cancelled.TransportCode, f.Transport.TransportCode }, withCancelled.Items.Select(item => item.TransportCode));
        Assert.Equal(("CANCELLED", null), (withCancelled.Items[1].Status, withCancelled.Items[1].CompletedAt));
    }

    [Fact]
    public async Task Archive_FiltersAndSortsAndPages()
    {
        await using var db = TestSupport.CreateDb(); var (f, other, cancelled) = await ArchiveAsync(db);
        Task<Audit.Response.ArchiveResponse> Run(Action<Audit.Request.ArchiveRequest> change)
        {
            var request = new Audit.Request.ArchiveRequest(); change(request);
            return f.Service.GetArchiveAsync(f.ManagerId, request);
        }

        Assert.Equal(f.Transport.TransportCode, Assert.Single((await Run(r => r.HasClaim = true)).Items).TransportCode);
        Assert.Equal(2, (await Run(r => r.HasClaim = false)).TotalItems); // only true narrows the list
        Assert.Equal(other.TransportCode, Assert.Single((await Run(r => { r.CompletedFrom = new(2026, 10, 15); r.CompletedTo = new(2026, 10, 31); })).Items).TransportCode);
        Assert.Equal(new[] { other.TransportCode, cancelled.TransportCode }, (await Run(r => { r.CompletedFrom = new(2026, 10, 15); r.IncludeCancelled = true; })).Items.Select(item => item.TransportCode));
        Assert.Equal(f.Transport.TransportCode, Assert.Single((await Run(r => { r.CompletedTo = new(2026, 10, 14); })).Items).TransportCode);
        Assert.Equal(other.TransportCode, Assert.Single((await Run(r => r.DestinationCountry = " germany ")).Items).TransportCode);
        Assert.Equal(2, (await Run(r => r.OriginCountry = "FRANCE")).TotalItems);
        Assert.Equal(f.Transport.TransportCode, (await Run(r => r.SortBy = "completedAt:asc")).Items[0].TransportCode);
        Assert.Equal(new[] { f.Transport.TransportCode, other.TransportCode }.Order(), (await Run(r => r.SortBy = "transportCode:asc")).Items.Select(item => item.TransportCode));
        var second = await Run(r => { r.PageSize = 1; r.PageIndex = 2; });
        Assert.Equal((2, 2, f.Transport.TransportCode), (second.TotalItems, second.TotalPages, Assert.Single(second.Items).TransportCode));
    }

    [Fact]
    public async Task Archive_IsForLogisticsManagersAndRejectsBadInput()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);

        foreach (var accountId in new[] { f.CustomerId, f.Driver.AccountId, Guid.NewGuid() })
            Assert.Equal("Only Logistics Manager can access the audit archive.", (await Assert.ThrowsAsync<ForbiddenException>(() => f.Service.GetArchiveAsync(accountId, new()))).Message);
        await AssertFieldsAsync(f.Service.GetArchiveAsync(f.ManagerId, new() { Search = new string('x', 201), PageSize = 0, SortBy = "bad", CompletedFrom = new(2026, 10, 2), CompletedTo = new(2026, 10, 1) }),
            "search", "pageSize", "sortBy", "completedTo");
    }

    // ---------------------------------------------------------------- AU2: the manager's view

    [Fact]
    public async Task Audit_AManagerGetsEveryBlock()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);

        var audit = await f.Service.GetAuditAsync(f.ManagerId, f.Transport.TransportId, new());

        Assert.Equal("FULL", audit.ViewMode); Assert.Equal(new DateTimeOffset(2026, 11, 1, 2, 0, 0, TimeSpan.Zero), audit.GeneratedAt);
        Assert.Equal(("COMPLETED", new DateTimeOffset(2026, 10, 14, 16, 5, 0, TimeSpan.Zero)), (audit.Transport.Status, audit.Transport.CompletedAt));

        var booking = audit.Booking!;
        Assert.Equal(("Test Customer", "France", "United Kingdom"), (booking.Customer.Name, booking.OriginCountry, booking.DestinationCountry));
        Assert.Equal(new[] { ("QUOTATION", 1_000_000L), ("FINAL", 5_000_000L) }, booking.Invoices.Select(item => (item.Type, item.TotalAmount)));
        Assert.Equal(new[] { ("CLM-LIVE", "RESOLVED"), ("CLM-GONE", "CANCELLED") }, booking.Claims.Select(item => (item.ClaimCode, item.Status)));

        var horse = Assert.Single(audit.Horses!);
        Assert.Equal(("985120012345678", "FR-2020-0001", 520.5m, 165m), (horse.MicrochipNumber, horse.PassportNumber, horse.WeightKg, horse.HeightCm));
        Assert.Equal(new[] { null, "Slight fever, monitor" }, horse.HealthLogs.Select(log => log.VeterinarianNote)); Assert.All(horse.HealthLogs, log => Assert.Equal(2, log.PhotoCount));

        var routing = audit.Routing!;
        Assert.Equal("RP-MAIN", routing.Primary!.RoutePlanCode);
        var (own, bought) = (routing.Primary.Segments[0], routing.Primary.Segments[1]);
        Assert.Equal(("INTERNAL", "VEH-0003", "51C-123.45", "Test Manager", (decimal?)null), (own.ExecutedBy, own.VehicleCode, own.LicensePlate, own.DriverName, own.OutsourcingCost));
        Assert.Equal(("OUTSOURCED", "Eurotunnel", 4_000_000m, "VND"), (bought.ExecutedBy, bought.ProviderName, bought.OutsourcingCost, bought.OutsourcingCurrency));
        Assert.Equal(new DateTimeOffset(2026, 10, 11, 6, 0, 0, TimeSpan.Zero), own.PlannedDepartureAt); Assert.Equal(new DateTimeOffset(2026, 10, 11, 6, 30, 0, TimeSpan.Zero), own.ActualDepartureAt);
        Assert.Equal("RP-BACKUP", Assert.Single(routing.Alternatives).RoutePlanCode);

        var dossier = Assert.Single(audit.Compliance!.Dossiers);
        Assert.Equal(("DOS-MAIN", "MAIN"), (dossier.DossierCode, dossier.DossierType));
        Assert.Equal(new[] { "EHC Certificate", "Coggins Test" }, dossier.Requirements.Select(item => item.RequirementName));
        var certificate = dossier.Requirements[0].Document!;
        Assert.Equal(new[] { 1, 2 }, certificate.Versions.Select(version => version.VersionNo));
        Assert.Equal(new[] { ("REJECTED", "Blurry scan") }, certificate.Versions[0].Reviews!.Select(review => (review.Result, review.ReviewComment)));
        Assert.Equal(("PENDING", 1), (dossier.Requirements[1].Document!.Status, dossier.Requirements[1].Document!.Versions.Count));
        Assert.Equal(("Dr. Martin", "Checked at the border post"), (dossier.QuarantineProof!.OfficialVeterinarianName, dossier.QuarantineProof.VerificationNote));
        Assert.Equal(("Dover", "MRN-1"), (dossier.CustomsClearanceProof!.CustomsPortName, dossier.CustomsClearanceProof.MrnNumber));

        var operations = audit.Operations!;
        Assert.Equal(new[] { "PICKUP", "BORDER_HANDOVER", "DELIVERY" }, operations.Handovers.Select(item => item.HandoverType));
        Assert.Equal(3, operations.Incidents.Count); Assert.Contains(operations.Incidents, item => item.Status == "CANCELLED");
        Assert.Equal(new[] { "INTERNAL", "PUBLIC" }, operations.AuditDossiers.Select(item => item.Visibility));

        var finance = audit.Finance!;
        Assert.Equal(new[] { "QUOTATION", "FINAL", "FINAL" }, finance.Invoices.Select(item => item.Type)); Assert.Equal("RCP-1", Assert.Single(finance.Invoices[1].Receipts).ReceiptCode);
        Assert.Equal(("FR-1", 5_000_000L, 3_500_000L, "Healthy margin"), (finance.FinancialReport!.ReportCode, finance.FinancialReport.TotalInvoicedAmount, finance.FinancialReport.TotalEmergencyCost, finance.FinancialReport.SummaryNote));
        Assert.Equal(("VEHICLE_REPAIR", 3_500_000L), (Assert.Single(finance.EmergencyCosts).Category, finance.EmergencyCosts[0].ApprovedAmount));
    }

    [Fact]
    public async Task Audit_OnlyTheAskedBlocksAreLoaded_AndTheTimelineIsAlwaysThere()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);

        var audit = await f.Service.GetAuditAsync(f.ManagerId, f.Transport.TransportId, new() { Blocks = "Booking, operations" });

        Assert.NotNull(audit.Booking); Assert.NotNull(audit.Operations);
        Assert.Null(audit.Horses); Assert.Null(audit.Routing); Assert.Null(audit.Compliance); Assert.Null(audit.Finance);
        Assert.Equal(12, audit.Timeline.Count);
    }

    [Fact]
    public async Task Audit_TheTimelineRunsOldestFirstAndStopsAtTheLimit()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);

        var all = (await f.Service.GetAuditAsync(f.ManagerId, f.Transport.TransportId, new() { Blocks = "booking" })).Timeline;
        var cut = (await f.Service.GetAuditAsync(f.ManagerId, f.Transport.TransportId, new() { Blocks = "booking", Limit = 3 })).Timeline;

        Assert.Equal(all.Select(item => item.At).Order(), all.Select(item => item.At));
        Assert.Equal(("TRANSPORT", "Transport created"), (all[0].Source, all[0].Title));
        Assert.Equal(("AUDIT", "Internal review notes", "INTERNAL"), (all[1].Source, all[1].Title, all[1].Visibility));
        Assert.Equal(all.Take(3), cut);
        Assert.Contains(all, item => item.Source == "HANDOVER" && item.Title == "Handover HO-3: Delivery, completed" && item.Visibility == "PUBLIC");
        Assert.Contains(all, item => item.Source == "HANDOVER" && item.Visibility == "INTERNAL");
        Assert.Contains(all, item => item.Source == "INCIDENT" && item.Title.StartsWith("Incident INC-") && item.Title.Contains(": Incident"));
    }

    [Fact]
    public async Task Audit_AnUnfinishedTransportCanStillBeRead()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        var onTheRoad = f.R.AddTransport("2026-10-25 10:00", status: TransportStatus.IN_TRANSIT); await db.SaveChangesAsync();

        var audit = await f.Service.GetAuditAsync(f.ManagerId, onTheRoad.TransportId, new());

        Assert.Equal("IN_TRANSIT", audit.Transport.Status); Assert.Empty(audit.Horses!); Assert.Null(audit.Routing!.Primary); Assert.Empty(audit.Compliance!.Dossiers); Assert.Null(audit.Finance!.FinancialReport);
    }

    // ---------------------------------------------------------------- AU2: the customer's view

    [Fact]
    public async Task Audit_ACustomerGetsTheShortViewOfTheirOwnTransport()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);

        // finance is asked for and silently dropped.
        var audit = await f.Service.GetAuditAsync(f.CustomerId, f.Transport.TransportId, new() { Blocks = "booking,horses,routing,compliance,operations,finance" });

        Assert.Equal("CUSTOMER", audit.ViewMode); Assert.Null(audit.Finance);
        Assert.Equal(new[] { "FINAL" }, audit.Booking!.Invoices.Select(item => item.Type));
        Assert.All(Assert.Single(audit.Horses!).HealthLogs, log => Assert.Null(log.VeterinarianNote));

        var legs = audit.Routing!.Primary!.Segments;
        Assert.All(legs, leg =>
        {
            Assert.Null(leg.ProviderName); Assert.Null(leg.OutsourcingCost); Assert.Null(leg.OutsourcingCurrency); Assert.Null(leg.VehicleCode); Assert.Null(leg.LicensePlate);
            Assert.Null(leg.DriverName); Assert.Null(leg.EscortName); Assert.Null(leg.PlannedDepartureAt); Assert.Null(leg.PlannedArrivalAt);
            Assert.NotNull(leg.ActualDepartureAt);
        });
        Assert.Equal(new[] { "INTERNAL", "OUTSOURCED" }, legs.Select(leg => leg.ExecutedBy));

        var dossier = Assert.Single(audit.Compliance!.Dossiers);
        var requirement = Assert.Single(dossier.Requirements);
        Assert.Equal(("EHC Certificate", "APPROVED"), (requirement.RequirementName, requirement.Document!.Status));
        var version = Assert.Single(requirement.Document.Versions);
        Assert.Equal(2, version.VersionNo); Assert.Null(version.Reviews); Assert.Null(dossier.QuarantineProof); Assert.Null(dossier.CustomsClearanceProof);

        var operations = audit.Operations!;
        Assert.Equal(new[] { "PICKUP", "DELIVERY" }, operations.Handovers.Select(item => item.HandoverType));
        var incident = Assert.Single(operations.Incidents);
        Assert.Equal(("HIGH", "VEHICLE"), (incident.Severity, incident.Category));
        Assert.Equal(new[] { "HANDOVER_COMPLETED" }, operations.AuditDossiers.Select(item => item.ActionType)); Assert.All(operations.AuditDossiers, item => Assert.Equal("PUBLIC", item.Visibility));

        Assert.Equal(8, audit.Timeline.Count); Assert.All(audit.Timeline, item => Assert.Equal("PUBLIC", item.Visibility));
        Assert.DoesNotContain(audit.Timeline, item => item.Title.Contains("Internal review") || item.Title.Contains("Incident INC-") && item.Title.Contains(':'));
    }

    [Fact]
    public async Task Audit_ACustomerNeverReachesAnotherCustomersTransport_AndOthersAreRefused()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);

        var other = await Assert.ThrowsAsync<NotFoundException>(() => f.Service.GetAuditAsync(f.OtherCustomerAccount.AccountId, f.Transport.TransportId, new()));
        Assert.Equal("TRANSPORT_NOT_FOUND", other.ErrorCode);
        Assert.Equal("TRANSPORT_NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(() => f.Service.GetAuditAsync(f.ManagerId, Guid.NewGuid(), new()))).ErrorCode);
        await Assert.ThrowsAsync<ForbiddenException>(() => f.Service.GetAuditAsync(f.Driver.AccountId, f.Transport.TransportId, new()));
        await Assert.ThrowsAsync<ForbiddenException>(() => f.Service.GetAuditAsync(Guid.NewGuid(), f.Transport.TransportId, new()));
        await AssertFieldsAsync(f.Service.GetAuditAsync(f.ManagerId, f.Transport.TransportId, new() { Blocks = "booking,other", Limit = 1001 }), "blocks", "limit");
    }
}
