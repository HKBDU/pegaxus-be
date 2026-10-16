using Pegaxus.Repository.Enum;
using Pegaxus.Service.Common;

namespace Pegaxus.Service.Test;

public class NotificationLinksTests
{
    private static readonly Guid Id = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Other = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Theory]
    [InlineData(null, "/bookings/11111111-1111-1111-1111-111111111111")]
    [InlineData(EmployeeRole.LOGISTICS_MANAGER, "/admin/transport-requests/11111111-1111-1111-1111-111111111111")]
    [InlineData(EmployeeRole.TRANSPORT_SPECIALIST, "/admin/approved-bookings/11111111-1111-1111-1111-111111111111")]
    [InlineData(EmployeeRole.FLEET_ROUTE_COORDINATOR, "/admin/approved-bookings/11111111-1111-1111-1111-111111111111")]
    [InlineData(EmployeeRole.DRIVER, null)]
    [InlineData(EmployeeRole.ESCORT, null)]
    public void Booking_GoesToThePageOfTheRecipientRole(EmployeeRole? role, string? expected) =>
        Assert.Equal(expected, NotificationLinks.Booking(role, Id));

    [Theory]
    [InlineData(null, "/compliance-dossiers/11111111-1111-1111-1111-111111111111")]
    [InlineData(EmployeeRole.LOGISTICS_MANAGER, "/admin/compliance-dossiers/11111111-1111-1111-1111-111111111111")]
    [InlineData(EmployeeRole.TRANSPORT_SPECIALIST, "/compliance-dossiers/11111111-1111-1111-1111-111111111111/details")]
    [InlineData(EmployeeRole.FLEET_ROUTE_COORDINATOR, null)]
    [InlineData(EmployeeRole.DRIVER, null)]
    public void Dossier_GoesToThePageOfTheRecipientRole(EmployeeRole? role, string? expected) =>
        Assert.Equal(expected, NotificationLinks.Dossier(role, Id));

    [Fact]
    public void Invoice_CustomerOpensItUnderTheBooking_ManagerOpensTheAdminPage()
    {
        Assert.Equal($"/bookings/{Other}/invoices/{Id}", NotificationLinks.Invoice(null, Other, Id));
        Assert.Equal($"/admin/invoices/{Id}", NotificationLinks.Invoice(EmployeeRole.LOGISTICS_MANAGER, Other, Id));
        Assert.Null(NotificationLinks.Invoice(EmployeeRole.TRANSPORT_SPECIALIST, Other, Id));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(EmployeeRole.DRIVER, null)]
    [InlineData(EmployeeRole.ESCORT, null)]
    [InlineData(EmployeeRole.TRANSPORT_SPECIALIST, "/admin/route-plans/11111111-1111-1111-1111-111111111111")]
    [InlineData(EmployeeRole.FLEET_ROUTE_COORDINATOR, "/admin/route-plans/11111111-1111-1111-1111-111111111111")]
    [InlineData(EmployeeRole.LOGISTICS_MANAGER, "/admin/route-plans/11111111-1111-1111-1111-111111111111")]
    public void RoutePlan_IsOnlyForManagersCoordinatorsAndSpecialists(EmployeeRole? role, string? expected) =>
        Assert.Equal(expected, NotificationLinks.RoutePlan(role, Id));

    [Fact]
    public void RoutePlan_ManagerCanOpenTheDecisionScreen_OthersGetThePlainPage()
    {
        Assert.Equal($"/admin/route-plans/{Id}/change-decision", NotificationLinks.RoutePlan(EmployeeRole.LOGISTICS_MANAGER, Id, "change-decision"));
        Assert.Equal($"/admin/route-plans/{Id}", NotificationLinks.RoutePlan(EmployeeRole.FLEET_ROUTE_COORDINATOR, Id, "change-decision"));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(EmployeeRole.DRIVER, null)]
    [InlineData(EmployeeRole.LOGISTICS_MANAGER, "/admin/transport-plans/11111111-1111-1111-1111-111111111111")]
    [InlineData(EmployeeRole.TRANSPORT_SPECIALIST, "/admin/transport-plans/11111111-1111-1111-1111-111111111111")]
    [InlineData(EmployeeRole.FLEET_ROUTE_COORDINATOR, "/admin/transport-plans/11111111-1111-1111-1111-111111111111")]
    public void TransportPlan_IsOnlyForPlanningStaff(EmployeeRole? role, string? expected) =>
        Assert.Equal(expected, NotificationLinks.TransportPlan(role, Id));

    [Fact]
    public void FleetCatalogPages_FollowTheRolesThatCanOpenThem()
    {
        Assert.Equal($"/vehicles/{Id}", NotificationLinks.Vehicle(EmployeeRole.FLEET_ROUTE_COORDINATOR, Id));
        Assert.Null(NotificationLinks.Vehicle(EmployeeRole.TRANSPORT_SPECIALIST, Id));
        Assert.Equal("/transport-providers", NotificationLinks.TransportProviders(EmployeeRole.LOGISTICS_MANAGER));
        Assert.Null(NotificationLinks.TransportProviders(EmployeeRole.FLEET_ROUTE_COORDINATOR));
    }
}
