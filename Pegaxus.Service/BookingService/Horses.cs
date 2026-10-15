using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;

using static Pegaxus.Service.Common.DbTime;
using static Pegaxus.Service.Common.Text;

using MailService = Pegaxus.Service.MailService;
using NotificationService = Pegaxus.Service.NotificationService;

namespace Pegaxus.Service.BookingService;

public sealed partial class Service
{
    public async Task AddHorseIntoBookingAsync(
        Guid accountId,
        Guid bookingId,
        Request.AddHorseRequest request,
        CancellationToken cancellationToken = default)
    {
        var booking = await GetOwnedDraftBookingAsync(accountId, bookingId, cancellationToken);
        var horse = await _db.Horses.SingleOrDefaultAsync(item =>
            item.HorseId == request.HorseId &&
            item.CustomerId == booking.CustomerId &&
            !item.IsDeleted,
            cancellationToken);
        if (horse is null)
            throw new NotFoundException("Horse not found", "HORSE_NOT_FOUND");

        if (await _db.BookingHorses.AnyAsync(item =>
                item.BookingId == bookingId && item.HorseId == request.HorseId,
                cancellationToken))
            throw new ConflictException("Horse already belongs to this booking", "HORSE_ALREADY_IN_BOOKING");

        var now = UtcNowTruncated();
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        _db.BookingHorses.Add(new BookingHorse
        {
            BookingId = bookingId,
            HorseId = request.HorseId,
            SpecialHandlingNotes = EmptyToNull(request.SpecialHandlingNotes),
            CreatedAt = now
        });
        booking.UpdatedAt = now;
        AddHistory(
            bookingId,
            accountId,
            BookingHistoryType.SUBMIT,
            BookingStatus.DRAFT,
            BookingStatus.DRAFT,
            $"Added horse: {horse.HorseName}",
            now);
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task UpdateHorseNotesAsync(
        Guid accountId,
        Guid bookingId,
        Guid horseId,
        Request.UpdateHorseNotesRequest request,
        CancellationToken cancellationToken = default)
    {
        var booking = await GetOwnedDraftBookingAsync(accountId, bookingId, cancellationToken);
        if (request.SpecialHandlingNotes?.Length > 2000)
            throw new BadRequestException("Special handling notes must not exceed 2000 characters");

        var bookingHorse = await _db.BookingHorses
            .Include(item => item.Horse)
            .SingleOrDefaultAsync(item =>
                item.BookingId == bookingId &&
                item.HorseId == horseId,
                cancellationToken);
        if (bookingHorse is null)
            throw new NotFoundException("Horse is not in this booking", "HORSE_NOT_IN_BOOKING");

        var now = UtcNowTruncated();
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        bookingHorse.SpecialHandlingNotes = EmptyToNull(request.SpecialHandlingNotes);
        booking.UpdatedAt = now;
        AddHistory(
            bookingId,
            accountId,
            BookingHistoryType.SUBMIT,
            BookingStatus.DRAFT,
            BookingStatus.DRAFT,
            $"Updated handling notes for horse: {bookingHorse.Horse.HorseName}",
            now);
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task RemoveHorseAsync(
        Guid accountId,
        Guid bookingId,
        Guid horseId,
        CancellationToken cancellationToken = default)
    {
        var booking = await GetOwnedDraftBookingAsync(accountId, bookingId, cancellationToken);
        var bookingHorse = await _db.BookingHorses
            .Include(item => item.Horse)
            .SingleOrDefaultAsync(item =>
                item.BookingId == bookingId &&
                item.HorseId == horseId,
                cancellationToken);
        if (bookingHorse is null)
            throw new NotFoundException("Horse is not in this booking", "HORSE_NOT_IN_BOOKING");

        var now = UtcNowTruncated();
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        _db.BookingHorses.Remove(bookingHorse);
        booking.UpdatedAt = now;
        AddHistory(
            bookingId,
            accountId,
            BookingHistoryType.SUBMIT,
            BookingStatus.DRAFT,
            BookingStatus.DRAFT,
            $"Removed horse: {bookingHorse.Horse.HorseName}",
            now);
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
