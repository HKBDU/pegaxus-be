using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Repository.Extensions;
using Pegaxus.Service.Exceptions;

using BookingEntity = Pegaxus.Repository.Entities.Booking;

namespace Pegaxus.Service.BookingService;

public partial class Service
{
    /// <summary>
    /// Cancelling an APPROVED booking also closes everything that was planned for its transport. Money already paid stays as it is:
    /// only invoices nobody has paid or started paying are cancelled.
    /// </summary>
    private async Task CancelTransportWorkAsync(BookingEntity booking, Transport transport, Guid accountId, string reason, DateTime now,
        CancellationToken cancellationToken)
    {
        await _db.LockRowsAsync(_db.Transports.Where(item => item.TransportId == transport.TransportId), item => item.TransportId, cancellationToken);
        await _db.Entry(transport).ReloadAsync(cancellationToken);
        // Once the horses are on the road a cancellation is not safe; the trip is brought back through a RETURN route plan instead.
        if (transport.Status is TransportStatus.IN_TRANSIT or TransportStatus.COMPLETED)
            throw new ConflictException("The transport has already departed. Create a return route plan instead of cancelling the booking.", "TRANSPORT_ALREADY_DEPARTED");

        var planIds = await _db.TransportPlans.Where(item => item.TransportId == transport.TransportId).OrderBy(item => item.TransportPlanId)
            .Select(item => item.TransportPlanId).ToListAsync(cancellationToken);
        foreach (var id in planIds)
            await _db.LockRowsAsync(_db.TransportPlans.Where(item => item.TransportPlanId == id), item => item.TransportPlanId, cancellationToken);
        var routePlanIds = await _db.RoutePlans.Where(item => item.TransportPlan.TransportId == transport.TransportId).OrderBy(item => item.RoutePlanId)
            .Select(item => item.RoutePlanId).ToListAsync(cancellationToken);
        foreach (var id in routePlanIds)
            await _db.LockRowsAsync(_db.RoutePlans.Where(item => item.RoutePlanId == id), item => item.RoutePlanId, cancellationToken);

        var plans = await _db.TransportPlans.Include(item => item.RoutePlans).ThenInclude(item => item.RoutePlanRoutes).ThenInclude(item => item.RoutePlanRouteVehicles)
            .Where(item => item.TransportId == transport.TransportId).ToListAsync(cancellationToken);
        var vehicleIds = new SortedSet<Guid>();
        foreach (var plan in plans)
        {
            foreach (var routePlan in plan.RoutePlans.Where(item => item.Status is not (RoutePlanStatus.COMPLETED or RoutePlanStatus.CANCELLED or RoutePlanStatus.REJECTED_BY_MANAGER)))
            {
                routePlan.Status = RoutePlanStatus.CANCELLED;
                routePlan.RouteNote = string.IsNullOrWhiteSpace(routePlan.RouteNote) ? $"Cancelled: {reason}" : $"{routePlan.RouteNote} | Cancelled: {reason}";
                routePlan.UpdatedAt = now;
                foreach (var link in routePlan.RoutePlanRoutes.SelectMany(segment => segment.RoutePlanRouteVehicles))
                    vehicleIds.Add(link.VehicleId);
            }
            if (plan.Status is not (TransportPlanStatus.COMPLETED or TransportPlanStatus.CANCELLED))
            {
                plan.Status = TransportPlanStatus.CANCELLED;
                plan.UpdatedAt = now;
            }
        }

        foreach (var extra in transport.ComplianceDossiers.Where(item => item.DossierType != DossierType.MAIN && item.Status != ComplianceDossierStatus.CLOSED))
        {
            extra.Status = ComplianceDossierStatus.CLOSED;
            extra.ClosedAt = extra.UpdatedAt = now;
            extra.Note = $"{extra.Note}\nBooking CANCELLED: {reason}".Trim();
            foreach (var item in extra.DossierRequirements.Where(item => !item.IsDeleted))
            {
                item.IsDeleted = true;
                item.DeletedAt = item.UpdatedAt = now;
                item.DeletedByAccountId = accountId;
            }
        }
        foreach (var request in await _db.DocumentRequests.Where(item => item.Status == DocumentRequestStatus.OPEN &&
                     item.DossierRequirement.ComplianceDossier.TransportId == transport.TransportId).ToListAsync(cancellationToken))
        {
            request.Status = DocumentRequestStatus.CANCELLED;
            request.CancellationReason = reason;
            request.CancelledAt = request.UpdatedAt = now;
        }
        foreach (var record in await _db.HandoverRecords.Where(item => item.TransportId == transport.TransportId &&
                     (item.Status == HandoverStatus.PLANNED || item.Status == HandoverStatus.IN_PROGRESS)).ToListAsync(cancellationToken))
        {
            record.Status = HandoverStatus.CANCELLED;
            record.CancellationReason = reason;
            record.CancelledAt = record.UpdatedAt = now;
        }

        await CancelUnpaidInvoicesAsync(booking.BookingId, accountId, reason, now, cancellationToken);
        await ReleaseVehiclesAsync(vehicleIds, transport.TransportId, now, cancellationToken);
        await NotifyCrewAsync(booking, transport, reason, now, cancellationToken);
    }

    private async Task CancelUnpaidInvoicesAsync(Guid bookingId, Guid accountId, string reason, DateTime now, CancellationToken cancellationToken)
    {
        var invoices = await _db.Invoices.Where(item => item.BookingId == bookingId &&
            (item.Status == InvoiceStatus.DRAFT || item.Status == InvoiceStatus.ISSUED || item.Status == InvoiceStatus.OVERDUE) &&
            !_db.Receipts.Any(receipt => receipt.InvoiceId == item.InvoiceId &&
                (receipt.Status == ReceiptStatus.CONFIRMED || receipt.Status == ReceiptStatus.PENDING))).ToListAsync(cancellationToken);
        foreach (var invoice in invoices)
        {
            _db.InvoiceStatusHistories.Add(new InvoiceStatusHistory
            {
                InvoiceStatusHistoryId = Guid.NewGuid(), InvoiceId = invoice.InvoiceId, Type = InvoiceHistoryType.CANCEL,
                PreviousStatus = invoice.Status, NewStatus = InvoiceStatus.CANCELLED, ChangedByAccountId = accountId,
                Reason = $"Booking cancelled: {reason}", ChangedAt = now, CreatedAt = now
            });
            invoice.Status = InvoiceStatus.CANCELLED;
            invoice.CancelledAt = now;
            invoice.CancellationReason = $"Booking cancelled: {reason}";
            invoice.UpdatedAt = now;
        }
    }

    /// <summary>A vehicle another transport still holds keeps its flag; only a vehicle nobody holds goes back to the pool.</summary>
    private async Task ReleaseVehiclesAsync(SortedSet<Guid> vehicleIds, Guid transportId, DateTime now, CancellationToken cancellationToken)
    {
        if (vehicleIds.Count == 0) return;
        var ids = vehicleIds.ToList();
        foreach (var id in ids)
            await _db.LockRowsAsync(_db.Vehicles.Where(item => item.VehicleId == id), item => item.VehicleId, cancellationToken);
        var held = await _db.RoutePlanRouteVehicles.AsNoTracking().Where(link => ids.Contains(link.VehicleId) &&
                link.RoutePlanRoute.RoutePlan.TransportPlan.TransportId != transportId && link.RoutePlanRoute.ActualArrivalAt == null &&
                (link.RoutePlanRoute.RoutePlan.Status == RoutePlanStatus.CONFIRMED || link.RoutePlanRoute.RoutePlan.Status == RoutePlanStatus.ACTIVE))
            .Select(link => link.VehicleId).Distinct().ToListAsync(cancellationToken);
        foreach (var vehicle in await _db.Vehicles.Where(item => ids.Contains(item.VehicleId)).ToListAsync(cancellationToken))
        {
            await _db.Entry(vehicle).ReloadAsync(cancellationToken);
            if (vehicle.Status != VehicleStatus.ASSIGNED || held.Contains(vehicle.VehicleId)) continue;
            vehicle.Status = VehicleStatus.AVAILABLE;
            vehicle.UpdatedAt = now;
        }
    }

    /// <summary>Transport Specialists are told by <see cref="NegotiateAsync"/>; everyone else on the plan (coordinator, drivers, escorts) is told here.</summary>
    private async Task NotifyCrewAsync(BookingEntity booking, Transport transport, string reason, DateTime now, CancellationToken cancellationToken)
    {
        var crew = await _db.Employees.Active().Where(employee => employee.Role != EmployeeRole.TRANSPORT_SPECIALIST &&
                employee.EmployeeTransportPlans.Any(link => link.TransportPlan.TransportId == transport.TransportId))
            .Select(employee => new { employee.AccountId, employee.Role }).Distinct().ToListAsync(cancellationToken);
        foreach (var member in crew)
            AddNotification(member.AccountId, member.Role, transport.TransportId, booking, "Booking cancelled",
                $"Booking {booking.BookingCode} ({RouteSummary(booking)}) has been cancelled. Reason: {reason}. Its transport plan is closed.", now, NotificationPriority.HIGH);
    }
}
