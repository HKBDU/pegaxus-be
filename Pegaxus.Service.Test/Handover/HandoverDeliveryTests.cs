using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.OutboxService;

using static Pegaxus.Service.Test.HandoverCompletionTests;
using static Pegaxus.Service.Test.HandoverTests;

using Handovers = Pegaxus.Service.HandoverService;

namespace Pegaxus.Service.Test;

/// <summary>Flow 6 section 11: what completing the DELIVERY record adds (effects, who is told, the financial report) and EP1, the e-POD PDF.</summary>
public class HandoverDeliveryTests
{
    internal static async Task<Handovers.Response.DetailResponse> DeliverAsync(Fixture f, Action<Handovers.Request.CompleteRequest>? tune = null)
    {
        await f.Service.CompleteAsync(f.Pickup.HandoverRecordId, Request(f.Pickup), f.Escort.AccountId);
        f.Clock.Now = f.Clock.Now.AddHours(1);
        await f.Service.CompleteAsync(f.Transfer.HandoverRecordId, Request(f.Transfer), f.Receiver.AccountId);
        f.Clock.Now = f.Clock.Now.AddHours(1);
        var request = Request(f.Delivery); tune?.Invoke(request);
        return await f.Service.CompleteAsync(f.Delivery.HandoverRecordId, request, f.Coordinator.AccountId);
    }

    private static IncidentReport AddRepairIncident(AppDbContext db, Fixture f, Vehicle vehicle, bool repairRequired = true, IncidentStatus status = IncidentStatus.REPORTED, Guid? transportId = null)
    {
        var incident = new IncidentReport { IncidentReportId = Guid.NewGuid(), TransportId = transportId ?? f.TransportId, IncidentCode = "INC-" + Guid.NewGuid().ToString("N"),
            EmployeeId = f.Driver.EmployeeId, LocationId = Guid.NewGuid(), Category = IncidentCategory.VEHICLE, Severity = IncidentSeverity.MEDIUM, Status = status,
            Title = "Flat tyre", Description = "Flat tyre on the road", OccurredAt = Fixture.Now, ReportedAt = Fixture.Now, CreatedAt = Fixture.Now, UpdatedAt = Fixture.Now };
        db.IncidentReports.Add(incident);
        db.VehicleIncidentDetails.Add(new VehicleIncidentDetail { VehicleIncidentDetailId = Guid.NewGuid(), IncidentReportId = incident.IncidentReportId, VehicleId = vehicle.VehicleId,
            IncidentType = VehicleIncidentType.TIRE_DAMAGE, RepairRequired = repairRequired, CreatedAt = Fixture.Now, UpdatedAt = Fixture.Now });
        return incident;
    }

    // ------------------------------------------------------------ effects

    [Fact]
    public async Task Delivery_ReportsTheDiscrepancyFlag_AndTheVehiclesItReleased()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateStartedAsync(db);
        var vehicle = AddVehicle(db, f.First, f.Second); vehicle.LicensePlate = "75A-123.45"; await db.SaveChangesAsync();

        var delivery = await DeliverAsync(f, request => request.DiscrepancyNote = "Scratch on the left leg");

        Assert.True(delivery.Effects!.HasDiscrepancy);
        var released = Assert.Single(delivery.Effects.ReleasedVehicles!);
        Assert.Equal(new Handovers.Response.ReleasedVehicle(vehicle.VehicleId, vehicle.VehicleCode, "75A-123.45", "AVAILABLE"), released);
        Assert.Equal(VehicleStatus.AVAILABLE, vehicle.Status);
    }

    [Fact]
    public async Task Delivery_WithoutAnyNoteOrVehicle_ReportsNoDiscrepancyAndAnEmptyList()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateStartedAsync(db);

        var delivery = await DeliverAsync(f);

        Assert.False(delivery.Effects!.HasDiscrepancy); Assert.Empty(delivery.Effects.ReleasedVehicles!);
    }

    [Fact]
    public async Task Delivery_SendsAVehicleThatNeedsRepairToMaintenance_AndIgnoresRepairsOfOtherTripsOrCancelledIncidents()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateStartedAsync(db);
        var repaired = AddVehicle(db, f.First); var cancelledIncident = AddVehicle(db, f.Second);
        var otherTrip = AddVehicle(db, f.First);
        AddRepairIncident(db, f, repaired);
        AddRepairIncident(db, f, cancelledIncident, status: IncidentStatus.CANCELLED);
        AddRepairIncident(db, f, otherTrip, transportId: Guid.NewGuid());
        await db.SaveChangesAsync();

        var delivery = await DeliverAsync(f);

        var byCode = delivery.Effects!.ReleasedVehicles!.ToDictionary(vehicle => vehicle.VehicleId, vehicle => vehicle.NewStatus);
        Assert.Equal("MAINTENANCE", byCode[repaired.VehicleId]); Assert.Equal("AVAILABLE", byCode[cancelledIncident.VehicleId]); Assert.Equal("AVAILABLE", byCode[otherTrip.VehicleId]);
        Assert.Equal(VehicleStatus.MAINTENANCE, repaired.Status); Assert.Equal(VehicleStatus.AVAILABLE, cancelledIncident.Status);
    }

    [Fact]
    public async Task Delivery_LeavesVehiclesThatAreNotInUseAlone()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateStartedAsync(db);
        var broken = AddVehicle(db, f.First, f.Second); broken.Status = VehicleStatus.OUT_OF_SERVICE; await db.SaveChangesAsync();

        var delivery = await DeliverAsync(f);

        Assert.Empty(delivery.Effects!.ReleasedVehicles!); Assert.Equal(VehicleStatus.OUT_OF_SERVICE, broken.Status);
    }

    [Fact]
    public async Task OtherRecordTypes_DoNotCarryTheDeliveryOnlyEffects()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateStartedAsync(db);

        var pickup = await f.Service.CompleteAsync(f.Pickup.HandoverRecordId, Request(f.Pickup), f.Escort.AccountId);

        Assert.Null(pickup.Effects!.HasDiscrepancy); Assert.Null(pickup.Effects.ReleasedVehicles);
    }

    // ------------------------------------------------------------ who is told

    [Fact]
    public async Task Delivery_TellsEachRoleWhatItNeedsToDo()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateStartedAsync(db);
        var vehicle = AddVehicle(db, f.First); AddRepairIncident(db, f, vehicle);
        var specialist = await PersonnelAssignmentTests.EmployeeAsync(db, EmployeeRole.TRANSPORT_SPECIALIST);
        var strangerCoordinator = await PersonnelAssignmentTests.EmployeeAsync(db, EmployeeRole.FLEET_ROUTE_COORDINATOR);
        var unassignedSpecialist = await PersonnelAssignmentTests.EmployeeAsync(db, EmployeeRole.TRANSPORT_SPECIALIST);
        db.Add(new EmployeeTransportPlan { Employee = specialist, TransportPlan = f.Route.TransportPlan, Responsibility = "TRANSPORT_SPECIALIST" });
        await db.SaveChangesAsync();
        var before = await db.Notifications.CountAsync();

        await DeliverAsync(f, request => request.DiscrepancyNote = "Scratch on the left leg");

        var created = await db.Notifications.OrderBy(item => item.CreatedAt).ToListAsync();
        var delivery = created.Where(item => item.Title.Contains("completed") || item.Title.Contains("delivered")).ToList();
        async Task<Notification> For(Guid accountId) => await db.AccountNotifications.Where(link => link.AccountId == accountId && delivery.Select(item => item.NotificationId).Contains(link.NotificationId))
            .Select(link => link.Notification).SingleAsync();

        var manager = await For(f.Manager.AccountId);
        Assert.Equal(NotificationType.HANDOVER, manager.NotificationType); Assert.Equal(NotificationPriority.NORMAL, manager.Priority);
        Assert.Equal($"/admin/transports/{f.TransportId}/financial-summary", manager.ActionUrl);
        Assert.Contains("create the FINAL invoice", manager.Title); Assert.Contains("on-site notes", manager.Message);

        var coordinator = await For(f.Coordinator.AccountId);
        Assert.Equal($"/admin/ongoing-transports/{f.TransportId}", coordinator.ActionUrl); Assert.Contains($"{vehicle.VehicleCode} (MAINTENANCE)", coordinator.Message);

        var legal = await For(specialist.AccountId);
        Assert.Equal(NotificationType.TRANSPORT, legal.NotificationType); Assert.Equal($"/admin/transports/{f.TransportId}", legal.ActionUrl); Assert.Contains("legal dossier", legal.Title);

        // Only the people assigned to this trip are told; the rest of the staff of the same role are not.
        var recipients = await db.AccountNotifications.Where(link => delivery.Select(item => item.NotificationId).Contains(link.NotificationId)).Select(link => link.AccountId).ToListAsync();
        Assert.DoesNotContain(strangerCoordinator.AccountId, recipients); Assert.DoesNotContain(unassignedSpecialist.AccountId, recipients);
        Assert.Contains(f.CustomerAccountId, recipients);
        Assert.True(created.Count > before);
    }

    [Fact]
    public async Task Delivery_TheManagerMessageHasNoOnSiteWarning_WhenThereIsNoNote()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateStartedAsync(db);

        await DeliverAsync(f);

        var manager = await db.AccountNotifications.Where(link => link.AccountId == f.Manager.AccountId && link.Notification.ActionUrl!.EndsWith("/financial-summary")).Select(link => link.Notification).SingleAsync();
        Assert.DoesNotContain("on-site notes", manager.Message);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Delivery_TheCustomerEmailLeadsToTheTransportPageAndRemindsAboutClaims(bool withNote)
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateStartedAsync(db);

        await DeliverAsync(f, request => request.DiscrepancyNote = withNote ? "Scratch on the left leg" : null);

        var email = OutboxQueue.Deserialize<EmailPayload>(Assert.Single(await db.OutboxMessages.ToListAsync()).Payload);
        Assert.Contains("download e-POD", email.Body); Assert.Contains("24 hours", email.Body);
        Assert.Contains($"/bookings/", email.Body);
        Assert.Equal(withNote, email.Body.Contains("on-site notes"));
    }

    // ------------------------------------------------------------ financial report

    [Fact]
    public async Task Delivery_CreatesTheFinancialReportFromTheInvoicesAfterTheCommit()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateStartedAsync(db);
        var booking = await db.Bookings.SingleAsync();
        var final = Invoice(db, booking.BookingId, f.Manager.EmployeeId, InvoiceType.FINAL, InvoiceStatus.PAID, 1_000);
        Invoice(db, booking.BookingId, f.Manager.EmployeeId, InvoiceType.SUPPLEMENTARY, InvoiceStatus.ISSUED, 200);
        Invoice(db, booking.BookingId, f.Manager.EmployeeId, InvoiceType.SUPPLEMENTARY, InvoiceStatus.CANCELLED, 5_000);
        Invoice(db, booking.BookingId, f.Manager.EmployeeId, InvoiceType.DEPOSIT, InvoiceStatus.PAID, 400);
        db.Receipts.Add(new Receipt { ReceiptId = Guid.NewGuid(), ReceiptCode = "RCP-1", InvoiceId = final.InvoiceId, Amount = 1_000, Currency = "VND", PaymentMethod = PaymentMethod.CASH,
            Status = ReceiptStatus.CONFIRMED, CreatedAt = Fixture.Now, UpdatedAt = Fixture.Now });
        db.Receipts.Add(new Receipt { ReceiptId = Guid.NewGuid(), ReceiptCode = "RCP-2", InvoiceId = final.InvoiceId, Amount = 77, Currency = "VND", PaymentMethod = PaymentMethod.CASH,
            Status = ReceiptStatus.PENDING, CreatedAt = Fixture.Now, UpdatedAt = Fixture.Now });
        var incident = AddRepairIncident(db, f, AddVehicle(db, f.First));
        db.EmergencyCostRequests.Add(new EmergencyCostRequest { EmergencyCostRequestId = Guid.NewGuid(), IncidentReportId = incident.IncidentReportId, Status = EmergencyCostStatus.APPROVED,
            RequestCode = "ECR-1", Description = "Tyre replacement", Justification = "The tyre burst", Currency = "VND", RequestedAmount = 600, ApprovedAmount = 500, CreatedAt = Fixture.Now, UpdatedAt = Fixture.Now });
        await db.SaveChangesAsync();

        await DeliverAsync(f);

        var report = await db.FinancialReports.SingleAsync();
        Assert.Equal(f.TransportId, report.TransportId); Assert.Matches("^FR-[0-9]{8}-[0-9]{4}$", report.ReportCode); Assert.Equal("VND", report.Currency);
        Assert.Equal(1_200, report.TotalInvoicedAmount); Assert.Equal(1_000, report.TotalPaidAmount); Assert.Equal(200, report.OutstandingAmount); Assert.Equal(500, report.TotalEmergencyCost);
        Assert.Null(report.SummaryNote);
    }

    [Fact]
    public async Task Delivery_AFailingReportDoesNotUndoTheSignedHandover()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateStartedAsync(db);
        f.FinancialReports = new FailingReports();

        var delivery = await DeliverAsync(f);

        Assert.Equal("COMPLETED", delivery.Status); Assert.Equal("COMPLETED", delivery.Effects!.TransportStatus);
        Assert.Equal(HandoverStatus.COMPLETED, (await db.HandoverRecords.SingleAsync(item => item.HandoverRecordId == f.Delivery.HandoverRecordId)).Status);
    }

    [Fact]
    public async Task EnsureReport_CreatesItOnlyOnce_AndNeverOverwritesAnExistingOne()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateStartedAsync(db);
        var reports = new Pegaxus.Service.FinancialReportService.Service(db, f.Clock);

        await reports.EnsureAsync(f.TransportId);
        var first = await db.FinancialReports.AsNoTracking().SingleAsync();
        f.Clock.Now = f.Clock.Now.AddDays(1);
        await reports.EnsureAsync(f.TransportId);

        var again = await db.FinancialReports.AsNoTracking().SingleAsync();
        Assert.Equal(first.FinancialReportId, again.FinancialReportId); Assert.Equal(first.ReportCode, again.ReportCode); Assert.Equal(first.GeneratedAt, again.GeneratedAt);
    }

    private static Invoice Invoice(AppDbContext db, Guid bookingId, Guid employeeId, InvoiceType type, InvoiceStatus status, decimal total)
    {
        var invoice = new Invoice { InvoiceId = Guid.NewGuid(), InvoiceCode = "INV-" + Guid.NewGuid().ToString("N"), BookingId = bookingId, EmployeeId = employeeId, Type = type, Status = status,
            Currency = "VND", Subtotal = total, TotalAmount = total, CreatedAt = Fixture.Now, UpdatedAt = Fixture.Now };
        db.Invoices.Add(invoice);
        return invoice;
    }

    private sealed class FailingReports : Pegaxus.Service.FinancialReportService.IFinancialReportService
    {
        public Task EnsureAsync(Guid transportId, CancellationToken cancellationToken = default) => throw new InvalidOperationException("The report store is down");
    }

    // ------------------------------------------------------------ EP1

    [Fact]
    public async Task Epod_ACompletedRecordGivesAPdfToEveryoneWhoMaySeeIt()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateStartedAsync(db);
        await DeliverAsync(f, request =>
        {
            request.HorseConditionNote = "Ngựa khỏe mạnh, chân trái trầy xước nhẹ"; // free text with diacritics must not break the document
            request.DiscrepancyNote = "Scratch on the left leg";
        });

        foreach (var viewer in new[] { f.Manager.AccountId, f.Coordinator.AccountId, f.Escort.AccountId, f.CustomerAccountId })
        {
            var epod = await f.Service.GetEpodAsync(f.Delivery.HandoverRecordId, "PDF", viewer);

            Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(epod.Content, 0, 4)); Assert.True(epod.Content.Length > 1_000);
            Assert.Equal($"{f.Delivery.HandoverCode}-epod.pdf", epod.FileName);
        }
        Assert.NotEmpty((await f.Service.GetEpodAsync(f.Pickup.HandoverRecordId, null, f.CustomerAccountId)).Content);
    }

    [Fact]
    public async Task Epod_StillComesOutWhenASignatureImageCannotBeLoaded()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateStartedAsync(db);
        await DeliverAsync(f);
        f.Delivery.SenderSignatureUrl = "https://res.cloudinary.com/test/image/authenticated/v1/gone.png"; // not a prefix of the fake store, so the download throws
        await db.SaveChangesAsync();

        var epod = await f.Service.GetEpodAsync(f.Delivery.HandoverRecordId, "pdf", f.Manager.AccountId);

        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(epod.Content, 0, 4));
    }

    [Fact]
    public async Task Epod_RefusesWhatTheCallerCannotSeeOrWhatIsNotSignedYet()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateStartedAsync(db);

        var notSigned = await Assert.ThrowsAsync<ConflictException>(() => f.Service.GetEpodAsync(f.Delivery.HandoverRecordId, "pdf", f.Manager.AccountId));
        Assert.Equal("INVALID_HANDOVER_STATUS", notSigned.ErrorCode);
        var format = await Assert.ThrowsAsync<BadRequestException>(() => f.Service.GetEpodAsync(f.Delivery.HandoverRecordId, "docx", f.Manager.AccountId));
        Assert.Equal("VALIDATION_ERROR", format.ErrorCode);
        var missing = await Assert.ThrowsAsync<NotFoundException>(() => f.Service.GetEpodAsync(Guid.NewGuid(), "pdf", f.Manager.AccountId));
        Assert.Equal("HANDOVER_RECORD_NOT_FOUND", missing.ErrorCode);

        await DeliverAsync(f);
        var stranger = await TestSupport.AddCustomerAsync(db);
        Assert.Equal("HANDOVER_RECORD_NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(() => f.Service.GetEpodAsync(f.Delivery.HandoverRecordId, "pdf", stranger.Account.AccountId))).ErrorCode);
        // A customer reaches only the pickup and delivery records, never a transfer between drivers.
        Assert.Equal("HANDOVER_RECORD_NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(() => f.Service.GetEpodAsync(f.Transfer.HandoverRecordId, "pdf", f.CustomerAccountId))).ErrorCode);
        // A crew member who is not assigned to this transport is refused.
        var outsider = await PersonnelAssignmentTests.EmployeeAsync(db, EmployeeRole.DRIVER);
        await Assert.ThrowsAsync<ForbiddenException>(() => f.Service.GetEpodAsync(f.Delivery.HandoverRecordId, "pdf", outsider.AccountId));
    }
}
