using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;

namespace Pegaxus.Service.Test;

public class BookingCancellationCascadeTests
{
    private static readonly DateTime At = new(2030, 1, 1, 8, 0, 0, DateTimeKind.Unspecified);

    private sealed record Seed(RouteChangeTests.Fixture F, Vehicle Vehicle, Invoice Draft, Invoice Issued, Invoice Paid, Invoice Pending, HandoverRecord Handover, DocumentRequest Request, ComplianceDossier Extra);

    private static async Task<Seed> SeedAsync(AppDbContext db, TransportStatus transportStatus = TransportStatus.READY)
    {
        var f = await RouteChangeTests.Fixture.CreateAsync(db);
        var vehicle = await db.Vehicles.SingleAsync(); vehicle.Status = VehicleStatus.ASSIGNED;
        f.Transport.Status = transportStatus; f.Plan.Status = RoutePlanStatus.CONFIRMED;
        var manager = await db.Employees.SingleAsync(item => item.AccountId == f.Manager.AccountId);
        var extra = await f.AddExtraAsync(ComplianceDossierStatus.IN_PREPARATION);
        var requirement = new DossierRequirement { DossierRequirementId = Guid.NewGuid(), ComplianceDossierId = extra.ComplianceDossierId, ComplianceRequirementId = Guid.NewGuid(),
            Status = DossierRequirementStatus.REQUESTED, IsMandatory = true, CreatedAt = At, UpdatedAt = At };
        var request = new DocumentRequest { DocumentRequestId = Guid.NewGuid(), DossierRequirementId = requirement.DossierRequirementId, Status = DocumentRequestStatus.OPEN,
            RequestCode = "DRQ-1", Message = "Please upload the passport", RequestedAt = At, CreatedAt = At, UpdatedAt = At };
        var handover = new HandoverRecord { HandoverRecordId = Guid.NewGuid(), TransportId = f.Transport.TransportId, HandoverCode = "HO-1", SequenceNo = 1,
            HandoverType = HandoverType.PICKUP, Status = HandoverStatus.PLANNED, FromType = HandoverPartyType.CUSTOMER, ToType = HandoverPartyType.EMPLOYEE, CreatedAt = At, UpdatedAt = At };
        Invoice NewInvoice(string code, InvoiceStatus status) => new() { InvoiceId = Guid.NewGuid(), InvoiceCode = code, BookingId = f.Transport.BookingId, EmployeeId = manager.EmployeeId,
            Type = InvoiceType.DEPOSIT, Status = status, Currency = "VND", Subtotal = 1_000_000, TotalAmount = 1_000_000, CreatedAt = At, UpdatedAt = At };
        var draft = NewInvoice("INV-DRAFT", InvoiceStatus.DRAFT); var issued = NewInvoice("INV-ISSUED", InvoiceStatus.ISSUED);
        var paid = NewInvoice("INV-PAID", InvoiceStatus.PAID); var pending = NewInvoice("INV-PENDING", InvoiceStatus.ISSUED);
        db.AddRange(requirement, request, handover, draft, issued, paid, pending,
            new Receipt { ReceiptId = Guid.NewGuid(), ReceiptCode = "RCP-1", InvoiceId = pending.InvoiceId, Amount = 1_000_000, Currency = "VND", PaymentMethod = PaymentMethod.BANK_TRANSFER,
                Status = ReceiptStatus.PENDING, CreatedAt = At, UpdatedAt = At });
        await db.SaveChangesAsync();
        return new(f, vehicle, draft, issued, paid, pending, handover, request, extra);
    }

    private static Pegaxus.Service.BookingService.Service CreateService(AppDbContext db) =>
        new(db, TestSupport.CreateMailService(), TestSupport.CreateNotifications(db));

    private static Task Cancel(AppDbContext db, Seed seed) => CreateService(db).NegotiateAsync(seed.F.Manager.AccountId, seed.F.Transport.BookingId,
        new() { Status = "CANCELLED", Reason = "Customer dropped the trip" });

    [Fact]
    public async Task Cancel_ClosesEverythingPlannedForTheTransport_AndReleasesTheVehicle()
    {
        await using var db = TestSupport.CreateDb(); var seed = await SeedAsync(db);

        await Cancel(db, seed);

        Assert.Equal(BookingStatus.CANCELLED, seed.F.Transport.Booking.Status);
        Assert.Equal(TransportStatus.CANCELLED, seed.F.Transport.Status);
        Assert.Equal(TransportPlanStatus.CANCELLED, (await db.TransportPlans.SingleAsync()).Status);
        Assert.Equal(RoutePlanStatus.CANCELLED, seed.F.Plan.Status); Assert.Contains("Cancelled: Customer dropped the trip", seed.F.Plan.RouteNote);
        Assert.Equal(ComplianceDossierStatus.CLOSED, seed.Extra.Status); Assert.NotNull(seed.Extra.ClosedAt);
        Assert.All(await db.ComplianceDossiers.ToListAsync(), item => Assert.Equal(ComplianceDossierStatus.CLOSED, item.Status));
        Assert.Equal(DocumentRequestStatus.CANCELLED, seed.Request.Status); Assert.Equal("Customer dropped the trip", seed.Request.CancellationReason);
        Assert.Equal(HandoverStatus.CANCELLED, seed.Handover.Status);
        Assert.Equal(VehicleStatus.AVAILABLE, seed.Vehicle.Status);
    }

    [Fact]
    public async Task Cancel_OnlyCancelsInvoicesNobodyHasPaidOrStartedPaying()
    {
        await using var db = TestSupport.CreateDb(); var seed = await SeedAsync(db);

        await Cancel(db, seed);

        Assert.Equal(InvoiceStatus.CANCELLED, seed.Draft.Status); Assert.Equal(InvoiceStatus.CANCELLED, seed.Issued.Status);
        Assert.Equal(InvoiceStatus.PAID, seed.Paid.Status); Assert.Equal(InvoiceStatus.ISSUED, seed.Pending.Status);
        Assert.Equal(2, await db.InvoiceStatusHistories.CountAsync(item => item.Type == InvoiceHistoryType.CANCEL));
        Assert.Equal("Booking cancelled: Customer dropped the trip", seed.Issued.CancellationReason);
    }

    [Fact]
    public async Task Cancel_KeepsTheVehicleFlagWhenAnotherTransportHoldsIt()
    {
        await using var db = TestSupport.CreateDb(); var seed = await SeedAsync(db);
        var other = new TransportPlan { TransportPlanId = Guid.NewGuid(), Transport = new() { TransportId = Guid.NewGuid(), TransportCode = "TR-OTHER", BookingId = Guid.NewGuid() },
            PlanCode = "TP-OTHER", Status = TransportPlanStatus.APPROVED };
        var held = new RoutePlanRoute { RoutePlanRouteId = Guid.NewGuid(), RoutePlan = new() { RoutePlanId = Guid.NewGuid(), TransportPlan = other, RoutePlanCode = "RP-OTHER", Status = RoutePlanStatus.CONFIRMED },
            RouteId = seed.F.Current.RouteId, PlannedDepartureAt = At, PlannedArrivalAt = At.AddHours(2) };
        db.Add(held); db.RoutePlanRouteVehicles.Add(new() { RoutePlanRoute = held, VehicleId = seed.Vehicle.VehicleId }); await db.SaveChangesAsync();

        await Cancel(db, seed);

        Assert.Equal(VehicleStatus.ASSIGNED, seed.Vehicle.Status);
    }

    [Fact]
    public async Task Cancel_TellsTheCoordinatorToo()
    {
        await using var db = TestSupport.CreateDb(); var seed = await SeedAsync(db);

        await Cancel(db, seed);

        Assert.Contains(await db.AccountNotifications.ToListAsync(), item => item.AccountId == seed.F.Coordinator.AccountId);
        Assert.Contains(await db.AccountNotifications.ToListAsync(), item => item.AccountId == seed.F.Transport.Booking.Customer.AccountId);
    }

    [Theory]
    [InlineData(TransportStatus.IN_TRANSIT)]
    [InlineData(TransportStatus.COMPLETED)]
    public async Task Cancel_IsBlockedOnceTheTransportHasDeparted_AndChangesNothing(TransportStatus status)
    {
        await using var db = TestSupport.CreateDb(); var seed = await SeedAsync(db, status);

        var error = await Assert.ThrowsAsync<ConflictException>(() => Cancel(db, seed));

        Assert.Equal("TRANSPORT_ALREADY_DEPARTED", error.ErrorCode);
        Assert.Equal(status, seed.F.Transport.Status);
        Assert.Equal(TransportPlanStatus.APPROVED, (await db.TransportPlans.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(DocumentRequestStatus.OPEN, (await db.DocumentRequests.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(InvoiceStatus.DRAFT, (await db.Invoices.AsNoTracking().SingleAsync(item => item.InvoiceCode == "INV-DRAFT")).Status);
    }
}
