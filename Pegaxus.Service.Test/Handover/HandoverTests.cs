using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.HandoverService.Validator;
using Pegaxus.Service.NotificationService;

using Handovers = Pegaxus.Service.HandoverService;

namespace Pegaxus.Service.Test;

public class HandoverTests
{
    [Fact]
    public async Task List_FiltersSortsAndPaginatesFromZero_RestrictingCustomersToTheirOwnPickupAndDelivery()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        var list = await f.Service.GetListAsync(f.TransportId, new() { PageSize = 1 }, f.Manager.AccountId);
        Assert.Equal(f.Clock.GetUtcNow(), list.ServerTime); Assert.Equal(0, list.PageIndex);
        Assert.Equal(3, list.TotalItems); Assert.Equal(3, list.TotalPages); Assert.Equal(f.Pickup.HandoverRecordId, Assert.Single(list.Items).HandoverRecordId);
        list = await f.Service.GetListAsync(f.TransportId, new() { PageIndex = 1, PageSize = 1, SortBy = "sequenceNo:desc" }, f.Manager.AccountId);
        Assert.Equal(f.Transfer.HandoverRecordId, Assert.Single(list.Items).HandoverRecordId);
        list = await f.Service.GetListAsync(f.TransportId, new() { HandoverType = "pickup, DELIVERY", Status = "planned,COMPLETED" }, f.Manager.AccountId);
        Assert.Equal(new[] { "PICKUP", "DELIVERY" }, list.Items.Select(item => item.HandoverType));
        Assert.Equal(2, (await f.Service.GetListAsync(f.TransportId, new(), f.CustomerAccountId)).TotalItems);
        list = await f.Service.GetListAsync(f.TransportId, new() { HandoverType = "TRANSFER" }, f.CustomerAccountId);
        Assert.Empty(list.Items); Assert.Equal(0, list.TotalPages);
        Assert.Empty((await f.Service.GetListAsync(f.TransportId, new() { PageIndex = 10 }, f.Manager.AccountId)).Items);
        var (stranger, _) = await TestSupport.AddCustomerAsync(db);
        Assert.Equal("TRANSPORT_NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(() => f.Service.GetListAsync(f.TransportId, new(), stranger.AccountId))).ErrorCode);
        await Assert.ThrowsAsync<NotFoundException>(() => f.Service.GetListAsync(Guid.NewGuid(), new(), f.Manager.AccountId));
    }

    [Fact]
    public async Task Detail_PreservesSnapshotsNotesSignaturesAndTimes_HidesEmployeePhonesAndForeignRecordsFromCustomers()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        f.Pickup.Status = HandoverStatus.COMPLETED; f.Pickup.CompletedAt = f.Pickup.ActualHandoverAt = Fixture.Now;
        f.Pickup.SenderSignatureUrl = "https://res.cloudinary.com/test/image/authenticated/v1/evidence/sender.png"; f.Pickup.ReceiverSignatureUrl = "https://res.cloudinary.com/test/image/authenticated/v1/evidence/receiver.png";
        await db.SaveChangesAsync();
        var customer = await f.Service.GetAsync(f.Pickup.HandoverRecordId, f.CustomerAccountId);
        Assert.Equal(f.TransportId, customer.Transport.TransportId); Assert.Equal(f.Route.TransportPlan.Transport.BookingId, customer.BookingId);
        Assert.Equal("CUSTOMER", customer.From.Type); Assert.Equal(f.CustomerId, customer.From.CustomerId);
        Assert.Equal("customer-phone", customer.From.ContactPhone); Assert.Null(customer.To.ContactPhone); Assert.Null(customer.To.TransportProviderId);
        Assert.Equal("Healthy horses", customer.HorseConditionNote); Assert.Equal("Documents checked", customer.DocumentHandoverNote);
        Assert.Equal("Equipment checked", customer.EquipmentHandoverNote); Assert.Equal("Minor discrepancy", customer.DiscrepancyNote);
        Assert.Equal(f.Pickup.SenderSignatureUrl + "?signed=1", customer.SenderSignatureUrl); Assert.Equal(customer.CompletedAt, customer.ActualHandoverAt);
        Assert.Null(customer.FromSegment); Assert.Equal(1, customer.ToSegment!.DisplayOrder); Assert.Empty(customer.AvailableActions);
        Assert.Equal("employee-phone", (await f.Service.GetAsync(f.Pickup.HandoverRecordId, f.Manager.AccountId)).To.ContactPhone);
        var delivery = await f.Service.GetAsync(f.Delivery.HandoverRecordId, f.CustomerAccountId);
        Assert.Null(delivery.From.ContactPhone); Assert.Equal("customer-phone", delivery.To.ContactPhone);
        Assert.Equal(2, delivery.FromSegment!.DisplayOrder); Assert.Null(delivery.ToSegment);
        Assert.Equal("HANDOVER_RECORD_NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(() => f.Service.GetAsync(f.Transfer.HandoverRecordId, f.CustomerAccountId))).ErrorCode);
        var (stranger, _) = await TestSupport.AddCustomerAsync(db);
        await Assert.ThrowsAsync<NotFoundException>(() => f.Service.GetAsync(f.Pickup.HandoverRecordId, stranger.AccountId));
    }

    [Theory]
    [InlineData(EmployeeRole.LOGISTICS_MANAGER, "START,CANCEL")]
    [InlineData(EmployeeRole.FLEET_ROUTE_COORDINATOR, "START,CANCEL")]
    [InlineData(EmployeeRole.TRANSPORT_SPECIALIST, "")]
    [InlineData(EmployeeRole.DRIVER, "START")]
    [InlineData(EmployeeRole.ESCORT, "START")]
    public async Task Actions_ReflectActualRoleBoundaryAssignmentAndTerminalState(EmployeeRole role, string expected)
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        var actor = role switch { EmployeeRole.DRIVER => f.Driver, EmployeeRole.ESCORT => f.Escort,
            EmployeeRole.LOGISTICS_MANAGER => f.Manager, EmployeeRole.FLEET_ROUTE_COORDINATOR => f.Coordinator,
            _ => await PersonnelAssignmentTests.EmployeeAsync(db, role) };
        Assert.Equal(expected, string.Join(',', (await f.Service.GetAsync(f.Pickup.HandoverRecordId, actor.AccountId)).AvailableActions));
        f.Pickup.Status = HandoverStatus.IN_PROGRESS; await db.SaveChangesAsync();
        Assert.Equal(expected.Replace("START", "COMPLETE"), string.Join(',', (await f.Service.GetAsync(f.Pickup.HandoverRecordId, actor.AccountId)).AvailableActions));
        foreach (var status in new[] { HandoverStatus.COMPLETED, HandoverStatus.CANCELLED })
        {
            f.Pickup.Status = status; await db.SaveChangesAsync();
            Assert.Empty((await f.Service.GetAsync(f.Pickup.HandoverRecordId, actor.AccountId)).AvailableActions);
        }
        actor.IsActive = false; await db.SaveChangesAsync();
        await Assert.ThrowsAsync<UnauthorizedException>(() => f.Service.GetAsync(f.Pickup.HandoverRecordId, actor.AccountId));
    }

    [Theory]
    [InlineData(HandoverStatus.PLANNED)]
    [InlineData(HandoverStatus.IN_PROGRESS)]
    [InlineData(HandoverStatus.COMPLETED)]
    [InlineData(HandoverStatus.CANCELLED)]
    public async Task Start_OnlyTransitionsPlannedAndNeverStartsJourney(HandoverStatus status)
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db); f.Pickup.Status = status; await db.SaveChangesAsync();
        if (status == HandoverStatus.PLANNED)
        {
            var started = await f.Service.StartAsync(f.Pickup.HandoverRecordId, f.Escort.AccountId);
            Assert.Equal("IN_PROGRESS", started.Status); Assert.Equal(f.Clock.GetUtcNow(), started.UpdatedAt);
            Assert.Equal(new[] { "COMPLETE" }, started.AvailableActions); Assert.Null(started.ActualHandoverAt); Assert.Null(started.CompletedAt);
        }
        else Assert.Equal("INVALID_HANDOVER_STATUS", (await Assert.ThrowsAsync<ConflictException>(() => f.Service.StartAsync(f.Pickup.HandoverRecordId, f.Driver.AccountId))).ErrorCode);
        Assert.Equal(TransportStatus.READY, f.Route.TransportPlan.Transport.Status); Assert.Equal(RoutePlanStatus.CONFIRMED, f.Route.Status);
        Assert.Null(f.First.ActualDepartureAt); Assert.Null(f.Route.TransportPlan.Transport.ActualDepartureAt);
        Assert.Empty(db.Notifications); Assert.Empty(f.Realtime.Messages);
    }

    [Fact]
    public async Task Crew_MayReadWholeAssignedTransportButMayOnlyStartLinkedSegments()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        Assert.Equal(3, (await f.Service.GetListAsync(f.TransportId, new(), f.Driver.AccountId)).TotalItems);
        Assert.Empty((await f.Service.GetAsync(f.Delivery.HandoverRecordId, f.Driver.AccountId)).AvailableActions);
        await Assert.ThrowsAsync<ForbiddenException>(() => f.Service.StartAsync(f.Delivery.HandoverRecordId, f.Driver.AccountId));
        var stranger = await PersonnelAssignmentTests.EmployeeAsync(db, EmployeeRole.DRIVER);
        await Assert.ThrowsAsync<ForbiddenException>(() => f.Service.GetListAsync(f.TransportId, new(), stranger.AccountId));
        await Assert.ThrowsAsync<ForbiddenException>(() => f.Service.GetAsync(f.Pickup.HandoverRecordId, stranger.AccountId));
        await Assert.ThrowsAsync<ForbiddenException>(() => f.Service.CancelAsync(f.Pickup.HandoverRecordId, f.Cancel(f.Pickup), f.Driver.AccountId));
    }

    [Theory]
    [InlineData("superseded", false)]
    [InlineData("executed", true)]
    [InlineData("outsourced", false)]
    [InlineData("draft", false)]
    public async Task CrewAssignment_IgnoresUnexecutedSupersededOutsourcedAndDraftSegments(string scenario, bool allowed)
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        if (scenario is "superseded" or "executed") f.First.SupersededAt = Fixture.Now;
        if (scenario == "executed") f.First.ActualDepartureAt = Fixture.Now.AddHours(-1);
        if (scenario == "outsourced") { f.First.TransportProvider = PersonnelAssignmentTests.Provider(); db.Add(f.First.TransportProvider); }
        if (scenario == "draft") f.Route.Status = RoutePlanStatus.DRAFT;
        await db.SaveChangesAsync();
        if (allowed) await f.Service.StartAsync(f.Pickup.HandoverRecordId, f.Driver.AccountId);
        else
        {
            await Assert.ThrowsAsync<ForbiddenException>(() => f.Service.GetAsync(f.Pickup.HandoverRecordId, f.Driver.AccountId));
            await Assert.ThrowsAsync<ForbiddenException>(() => f.Service.StartAsync(f.Pickup.HandoverRecordId, f.Driver.AccountId));
        }
    }

    [Theory]
    [InlineData(HandoverStatus.PLANNED)]
    [InlineData(HandoverStatus.IN_PROGRESS)]
    [InlineData(HandoverStatus.COMPLETED)]
    [InlineData(HandoverStatus.CANCELLED)]
    public async Task Cancel_OnlyTransitionsOpenRecordsAndQueuesDistinctActivePartiesAndAssignedCoordinators(HandoverStatus status)
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db); f.Transfer.Status = status;
        var unassigned = await PersonnelAssignmentTests.EmployeeAsync(db, EmployeeRole.FLEET_ROUTE_COORDINATOR);
        var inactive = await PersonnelAssignmentTests.EmployeeAsync(db, EmployeeRole.FLEET_ROUTE_COORDINATOR); inactive.Account.Status = AccountStatus.INACTIVE;
        db.Add(new EmployeeTransportPlan { Employee = inactive, TransportPlan = f.Route.TransportPlan, Responsibility = "FLEET_ROUTE_COORDINATOR" });
        db.Add(new EmployeeTransportPlan { Employee = f.Driver, TransportPlan = f.Route.TransportPlan, Responsibility = "DRIVER" });
        f.First.ActualDepartureAt = Fixture.Now.AddHours(-1); f.First.ActualArrivalAt = Fixture.Now.AddMinutes(-30);
        await db.SaveChangesAsync(); var version = f.Transfer.UpdatedAt;
        if (status is HandoverStatus.PLANNED or HandoverStatus.IN_PROGRESS)
        {
            var cancelled = await f.Service.CancelAsync(f.Transfer.HandoverRecordId, f.Cancel(f.Transfer), f.Coordinator.AccountId);
            Assert.Equal("CANCELLED", cancelled.Status); Assert.Empty(cancelled.AvailableActions);
            Assert.Equal("Primary road is closed", cancelled.CancellationReason); Assert.Equal(cancelled.UpdatedAt, cancelled.CancelledAt);
            Assert.Equal(new[] { f.Driver.AccountId, f.Receiver.AccountId, f.Coordinator.AccountId }.Order(),
                (await db.AccountNotifications.Select(item => item.AccountId).ToArrayAsync()).Order());
            Assert.Equal(3, f.Realtime.Messages.Count);
            Assert.All(await db.Notifications.ToArrayAsync(), notification =>
            {
                Assert.Equal(NotificationType.HANDOVER, notification.NotificationType); Assert.Equal(NotificationPriority.HIGH, notification.Priority);
                Assert.Equal("HandoverRecord", notification.ReferenceType); Assert.Equal(f.Transfer.HandoverRecordId, notification.ReferenceId);
                Assert.Equal(f.TransportId, notification.TransportId); Assert.Equal($"/admin/ongoing-transports/{f.TransportId}", notification.ActionUrl);
            });
        }
        else
        {
            Assert.Equal("INVALID_HANDOVER_STATUS", (await Assert.ThrowsAsync<ConflictException>(() => f.Service.CancelAsync(f.Transfer.HandoverRecordId, f.Cancel(f.Transfer), f.Manager.AccountId))).ErrorCode);
            Assert.Equal(version, f.Transfer.UpdatedAt); Assert.Empty(db.Notifications);
        }
        Assert.Equal(Fixture.Now.AddHours(-1), f.First.ActualDepartureAt); Assert.Equal(Fixture.Now.AddMinutes(-30), f.First.ActualArrivalAt);
        Assert.Null(f.Second.ActualDepartureAt); Assert.Equal(TransportStatus.READY, f.Route.TransportPlan.Transport.Status);
    }

    [Fact]
    public async Task Cancel_UsesUtcMicrosecondVersionAndDeduplicatesPartyWhoIsAlsoCoordinator()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        f.Pickup.ToEmployeeId = f.Coordinator.EmployeeId; await db.SaveChangesAsync();
        var stale = f.Cancel(f.Pickup); stale.LastUpdatedAt = stale.LastUpdatedAt!.Value.AddSeconds(-1);
        Assert.Equal("HANDOVER_MODIFIED", (await Assert.ThrowsAsync<ConflictException>(() => f.Service.CancelAsync(f.Pickup.HandoverRecordId, stale, f.Manager.AccountId))).ErrorCode);
        Assert.Empty(db.Notifications);
        var valid = f.Cancel(f.Pickup); valid.LastUpdatedAt = valid.LastUpdatedAt!.Value.ToOffset(TimeSpan.FromHours(7)).AddTicks(9);
        await f.Service.CancelAsync(f.Pickup.HandoverRecordId, valid, f.Manager.AccountId);
        Assert.Single(db.Notifications); Assert.Equal(f.Coordinator.AccountId, (await db.AccountNotifications.SingleAsync()).AccountId);
    }

    [Theory]
    [InlineData("missingVersion")]
    [InlineData("defaultVersion")]
    [InlineData("missingReason")]
    [InlineData("shortReason")]
    public async Task Cancel_ValidatesBeforeMutating(string scenario)
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db); var request = f.Cancel(f.Pickup);
        if (scenario == "missingVersion") request.LastUpdatedAt = null;
        if (scenario == "defaultVersion") request.LastUpdatedAt = default(DateTimeOffset);
        if (scenario == "missingReason") request.CancellationReason = null;
        if (scenario == "shortReason") request.CancellationReason = "   123456789   ";
        Assert.Equal("VALIDATION_ERROR", (await Assert.ThrowsAsync<BadRequestException>(() => f.Service.CancelAsync(f.Pickup.HandoverRecordId, request, f.Manager.AccountId))).ErrorCode);
        Assert.Equal(HandoverStatus.PLANNED, f.Pickup.Status); Assert.Empty(db.Notifications);
    }

    [Theory]
    [InlineData(TransportStatus.CREATED, false)]
    [InlineData(TransportStatus.PLANNING, false)]
    [InlineData(TransportStatus.READY, true)]
    [InlineData(TransportStatus.IN_TRANSIT, true)]
    [InlineData(TransportStatus.COMPLETED, true)]
    [InlineData(TransportStatus.CANCELLED, false)]
    public async Task List_RestrictsTransportLifecycleAndRequiresEligibleRoutePlan(TransportStatus status, bool allowed)
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db); f.Route.TransportPlan.Transport.Status = status; await db.SaveChangesAsync();
        if (allowed) await f.Service.GetListAsync(f.TransportId, new(), f.Manager.AccountId);
        else Assert.Equal("INVALID_TRANSPORT_STATUS", (await Assert.ThrowsAsync<ConflictException>(() => f.Service.GetListAsync(f.TransportId, new(), f.CustomerAccountId))).ErrorCode);
        f.Route.TransportPlan.Transport.Status = TransportStatus.READY; f.Route.Status = RoutePlanStatus.DRAFT; await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ConflictException>(() => f.Service.GetListAsync(f.TransportId, new(), f.Manager.AccountId));
    }

    [Fact]
    public async Task Detail_UsesEffectiveDisplayOrderAcrossActiveMixedBackupBoundary()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        f.First.ActualDepartureAt = Fixture.Now.AddHours(-1); f.Second.SupersededAt = Fixture.Now;
        var backup = new RoutePlan { RoutePlanId = Guid.NewGuid(), RoutePlanCode = "BACKUP", TransportPlan = f.Route.TransportPlan,
            PrimaryRoutePlanId = f.Route.RoutePlanId, Status = RoutePlanStatus.ACTIVE, UpdatedAt = Fixture.Now };
        var copied = PersonnelAssignmentTests.Segment(backup, 1);
        var branch = PersonnelAssignmentTests.Segment(backup, 3); branch.RouteType = RouteType.BACKUP; branch.DriverEmployeeId = f.Receiver.EmployeeId;
        var tail = PersonnelAssignmentTests.Segment(backup, 4);
        db.AddRange(copied, branch, tail); f.Transfer.ToRoutePlanRoute = branch; await db.SaveChangesAsync();
        var detail = await f.Service.GetAsync(f.Transfer.HandoverRecordId, f.Manager.AccountId);
        Assert.Equal(1, detail.FromSegment!.DisplayOrder); Assert.Equal(2, detail.ToSegment!.DisplayOrder);
        Assert.Equal(branch.RoutePlanRouteId, detail.ToSegment.RoutePlanRouteId);
    }

    [Fact]
    public async Task Detail_KeepsLinkedHistoricalSegmentsWhenTheirRoutePlanIsCancelled()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        f.Route.Status = RoutePlanStatus.CANCELLED; f.Pickup.Status = HandoverStatus.CANCELLED; await db.SaveChangesAsync();
        var detail = await f.Service.GetAsync(f.Pickup.HandoverRecordId, f.Manager.AccountId);
        Assert.Equal(f.First.RoutePlanRouteId, detail.ToSegment!.RoutePlanRouteId); Assert.Equal(1, detail.ToSegment.DisplayOrder);
        Assert.Empty(detail.AvailableActions);
    }

    [Fact]
    public void ListValidator_RejectsInvalidEnumNamesNumericEnumsSortsAndPaging()
    {
        var validator = new ListRequestValidator();
        foreach (var query in new Handovers.Request.ListRequest[] { new() { Status = "1" }, new() { Status = "PLANNED," }, new() { HandoverType = "UNKNOWN" },
                     new() { SortBy = "createdAt:desc" }, new() { SortBy = null! }, new() { PageIndex = -1 }, new() { PageSize = 0 }, new() { PageSize = 101 }, new() { PageIndex = int.MaxValue } })
            Assert.False(validator.Validate(query).IsValid);
        Assert.True(validator.Validate(new Handovers.Request.ListRequest { HandoverType = "pickup,delivery", Status = "PLANNED,CANCELLED" }).IsValid);
    }

    internal sealed class Fixture(AppDbContext db)
    {
        internal static readonly DateTime Now = new(2026, 10, 16, 12, 0, 0, DateTimeKind.Unspecified);
        internal Clock Clock { get; } = new();
        internal RecordingRealtime Realtime { get; } = new();
        internal HandoverCompletionTests.SignatureMedia Media { get; } = new();
        internal required Employee Manager { get; init; }
        internal required Employee Coordinator { get; init; }
        internal required Employee Driver { get; init; }
        internal required Employee Escort { get; init; }
        internal required Employee Receiver { get; init; }
        internal required Guid CustomerAccountId { get; init; }
        internal required Guid CustomerId { get; init; }
        internal required RoutePlan Route { get; init; }
        internal required RoutePlanRoute First { get; init; }
        internal required RoutePlanRoute Second { get; init; }
        internal required HandoverRecord Pickup { get; init; }
        internal required HandoverRecord Transfer { get; init; }
        internal required HandoverRecord Delivery { get; init; }
        internal Guid TransportId => Route.TransportPlan.TransportId;
        internal Handovers.Service Service => CreateService(db);
        internal Handovers.Service CreateService(AppDbContext context) => new(context, Clock, new ListRequestValidator(), new CancelRequestValidator(), new CreateRequestValidator(),
            new Pegaxus.Service.NotificationService.Service(context, Realtime), new CompleteRequestValidator(), Media,
            Microsoft.Extensions.Options.Options.Create(new Pegaxus.Service.LocationTrackingService.TrackingOptions()), Microsoft.Extensions.Logging.Abstractions.NullLogger<Handovers.Service>.Instance);
        internal Handovers.Request.CancelRequest Cancel(HandoverRecord item) => new() { LastUpdatedAt = DbTime.AsUtc(item.UpdatedAt), CancellationReason = "  Primary road is closed  " };

        /// <summary>Records start IN_PROGRESS: completing a scheduled (PLANNED) record is refused, so completion tests begin from a started one.</summary>
        internal static async Task<Fixture> CreateStartedAsync(AppDbContext db)
        {
            var f = await CreateAsync(db);
            foreach (var record in new[] { f.Pickup, f.Transfer, f.Delivery }) record.Status = HandoverStatus.IN_PROGRESS;
            await db.SaveChangesAsync();
            return f;
        }

        internal static async Task<Fixture> CreateAsync(AppDbContext db)
        {
            var manager = await PersonnelAssignmentTests.EmployeeAsync(db, EmployeeRole.LOGISTICS_MANAGER);
            var coordinator = await PersonnelAssignmentTests.EmployeeAsync(db, EmployeeRole.FLEET_ROUTE_COORDINATOR);
            var driver = await PersonnelAssignmentTests.EmployeeAsync(db, EmployeeRole.DRIVER);
            var escort = await PersonnelAssignmentTests.EmployeeAsync(db, EmployeeRole.ESCORT);
            var receiver = await PersonnelAssignmentTests.EmployeeAsync(db, EmployeeRole.DRIVER);
            var (route, first) = await PersonnelAssignmentTests.RouteAsync(db); route.TransportPlan.Transport.Status = TransportStatus.READY;
            first.DriverEmployeeId = driver.EmployeeId; first.EscortEmployeeId = escort.EmployeeId;
            var second = PersonnelAssignmentTests.Segment(route, 2); second.DriverEmployeeId = receiver.EmployeeId; db.Add(second);
            db.Add(new EmployeeTransportPlan { Employee = coordinator, TransportPlan = route.TransportPlan, Responsibility = "FLEET_ROUTE_COORDINATOR" });
            var customer = await db.Bookings.Where(booking => booking.BookingId == route.TransportPlan.Transport.BookingId)
                .Select(booking => new { booking.CustomerId, booking.Customer.AccountId }).SingleAsync();
            var records = new[] { HandoverType.PICKUP, HandoverType.TRANSFER, HandoverType.DELIVERY }.Select((type, index) => new HandoverRecord
            {
                HandoverRecordId = Guid.NewGuid(), Transport = route.TransportPlan.Transport, SequenceNo = index + 1,
                HandoverCode = "HO-" + Guid.NewGuid().ToString("N"), HandoverType = type, Status = HandoverStatus.PLANNED,
                FromType = type == HandoverType.PICKUP ? HandoverPartyType.CUSTOMER : HandoverPartyType.EMPLOYEE,
                FromCustomerId = type == HandoverType.PICKUP ? customer.CustomerId : null,
                FromEmployeeId = type == HandoverType.PICKUP ? null : type == HandoverType.DELIVERY ? receiver.EmployeeId : driver.EmployeeId,
                ToType = type == HandoverType.DELIVERY ? HandoverPartyType.CUSTOMER : HandoverPartyType.EMPLOYEE,
                ToCustomerId = type == HandoverType.DELIVERY ? customer.CustomerId : null,
                ToEmployeeId = type == HandoverType.DELIVERY ? null : type == HandoverType.PICKUP ? driver.EmployeeId : receiver.EmployeeId,
                FromRoutePlanRoute = type == HandoverType.PICKUP ? null : type == HandoverType.DELIVERY ? second : first,
                ToRoutePlanRoute = type == HandoverType.DELIVERY ? null : type == HandoverType.PICKUP ? first : second,
                FromPartyName = "Sender snapshot", ToPartyName = "Receiver snapshot", FromContactName = "Sender", ToContactName = "Receiver",
                FromContactPhone = type == HandoverType.PICKUP ? "customer-phone" : "employee-phone",
                ToContactPhone = type == HandoverType.DELIVERY ? "customer-phone" : "employee-phone",
                LocationName = "Stable snapshot", Address = "Snapshot address", Country = "FR", HorseConditionNote = "Healthy horses",
                DocumentHandoverNote = "Documents checked", EquipmentHandoverNote = "Equipment checked", DiscrepancyNote = "Minor discrepancy",
                PlannedHandoverAt = Now.AddHours(1), CreatedAt = Now.AddHours(-1), UpdatedAt = Now.AddMinutes(-1)
            }).ToArray();
            db.AddRange(records); await db.SaveChangesAsync();
            return new(db) { Manager = manager, Coordinator = coordinator, Driver = driver, Escort = escort, Receiver = receiver,
                CustomerAccountId = customer.AccountId, CustomerId = customer.CustomerId, Route = route, First = first, Second = second,
                Pickup = records[0], Transfer = records[1], Delivery = records[2] };
        }
    }

    internal sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = DbTime.AsUtc(Fixture.Now);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    internal sealed class RecordingRealtime : INotificationRealtimeService
    {
        internal List<NotificationRealtimeDto> Messages { get; } = [];
        public Task SendToAccountAsync(Guid accountId, NotificationRealtimeDto notification, CancellationToken cancellationToken = default)
        { Messages.Add(notification); return Task.CompletedTask; }
    }
}
