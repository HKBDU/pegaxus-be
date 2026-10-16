using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;

namespace Pegaxus.Service.Test;

/// <summary>A booking with one transport, one confirmed primary route plan of two segments and a full crew.</summary>
internal sealed class TransportScenario
{
    internal static readonly DateTime T0 = new(2030, 1, 1, 8, 0, 0, DateTimeKind.Unspecified);

    internal Account CustomerAccount = null!, OtherCustomerAccount = null!;
    internal Customer Customer = null!;
    internal Booking Booking = null!;
    internal Horse Horse = null!;
    internal Transport Transport = null!;
    internal TransportPlan TransportPlan = null!;
    internal RoutePlan Primary = null!;
    internal RoutePlanRoute First = null!, Second = null!;
    internal (Account Account, Employee Employee) Manager, Coordinator, Specialist, Driver, Escort, OtherDriver;

    internal static async Task<TransportScenario> CreateAsync(AppDbContext db, TransportStatus status = TransportStatus.READY)
    {
        var s = new TransportScenario();
        (s.CustomerAccount, s.Customer) = await TestSupport.AddCustomerAsync(db);
        (s.OtherCustomerAccount, _) = await TestSupport.AddCustomerAsync(db);
        var (booking, horse) = await TestSupport.AddPendingBookingAsync(db, s.Customer, withHorse: true);
        s.Booking = booking;
        s.Horse = horse!;
        s.Manager = await AddEmployeeAsync(db, EmployeeRole.LOGISTICS_MANAGER);
        s.Coordinator = await AddEmployeeAsync(db, EmployeeRole.FLEET_ROUTE_COORDINATOR);
        s.Specialist = await AddEmployeeAsync(db, EmployeeRole.TRANSPORT_SPECIALIST);
        s.Driver = await AddEmployeeAsync(db, EmployeeRole.DRIVER);
        s.Escort = await AddEmployeeAsync(db, EmployeeRole.ESCORT);
        s.OtherDriver = await AddEmployeeAsync(db, EmployeeRole.DRIVER);

        s.Transport = new() { TransportId = Guid.NewGuid(), TransportCode = "TR-" + Guid.NewGuid().ToString("N"), BookingId = booking.BookingId, Status = status, CreatedAt = T0, UpdatedAt = T0 };
        s.TransportPlan = new() { TransportPlanId = Guid.NewGuid(), TransportId = s.Transport.TransportId, PlanCode = "TP-" + Guid.NewGuid().ToString("N"), Status = TransportPlanStatus.APPROVED, CreatedAt = T0, UpdatedAt = T0 };
        s.Primary = new() { RoutePlanId = Guid.NewGuid(), TransportPlanId = s.TransportPlan.TransportPlanId, RoutePlanCode = "RP-" + Guid.NewGuid().ToString("N"), Status = RoutePlanStatus.CONFIRMED, CreatedAt = T0, UpdatedAt = T0 };
        s.First = Segment(s.Primary, 1, s.Driver.Employee, s.Escort.Employee);
        s.Second = Segment(s.Primary, 2, s.Driver.Employee, s.Escort.Employee);
        db.AddRange(s.Transport, s.TransportPlan, s.Primary, s.First, s.Second,
            new EmployeeTransportPlan { EmployeeId = s.Coordinator.Employee.EmployeeId, TransportPlanId = s.TransportPlan.TransportPlanId, Responsibility = "FLEET_ROUTE_COORDINATOR", AssignedAt = T0, IsPrimary = true });
        await db.SaveChangesAsync();
        return s;
    }

    internal static async Task<(Account Account, Employee Employee)> AddEmployeeAsync(AppDbContext db, EmployeeRole role)
    {
        var (account, employee) = await TestSupport.AddLogisticsManagerAsync(db);
        employee.Role = role;
        await db.SaveChangesAsync();
        return (account, employee);
    }

    internal static RoutePlanRoute Segment(RoutePlan plan, int sequenceNo, Employee? driver, Employee? escort) => new()
    {
        RoutePlanRouteId = Guid.NewGuid(), RoutePlanId = plan.RoutePlanId, RouteId = Guid.NewGuid(), SequenceNo = sequenceNo,
        DriverEmployeeId = driver?.EmployeeId, EscortEmployeeId = escort?.EmployeeId,
        PlannedDepartureAt = T0.AddHours(sequenceNo), PlannedArrivalAt = T0.AddHours(sequenceNo + 1), CreatedAt = T0, UpdatedAt = T0
    };

    internal HandoverRecord Handover(HandoverType type, Employee? from = null, Employee? to = null, RoutePlanRoute? toRoute = null) => new()
    {
        HandoverRecordId = Guid.NewGuid(), TransportId = Transport.TransportId, HandoverCode = "HO-20300101-0001", SequenceNo = 1,
        HandoverType = type, FromEmployeeId = from?.EmployeeId, ToEmployeeId = to?.EmployeeId, ToRoutePlanRouteId = toRoute?.RoutePlanRouteId
    };
}
