using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Repository.Extensions;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.NotificationService;
using Pegaxus.Service.OutboxService;

using static Pegaxus.Service.Common.DbTime;

namespace Pegaxus.Service.ClaimService;

public sealed partial class Service
{
    public async Task<Response.ClaimResponse> SubmitAsync(Guid accountId, Guid bookingId, Request.SubmitRequest request,
        CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(accountId, cancellationToken);
        if (!actor.IsCustomer) throw new ForbiddenException("Only Customers are authorized to submit claims.");
        Validation.ValidateFields(request, submitValidator);

        Guid claimId;
        try
        {
            await using var transaction = await db.Database.BeginSerializableAsync(cancellationToken);
            var booking = await db.Bookings.AsNoTracking().Where(item => item.BookingId == bookingId && item.Customer.AccountId == accountId)
                .Select(item => new { item.BookingId, item.BookingCode, item.CustomerId, item.Customer.FullName, item.Customer.Account.Email })
                .SingleOrDefaultAsync(cancellationToken) ?? throw new NotFoundException("Booking not found", "BOOKING_NOT_FOUND");
            var delivery = await DeliveryAsync(bookingId, cancellationToken) ?? throw new UnprocessableEntityException(
                "The handover record has not been signed yet. A claim can only be submitted after the handover is completed.", "HANDOVER_NOT_COMPLETED");
            var now = Truncate(clock.GetUtcNow());
            if (now > delivery.CompletedAt!.Value + ClaimWindow)
                throw new UnprocessableEntityException(
                    "The claim period for this handover (24 hours after the e-POD is signed) has expired. Please contact customer care for assistance.",
                    "CLAIM_WINDOW_EXPIRED");

            var claim = new Claim
            {
                ClaimId = Guid.NewGuid(), CustomerId = booking.CustomerId, HandoverRecordId = delivery.HandoverRecordId,
                ClaimCode = await CodeGeneration.NextAsync("CLM", now, code => db.Claims.AnyAsync(item => item.ClaimCode == code, cancellationToken),
                    "CONCURRENT_UPDATE", "Unable to generate a unique claim code. Please try again."),
                ClaimType = Enum.Parse<ClaimType>(request.ClaimType!.Trim(), true), Status = ClaimStatus.PENDING,
                Title = request.Title!.Trim(), Description = request.Description!.Trim(),
                RequestedCompensationAmount = request.RequestedCompensationAmount, Currency = Vnd.Currency,
                EvidenceNote = Text.EmptyToNull(request.EvidenceNote)?.Trim(), CustomerNote = Text.EmptyToNull(request.CustomerNote)?.Trim(),
                SubmittedAt = now, CreatedAt = now, UpdatedAt = now
            };
            db.Claims.Add(claim);

            var transportId = await db.Transports.AsNoTracking().Where(item => item.BookingId == bookingId)
                .Select(item => (Guid?)item.TransportId).SingleOrDefaultAsync(cancellationToken);
            var managers = await db.Employees.AsNoTracking().Active().Where(item => item.Role == EmployeeRole.LOGISTICS_MANAGER)
                .Select(item => item.AccountId).ToListAsync(cancellationToken);
            foreach (var managerAccountId in managers)
                notifications.Queue(new NotificationCreateRequest
                {
                    AccountId = managerAccountId, TransportId = transportId, Type = NotificationType.CLAIM, Priority = NotificationPriority.HIGH,
                    Title = "New claim submitted", ReferenceType = nameof(Claim), ReferenceId = claim.ClaimId,
                    Message = $"Claim {claim.ClaimCode} for booking {booking.BookingCode} was submitted by {booking.FullName}: {claim.Title}",
                    ActionUrl = NotificationLinks.Claim(EmployeeRole.LOGISTICS_MANAGER, bookingId, claim.ClaimId), CreatedAt = now
                });
            // The customer just acted on screen, so the confirmation is an email only.
            OutboxQueue.AddEmail(db, new()
            {
                To = booking.Email, Subject = $"Claim received — {claim.ClaimCode}",
                Body = MailService.EmailBodies.ClaimReceivedBody(booking.FullName, claim.ClaimCode, booking.BookingCode, claim.Title,
                    NotificationLinks.Claim(null, bookingId, claim.ClaimId))
            }, now);

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            await notifications.FlushAsync(cancellationToken);
            claimId = claim.ClaimId;
        }
        catch (Exception ex) when (IsWriteConflict(ex))
        {
            throw new ConflictException("The record changed. Reload it and try again.", "CONCURRENT_UPDATE");
        }
        return await BuildAsync(claimId, actor, cancellationToken);
    }
}
