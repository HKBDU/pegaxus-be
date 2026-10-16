using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Repository.Extensions;

using static Pegaxus.Service.Common.TransportItinerary;

namespace Pegaxus.Service.NotificationService;

/// <summary>
/// Builds the persistent notifications that the Flow 4 APIs write themselves. The caller queues the returned
/// requests with <see cref="INotificationService.Queue"/> in the same transaction as the data change.
/// </summary>
public sealed class TransportNotifications(AppDbContext db)
{
    public async Task<IReadOnlyList<NotificationCreateRequest>> HealthAlertAsync(
        Guid transportId, IReadOnlyList<HorseHealthLog> logs, DateTime now, CancellationToken cancellationToken = default)
    {
        if (logs.Count == 0) return [];
        var first = logs[0];
        var message = logs.Count == 1
            ? $"{await HorseNameAsync(first.HorseId, cancellationToken)} is {first.HealthStatus}{(first.VeterinarianRequired ? " and needs a veterinarian" : "")}."
            : $"{logs.Count} horses need urgent attention.";
        var recipients = (await ManagersAsync(cancellationToken)).Concat(await CoordinatorsAsync(transportId, cancellationToken));
        return Build(recipients, transportId, NotificationType.HORSE_HEALTH, NotificationPriority.URGENT,
            "Critical horse health alert", message, nameof(HorseHealthLog), first.HorseHealthLogId,
            $"/admin/ongoing-transports/{transportId}/health", now);
    }

    /// <summary>A manager or coordinator scheduled a handover: tell the giving and receiving employees.</summary>
    public async Task<IReadOnlyList<NotificationCreateRequest>> HandoverScheduledAsync(
        HandoverRecord record, DateTime now, CancellationToken cancellationToken = default) =>
        Build(await EmployeeAccountsAsync([record.FromEmployeeId, record.ToEmployeeId], cancellationToken), record.TransportId,
            NotificationType.HANDOVER, NotificationPriority.NORMAL, "Handover scheduled",
            $"Handover {record.HandoverCode} is scheduled.", nameof(HandoverRecord), record.HandoverRecordId,
            $"/crew/transports/{record.TransportId}/handovers/{record.HandoverRecordId}", now);

    /// <param name="position">Where the handover sits in the itinerary; the effects depend on the position, not on the type name.</param>
    public async Task<IReadOnlyList<NotificationCreateRequest>> HandoverCompletedAsync(
        HandoverRecord record, BoundaryPosition? position, DateTime now, CancellationToken cancellationToken = default)
    {
        if (position is null) return [];
        var transportId = record.TransportId;
        var customerUrl = await CustomerUrlAsync(transportId, cancellationToken);
        var customer = await CustomerAccountAsync(transportId, cancellationToken);
        var backOffice = (await ManagersAsync(cancellationToken)).Concat(await CoordinatorsAsync(transportId, cancellationToken)).ToList();
        var adminUrl = $"/admin/ongoing-transports/{transportId}";
        var reference = (nameof(HandoverRecord), record.HandoverRecordId);

        switch (position)
        {
            case BoundaryPosition.Start:
                return Build([customer], transportId, NotificationType.TRANSPORT, NotificationPriority.HIGH,
                        "Your horses have been picked up", $"Handover {record.HandoverCode} is complete and the transport has started.",
                        reference.Item1, reference.Item2, customerUrl, now)
                    .Concat(Build(backOffice, transportId, NotificationType.TRANSPORT, NotificationPriority.NORMAL,
                        "Transport started", $"Handover {record.HandoverCode} is complete.", reference.Item1, reference.Item2, adminUrl, now))
                    .ToList();
            case BoundaryPosition.Middle:
                return Build(await SegmentCrewAccountsAsync(record.ToRoutePlanRouteId, cancellationToken), transportId,
                    NotificationType.ROUTE, NotificationPriority.HIGH, "You can start your segment",
                    $"Handover {record.HandoverCode} is complete. Your segment can start.", reference.Item1, reference.Item2,
                    $"/crew/transports/{transportId}", now);
            default:
                return Build([customer], transportId, NotificationType.HANDOVER, NotificationPriority.HIGH,
                        "Your horses have been delivered", $"Handover {record.HandoverCode} is complete and the transport has ended.",
                        reference.Item1, reference.Item2, customerUrl, now)
                    .Concat(Build(backOffice, transportId, NotificationType.HANDOVER, NotificationPriority.NORMAL,
                        "Transport completed", $"Handover {record.HandoverCode} is complete.", reference.Item1, reference.Item2, adminUrl, now))
                    .ToList();
        }
    }

    public async Task<IReadOnlyList<NotificationCreateRequest>> HandoverCancelledAsync(
        HandoverRecord record, DateTime now, CancellationToken cancellationToken = default)
    {
        var recipients = (await CoordinatorsAsync(record.TransportId, cancellationToken))
            .Concat(await EmployeeAccountsAsync([record.FromEmployeeId, record.ToEmployeeId], cancellationToken));
        return Build(recipients, record.TransportId, NotificationType.HANDOVER, NotificationPriority.HIGH, "Handover cancelled",
            $"Handover {record.HandoverCode} was cancelled. Schedule a new handover.", nameof(HandoverRecord), record.HandoverRecordId,
            $"/admin/ongoing-transports/{record.TransportId}", now);
    }

    public async Task<IReadOnlyList<NotificationCreateRequest>> GpsSharingDisabledAsync(
        Guid transportId, DateTime now, CancellationToken cancellationToken = default) =>
        Build(await CoordinatorsAsync(transportId, cancellationToken), transportId, NotificationType.TRANSPORT, NotificationPriority.HIGH,
            "Driver GPS sharing is disabled", "The driver denied location access, so the vehicle cannot be tracked.",
            nameof(Transport), transportId, $"/admin/ongoing-transports/{transportId}", now);

    private static List<NotificationCreateRequest> Build(
        IEnumerable<Guid> accountIds, Guid transportId, NotificationType type, NotificationPriority priority,
        string title, string message, string referenceType, Guid referenceId, string actionUrl, DateTime now) =>
        accountIds.Distinct().Select(accountId => new NotificationCreateRequest
        {
            AccountId = accountId, TransportId = transportId, Type = type, Priority = priority, Title = title, Message = message,
            ReferenceType = referenceType, ReferenceId = referenceId, ActionUrl = actionUrl, CreatedAt = now
        }).ToList();

    private async Task<List<Guid>> ManagersAsync(CancellationToken cancellationToken) =>
        await db.Employees.AsNoTracking().Active().Where(item => item.Role == EmployeeRole.LOGISTICS_MANAGER)
            .Select(item => item.AccountId).ToListAsync(cancellationToken);

    /// <summary>Coordinators assigned to a transport plan of the transport.</summary>
    private async Task<List<Guid>> CoordinatorsAsync(Guid transportId, CancellationToken cancellationToken) =>
        await db.EmployeeTransportPlans.AsNoTracking()
            .Where(link => link.TransportPlan.TransportId == transportId)
            .Select(link => link.Employee).Where(employee => employee.Role == EmployeeRole.FLEET_ROUTE_COORDINATOR)
            .Where(employee => !employee.IsDeleted && employee.IsActive && !employee.Account.IsDeleted && employee.Account.Status == AccountStatus.ACTIVE)
            .Select(employee => employee.AccountId).ToListAsync(cancellationToken);

    private async Task<List<Guid>> EmployeeAccountsAsync(IEnumerable<Guid?> employeeIds, CancellationToken cancellationToken)
    {
        var ids = employeeIds.OfType<Guid>().ToArray();
        return await db.Employees.AsNoTracking().Active().Where(item => ids.Contains(item.EmployeeId))
            .Select(item => item.AccountId).ToListAsync(cancellationToken);
    }

    private async Task<List<Guid>> SegmentCrewAccountsAsync(Guid? routePlanRouteId, CancellationToken cancellationToken)
    {
        if (routePlanRouteId is null) return [];
        var crew = await db.RoutePlanRoutes.AsNoTracking().Where(route => route.RoutePlanRouteId == routePlanRouteId)
            .Select(route => new { route.DriverEmployeeId, route.EscortEmployeeId }).SingleOrDefaultAsync(cancellationToken);
        return crew is null ? [] : await EmployeeAccountsAsync([crew.DriverEmployeeId, crew.EscortEmployeeId], cancellationToken);
    }

    private Task<Guid> CustomerAccountAsync(Guid transportId, CancellationToken cancellationToken) =>
        db.Transports.AsNoTracking().Where(transport => transport.TransportId == transportId)
            .Select(transport => transport.Booking.Customer.AccountId).SingleAsync(cancellationToken);

    private async Task<string> CustomerUrlAsync(Guid transportId, CancellationToken cancellationToken)
    {
        var bookingId = await db.Transports.AsNoTracking().Where(transport => transport.TransportId == transportId)
            .Select(transport => transport.BookingId).SingleAsync(cancellationToken);
        return $"/bookings/{bookingId}/transports/{transportId}";
    }

    private Task<string> HorseNameAsync(Guid horseId, CancellationToken cancellationToken) =>
        db.Horses.AsNoTracking().Where(horse => horse.HorseId == horseId).Select(horse => horse.HorseName).SingleAsync(cancellationToken);
}
