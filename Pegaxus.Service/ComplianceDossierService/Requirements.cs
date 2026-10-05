using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;

namespace Pegaxus.Service.ComplianceDossierService;

public sealed partial class Service
{
    public async Task<Response.RequirementResponse> AddRequirementAsync(Guid accountId, Guid dossierId,
        Request.RequirementRequest request, CancellationToken cancellationToken = default)
    {
        await RequireSpecialistAsync(accountId, cancellationToken);
        ValidateRequirement(request.LeadTimeDays, request.Instruction, request.Reason);
        if (request.ComplianceRequirementId == Guid.Empty)
            throw new BadRequestException("ComplianceRequirementId is required.");
        if (request.DueDate.HasValue && request.LeadTimeDays.HasValue)
            throw new BadRequestException("Provide DueDate or LeadTimeDays, not both.");
        var dossier = await EditableDossierAsync(dossierId, cancellationToken);
        var booking = dossier.Transport.Booking;
        var requirement = await _db.ComplianceRequirements.SingleOrDefaultAsync(item =>
            item.ComplianceRequirementId == request.ComplianceRequirementId && !item.IsDeleted &&
            item.Status == ComplianceRequirementStatus.ACTIVE, cancellationToken)
            ?? throw new BadRequestException("The ComplianceRequirement is not ACTIVE", "REQUIREMENT_INACTIVE");
        var horse = request.HorseId.HasValue ? await _db.BookingHorses
            .Where(value => value.BookingId == booking.BookingId && value.HorseId == request.HorseId && !value.Horse.IsDeleted)
            .Select(value => value.Horse).SingleOrDefaultAsync(cancellationToken) : null;
        if (request.HorseId.HasValue && horse is null)
            throw new BadRequestException("horseId does not belong to the booking of this dossier", "HORSE_NOT_IN_BOOKING");
        if (await _db.DossierRequirements.AnyAsync(item => item.ComplianceDossierId == dossierId &&
            item.ComplianceRequirementId == request.ComplianceRequirementId && item.HorseId == request.HorseId &&
            !item.IsDeleted, cancellationToken))
            throw new ConflictException("This ComplianceRequirement is already required for this dossier/horse combination", "DR_DUPLICATE");

        var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        var item = new DossierRequirement
        {
            DossierRequirementId = Guid.NewGuid(), ComplianceDossierId = dossierId,
            ComplianceRequirementId = requirement.ComplianceRequirementId, HorseId = request.HorseId,
            IsMandatory = request.IsMandatory, DueDate = request.DueDate ?? (request.LeadTimeDays.HasValue
                ? DueDate(booking.RequestedDepartureDate, request.LeadTimeDays.Value) : null),
            Instruction = request.Instruction?.Trim(), Status = DossierRequirementStatus.PENDING,
            CreatedAt = now, UpdatedAt = now, ComplianceRequirement = requirement, Horse = horse
        };
        _db.DossierRequirements.Add(item);
        AuditRequirement(dossierId, item.DossierRequirementId, accountId, "ADDED",
            string.IsNullOrWhiteSpace(request.Reason) ? "DossierRequirement added" : request.Reason, now,
            new { item.ComplianceRequirementId, item.HorseId, item.IsMandatory, item.DueDate, item.Instruction });
        AddNotification(booking.Customer.AccountId, dossier, "DossierRequirement added", now,
            message: $"A new DossierRequirement ({requirement.RequirementName}) was added to your ComplianceDossier.");
        try { await _db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: "DossierRequirement_dossier_requirement_global_key" or
                    "DossierRequirement_dossier_requirement_horse_key" })
        {
            throw new ConflictException("This ComplianceRequirement is already required for this dossier/horse combination", "DR_DUPLICATE");
        }
        await _notificationService.FlushAsync(cancellationToken);
        return MapRequirement(item, booking.RequestedDepartureDate);
    }

    public async Task<Response.RequirementResponse> WaiveRequirementAsync(Guid accountId, Guid dossierId,
        Guid requirementId, Request.WaiveRequirementRequest request, CancellationToken cancellationToken = default)
    {
        await RequireSpecialistAsync(accountId, cancellationToken);
        if (string.IsNullOrWhiteSpace(request.WaivedReason))
            throw new BadRequestException("Please provide the reason for waiving this DossierRequirement", "WAIVE_REASON_REQUIRED");
        if (request.WaivedReason.Length > 2000)
            throw new BadRequestException("WaivedReason must not exceed 2000 characters.");
        var dossier = await EditableDossierAsync(dossierId, cancellationToken);
        var item = await _db.DossierRequirements.Include(value => value.ComplianceRequirement).Include(value => value.Horse)
            .SingleOrDefaultAsync(value => value.DossierRequirementId == requirementId &&
                value.ComplianceDossierId == dossierId && !value.IsDeleted, cancellationToken)
            ?? throw new NotFoundException("DossierRequirement not found.", "REQUIREMENT_NOT_FOUND");
        var before = new { item.Status, item.WaivedReason, item.SatisfiedAt };
        var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        item.Status = DossierRequirementStatus.WAIVED;
        item.WaivedReason = request.WaivedReason.Trim();
        item.SatisfiedAt = null;
        item.UpdatedAt = now;
        dossier.UpdatedAt = now;
        AuditRequirement(dossierId, requirementId, accountId, "WAIVED", item.WaivedReason, now,
            new { Before = before, After = new { item.Status, item.WaivedReason, item.SatisfiedAt } });
        AddNotification(dossier.Transport.Booking.Customer.AccountId, dossier, "DossierRequirement waived", now,
            message: $"DossierRequirement ({item.ComplianceRequirement.RequirementName}) was waived.");
        await _db.SaveChangesAsync(cancellationToken);
        await _notificationService.FlushAsync(cancellationToken);
        return MapRequirement(item, dossier.Transport.Booking.RequestedDepartureDate);
    }

    public async Task<Response.RequirementResponse> UpdateRequirementAsync(Guid accountId, Guid dossierId,
        Guid requirementId, Request.UpdateRequirementRequest request, CancellationToken cancellationToken = default)
    {
        await RequireSpecialistAsync(accountId, cancellationToken);
        ValidateReason(request.Reason);
        if (request.IsMandatory is null && request.LeadTimeDays is null && request.Instruction is null)
            throw new BadRequestException("Provide at least one DossierRequirement change.");
        if (request.LeadTimeDays is < 0 or > 3650)
            throw new BadRequestException("LeadTimeDays must be between 0 and 3650.");
        if (request.Instruction?.Length > 2000)
            throw new BadRequestException("Instruction must not exceed 2000 characters.");
        var dossier = await EditableDossierAsync(dossierId, cancellationToken);
        var item = await _db.DossierRequirements.Include(value => value.ComplianceRequirement).Include(value => value.Horse)
            .SingleOrDefaultAsync(value => value.DossierRequirementId == requirementId &&
                value.ComplianceDossierId == dossierId && !value.IsDeleted, cancellationToken)
            ?? throw new NotFoundException("DossierRequirement not found.", "REQUIREMENT_NOT_FOUND");
        // ponytail: protect every mandatory item; add a core marker if custom mandatory items must be downgraded.
        if (item.IsMandatory && request.IsMandatory == false)
            throw new ConflictException("Mandatory core DossierRequirements cannot be removed. You may only modify optional requirements.");

        var before = new { item.IsMandatory, item.DueDate, item.Instruction };
        if (request.IsMandatory.HasValue) item.IsMandatory = request.IsMandatory.Value;
        if (request.LeadTimeDays.HasValue)
            item.DueDate = DueDate(dossier.Transport.Booking.RequestedDepartureDate, request.LeadTimeDays.Value);
        if (request.Instruction is not null) item.Instruction = request.Instruction.Trim();
        var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        item.UpdatedAt = now;
        AuditRequirement(dossierId, requirementId, accountId, "UPDATED", request.Reason, now,
            new { Before = before, After = new { item.IsMandatory, item.DueDate, item.Instruction } });
        AddNotification(dossier.Transport.Booking.Customer.AccountId, dossier, "DossierRequirement updated", now,
            message: $"DossierRequirement ({item.ComplianceRequirement.RequirementName}) was updated.");
        await _db.SaveChangesAsync(cancellationToken);
        await _notificationService.FlushAsync(cancellationToken);
        return MapRequirement(item, dossier.Transport.Booking.RequestedDepartureDate);
    }

    public async Task<Response.RemovalResponse> RemoveRequirementAsync(Guid accountId, Guid dossierId, Guid requirementId,
        Request.RemoveRequirementRequest request, CancellationToken cancellationToken = default)
    {
        await RequireSpecialistAsync(accountId, cancellationToken);
        ValidateReason(request.Reason);
        var dossier = await EditableDossierAsync(dossierId, cancellationToken);
        var item = await _db.DossierRequirements.Include(value => value.ComplianceRequirement)
            .SingleOrDefaultAsync(value => value.DossierRequirementId == requirementId &&
                value.ComplianceDossierId == dossierId && !value.IsDeleted, cancellationToken)
            ?? throw new NotFoundException("DossierRequirement not found.", "REQUIREMENT_NOT_FOUND");
        if (item.IsMandatory)
            throw new ConflictException("Mandatory core DossierRequirements cannot be removed. You may only modify optional requirements.");
        var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        item.IsDeleted = true;
        item.DeletedAt = now;
        item.DeletedByAccountId = accountId;
        item.UpdatedAt = now;
        AuditRequirement(dossierId, requirementId, accountId, "REMOVED", request.Reason, now,
            new { item.ComplianceRequirementId, item.HorseId });
        AddNotification(dossier.Transport.Booking.Customer.AccountId, dossier, "DossierRequirement removed", now,
            message: $"DossierRequirement ({item.ComplianceRequirement.RequirementName}) was removed.");
        await _db.SaveChangesAsync(cancellationToken);
        await _notificationService.FlushAsync(cancellationToken);
        var statuses = await _db.DossierRequirements.AsNoTracking()
            .Where(value => value.ComplianceDossierId == dossierId && !value.IsDeleted)
            .Select(value => value.Status).ToListAsync(cancellationToken);
        var fulfilled = statuses.Count(status => status is DossierRequirementStatus.SATISFIED or DossierRequirementStatus.WAIVED);
        return new Response.RemovalResponse
        {
            DossierRequirementId = requirementId, FulfilledCount = fulfilled, RequirementCount = statuses.Count,
            CompletionPercent = statuses.Count == 0 ? 0 : 100m * fulfilled / statuses.Count
        };
    }

    private async Task<ComplianceDossier> EditableDossierAsync(Guid dossierId, CancellationToken cancellationToken)
    {
        var dossier = await _db.ComplianceDossiers.Include(item => item.Transport)
            .ThenInclude(item => item.Booking).ThenInclude(item => item.Customer)
            .SingleOrDefaultAsync(item => item.ComplianceDossierId == dossierId, cancellationToken)
            ?? throw new NotFoundException("ComplianceDossier not found.", "DOSSIER_NOT_FOUND");
        if (dossier.Status is not (ComplianceDossierStatus.DRAFT or ComplianceDossierStatus.IN_PREPARATION or ComplianceDossierStatus.READY_FOR_SUBMISSION))
            throw new ConflictException("Cannot modify DossierRequirements after dossier submission to official authorities.", "DOSSIER_LOCKED");
        return dossier;
    }


    private static DateOnly DueDate(DateOnly departure, int leadTimeDays)
    {
        if (leadTimeDays > departure.DayNumber)
            throw new BadRequestException("LeadTimeDays exceeds the departure date.");
        return departure.AddDays(-leadTimeDays);
    }

    private static void ValidateRequirement(int? leadTimeDays, string? instruction, string reason)
    {
        if (leadTimeDays is < 0 or > 3650)
            throw new BadRequestException("LeadTimeDays must be between 0 and 3650.");
        if (instruction?.Length > 2000)
            throw new BadRequestException("Instruction must not exceed 2000 characters.");
        if (!string.IsNullOrWhiteSpace(reason)) ValidateReason(reason);
    }

    private static void ValidateReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 500)
            throw new BadRequestException("Reason is required and must not exceed 500 characters.");
    }

    private void AuditRequirement(Guid dossierId, Guid requirementId, Guid accountId, string action,
        string reason, DateTime now, object details) => _db.AuditDossiers.Add(new AuditDossier
    {
        AuditDossierId = Guid.NewGuid(), ComplianceDossierId = dossierId, TargetEntity = nameof(DossierRequirement),
        TargetId = requirementId, ActorAccountId = accountId, ActionType = $"DOSSIER_REQUIREMENT_{action}", Summary = reason.Trim(),
        Details = JsonSerializer.SerializeToDocument(details), Visibility = AuditVisibility.Internal, CreatedAt = now
    });

    private static Response.RequirementResponse MapRequirement(DossierRequirement item, DateOnly departureDate) => new()
    {
        DossierRequirementId = item.DossierRequirementId, ComplianceRequirementId = item.ComplianceRequirementId,
        RequirementCode = item.ComplianceRequirement.RequirementCode, RequirementName = item.ComplianceRequirement.RequirementName,
        HorseId = item.HorseId, HorseName = item.Horse?.HorseName, IsMandatory = item.IsMandatory,
        Status = item.Status.ToString(), DueDate = item.DueDate,
        LeadTimeDays = item.DueDate.HasValue ? departureDate.DayNumber - item.DueDate.Value.DayNumber : null,
        Instruction = item.Instruction,
        OpenDocumentRequest = item.DocumentRequests.Where(request => request.Status == DocumentRequestStatus.OPEN)
            .OrderByDescending(request => request.RequestedAt).ThenByDescending(request => request.DocumentRequestId)
            .Select(MapDocumentRequest).FirstOrDefault()
    };
}
