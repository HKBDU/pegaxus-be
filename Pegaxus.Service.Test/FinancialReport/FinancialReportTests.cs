using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;

using Reports = Pegaxus.Service.FinancialReportService;

namespace Pegaxus.Service.Test;

public class FinancialReportTests
{
    internal static readonly DateTimeOffset Now = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);

    internal sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Value { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Value;
    }

    private static DateTime Db(DateTimeOffset value) => DateTime.SpecifyKind(value.UtcDateTime, DateTimeKind.Unspecified);

    internal sealed class Fixture
    {
        internal required AppDbContext Context { get; init; }
        internal required FixedClock Clock { get; init; }
        internal required Account ManagerAccount { get; init; }
        internal required Employee Manager { get; init; }
        internal required Account CustomerAccount { get; init; }
        internal required Booking Booking { get; init; }
        internal required Transport Transport { get; init; }

        internal Reports.Service Service => For(Context);
        internal Reports.Service For(AppDbContext context) => new(context, Clock);

        internal static async Task<Fixture> CreateAsync(AppDbContext db, TransportStatus status = TransportStatus.COMPLETED, int horses = 4, bool withTimes = true)
        {
            var (customerAccount, customer) = await TestSupport.AddCustomerAsync(db);
            var (managerAccount, manager) = await TestSupport.AddLogisticsManagerAsync(db);
            var (booking, _) = await TestSupport.AddPendingBookingAsync(db, customer);
            booking.Status = BookingStatus.APPROVED;
            var time = new DateTime(2026, 10, 10, 8, 0, 0);
            var transport = new Transport { TransportId = Guid.NewGuid(), BookingId = booking.BookingId, Booking = booking, Status = status, TransportCode = "TR-" + Guid.NewGuid().ToString("N"),
                ActualDepartureAt = withTimes ? new DateTime(2026, 10, 15, 6, 0, 0) : null, ActualArrivalAt = withTimes ? new DateTime(2026, 10, 18, 4, 0, 0) : null, CreatedAt = time, UpdatedAt = time };
            db.Transports.Add(transport);
            for (var index = 0; index < horses; index++)
            {
                var horse = new Horse { HorseId = Guid.NewGuid(), CustomerId = customer.CustomerId, Customer = customer, HorseName = "Horse " + index, CreatedAt = time, UpdatedAt = time };
                db.Horses.Add(horse);
                db.BookingHorses.Add(new BookingHorse { BookingId = booking.BookingId, Booking = booking, HorseId = horse.HorseId, Horse = horse, CreatedAt = time });
            }
            await db.SaveChangesAsync();
            return new() { Context = db, Clock = new FixedClock(Now), ManagerAccount = managerAccount, Manager = manager, CustomerAccount = customerAccount, Booking = booking, Transport = transport };
        }

        internal Invoice AddInvoice(InvoiceType type, InvoiceStatus status, decimal total, DateTimeOffset? updatedAt = null)
        {
            var at = Db(updatedAt ?? Now.AddDays(-1));
            var invoice = new Invoice { InvoiceId = Guid.NewGuid(), InvoiceCode = "INV-" + Guid.NewGuid().ToString("N")[..12], BookingId = Booking.BookingId, EmployeeId = Manager.EmployeeId,
                Type = type, Status = status, Currency = "VND", Subtotal = total, TotalAmount = total, CreatedAt = at, UpdatedAt = at };
            Context.Invoices.Add(invoice);
            return invoice;
        }

        internal Receipt AddReceipt(Invoice invoice, decimal amount, ReceiptStatus status = ReceiptStatus.CONFIRMED, DateTimeOffset? updatedAt = null)
        {
            var at = Db(updatedAt ?? Now.AddDays(-1));
            var receipt = new Receipt { ReceiptId = Guid.NewGuid(), ReceiptCode = "RCP-" + Guid.NewGuid().ToString("N")[..12], InvoiceId = invoice.InvoiceId, Amount = amount, Currency = "VND",
                PaymentMethod = PaymentMethod.CASH, Status = status, CreatedAt = at, UpdatedAt = at };
            Context.Receipts.Add(receipt);
            return receipt;
        }

        internal EmergencyCostRequest AddEmergencyCost(string incidentCode, decimal approved, EmergencyCostStatus status = EmergencyCostStatus.APPROVED, DateTimeOffset? updatedAt = null)
        {
            var at = Db(updatedAt ?? Now.AddDays(-1));
            var location = new Location { LocationId = Guid.NewGuid(), LocationName = "Roadside", Country = "FR" };
            var incident = new IncidentReport { IncidentReportId = Guid.NewGuid(), TransportId = Transport.TransportId, IncidentCode = incidentCode, EmployeeId = Manager.EmployeeId,
                LocationId = location.LocationId, Category = IncidentCategory.VEHICLE, Severity = IncidentSeverity.MEDIUM, Status = IncidentStatus.REPORTED, Title = "Tyre", Description = "Flat tyre",
                OccurredAt = at, ReportedAt = at, CreatedAt = at, UpdatedAt = at };
            var cost = new EmergencyCostRequest { EmergencyCostRequestId = Guid.NewGuid(), IncidentReportId = incident.IncidentReportId, RequestedByEmployeeId = Manager.EmployeeId, RequestCode = "ECR-" + Guid.NewGuid().ToString("N")[..8], Category = EmergencyCostCategory.OTHER,
                Status = status, Description = "Tyre", Justification = "Burst", Currency = "VND", RequestedAmount = approved, ApprovedAmount = status == EmergencyCostStatus.APPROVED ? approved : null,
                CreatedAt = at, UpdatedAt = at };
            Context.AddRange(location, incident, cost);
            return cost;
        }

        internal ComplianceDossier AddDossier(Guid? templateId = null)
        {
            var dossier = new ComplianceDossier { ComplianceDossierId = Guid.NewGuid(), TransportId = Transport.TransportId, DossierCode = "CD-" + Guid.NewGuid().ToString("N")[..8],
                DossierType = DossierType.MAIN, DossierTemplateId = templateId, CreatedAt = Db(Now.AddDays(-5)), UpdatedAt = Db(Now.AddDays(-5)) };
            Context.ComplianceDossiers.Add(dossier);
            return dossier;
        }
    }

    private static async Task<T> ThrowsAsync<T>(Task task, string errorCode) where T : Exceptions.Exceptions
    {
        var error = await Assert.ThrowsAsync<T>(() => task);
        Assert.Equal(errorCode, error.ErrorCode);
        return error;
    }

    // ---------------------------------------------------------------- FS1

    [Fact]
    public async Task Get_ACompletedTransportCreatesItsReportOnceAndReturnsTheSavedNumbers()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        var final = f.AddInvoice(InvoiceType.FINAL, InvoiceStatus.PAID, 1_000); f.AddReceipt(final, 1_000);
        f.AddInvoice(InvoiceType.SUPPLEMENTARY, InvoiceStatus.ISSUED, 200);
        f.AddInvoice(InvoiceType.DEPOSIT, InvoiceStatus.PAID, 400); f.AddInvoice(InvoiceType.FINAL, InvoiceStatus.CANCELLED, 9_000);
        f.AddEmergencyCost("INC-20261016-0001", 500); f.AddEmergencyCost("INC-20261016-0002", 300, EmergencyCostStatus.REJECTED);
        await db.SaveChangesAsync();

        var first = await f.Service.GetAsync(f.ManagerAccount.AccountId, f.Transport.TransportId, false);
        f.Clock.Value = Now.AddHours(5);
        var second = await f.Service.GetAsync(f.ManagerAccount.AccountId, f.Transport.TransportId, false);

        Assert.False(first.IsProvisional); Assert.Matches("^FR-20261020-[0-9]{4}$", first.ReportCode); Assert.Equal(first.FinancialReportId, second.FinancialReportId);
        Assert.Equal(Now, first.GeneratedAt); Assert.Equal(Now, second.GeneratedAt); Assert.Single(db.FinancialReports);
        Assert.Equal((1_200L, 1_000L, 500L, 200L), (first.TotalInvoicedAmount, first.TotalPaidAmount, first.TotalEmergencyCost, first.OutstandingAmount));
        Assert.Equal("VND", first.Currency); Assert.Empty(first.Warnings); Assert.Null(first.SummaryNote); Assert.False(first.IsStale);
        Assert.Equal(f.Transport.TransportCode, first.Transport.TransportCode); Assert.Equal(f.Booking.BookingCode, first.Booking.BookingCode);
        Assert.Equal(first.CreatedAt, first.UpdatedAt);
    }

    [Fact]
    public async Task Get_AnUnfinishedTransportNeedsConsentAndThenSavesNothing()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db, TransportStatus.IN_TRANSIT);
        f.AddInvoice(InvoiceType.SUPPLEMENTARY, InvoiceStatus.ISSUED, 700); await db.SaveChangesAsync();

        await ThrowsAsync<ConflictException>(f.Service.GetAsync(f.ManagerAccount.AccountId, f.Transport.TransportId, false), "TRANSPORT_NOT_COMPLETED");
        var provisional = await f.Service.GetAsync(f.ManagerAccount.AccountId, f.Transport.TransportId, true);

        Assert.True(provisional.IsProvisional); Assert.Null(provisional.FinancialReportId); Assert.Null(provisional.ReportCode); Assert.Null(provisional.GeneratedAt);
        Assert.Null(provisional.CreatedAt); Assert.Null(provisional.UpdatedAt); Assert.False(provisional.IsStale);
        Assert.Equal(["TRANSPORT_NOT_COMPLETED"], provisional.Warnings); Assert.Equal(700, provisional.TotalInvoicedAmount); Assert.Equal(700, provisional.OutstandingAmount);
        Assert.Empty(db.FinancialReports);
    }

    [Fact]
    public async Task Get_RefusesACancelledTransport_AnUnknownOne_AndEveryoneButAManager()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db, TransportStatus.CANCELLED);

        await ThrowsAsync<ConflictException>(f.Service.GetAsync(f.ManagerAccount.AccountId, f.Transport.TransportId, true), "INVALID_TRANSPORT_STATUS");
        await ThrowsAsync<NotFoundException>(f.Service.GetAsync(f.ManagerAccount.AccountId, Guid.NewGuid(), true), "TRANSPORT_NOT_FOUND");
        await Assert.ThrowsAsync<ForbiddenException>(() => f.Service.GetAsync(f.CustomerAccount.AccountId, f.Transport.TransportId, true));
        var coordinator = await PersonnelAssignmentTests.EmployeeAsync(db, EmployeeRole.FLEET_ROUTE_COORDINATOR);
        await Assert.ThrowsAsync<ForbiddenException>(() => f.Service.GetAsync(coordinator.AccountId, f.Transport.TransportId, true));
    }

    [Fact]
    public async Task Get_SaysTheNumbersAreStaleOnceAnInvoiceReceiptOrCostChangesAfterTheScan()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        var final = f.AddInvoice(InvoiceType.FINAL, InvoiceStatus.ISSUED, 1_000); await db.SaveChangesAsync();
        Assert.False((await f.Service.GetAsync(f.ManagerAccount.AccountId, f.Transport.TransportId, false)).IsStale);

        f.Clock.Value = Now.AddHours(1);
        final.UpdatedAt = Db(Now.AddMinutes(30)); await db.SaveChangesAsync();
        Assert.True((await f.Service.GetAsync(f.ManagerAccount.AccountId, f.Transport.TransportId, false)).IsStale);

        var refreshed = await f.Service.RefreshAsync(f.ManagerAccount.AccountId, f.Transport.TransportId);
        Assert.False(refreshed.IsStale);
        var receipt = f.AddReceipt(final, 1_000, updatedAt: Now.AddHours(2)); await db.SaveChangesAsync();
        f.Clock.Value = Now.AddHours(3);
        Assert.True((await f.Service.GetAsync(f.ManagerAccount.AccountId, f.Transport.TransportId, false)).IsStale);
        await f.Service.RefreshAsync(f.ManagerAccount.AccountId, f.Transport.TransportId);
        f.AddEmergencyCost("INC-1", 50, updatedAt: Now.AddHours(4)); await db.SaveChangesAsync();
        f.Clock.Value = Now.AddHours(5);
        Assert.True((await f.Service.GetAsync(f.ManagerAccount.AccountId, f.Transport.TransportId, false)).IsStale);
        Assert.NotNull(receipt);
    }

    [Fact]
    public async Task Get_ReconciliationFlagsEveryKindOfMismatch_AndIgnoresWhatAgrees()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        var good = f.AddInvoice(InvoiceType.FINAL, InvoiceStatus.PAID, 1_000); f.AddReceipt(good, 1_000);
        var paidWithout = f.AddInvoice(InvoiceType.SUPPLEMENTARY, InvoiceStatus.PAID, 300);
        var wrongAmount = f.AddInvoice(InvoiceType.SUPPLEMENTARY, InvoiceStatus.PAID, 400); f.AddReceipt(wrongAmount, 350);
        var unpaid = f.AddInvoice(InvoiceType.SUPPLEMENTARY, InvoiceStatus.ISSUED, 500); f.AddReceipt(unpaid, 500);
        var twice = f.AddInvoice(InvoiceType.DEPOSIT, InvoiceStatus.PAID, 100); f.AddReceipt(twice, 100); f.AddReceipt(twice, 100);
        var pending = f.AddInvoice(InvoiceType.DEPOSIT, InvoiceStatus.ISSUED, 90); f.AddReceipt(pending, 90, ReceiptStatus.PENDING);
        var cancelled = f.AddInvoice(InvoiceType.SUPPLEMENTARY, InvoiceStatus.CANCELLED, 70); f.AddReceipt(cancelled, 1);
        f.AddInvoice(InvoiceType.CREDIT_NOTE, InvoiceStatus.PAID, 60);
        await db.SaveChangesAsync();

        var result = await f.Service.GetAsync(f.ManagerAccount.AccountId, f.Transport.TransportId, false);

        Assert.True(result.Reconciliation.HasMismatch);
        var byIssue = result.Reconciliation.Items.ToDictionary(item => item.InvoiceId);
        Assert.Equal(4, byIssue.Count);
        Assert.Equal(new Reports.Response.ReconciliationItem(paidWithout.InvoiceId, paidWithout.InvoiceCode, "PAID_WITHOUT_RECEIPT", 300, 0), byIssue[paidWithout.InvoiceId]);
        Assert.Equal(new Reports.Response.ReconciliationItem(wrongAmount.InvoiceId, wrongAmount.InvoiceCode, "RECEIPT_AMOUNT_MISMATCH", 400, 350), byIssue[wrongAmount.InvoiceId]);
        Assert.Equal(new Reports.Response.ReconciliationItem(unpaid.InvoiceId, unpaid.InvoiceCode, "RECEIPT_ON_UNPAID_INVOICE", 500, 500), byIssue[unpaid.InvoiceId]);
        Assert.Equal(new Reports.Response.ReconciliationItem(twice.InvoiceId, twice.InvoiceCode, "MULTIPLE_CONFIRMED_RECEIPTS", 100, 200), byIssue[twice.InvoiceId]);
    }

    [Fact]
    public async Task Get_ACleanTripHasNoReconciliationItems()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);

        var result = await f.Service.GetAsync(f.ManagerAccount.AccountId, f.Transport.TransportId, false);

        Assert.False(result.Reconciliation.HasMismatch); Assert.Empty(result.Reconciliation.Items);
    }

    // ---------------------------------------------------------------- suggested items

    [Fact]
    public async Task Suggestions_AreTheNineListLinesForTheRealHorseCountAndDuration()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db); // 4 horses, left 15 Oct 06:00 and arrived 18 Oct 04:00: 70 hours = 3 days
        var dossier = f.AddDossier(Guid.NewGuid()); await db.SaveChangesAsync();

        var result = await f.Service.GetAsync(f.ManagerAccount.AccountId, f.Transport.TransportId, false);

        Assert.Equal(dossier.DossierTemplateId, result.DossierTemplateId);
        var lines = result.SuggestedItems;
        Assert.Equal(9, lines.Count);
        Assert.Equal(["TRANSPORT_FEE", "TRANSPORT_FEE", "HORSE_SERVICE_FEE", "HORSE_SERVICE_FEE", "HANDLING_FEE", "HANDLING_FEE", "COMPLIANCE_FEE", "COMPLIANCE_FEE", "OTHER"], lines.Select(line => line.ItemType));
        Assert.Equal([1, 4, 12, 4, 4, 1, 1, 4, 4], lines.Select(line => line.Quantity));
        Assert.Equal<long?>([2_400, 120, 35, 110, 50, 150, 250, 90, 80], lines.Select(line => line.SuggestedPrice));
        Assert.All(lines, line => { Assert.Null(line.ReferenceAmount); Assert.Null(line.Warning); });
        Assert.Equal("TIER_M", lines[0].Basis["vehicleTier"]); Assert.Equal(4, lines[0].Basis["horseCount"]); Assert.Equal(3, lines[2].Basis["transportDays"]);
        Assert.Empty(lines[5].Basis); Assert.Equal(dossier.DossierTemplateId, lines[6].Basis["dossierTemplateId"]);
        Assert.Equal(new Reports.Response.SourceRef("ComplianceDossier", dossier.ComplianceDossierId), Assert.Single(lines[6].SourceRefs)); Assert.Empty(lines[0].SourceRefs);
    }

    [Theory]
    [InlineData(1, 1, "S", 1_600)]
    [InlineData(3, 3, "S", 1_600)]
    [InlineData(5, 3, "M", 2_400)]
    [InlineData(9, 3, "L", 3_400)]
    public async Task Suggestions_TheVehicleTierFollowsTheHorseCount(int horses, int days, string tier, long baseFare)
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db, horses: horses);
        f.Transport.ActualDepartureAt = new DateTime(2026, 10, 15, 6, 0, 0); f.Transport.ActualArrivalAt = new DateTime(2026, 10, 15, 6, 0, 0).AddHours(days * 24 - 2); await db.SaveChangesAsync();

        var lines = (await f.Service.GetAsync(f.ManagerAccount.AccountId, f.Transport.TransportId, false)).SuggestedItems;

        Assert.Equal("TIER_" + tier, lines[0].Basis["vehicleTier"]); Assert.Equal(baseFare, lines[0].SuggestedPrice);
        Assert.Equal(horses * days, lines[2].Quantity); Assert.Equal(days, lines[2].Basis["transportDays"]);
    }

    [Fact]
    public async Task Suggestions_WithoutADossierTheTwoFilingLinesAreLeftOut()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);

        var lines = (await f.Service.GetAsync(f.ManagerAccount.AccountId, f.Transport.TransportId, false)).SuggestedItems;

        Assert.Equal(7, lines.Count); Assert.DoesNotContain(lines, line => line.ItemType == "COMPLIANCE_FEE");
    }

    [Fact]
    public async Task Suggestions_MissingTimesFallBackToTheDefaultDuration()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db, withTimes: false);

        var feed = (await f.Service.GetAsync(f.ManagerAccount.AccountId, f.Transport.TransportId, false)).SuggestedItems[2];

        Assert.Equal(8, feed.Quantity); Assert.Equal(2, feed.Basis["transportDays"]);
    }

    [Fact]
    public async Task Suggestions_ApprovedEmergencyCostsAreOneReferenceLineWithoutAPrice()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        var first = f.AddEmergencyCost("INC-20261016-0003", 3_500); var second = f.AddEmergencyCost("INC-20261016-0003", 500);
        f.AddEmergencyCost("INC-20261016-0004", 999, EmergencyCostStatus.PENDING); await db.SaveChangesAsync();

        var line = (await f.Service.GetAsync(f.ManagerAccount.AccountId, f.Transport.TransportId, false)).SuggestedItems.Last();

        Assert.Equal("EMERGENCY_COST", line.ItemType); Assert.Null(line.SuggestedPrice); Assert.Equal(4_000, line.ReferenceAmount); Assert.Equal(1, line.Quantity);
        Assert.Equal(2, line.Basis["requestCount"]); Assert.Equal(new[] { "INC-20261016-0003" }, (string[])line.Basis["incidentCodes"]!);
        Assert.Equal(new[] { first.EmergencyCostRequestId, second.EmergencyCostRequestId }.Order(), line.SourceRefs.Select(item => item.Id).Order());
        Assert.All(line.SourceRefs, item => Assert.Equal("EmergencyCostRequest", item.Type));
    }

    [Fact]
    public async Task Suggestions_AnUnsupportedHorseCountKeepsOnlyTheEmergencyCostLine()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db, horses: 10);
        f.AddEmergencyCost("INC-1", 100); await db.SaveChangesAsync();

        var result = await f.Service.GetAsync(f.ManagerAccount.AccountId, f.Transport.TransportId, false);

        Assert.DoesNotContain("HORSE_COUNT_UNSUPPORTED", result.Warnings);
        Assert.Equal("EMERGENCY_COST", Assert.Single(result.SuggestedItems).ItemType); // only the reference to what really happened remains
    }

    // ---------------------------------------------------------------- FS2

    [Fact]
    public async Task Refresh_OverwritesTheAmountsAndKeepsTheCodeAndTheNote()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        var final = f.AddInvoice(InvoiceType.FINAL, InvoiceStatus.ISSUED, 1_000); await db.SaveChangesAsync();
        var first = await f.Service.GetAsync(f.ManagerAccount.AccountId, f.Transport.TransportId, false);
        var noted = await f.Service.UpdateNoteAsync(f.ManagerAccount.AccountId, f.Transport.TransportId, new() { LastUpdatedAt = first.UpdatedAt, SummaryNote = "Looks fine" });
        f.AddReceipt(final, 1_000, updatedAt: Now.AddHours(1)); final.Status = InvoiceStatus.PAID; final.UpdatedAt = Db(Now.AddHours(1)); f.AddInvoice(InvoiceType.SUPPLEMENTARY, InvoiceStatus.ISSUED, 250, Now.AddHours(1));
        await db.SaveChangesAsync();
        f.Clock.Value = Now.AddHours(2);

        var result = await f.Service.RefreshAsync(f.ManagerAccount.AccountId, f.Transport.TransportId);

        Assert.Equal(first.ReportCode, result.ReportCode); Assert.Equal(first.FinancialReportId, result.FinancialReportId); Assert.Equal("Looks fine", result.SummaryNote);
        Assert.Equal((1_250L, 1_000L, 250L), (result.TotalInvoicedAmount, result.TotalPaidAmount, result.OutstandingAmount));
        Assert.Equal(Now.AddHours(2), result.GeneratedAt); Assert.True(result.UpdatedAt > noted.UpdatedAt); Assert.False(result.IsStale);
    }

    [Fact]
    public async Task Refresh_NeedsACompletedTransport_AndCreatesTheReportWhenItIsMissing()
    {
        await using var db = TestSupport.CreateDb(); var running = await Fixture.CreateAsync(db, TransportStatus.READY);
        await ThrowsAsync<ConflictException>(running.Service.RefreshAsync(running.ManagerAccount.AccountId, running.Transport.TransportId), "TRANSPORT_NOT_COMPLETED");
        await using var other = TestSupport.CreateDb(); var cancelled = await Fixture.CreateAsync(other, TransportStatus.CANCELLED);
        await ThrowsAsync<ConflictException>(cancelled.Service.RefreshAsync(cancelled.ManagerAccount.AccountId, cancelled.Transport.TransportId), "INVALID_TRANSPORT_STATUS");
        await using var third = TestSupport.CreateDb(); var done = await Fixture.CreateAsync(third);

        var created = await done.Service.RefreshAsync(done.ManagerAccount.AccountId, done.Transport.TransportId);

        Assert.NotNull(created.ReportCode); Assert.Single(third.FinancialReports); Assert.Empty(db.FinancialReports);
    }

    // ---------------------------------------------------------------- FS3

    [Fact]
    public async Task Note_IsTrimmedStored_ClearedByEmpty_AndLeavesTheAmountsAlone()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        f.AddInvoice(InvoiceType.FINAL, InvoiceStatus.ISSUED, 1_000); await db.SaveChangesAsync();
        var report = await f.Service.GetAsync(f.ManagerAccount.AccountId, f.Transport.TransportId, false);
        f.Clock.Value = Now.AddMinutes(10);

        var saved = await f.Service.UpdateNoteAsync(f.ManagerAccount.AccountId, f.Transport.TransportId, new() { LastUpdatedAt = report.UpdatedAt, SummaryNote = "  Margin is good  " });
        var cleared = await f.Service.UpdateNoteAsync(f.ManagerAccount.AccountId, f.Transport.TransportId, new() { LastUpdatedAt = saved.UpdatedAt, SummaryNote = "   " });

        Assert.Equal("Margin is good", saved.SummaryNote); Assert.Equal(Now.AddMinutes(10), saved.UpdatedAt); Assert.Equal(report.GeneratedAt, saved.GeneratedAt);
        Assert.Equal(report.TotalInvoicedAmount, saved.TotalInvoicedAmount); Assert.Null(cleared.SummaryNote); Assert.True(cleared.UpdatedAt >= saved.UpdatedAt);
    }

    [Fact]
    public async Task Note_RefusesAStaleVersion_AMissingReport_AndBadInput()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        await ThrowsAsync<NotFoundException>(f.Service.UpdateNoteAsync(f.ManagerAccount.AccountId, f.Transport.TransportId, new() { LastUpdatedAt = Now, SummaryNote = "x" }), "FINANCIAL_REPORT_NOT_FOUND");
        await ThrowsAsync<NotFoundException>(f.Service.UpdateNoteAsync(f.ManagerAccount.AccountId, Guid.NewGuid(), new() { LastUpdatedAt = Now }), "TRANSPORT_NOT_FOUND");
        var report = await f.Service.GetAsync(f.ManagerAccount.AccountId, f.Transport.TransportId, false);

        await ThrowsAsync<ConflictException>(f.Service.UpdateNoteAsync(f.ManagerAccount.AccountId, f.Transport.TransportId,
            new() { LastUpdatedAt = report.UpdatedAt!.Value.AddSeconds(-1), SummaryNote = "stale" }), "FINANCIAL_REPORT_MODIFIED");
        var tooLong = await Assert.ThrowsAsync<FieldValidationException>(() => f.Service.UpdateNoteAsync(f.ManagerAccount.AccountId, f.Transport.TransportId,
            new() { LastUpdatedAt = report.UpdatedAt, SummaryNote = new string('x', 2_001) }));
        Assert.Contains(tooLong.Errors, item => item.Field == "summaryNote");
        var noVersion = await Assert.ThrowsAsync<FieldValidationException>(() => f.Service.UpdateNoteAsync(f.ManagerAccount.AccountId, f.Transport.TransportId, new() { SummaryNote = "x" }));
        Assert.Contains(noVersion.Errors, item => item.Field == "lastUpdatedAt");
        await Assert.ThrowsAsync<ForbiddenException>(() => f.Service.UpdateNoteAsync(f.CustomerAccount.AccountId, f.Transport.TransportId, new() { LastUpdatedAt = report.UpdatedAt }));
    }
}
