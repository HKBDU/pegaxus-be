using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Enum;
using Pegaxus.Repository.Extensions;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;

using static Pegaxus.Service.Common.DbTime;

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
            // Updating the key to itself holds a row lock until the transaction ends.
        await _db.LockRowsAsync(_db.Bookings.Where(item => item.BookingId == bookingId), item => item.BookingId, cancellationToken);
        var booking = await _db.Bookings.Include(value => value.Customer)
            .Include(value => value.Transport).ThenInclude(value => value!.ComplianceDossiers).ThenInclude(value => value!.DossierRequirements)
            .SingleOrDefaultAsync(value => value.BookingId == bookingId, cancellationToken)
            ?? throw new NotFoundException("Booking not found.", "BOOKING_NOT_FOUND");
        await _db.Entry(booking).ReloadAsync(cancellationToken);
        if (booking.Status != BookingStatus.APPROVED)
            throw new ConflictException("Only APPROVED bookings can be changed through negotiation.", "INVALID_BOOKING_STATUS");

        var now = UtcNowTruncated();
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

        var dossier = booking.Transport?.MainDossier;
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
        var specialists = await _db.Employees.Active().Where(employee => employee.Role == EmployeeRole.TRANSPORT_SPECIALIST &&
                employee.EmployeeTransportPlans.Any(assignment => assignment.TransportPlan.Transport.BookingId == bookingId))
            .Select(employee => employee.AccountId).Distinct().ToListAsync(cancellationToken);
        var (title, outcome) = status == BookingStatus.CANCELLED
            ? ("Booking cancelled", "has been cancelled")
            : ("Booking returned to draft", "was returned to draft for changes");
        var message = $"Booking {booking.BookingCode} ({RouteSummary(booking)}) {outcome}. Reason: {reason}. Work on its compliance dossier is suspended.";
        foreach (var recipient in specialists)
            AddNotification(recipient, EmployeeRole.TRANSPORT_SPECIALIST, booking.Transport?.TransportId, booking, title, message, now);
        AddNotification(booking.Customer.AccountId, null, booking.Transport?.TransportId, booking, title, message, now, NotificationPriority.HIGH);
        await MailService.MailRecipients.QueueAsync(_db, specialists.Append(booking.Customer.AccountId), now, recipient => MailService.MailRecipients.Mail(
            recipient, $"{title} — {booking.BookingCode}",
            MailService.EmailBodies.BookingChangedBody(recipient.FullName, booking.BookingCode,
                $"{booking.OriginCountry} to {booking.DestinationCountry}", status == BookingStatus.CANCELLED, reason, recipient.Role is not null,
                NotificationLinks.Booking(recipient.Role, booking.BookingId))), cancellationToken);
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
