using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;

namespace Pegaxus.Service.ComplianceDossierService;

/// <summary>
/// The dossier of a return booking (Booking.ReturnOfTransportId) brings back a horse whose papers were already checked on the original
/// trip, so the Transport Specialist may waive the whole checklist at once and point to the original dossier instead.
/// </summary>
public sealed partial class Service
{
    private const int WaiveReasonMaxLength = 2000;

    public async Task<Response.WaiveForReturnResponse> WaiveForReturnAsync(Guid accountId, Guid dossierId, Request.WaiveForReturnRequest request,
        CancellationToken cancellationToken = default)
    {
        await RequireSpecialistAsync(accountId, cancellationToken);
        if (request.Reason?.Length > WaiveReasonMaxLength)
            throw new BadRequestException($"Reason must not exceed {WaiveReasonMaxLength} characters.");
        var dossier = await EditableDossierAsync(dossierId, cancellationToken);
        var source = await ReturnSourceAsync(dossier.Transport.Booking.ReturnOfTransportId, dossier.Transport.BookingId, cancellationToken)
            ?? throw new UnprocessableEntityException("Only the dossier of a return booking can be waived as a whole", "NOT_RETURN_BOOKING");
        var reason = string.IsNullOrWhiteSpace(request.Reason)
            ? $"Return trip of {source.HorseName ?? "the horse"} from transport {source.TransportCode}; documents reused from dossier {source.DossierCode ?? "of the original trip"}."
            : request.Reason.Trim();

        var open = await _db.DossierRequirements.Where(item => item.ComplianceDossierId == dossierId && !item.IsDeleted &&
                item.Status != DossierRequirementStatus.SATISFIED && item.Status != DossierRequirementStatus.WAIVED)
            .OrderBy(item => item.DossierRequirementId).ToListAsync(cancellationToken);
        var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        foreach (var item in open)
        {
            var before = new { item.Status, item.WaivedReason, item.SatisfiedAt };
            item.Status = DossierRequirementStatus.WAIVED;
            item.WaivedReason = reason;
            item.SatisfiedAt = null;
            item.UpdatedAt = now;
            AuditRequirement(dossierId, item.DossierRequirementId, accountId, "WAIVED", reason, now,
                new { Before = before, After = new { item.Status, item.WaivedReason, item.SatisfiedAt }, ReturnOfTransportId = source.TransportId });
        }
        if (open.Count > 0)
        {
            dossier.UpdatedAt = now;
            if (dossier.Status != ComplianceDossierStatus.DRAFT)
                AddNotification(dossier.Transport.Booking.Customer.AccountId, null, dossier, "Documents reused for the return trip",
                    $"The documents of the original trip are reused for dossier {dossier.DossierCode}. No action is needed from you.", now);
        }
        await _db.SaveChangesAsync(cancellationToken);
        await _notificationService.FlushAsync(cancellationToken);
        var total = await _db.DossierRequirements.AsNoTracking().CountAsync(item => item.ComplianceDossierId == dossierId && !item.IsDeleted, cancellationToken);
        return new Response.WaiveForReturnResponse { ComplianceDossierId = dossierId, WaivedCount = open.Count, RequirementCount = total, ReturnOf = source };
    }

    /// <summary>The original trip and its MAIN dossier, when <paramref name="returnOfTransportId"/> is set.</summary>
    private async Task<Response.ReturnSourceResponse?> ReturnSourceAsync(Guid? returnOfTransportId, Guid bookingId, CancellationToken cancellationToken)
    {
        if (returnOfTransportId is not { } transportId) return null;
        var horseIds = await _db.BookingHorses.AsNoTracking().Where(item => item.BookingId == bookingId).Select(item => item.HorseId).ToListAsync(cancellationToken);
        return await _db.Transports.AsNoTracking().Where(item => item.TransportId == transportId)
            .Select(item => new Response.ReturnSourceResponse
            {
                TransportId = item.TransportId,
                TransportCode = item.TransportCode,
                BookingId = item.BookingId,
                BookingCode = item.Booking.BookingCode,
                DossierId = item.ComplianceDossiers.Where(dossier => dossier.DossierType == DossierType.MAIN).Select(dossier => (Guid?)dossier.ComplianceDossierId).FirstOrDefault(),
                DossierCode = item.ComplianceDossiers.Where(dossier => dossier.DossierType == DossierType.MAIN).Select(dossier => dossier.DossierCode).FirstOrDefault(),
                HorseName = item.Booking.BookingHorses.Where(link => horseIds.Contains(link.HorseId) && link.DetachedAt != null)
                    .Select(link => link.Horse.HorseName).FirstOrDefault()
            }).SingleOrDefaultAsync(cancellationToken);
    }
}
