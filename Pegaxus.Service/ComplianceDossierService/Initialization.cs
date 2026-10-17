using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Repository.Extensions;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;

namespace Pegaxus.Service.ComplianceDossierService;

public sealed partial class Service
{
    public async Task<IReadOnlyList<Response.TemplateOptionResponse>> GetTemplateOptionsAsync(
        Guid accountId, Guid bookingId, CancellationToken cancellationToken = default)
    {
        await RequireSpecialistAsync(accountId, cancellationToken);
        var booking = await GetBookingForTemplateOptionsAsync(bookingId, cancellationToken);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var origin = booking.OriginCountry.Trim().ToLowerInvariant();
        var destination = booking.DestinationCountry.Trim().ToLowerInvariant();
        // ponytail: rank by requirement country; add explicit template routes when route metadata exists.
        return await _db.DossierTemplates.AsNoTracking()
            .Where(template => !template.IsDeleted && template.Status == DossierTemplateStatus.ACTIVE &&
                (template.EffectiveFrom == null || template.EffectiveFrom <= today) &&
                (template.EffectiveTo == null || template.EffectiveTo >= today) &&
                template.DossierTemplateItems.Any() && template.DossierTemplateItems.All(item =>
                    !item.ComplianceRequirement.IsDeleted && item.ComplianceRequirement.Status == ComplianceRequirementStatus.ACTIVE))
            .Select(template => new Response.TemplateOptionResponse
            {
                DossierTemplateId = template.DossierTemplateId, TemplateCode = template.TemplateCode,
                TemplateName = template.TemplateName, VersionNo = template.VersionNo,
                MatchingRequirements = template.DossierTemplateItems.Count(item => item.ComplianceRequirement.Country != null &&
                    (item.ComplianceRequirement.Country.Trim().ToLower() == origin || item.ComplianceRequirement.Country.Trim().ToLower() == destination))
            })
            .OrderByDescending(template => template.MatchingRequirements).ThenBy(template => template.TemplateCode)
            .ToListAsync(cancellationToken);
    }

    public async Task<Response.InitializationResponse> PreviewAsync(
        Guid accountId, Guid transportId, Request.InitializeRequest request, CancellationToken cancellationToken = default)
    {
        await RequireSpecialistAsync(accountId, cancellationToken);
        var (booking, requirements) = await PrepareInitializationAsync(transportId, request, cancellationToken, byTransportId: true);
        return InitializationResponse(booking, request, requirements);
    }

    public async Task<Response.InitializationResponse> InitializeAsync(
        Guid accountId, Guid bookingId, Request.InitializeRequest request, CancellationToken cancellationToken = default)
    {
        await RequireSpecialistAsync(accountId, cancellationToken);
        await using var transaction = await _db.Database.BeginSerializableAsync(cancellationToken);
        // The negotiated status change takes the same lock before changing the booking or its dossier.
            // Updating the key to itself holds a row lock until the transaction ends.
        await _db.LockRowsAsync(_db.Bookings.Where(item => item.BookingId == bookingId), item => item.BookingId, cancellationToken);

        var (booking, requirements) = await PrepareInitializationAsync(bookingId, request, cancellationToken);
        var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        var dossier = new ComplianceDossier
        {
            ComplianceDossierId = Guid.NewGuid(), DossierCode = await NextDossierCodeAsync(now, cancellationToken),
            TransportId = booking.Transport!.TransportId, DossierTemplateId = request.DossierTemplateId,
            Status = ComplianceDossierStatus.IN_PREPARATION, Note = request.Note?.Trim(),
            OpenedAt = now, CreatedAt = now, UpdatedAt = now
        };
        foreach (var item in requirements)
        {
            item.DossierRequirementId = Guid.NewGuid();
            dossier.DossierRequirements.Add(new DossierRequirement
            {
                DossierRequirementId = item.DossierRequirementId.Value, ComplianceRequirementId = item.ComplianceRequirementId,
                HorseId = item.HorseId, IsMandatory = item.IsMandatory, Instruction = item.Instruction,
                ValidUntil = item.ValidUntil, Status = DossierRequirementStatus.PENDING,
                CreatedAt = now, UpdatedAt = now
            });
        }
        _db.ComplianceDossiers.Add(dossier);
        AddNotification(booking.Customer.AccountId, null, dossier, "Compliance dossier opened",
            $"Compliance dossier {dossier.DossierCode} is ready for booking {booking.BookingCode}. Open it to see the documents you need to upload.", now);
        await QueueDossierUpdateMailAsync(booking.Customer.AccountId, dossier, MailService.EmailBodies.DossierUpdateKind.Opened, null, null, now, cancellationToken);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "ComplianceDossier_transport_id_dossier_type_key" })
        {
            throw DossierAlreadyExists();
        }
        await _notificationService.FlushAsync(cancellationToken);
        return InitializationResponse(booking, request, requirements, dossier);
    }

    private async Task<(Repository.Entities.Booking Booking, List<Response.GeneratedRequirementResponse> Requirements)> PrepareInitializationAsync(
        Guid id, Request.InitializeRequest request, CancellationToken cancellationToken, bool byTransportId = false)
    {
        var validation = await _initializeValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
            throw new BadRequestException(string.Join(" ", validation.Errors.Select(error => error.ErrorMessage)));
        var booking = byTransportId
            ? await GetInitializationBookingByTransportAsync(id, request.BookingUpdatedAt, cancellationToken)
            : await GetInitializationBookingByBookingAsync(id, request.BookingUpdatedAt, cancellationToken);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var items = request.Items;
        if (request.DossierTemplateId is Guid templateId)
        {
            var template = await _db.DossierTemplates.AsNoTracking().Include(value => value.DossierTemplateItems)
                .SingleOrDefaultAsync(value => value.DossierTemplateId == templateId && !value.IsDeleted, cancellationToken)
                ?? throw new NotFoundException("DossierTemplate not found.", "DOSSIER_TEMPLATE_NOT_FOUND");
            if (template.Status != DossierTemplateStatus.ACTIVE || template.EffectiveFrom > today || template.EffectiveTo < today)
                throw new BadRequestException("Select an ACTIVE DossierTemplate that is effective today.");
            items ??= template.DossierTemplateItems.OrderBy(item => item.SequenceNo).Select(item => new Request.InitializationItemRequest
            {
                ComplianceRequirementId = item.ComplianceRequirementId, IsMandatory = item.IsMandatory,
                AppliesPerHorse = item.AppliesPerHorse, Instruction = item.Instruction
            }).ToList();
        }
        if (items is null || items.Count == 0)
            throw new BadRequestException("Select at least one ComplianceRequirement.");
        var ids = items.Select(item => item.ComplianceRequirementId).ToArray();
        var requirements = await _db.ComplianceRequirements.AsNoTracking()
            .Where(requirement => ids.Contains(requirement.ComplianceRequirementId) && !requirement.IsDeleted &&
                requirement.Status == ComplianceRequirementStatus.ACTIVE)
            .ToDictionaryAsync(requirement => requirement.ComplianceRequirementId, cancellationToken);
        if (requirements.Count != ids.Length)
            throw new BadRequestException("All selected ComplianceRequirements must be unique, existing and ACTIVE.");

        var result = new List<Response.GeneratedRequirementResponse>();
        foreach (var item in items)
        {
            var requirement = requirements[item.ComplianceRequirementId];
            if (requirement.ValidityPeriodDays is int days && (days <= 0 || days > DateOnly.MaxValue.DayNumber - today.DayNumber))
                throw new BadRequestException($"ValidityPeriodDays is invalid for {requirement.RequirementCode}.");
            var horses = item.AppliesPerHorse
                ? booking.BookingHorses.OrderBy(horse => horse.HorseId).Select(horse => horse.Horse).ToArray()
                : new Repository.Entities.Horse?[] { null };
            foreach (var horse in horses)
                result.Add(new Response.GeneratedRequirementResponse
                {
                    ComplianceRequirementId = requirement.ComplianceRequirementId, RequirementCode = requirement.RequirementCode,
                    RequirementName = requirement.RequirementName, HorseId = horse?.HorseId, HorseName = horse?.HorseName,
                    IsMandatory = item.IsMandatory, Instruction = item.Instruction?.Trim(),
                    ValidUntil = requirement.ValidityPeriodDays is int period ? today.AddDays(period) : null
                });
        }
        return (booking, result);
    }

    private async Task<Repository.Entities.Booking> GetInitializationBookingByBookingAsync(
        Guid bookingId, DateTimeOffset? expectedUpdatedAt, CancellationToken cancellationToken)
    {
        var booking = await _db.Bookings.AsNoTracking()
            .Include(value => value.Customer)
            .Include(value => value.BookingHorses).ThenInclude(value => value.Horse)
            .Include(value => value.Transport).ThenInclude(value => value!.ComplianceDossiers)
            .SingleOrDefaultAsync(value => value.BookingId == bookingId, cancellationToken)
            ?? throw new NotFoundException("Booking not found.", "BOOKING_NOT_FOUND");
        var transport = booking.Transport;
        if (transport is null)
            throw new NotFoundException("Transport not found.", "TRANSPORT_NOT_FOUND");
        if (expectedUpdatedAt.HasValue &&
            (DateTime.SpecifyKind(booking.UpdatedAt, DateTimeKind.Utc) != expectedUpdatedAt.Value.UtcDateTime || booking.Status != BookingStatus.APPROVED))
            throw new ConflictException("Operation aborted: The associated Booking status has changed and is no longer APPROVED.", "BOOKING_STATUS_CHANGED");
        if (booking.Status != BookingStatus.APPROVED)
            throw new ConflictException("ComplianceDossier can only be initialized for APPROVED transport bookings.", "BOOKING_NOT_APPROVED");
        if (transport.Status is TransportStatus.CANCELLED or TransportStatus.COMPLETED)
            throw new ConflictException("The Transport is no longer active.", "TRANSPORT_NOT_ACTIVE");
        if (transport.MainDossier is not null)
            throw DossierAlreadyExists();
        if (booking.BookingHorses.Count == 0 || booking.BookingHorses.Any(item => item.Horse.IsDeleted || item.Horse.CustomerId != booking.CustomerId))
            throw new BadRequestException("The booking must contain available horses owned by its customer.");
        return booking;
    }

    private async Task<Repository.Entities.Booking> GetInitializationBookingByTransportAsync(
        Guid transportId, DateTimeOffset? expectedUpdatedAt, CancellationToken cancellationToken)
    {
        var transport = await _db.Transports.AsNoTracking()
            .Include(value => value.Booking).ThenInclude(value => value.Customer)
            .Include(value => value.Booking).ThenInclude(value => value.BookingHorses).ThenInclude(value => value.Horse)
            .Include(value => value.ComplianceDossiers)
            .SingleOrDefaultAsync(value => value.TransportId == transportId, cancellationToken);
        if (transport is null)
        {
            // Internal compatibility for legacy service callers only. The public
            // Preview endpoint accepts transportId exclusively.
            return await GetInitializationBookingByBookingAsync(transportId, expectedUpdatedAt, cancellationToken);
        }
        var booking = transport.Booking;
        if (expectedUpdatedAt.HasValue &&
            (DateTime.SpecifyKind(booking.UpdatedAt, DateTimeKind.Utc) != expectedUpdatedAt.Value.UtcDateTime || booking.Status != BookingStatus.APPROVED))
            throw new ConflictException("Operation aborted: The associated Booking status has changed and is no longer APPROVED.", "BOOKING_STATUS_CHANGED");
        if (booking.Status != BookingStatus.APPROVED)
            throw new ConflictException("ComplianceDossier can only be initialized for APPROVED transport bookings.", "BOOKING_NOT_APPROVED");
        if (transport.Status is TransportStatus.CANCELLED or TransportStatus.COMPLETED)
            throw new ConflictException("The Transport is no longer active.", "TRANSPORT_NOT_ACTIVE");
        if (transport.MainDossier is not null)
            throw DossierAlreadyExists();
        if (booking.BookingHorses.Count == 0 || booking.BookingHorses.Any(item => item.Horse.IsDeleted || item.Horse.CustomerId != booking.CustomerId))
            throw new BadRequestException("The booking must contain available horses owned by its customer.");
        return booking;
    }

    private async Task<Repository.Entities.Booking> GetBookingForTemplateOptionsAsync(
        Guid bookingId, CancellationToken cancellationToken)
    {
        var booking = await _db.Bookings.AsNoTracking()
            .SingleOrDefaultAsync(value => value.BookingId == bookingId, cancellationToken)
            ?? throw new NotFoundException("Booking not found.", "BOOKING_NOT_FOUND");
        if (booking.Status != BookingStatus.APPROVED)
            throw new ConflictException("DossierTemplates can only be suggested for APPROVED transport bookings.", "BOOKING_NOT_APPROVED");
        return booking;
    }

    private async Task RequireSpecialistAsync(Guid accountId, CancellationToken cancellationToken, string? errorMessage = null)
    {
        if (!await _db.Employees.Active().AnyAsync(employee => employee.AccountId == accountId && employee.Role == EmployeeRole.TRANSPORT_SPECIALIST && employee.Account.AccountType == AccountType.EMPLOYEE, cancellationToken))
            throw new ForbiddenException(errorMessage ?? "Only an active Transport Specialist can manage ComplianceDossiers.");
    }

    private static ConflictException DossierAlreadyExists() => new(
        "A ComplianceDossier already exists for this booking. Please manage individual DossierRequirements instead.", "DOSSIER_ALREADY_EXISTS");

    private static Response.InitializationResponse InitializationResponse(Repository.Entities.Booking booking,
        Request.InitializeRequest request, List<Response.GeneratedRequirementResponse> requirements, ComplianceDossier? dossier = null) => new()
    {
        ComplianceDossierId = dossier?.ComplianceDossierId, DossierCode = dossier?.DossierCode,
        BookingId = booking.BookingId, BookingUpdatedAt = new DateTimeOffset(DateTime.SpecifyKind(booking.UpdatedAt, DateTimeKind.Utc)),
        TransportId = booking.Transport!.TransportId, DossierTemplateId = request.DossierTemplateId, Requirements = requirements,
        Items = requirements.GroupBy(item => item.ComplianceRequirementId).Select(group => new Request.InitializationItemRequest
        {
            ComplianceRequirementId = group.Key, AppliesPerHorse = group.First().HorseId.HasValue,
            IsMandatory = group.First().IsMandatory, Instruction = group.First().Instruction
        }).ToList()
    };
}
