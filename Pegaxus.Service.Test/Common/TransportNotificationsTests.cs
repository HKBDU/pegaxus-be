using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.NotificationService;

using static Pegaxus.Service.Common.TransportItinerary;

namespace Pegaxus.Service.Test;

public class TransportNotificationsTests
{
    private static readonly DateTime Now = TransportScenario.T0.AddDays(1);

    private static HorseHealthLog Critical(TransportScenario s, bool vet = false) => new()
    {
        HorseHealthLogId = Guid.NewGuid(), TransportId = s.Transport.TransportId, HorseId = s.Horse.HorseId,
        EmployeeId = s.Escort.Employee.EmployeeId, HealthStatus = HorseHealthStatus.CRITICAL, VeterinarianRequired = vet
    };

    private static Guid[] Accounts(IEnumerable<NotificationCreateRequest> requests) =>
        requests.Select(item => item.AccountId).Order().ToArray();

    private static Guid[] Order(params Guid[] ids) => ids.Order().ToArray();

    [Fact]
    public async Task HealthAlert_GoesToActiveManagersAndTheAssignedCoordinator()
    {
        await using var db = TestSupport.CreateDb();
        var s = await TransportScenario.CreateAsync(db);
        var secondManager = await TransportScenario.AddEmployeeAsync(db, EmployeeRole.LOGISTICS_MANAGER);
        var inactiveManager = await TransportScenario.AddEmployeeAsync(db, EmployeeRole.LOGISTICS_MANAGER);
        inactiveManager.Employee.IsActive = false;
        await TransportScenario.AddEmployeeAsync(db, EmployeeRole.FLEET_ROUTE_COORDINATOR); // not assigned to the plan
        await db.SaveChangesAsync();
        var log = Critical(s, vet: true);

        var requests = await new TransportNotifications(db).HealthAlertAsync(s.Transport.TransportId, [log], Now);

        Assert.Equal(Order(s.Manager.Account.AccountId, secondManager.Account.AccountId, s.Coordinator.Account.AccountId), Accounts(requests));
        var first = requests[0];
        Assert.Equal(NotificationType.HORSE_HEALTH, first.Type);
        Assert.Equal(NotificationPriority.URGENT, first.Priority);
        Assert.Equal("Critical horse health alert", first.Title);
        Assert.Contains("Silver Arrow", first.Message);
        Assert.Contains("veterinarian", first.Message);
        Assert.Equal(nameof(HorseHealthLog), first.ReferenceType);
        Assert.Equal(log.HorseHealthLogId, first.ReferenceId);
        Assert.Equal($"/admin/ongoing-transports/{s.Transport.TransportId}/health", first.ActionUrl);
        Assert.All(requests, item => Assert.Equal(s.Transport.TransportId, item.TransportId));
        Assert.All(requests, item => Assert.Equal(Now, item.CreatedAt));
    }

    [Fact]
    public async Task HealthAlert_ForABatch_IsASingleMessagePerRecipient()
    {
        await using var db = TestSupport.CreateDb();
        var s = await TransportScenario.CreateAsync(db);
        var requests = await new TransportNotifications(db).HealthAlertAsync(s.Transport.TransportId, [Critical(s), Critical(s)], Now);
        Assert.Equal(2, requests.Count);
        Assert.All(requests, item => Assert.Contains("2 horses", item.Message));
        Assert.Empty(await new TransportNotifications(db).HealthAlertAsync(s.Transport.TransportId, [], Now));
    }

    [Fact]
    public async Task HandoverScheduled_GoesToTheGivingAndReceivingEmployees()
    {
        await using var db = TestSupport.CreateDb();
        var s = await TransportScenario.CreateAsync(db);
        var record = s.Handover(HandoverType.TRANSFER, s.Driver.Employee, s.OtherDriver.Employee);

        var requests = await new TransportNotifications(db).HandoverScheduledAsync(record, Now);

        Assert.Equal(Order(s.Driver.Account.AccountId, s.OtherDriver.Account.AccountId), Accounts(requests));
        Assert.All(requests, item =>
        {
            Assert.Equal(NotificationType.HANDOVER, item.Type);
            Assert.Equal(NotificationPriority.NORMAL, item.Priority);
            Assert.Equal("Handover scheduled", item.Title);
            Assert.Equal($"/crew/transports/{s.Transport.TransportId}/handovers/{record.HandoverRecordId}", item.ActionUrl);
        });
    }

    [Fact]
    public async Task PickupCompleted_TellsTheCustomerAndTheBackOffice()
    {
        await using var db = TestSupport.CreateDb();
        var s = await TransportScenario.CreateAsync(db);
        var record = s.Handover(HandoverType.PICKUP, to: s.Driver.Employee, toRoute: s.First);

        var requests = await new TransportNotifications(db).HandoverCompletedAsync(record, BoundaryPosition.Start, Now);

        var customer = Assert.Single(requests, item => item.AccountId == s.CustomerAccount.AccountId);
        Assert.Equal((NotificationType.TRANSPORT, NotificationPriority.HIGH, "Your horses have been picked up"),
            (customer.Type, customer.Priority, customer.Title));
        Assert.Equal($"/bookings/{s.Booking.BookingId}/transports/{s.Transport.TransportId}", customer.ActionUrl);
        var office = requests.Where(item => item.AccountId != s.CustomerAccount.AccountId).ToList();
        Assert.Equal(Order(s.Manager.Account.AccountId, s.Coordinator.Account.AccountId), Accounts(office));
        Assert.All(office, item =>
        {
            Assert.Equal((NotificationType.TRANSPORT, NotificationPriority.NORMAL, "Transport started"), (item.Type, item.Priority, item.Title));
            Assert.Equal($"/admin/ongoing-transports/{s.Transport.TransportId}", item.ActionUrl);
        });
    }

    [Fact]
    public async Task TransferCompleted_TellsTheDriverAndEscortOfTheNextSegment()
    {
        await using var db = TestSupport.CreateDb();
        var s = await TransportScenario.CreateAsync(db);
        s.Second.DriverEmployeeId = s.OtherDriver.Employee.EmployeeId;
        s.Second.EscortEmployeeId = s.Escort.Employee.EmployeeId;
        await db.SaveChangesAsync();
        var record = s.Handover(HandoverType.TRANSFER, s.Driver.Employee, s.OtherDriver.Employee, s.Second);

        var requests = await new TransportNotifications(db).HandoverCompletedAsync(record, BoundaryPosition.Middle, Now);

        Assert.Equal(Order(s.OtherDriver.Account.AccountId, s.Escort.Account.AccountId), Accounts(requests));
        Assert.All(requests, item =>
        {
            Assert.Equal((NotificationType.ROUTE, NotificationPriority.HIGH, "You can start your segment"), (item.Type, item.Priority, item.Title));
            Assert.Equal($"/crew/transports/{s.Transport.TransportId}", item.ActionUrl);
        });
    }

    [Fact]
    public async Task DeliveryCompleted_TellsTheCustomerAndTheBackOffice()
    {
        await using var db = TestSupport.CreateDb();
        var s = await TransportScenario.CreateAsync(db);
        var record = s.Handover(HandoverType.DELIVERY, from: s.Driver.Employee);

        var requests = await new TransportNotifications(db).HandoverCompletedAsync(record, BoundaryPosition.End, Now);

        var customer = Assert.Single(requests, item => item.AccountId == s.CustomerAccount.AccountId);
        Assert.Equal((NotificationType.HANDOVER, NotificationPriority.HIGH, "Your horses have been delivered"),
            (customer.Type, customer.Priority, customer.Title));
        var office = requests.Where(item => item.AccountId != s.CustomerAccount.AccountId).ToList();
        Assert.Equal(2, office.Count);
        Assert.All(office, item =>
            Assert.Equal((NotificationType.HANDOVER, NotificationPriority.NORMAL, "Transport completed"), (item.Type, item.Priority, item.Title)));
    }

    [Fact]
    public async Task HandoverOutsideTheItinerary_NotifiesNobody()
    {
        await using var db = TestSupport.CreateDb();
        var s = await TransportScenario.CreateAsync(db);
        Assert.Empty(await new TransportNotifications(db).HandoverCompletedAsync(s.Handover(HandoverType.OTHER), null, Now));
    }

    [Fact]
    public async Task HandoverCancelled_GoesToTheCoordinatorAndBothEmployees()
    {
        await using var db = TestSupport.CreateDb();
        var s = await TransportScenario.CreateAsync(db);
        var record = s.Handover(HandoverType.TRANSFER, s.Driver.Employee, s.OtherDriver.Employee);

        var requests = await new TransportNotifications(db).HandoverCancelledAsync(record, Now);

        Assert.Equal(Order(s.Coordinator.Account.AccountId, s.Driver.Account.AccountId, s.OtherDriver.Account.AccountId), Accounts(requests));
        Assert.All(requests, item =>
        {
            Assert.Equal((NotificationType.HANDOVER, NotificationPriority.HIGH, "Handover cancelled"), (item.Type, item.Priority, item.Title));
            Assert.Equal($"/admin/ongoing-transports/{s.Transport.TransportId}", item.ActionUrl);
        });
    }

    [Fact]
    public async Task GpsSharingDisabled_GoesToTheAssignedCoordinatorOnly()
    {
        await using var db = TestSupport.CreateDb();
        var s = await TransportScenario.CreateAsync(db);

        var requests = await new TransportNotifications(db).GpsSharingDisabledAsync(s.Transport.TransportId, Now);

        var request = Assert.Single(requests);
        Assert.Equal(s.Coordinator.Account.AccountId, request.AccountId);
        Assert.Equal((NotificationType.TRANSPORT, NotificationPriority.HIGH, "Driver GPS sharing is disabled"),
            (request.Type, request.Priority, request.Title));
        Assert.Equal((nameof(Transport), s.Transport.TransportId), (request.ReferenceType, request.ReferenceId));
        Assert.Equal($"/admin/ongoing-transports/{s.Transport.TransportId}", request.ActionUrl);
    }
}
