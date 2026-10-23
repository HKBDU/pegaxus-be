using Pegaxus.Repository.Enum;

namespace Pegaxus.Service.Common;

/// <summary>
/// The front-end page a notification opens, per recipient. <c>role</c> is the recipient's employee role, or
/// <c>null</c> for the booking's customer. A recipient with no page for the item gets <c>null</c> (the inbox only).
/// </summary>
public static class NotificationLinks
{
    public static string? Booking(EmployeeRole? role, Guid bookingId) => role switch
    {
        null => $"/bookings/{bookingId}",
        EmployeeRole.LOGISTICS_MANAGER => $"/admin/transport-requests/{bookingId}",
        EmployeeRole.TRANSPORT_SPECIALIST or EmployeeRole.FLEET_ROUTE_COORDINATOR => $"/admin/approved-bookings/{bookingId}",
        _ => null
    };

    public static string? Dossier(EmployeeRole? role, Guid dossierId) => role switch
    {
        null => $"/compliance-dossiers/{dossierId}",
        EmployeeRole.LOGISTICS_MANAGER => $"/admin/compliance-dossiers/{dossierId}",
        EmployeeRole.TRANSPORT_SPECIALIST => $"/compliance-dossiers/{dossierId}/details",
        _ => null
    };

    public static string? Invoice(EmployeeRole? role, Guid bookingId, Guid invoiceId) => role switch
    {
        null => $"/bookings/{bookingId}/invoices/{invoiceId}",
        EmployeeRole.LOGISTICS_MANAGER => $"/admin/invoices/{invoiceId}",
        _ => null
    };

    public static string? Claim(EmployeeRole? role, Guid bookingId, Guid claimId) => role switch
    {
        null => $"/bookings/{bookingId}?claimId={claimId}",
        EmployeeRole.LOGISTICS_MANAGER => $"/admin/claims/{claimId}",
        _ => null
    };

    /// <param name="view">Optional manager screen of the route plan: <c>change-decision</c> or <c>return-activation</c>.</param>
    public static string? RoutePlan(EmployeeRole? role, Guid routePlanId, string? view = null) => role switch
    {
        EmployeeRole.LOGISTICS_MANAGER => $"/admin/route-plans/{routePlanId}{(view is null ? "" : "/" + view)}",
        EmployeeRole.FLEET_ROUTE_COORDINATOR or EmployeeRole.TRANSPORT_SPECIALIST => $"/admin/route-plans/{routePlanId}",
        _ => null
    };

    public static string? TransportPlan(EmployeeRole? role, Guid transportPlanId) => role switch
    {
        EmployeeRole.LOGISTICS_MANAGER or EmployeeRole.FLEET_ROUTE_COORDINATOR or EmployeeRole.TRANSPORT_SPECIALIST =>
            $"/admin/transport-plans/{transportPlanId}",
        _ => null
    };

    public static string? Vehicle(EmployeeRole? role, Guid vehicleId) => role switch
    {
        EmployeeRole.LOGISTICS_MANAGER or EmployeeRole.FLEET_ROUTE_COORDINATOR => $"/vehicles/{vehicleId}",
        _ => null
    };

    public static string? TransportProviders(EmployeeRole? role) => role == EmployeeRole.LOGISTICS_MANAGER ? "/transport-providers" : null;
}
