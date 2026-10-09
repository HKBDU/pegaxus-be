using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;

namespace Pegaxus.Service.BookingService;

public partial class Service
{
    public async Task<Response.NegotiatedBookingResponse> NegotiateAsync(
        Guid accountId, Guid bookingId, Request.NegotiateBookingRequest request, CancellationToken cancellationToken = default)
    {
        await GetLogisticsManagerAsync(accountId, cancellationToken);
        var validation = await new Validations.NegotiateBookingRequestValidator().ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
            throw new BadRequestException(string.Join(" ", validation.Errors.Select(error => error.ErrorMessage)));

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        if (_db.Database.IsRelational())
            // Updating the key to itself holds a row lock until the transaction ends.
            await _db.Bookings.Where(item => item.BookingId == bookingId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.BookingId, item => item.BookingId), cancellationToken);
        var booking = await _db.Bookings.Include(value => value.Customer)
            .Include(value => value.Transport).ThenInclude(value => value!.ComplianceDossier).ThenInclude(value => value!.DossierRequirements)
            .SingleOrDefaultAsync(value => value.BookingId == bookingId, cancellationToken)
            ?? throw new NotFoundException("Booking not found.", "BOOKING_NOT_FOUND");
        await _db.Entry(booking).ReloadAsync(cancellationToken);
        if (booking.Status != BookingStatus.APPROVED)
            throw new ConflictException("Only APPROVED bookings can be changed through negotiation.", "INVALID_BOOKING_STATUS");

        var now = UtcNow();
        var status = request.Status.Trim().ToUpperInvariant() == "CANCELLED" ? BookingStatus.CANCELLED : BookingStatus.DRAFT;
        var reason = request.Reason.Trim();
        booking.Status = status;
        booking.UpdatedAt = now;
        booking.ApprovedAt = null;
        if (status == BookingStatus.CANCELLED)
        {
            booking.CancelledAt = now;
            booking.CancellationReason = reason;
            if (booking.Transport is not null)
            {
                booking.Transport.Status = TransportStatus.CANCELLED;
                booking.Transport.CancelledAt = now;
                booking.Transport.CancellationReason = reason;
                booking.Transport.UpdatedAt = now;
            }
        }

        var dossier = booking.Transport?.ComplianceDossier;
        if (dossier is not null)
        {
            dossier.Status = status == BookingStatus.CANCELLED ? ComplianceDossierStatus.CLOSED : ComplianceDossierStatus.DRAFT;
            dossier.ClosedAt = status == BookingStatus.CANCELLED ? now : null;
            dossier.UpdatedAt = now;
            dossier.Note = $"{dossier.Note}\nBooking {status}: {reason}".Trim();
            foreach (var item in dossier.DossierRequirements.Where(item => !item.IsDeleted))
            {
                item.IsDeleted = true;
                item.DeletedAt = item.UpdatedAt = now;
                item.DeletedByAccountId = accountId;
            }
        }
        AddHistory(bookingId, accountId, status == BookingStatus.CANCELLED ? BookingHistoryType.CANCEL : BookingHistoryType.SUBMIT,
            BookingStatus.APPROVED, status, reason, now);
        var recipients = await _db.Employees.Where(employee => employee.Role == EmployeeRole.TRANSPORT_SPECIALIST &&
                employee.IsActive && !employee.IsDeleted && !employee.Account.IsDeleted && employee.Account.Status == AccountStatus.ACTIVE)
            .Select(employee => employee.AccountId).ToListAsync(cancellationToken);
        recipients.Add(booking.Customer.AccountId);
        foreach (var recipient in recipients.Distinct())
            AddNotification(recipient, booking.Transport?.TransportId, booking, $"Booking {status}; dossier operations suspended", now);
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await _notificationService.FlushAsync(cancellationToken);
        return new Response.NegotiatedBookingResponse
        {
            BookingId = bookingId, Status = status.ToString(), ComplianceDossierId = dossier?.ComplianceDossierId,
            DossierStatus = dossier?.Status.ToString(), Reason = reason
        };
    }
}
