using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.OutboxService;

using static Pegaxus.Service.Common.DbTime;
using static Pegaxus.Service.Common.Text;

using BookingEntity = Pegaxus.Repository.Entities.Booking;

namespace Pegaxus.Service.BookingService;

public sealed partial class Service
{
    public async Task ChangeStatusAsync(
        Guid accountId,
        Guid bookingId,
        Request.ChangeStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        var customer = await GetCustomerAsync(accountId, cancellationToken);
        var booking = await _db.Bookings.SingleOrDefaultAsync(item =>
            item.BookingId == bookingId && item.CustomerId == customer.CustomerId,
            cancellationToken);
        if (booking is null)
            throw new NotFoundException("Booking not found", "BOOKING_NOT_FOUND");

        if (!Enum.TryParse<BookingStatus>(request.Status.Trim(), true, out var targetStatus) ||
            targetStatus is not (BookingStatus.DRAFT or BookingStatus.PENDING))
            throw new BadRequestException("Only DRAFT and PENDING statuses are supported", "INVALID_BOOKING_STATUS");

        var validTransition = booking.Status == BookingStatus.PENDING && targetStatus == BookingStatus.DRAFT ||
                              booking.Status == BookingStatus.DRAFT && targetStatus == BookingStatus.PENDING;
        if (!validTransition)
            throw new ConflictException(
                "The booking status cannot be changed from its current state",
                "INVALID_BOOKING_STATUS");
        if (targetStatus == BookingStatus.PENDING && !await HasHorseAsync(bookingId, cancellationToken))
            throw new UnprocessableEntityException(
                "The booking has no horse and cannot be submitted",
                "BOOKING_HAS_NO_HORSE");

        var now = UtcNowTruncated();
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var previousStatus = booking.Status;
        booking.Status = targetStatus;
        booking.UpdatedAt = now;
        AddHistory(
            bookingId,
            accountId,
            BookingHistoryType.SUBMIT,
            previousStatus,
            targetStatus,
            targetStatus == BookingStatus.DRAFT
                ? "Booking moved to draft"
                : "Booking submitted for review",
            now);
        if (targetStatus == BookingStatus.PENDING)
            await NotifyManagersAsync(booking, "Booking submitted for review", now, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await _notificationService.FlushAsync(cancellationToken);
    }

    public async Task<Response.CreateBookingResponse> CreateAsync(
        Guid accountId,
        Request.CreateBookingRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateDates(request.RequestedDepartureDate, request.RequestedArrivalDate);
        if (request.Horses is null || request.Horses.Count == 0)
            throw new BadRequestException("At least one horse is required", "BOOKING_HAS_NO_HORSE");
        if (request.Horses.Select(horse => horse.HorseId).Distinct().Count() != request.Horses.Count)
            throw new BadRequestException(
                "A horse can only be selected once per booking",
                "DUPLICATE_HORSE_IN_BOOKING");

        var customer = await GetCustomerAsync(accountId, cancellationToken);
        var horseIds = request.Horses.Select(horse => horse.HorseId).ToArray();
        if (horseIds.Length > 0)
        {
            var ownedHorseCount = await _db.Horses.CountAsync(horse =>
                horseIds.Contains(horse.HorseId) &&
                horse.CustomerId == customer.CustomerId &&
                !horse.IsDeleted,
                cancellationToken);
            if (ownedHorseCount != horseIds.Length)
                throw new NotFoundException("Horse not found", "HORSE_NOT_FOUND");
        }

        var (isRouteValid, routeErrorMessage) = CountryUtils.ValidateRoute(request.OriginCountry, request.DestinationCountry);
        if (!isRouteValid)
            throw new BadRequestException(routeErrorMessage);

        var now = UtcNowTruncated();
        var booking = new BookingEntity
        {
            BookingId = Guid.NewGuid(),
            BookingCode = await GenerateBookingCodeAsync(cancellationToken),
            CustomerId = customer.CustomerId,
            Status = BookingStatus.PENDING,
            OriginAddress = request.OriginAddress.Trim(),
            OriginCountry = request.OriginCountry.Trim(),
            DestinationAddress = request.DestinationAddress.Trim(),
            DestinationCountry = request.DestinationCountry.Trim(),
            RequestedDepartureDate = request.RequestedDepartureDate,
            RequestedArrivalDate = request.RequestedArrivalDate,
            SpecialRequirements = EmptyToNull(request.SpecialRequirements),
            CustomerNote = EmptyToNull(request.CustomerNote),
            SubmittedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        _db.Bookings.Add(booking);
        _db.BookingHorses.AddRange(request.Horses.Select(horse => new BookingHorse
        {
            BookingId = booking.BookingId,
            HorseId = horse.HorseId,
            SpecialHandlingNotes = EmptyToNull(horse.SpecialHandlingNotes),
            CreatedAt = now
        }));
        AddHistory(booking.BookingId, accountId, BookingHistoryType.SUBMIT, null, BookingStatus.PENDING, null, now);
        await NotifyManagersAsync(booking, "Booking submitted for review", now, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await _notificationService.FlushAsync(cancellationToken);

        return new Response.CreateBookingResponse
        {
            BookingId = booking.BookingId,
            BookingCode = booking.BookingCode
        };
    }

    public async Task<Response.UpdateBookingResponse> UpdateAsync(
        Guid accountId,
        Guid bookingId,
        Request.UpdateBookingRequest request,
        CancellationToken cancellationToken = default)
    {
        var booking = await GetOwnedDraftBookingAsync(accountId, bookingId, cancellationToken);
        var clientUpdatedAtUtc = Truncate(request.LastUpdatedAt.UtcDateTime);
        var clientUpdatedAtValue = Truncate(request.LastUpdatedAt.DateTime);
        if (booking.UpdatedAt != clientUpdatedAtUtc && booking.UpdatedAt != clientUpdatedAtValue)
            throw new ConflictException(
                "The booking was modified. Refresh and try again",
                "BOOKING_MODIFIED");

        var departureDate = request.RequestedDepartureDate ?? booking.RequestedDepartureDate;
        var arrivalDate = request.RequestedArrivalDate ?? booking.RequestedArrivalDate;
        ValidateDates(departureDate, arrivalDate);

        var targetOrigin = request.OriginCountry?.Trim() ?? booking.OriginCountry;
        var targetDestination = request.DestinationCountry?.Trim() ?? booking.DestinationCountry;
        var (isUpdatedRouteValid, updatedRouteErrorMessage) = CountryUtils.ValidateRoute(targetOrigin, targetDestination);
        if (!isUpdatedRouteValid)
            throw new BadRequestException(updatedRouteErrorMessage);

        var changedFields = new List<string>();
        Set(request.OriginAddress, value => booking.OriginAddress = value, "originAddress", changedFields);
        Set(request.OriginCountry, value => booking.OriginCountry = value, "originCountry", changedFields);
        Set(request.DestinationAddress, value => booking.DestinationAddress = value, "destinationAddress", changedFields);
        Set(request.DestinationCountry, value => booking.DestinationCountry = value, "destinationCountry", changedFields);
        if (request.RequestedDepartureDate.HasValue)
        {
            booking.RequestedDepartureDate = request.RequestedDepartureDate.Value;
            changedFields.Add("requestedDepartureDate");
        }
        if (request.RequestedArrivalDate.HasValue)
        {
            booking.RequestedArrivalDate = request.RequestedArrivalDate;
            changedFields.Add("requestedArrivalDate");
        }
        SetOptional(request.SpecialRequirements, value => booking.SpecialRequirements = value, "specialRequirements", changedFields);
        SetOptional(request.CustomerNote, value => booking.CustomerNote = value, "customerNote", changedFields);

        var now = UtcNowTruncated();
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        booking.UpdatedAt = now;
        AddHistory(
            bookingId,
            accountId,
            BookingHistoryType.SUBMIT,
            BookingStatus.DRAFT,
            BookingStatus.DRAFT,
            $"Updated: {string.Join(", ", changedFields)}",
            now);
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new Response.UpdateBookingResponse { UpdatedAt = AsUtc(booking.UpdatedAt) };
    }

    public async Task CancelAsync(
        Guid accountId,
        Guid bookingId,
        Request.CancelBookingRequest request,
        CancellationToken cancellationToken = default)
    {
        var booking = await GetOwnedPendingBookingAsync(accountId, bookingId, cancellationToken);
        var now = UtcNowTruncated();
        var reason = request.Reason.Trim();
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        booking.Status = BookingStatus.CANCELLED;
        booking.CancelledAt = now;
        booking.CancellationReason = reason;
        booking.UpdatedAt = now;
        AddHistory(bookingId, accountId, BookingHistoryType.CANCEL, BookingStatus.PENDING, BookingStatus.CANCELLED, reason, now);
        await NotifyManagersAsync(booking, "Booking cancelled by customer", now, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await _notificationService.FlushAsync(cancellationToken);
    }

    public async Task<Response.ApproveBookingResponse> ApproveAsync(
        Guid accountId,
        Guid bookingId,
        CancellationToken cancellationToken = default)
    {
        var employee = await GetLogisticsManagerAsync(accountId, cancellationToken);
        var booking = await _db.Bookings
            .Include(item => item.Customer).ThenInclude(customer => customer.Account)
            .Include(item => item.Transport)
            .SingleOrDefaultAsync(item => item.BookingId == bookingId, cancellationToken);
        EnsurePending(booking);
        if (booking!.Transport is not null)
            throw new ConflictException("The booking already has a transport", "INVALID_BOOKING_STATUS");
        if (!await HasHorseAsync(bookingId, cancellationToken))
            throw new UnprocessableEntityException(
                "The booking has no horse and cannot be approved",
                "BOOKING_HAS_NO_HORSE");

        var now = UtcNowTruncated();
        var transport = new Transport
        {
            TransportId = Guid.NewGuid(),
            TransportCode = await GenerateTransportCodeAsync(cancellationToken),
            BookingId = bookingId,
            Status = TransportStatus.CREATED,
            CreatedAt = now,
            UpdatedAt = now
        };
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        booking.Status = BookingStatus.APPROVED;
        booking.AssignedEmployeeId = employee.EmployeeId;
        booking.ApprovedAt = now;
        booking.UpdatedAt = now;
        _db.Transports.Add(transport);
        AddHistory(bookingId, accountId, BookingHistoryType.APPROVE, BookingStatus.PENDING, BookingStatus.APPROVED, null, now);
        AddNotification(booking.Customer.AccountId, transport.TransportId, booking, "Booking approved", now);
        var approvalMail = new MailService.MailContent
        {
            To = booking.Customer.Account.Email,
            Subject = $"Booking approved — {booking.BookingCode}",
            Body = MailService.EmailBodies.BookingApprovedBody(
                customerName: booking.Customer.FullName,
                bookingCode: booking.BookingCode,
                transportCode: transport.TransportCode,
                departureDate: booking.RequestedDepartureDate,
                actionUrl: null)
        };
        if (OutboxQueue.IsEnabled(_db))
            OutboxQueue.AddEmail(_db, approvalMail, now);
        await _db.SaveChangesAsync(cancellationToken);
        if (!OutboxQueue.IsEnabled(_db))
            await _mailService.SendMail(approvalMail, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await _notificationService.FlushAsync(cancellationToken);

        return new Response.ApproveBookingResponse
        {
            Status = booking.Status,
            Transport = new Response.TransportResponse
            {
                TransportId = transport.TransportId,
                TransportCode = transport.TransportCode,
                Status = transport.Status
            }
        };
    }

    public async Task RejectAsync(
        Guid accountId,
        Guid bookingId,
        Request.RejectBookingRequest request,
        CancellationToken cancellationToken = default)
    {
        var employee = await GetLogisticsManagerAsync(accountId, cancellationToken);
        var booking = await _db.Bookings
            .Include(item => item.Customer).ThenInclude(customer => customer.Account)
            .Include(item => item.Transport)
            .SingleOrDefaultAsync(item => item.BookingId == bookingId, cancellationToken);
        EnsurePending(booking);
        if (booking!.Transport is not null)
            throw new ConflictException("The booking already has a transport", "INVALID_BOOKING_STATUS");

        var now = UtcNowTruncated();
        var reason = request.Reason.Trim();
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        booking.Status = BookingStatus.REJECTED;
        booking.AssignedEmployeeId = employee.EmployeeId;
        booking.RejectedAt = now;
        booking.RejectionReason = reason;
        booking.UpdatedAt = now;
        AddHistory(bookingId, accountId, BookingHistoryType.REJECT, BookingStatus.PENDING, BookingStatus.REJECTED, reason, now);
        AddNotification(booking.Customer.AccountId, null, booking, "Booking rejected", now);
        var rejectionMail = new MailService.MailContent
        {
            To = booking.Customer.Account.Email,
            Subject = $"Booking rejected — {booking.BookingCode}",
            Body = MailService.EmailBodies.BookingRejectedBody(
                customerName: booking.Customer.FullName,
                bookingCode: booking.BookingCode,
                reason: reason,
                actionUrl: null)
        };
        if (OutboxQueue.IsEnabled(_db))
            OutboxQueue.AddEmail(_db, rejectionMail, now);
        await _db.SaveChangesAsync(cancellationToken);
        if (!OutboxQueue.IsEnabled(_db))
            await _mailService.SendMail(rejectionMail, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await _notificationService.FlushAsync(cancellationToken);
    }

    private async Task<BookingEntity> GetOwnedPendingBookingAsync(
        Guid accountId,
        Guid bookingId,
        CancellationToken cancellationToken)
    {
        var booking = await _db.Bookings.SingleOrDefaultAsync(item =>
            item.BookingId == bookingId && item.Customer.AccountId == accountId,
            cancellationToken);
        if (booking is null)
            throw new NotFoundException("Booking not found", "BOOKING_NOT_FOUND");
        if (booking.Status != BookingStatus.PENDING)
            throw new ConflictException("The action is not allowed in the current booking status", "INVALID_BOOKING_STATUS");
        return booking;
    }

    private async Task<BookingEntity> GetOwnedDraftBookingAsync(
        Guid accountId,
        Guid bookingId,
        CancellationToken cancellationToken)
    {
        var booking = await _db.Bookings.SingleOrDefaultAsync(item =>
            item.BookingId == bookingId && item.Customer.AccountId == accountId,
            cancellationToken);
        if (booking is null)
            throw new NotFoundException("Booking not found", "BOOKING_NOT_FOUND");
        if (booking.Status != BookingStatus.DRAFT)
            throw new ConflictException("The booking must be in DRAFT status to be edited", "INVALID_BOOKING_STATUS");
        return booking;
    }

    private static void EnsurePending(BookingEntity? booking)
    {
        if (booking is null)
            throw new NotFoundException("Booking not found", "BOOKING_NOT_FOUND");
        if (booking.Status != BookingStatus.PENDING)
            throw new ConflictException("The action is not allowed in the current booking status", "INVALID_BOOKING_STATUS");
    }

    private async Task<string> GenerateBookingCodeAsync(CancellationToken cancellationToken) =>
        await GenerateCodeAsync("REQ", code => _db.Bookings.AnyAsync(booking => booking.BookingCode == code, cancellationToken));

    private async Task<string> GenerateTransportCodeAsync(CancellationToken cancellationToken) =>
        await GenerateCodeAsync("TP", code => _db.Transports.AnyAsync(transport => transport.TransportCode == code, cancellationToken));

    private static async Task<string> GenerateCodeAsync(string prefix, Func<string, Task<bool>> exists)
    {
        var date = DateTime.UtcNow.ToString("yyyyMMdd");
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var code = $"{prefix}-{date}-{RandomNumberGenerator.GetInt32(10_000):D4}";
            if (!await exists(code)) return code;
        }
        throw new InvalidOperationException($"Unable to generate a unique {prefix} code");
    }

    private static void ValidateDates(DateOnly departureDate, DateOnly? arrivalDate)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (departureDate < today.AddDays(21) || departureDate > today.AddMonths(2))
            throw new BadRequestException(
                "The departure date must be between 3 weeks and 2 months from today",
                "DEPARTURE_DATE_OUT_OF_RANGE");
        if (arrivalDate.HasValue && arrivalDate.Value < departureDate)
            throw new BadRequestException(
                "The arrival date cannot be before the departure date",
                "ARRIVAL_DATE_INVALID");
    }

    private static void Set(string? value, Action<string> setter, string field, ICollection<string> changedFields)
    {
        if (value is null) return;
        setter(value.Trim());
        changedFields.Add(field);
    }

    private static void SetOptional(string? value, Action<string?> setter, string field, ICollection<string> changedFields)
    {
        if (value is null) return;
        setter(EmptyToNull(value));
        changedFields.Add(field);
    }
}
