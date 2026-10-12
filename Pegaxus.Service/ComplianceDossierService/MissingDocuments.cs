using System.Data;
using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;

namespace Pegaxus.Service.ComplianceDossierService;

public sealed partial class Service
{
    public async Task<IReadOnlyList<Response.RequirementResponse>> GetMissingRequirementsAsync(Guid accountId, Guid dossierId,
        CancellationToken cancellationToken = default)
    {
        await RequireSpecialistAsync(accountId, cancellationToken);
        var dossier = await MissingReminderDossierAsync(dossierId, false, cancellationToken);
        EnsurePreparingForReminder(dossier);
        var requirements = await MissingRequirementsQuery(dossierId).AsNoTracking().Include(item => item.ComplianceRequirement)
            .Include(item => item.Horse).OrderBy(item => item.ComplianceRequirement.RequirementName).ToListAsync(cancellationToken);
        return requirements.Select(item => MapRequirement(item, dossier.Transport.Booking.RequestedDepartureDate)).ToList();
    }

    public async Task<Response.MissingDocumentsResponse> RequestMissingDocumentsAsync(Guid accountId, Guid dossierId,
        Request.MissingDocumentsRequest request, CancellationToken cancellationToken = default)
    {
        await RequireSpecialistAsync(accountId, cancellationToken);
        if (_mailService is null)
            throw new InvalidOperationException("MailService must be configured for missing-document reminders.");
        var now = ReminderNow();
        var today = DateOnly.FromDateTime(now);
        if (request.RequirementIds is null || request.RequirementIds.Count is < 1 or > 100 ||
            request.RequirementIds.Any(item => item == Guid.Empty) || request.RequirementIds.Distinct().Count() != request.RequirementIds.Count)
            throw new BadRequestException("Select 1 to 100 distinct DossierRequirement IDs.");
        if (request.DueDate == default || request.DueDate < today || request.Note?.Length > 2000)
            throw new BadRequestException("DueDate cannot be in the past; Note must not exceed 2000 characters.");
        Response.MissingDocumentsResponse response;
        await using (var transaction = await BeginReminderTransactionAsync(cancellationToken))
        {
            try
            {
                var dossier = await MissingReminderDossierAsync(dossierId, true, cancellationToken);
                EnsurePreparingForReminder(dossier);
                if (request.DueDate > dossier.Transport.Booking.RequestedDepartureDate)
                    throw new BadRequestException("DueDate must not be after departure.");
                var missing = await MissingRequirementsQuery(dossierId).AsTracking().Include(item => item.ComplianceRequirement)
                    .Include(item => item.Horse).OrderBy(item => item.DossierRequirementId).ToListAsync(cancellationToken);
                if (missing.Count == 0)
                    throw new ConflictException("Tất cả các mục giấy tờ đã được tải lên hoặc đã được duyệt. Không có giấy tờ nào cần nhắc nộp.");
                var selected = missing.Where(item => request.RequirementIds.Contains(item.DossierRequirementId)).ToList();
                if (selected.Count != request.RequirementIds.Count)
                    throw new BadRequestException("Every selected requirement must belong to this dossier and still be missing.");
                var openRequests = await _db.DocumentRequests.Where(item => request.RequirementIds.Contains(item.DossierRequirementId) &&
                    item.Status == DocumentRequestStatus.OPEN && item.RequestType == DocumentRequestType.MISSING_DOCUMENT)
                    .OrderByDescending(item => item.RequestedAt).ToListAsync(cancellationToken);
                foreach (var item in selected)
                {
                    await _db.Entry(item).ReloadAsync(cancellationToken);
                    var previousStatus = item.Status;
                    var previousDueDate = item.DueDate;
                    item.Status = DossierRequirementStatus.REQUESTED;
                    item.DueDate = request.DueDate;
                    item.UpdatedAt = now;
                    var revision = openRequests.FirstOrDefault(value => value.DossierRequirementId == item.DossierRequirementId);
                    if (revision is null)
                    {
                        revision = new DocumentRequest
                        {
                            DocumentRequestId = Guid.NewGuid(), DossierRequirementId = item.DossierRequirementId,
                            RequestedByAccountId = accountId, RequestCode = $"DRQ-{Guid.NewGuid():N}",
                            RequestType = DocumentRequestType.MISSING_DOCUMENT, Status = DocumentRequestStatus.OPEN, CreatedAt = now
                        };
                        _db.DocumentRequests.Add(revision);
                    }
                    revision.RequestedByAccountId = accountId;
                    revision.Message = MissingReminderMessage(dossier, [item], request.DueDate, request.Note);
                    revision.RequestedAt = now;
                    revision.DueDate = request.DueDate;
                    revision.UpdatedAt = now;
                    _db.AuditDossiers.Add(new AuditDossier
                    {
                        AuditDossierId = Guid.NewGuid(), ComplianceDossierId = dossierId, ActorAccountId = accountId,
                        ActionType = "DOSSIER_REQUIREMENT_REMINDED", TargetEntity = nameof(DossierRequirement), TargetId = item.DossierRequirementId,
                        Summary = "Nhắc nộp bổ sung giấy tờ còn thiếu.", Visibility = AuditVisibility.Public, CreatedAt = now,
                        Details = JsonSerializer.SerializeToDocument(new { PreviousStatus = previousStatus.ToString(), PreviousDueDate = previousDueDate,
                            NewStatus = item.Status.ToString(), item.DueDate, DocumentRequestId = revision.DocumentRequestId, request.Note })
                    });
                }
                dossier.UpdatedAt = now;
                var reminder = QueueMissingReminder(dossier, selected, request.DueDate, request.Note, null, now);
                await _db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                response = new Response.MissingDocumentsResponse
                {
                    ReminderId = reminder.MissingDocumentReminderId,
                    Requirements = selected.Select(item => MapRequirement(item, dossier.Transport.Booking.RequestedDepartureDate)).ToList()
                };
            }
            catch (Exception error) when (ReminderWriteConflict(error))
            {
                throw new ConflictException("The dossier changed. Reload before requesting missing documents.", "MISSING_DOCUMENT_REMINDER_CONFLICT");
            }
        }
        await _notificationService.FlushAsync(CancellationToken.None);
        response.EmailSent = await DeliverMissingReminderAsync(response.ReminderId, CancellationToken.None);
        if (!response.EmailSent)
            response.DeliveryWarning = "Email chưa được xác nhận gửi; thông báo Web đã lưu. Hệ thống sẽ thử gửi lại.";
        return response;
    }

    public async Task<int> QueueAutomaticMissingDocumentRemindersAsync(CancellationToken cancellationToken = default)
    {
        var now = ReminderNow();
        var today = DateOnly.FromDateTime(now);
        var dayStart = today.ToDateTime(TimeOnly.MinValue);
        var tomorrow = dayStart.AddDays(1);
        var domesticLimit = today.AddDays(21);
        var internationalLimit = today.AddDays(60);
        var candidates = await _db.ComplianceDossiers.AsNoTracking().Where(item => item.Status == ComplianceDossierStatus.IN_PREPARATION &&
            item.Transport.Booking.Status != BookingStatus.CANCELLED && item.Transport.Booking.Status != BookingStatus.REJECTED &&
            item.DossierRequirements.Any(requirement => !requirement.IsDeleted &&
                (requirement.Status == DossierRequirementStatus.PENDING || requirement.Status == DossierRequirementStatus.REQUESTED) &&
                (requirement.ComplianceDocument == null || !requirement.ComplianceDocument.DocumentVersions.Any(version => version.FileUrl != null && version.FileUrl.Trim() != ""))) &&
            item.Transport.Booking.RequestedDepartureDate >= today &&
            ((item.Transport.Booking.OriginCountry.Trim().ToLower() == item.Transport.Booking.DestinationCountry.Trim().ToLower() &&
                item.Transport.Booking.RequestedDepartureDate <= domesticLimit) ||
             (item.Transport.Booking.OriginCountry.Trim().ToLower() != item.Transport.Booking.DestinationCountry.Trim().ToLower() &&
                item.Transport.Booking.RequestedDepartureDate <= internationalLimit)) &&
            !_db.MissingDocumentReminders.Any(reminder => reminder.ComplianceDossierId == item.ComplianceDossierId &&
                ((reminder.CreatedAt >= dayStart && reminder.CreatedAt < tomorrow) ||
                 (reminder.EmailSentAt == null && reminder.CancelledAt == null))))
            .OrderBy(item => item.Transport.Booking.RequestedDepartureDate).ThenBy(item => item.ComplianceDossierId)
            .Select(item => item.ComplianceDossierId).Take(100).ToListAsync(cancellationToken);
        var queued = 0;
        foreach (var dossierId in candidates)
        {
            await using var transaction = await BeginReminderTransactionAsync(cancellationToken);
            try
            {
                var dossier = await MissingReminderDossierAsync(dossierId, true, cancellationToken);
                if (!AutomaticReminderEligible(dossier, today) || await _db.MissingDocumentReminders.AnyAsync(item => item.ComplianceDossierId == dossierId &&
                    ((item.CreatedAt >= dayStart && item.CreatedAt < tomorrow) || (item.EmailSentAt == null && item.CancelledAt == null)), cancellationToken))
                    continue;
                var missing = await MissingRequirementsQuery(dossierId).AsNoTracking().Include(item => item.ComplianceRequirement)
                    .Include(item => item.Horse).OrderBy(item => item.DossierRequirementId).ToListAsync(cancellationToken);
                if (missing.Count == 0)
                    continue;
                QueueMissingReminder(dossier, missing, today, null, today, now);
                await _db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                queued++;
                await _notificationService.FlushAsync(cancellationToken);
            }
            catch (Exception error) when (ReminderWriteConflict(error))
            {
                _db.ChangeTracker.Clear();
                return queued;
            }
        }
        return queued;
    }

    public async Task DeliverPendingMissingDocumentRemindersAsync(CancellationToken cancellationToken = default)
    {
        var retryBefore = ReminderNow().AddMinutes(-5);
        var ids = await _db.MissingDocumentReminders.AsNoTracking().Where(item => item.EmailSentAt == null && item.CancelledAt == null &&
            (item.LastAttemptAt == null || item.LastAttemptAt <= retryBefore)).OrderBy(item => item.CreatedAt)
            .Select(item => item.MissingDocumentReminderId).Take(100).ToListAsync(cancellationToken);
        foreach (var id in ids)
            await DeliverMissingReminderAsync(id, cancellationToken);
    }

    private async Task<bool> DeliverMissingReminderAsync(Guid reminderId, CancellationToken cancellationToken)
    {
        var mail = _mailService ?? throw new InvalidOperationException("MailService must be configured for missing-document reminders.");
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        // Updating the key to itself holds a row lock until the transaction ends.
        if (_db.Database.IsRelational())
            await _db.MissingDocumentReminders.Where(item => item.MissingDocumentReminderId == reminderId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.MissingDocumentReminderId, item => item.MissingDocumentReminderId), cancellationToken);
        var reminders = _db.MissingDocumentReminders.AsQueryable();
        var reminder = await reminders.AsTracking().SingleAsync(item => item.MissingDocumentReminderId == reminderId, cancellationToken);
        await _db.Entry(reminder).ReloadAsync(cancellationToken);
        var now = ReminderNow();
        if (reminder.EmailSentAt.HasValue || reminder.CancelledAt.HasValue || reminder.LastAttemptAt > now.AddMinutes(-5))
            return reminder.EmailSentAt.HasValue;
        var dossier = await MissingReminderDossierAsync(reminder.ComplianceDossierId, false, cancellationToken);
        var missing = await MissingRequirementsQuery(reminder.ComplianceDossierId).AsNoTracking()
            .Where(item => reminder.RequirementIds.Contains(item.DossierRequirementId))
            .Include(item => item.ComplianceRequirement).Include(item => item.Horse).ToListAsync(cancellationToken);
        var recipient = await _db.Accounts.AsNoTracking().SingleAsync(item => item.AccountId == reminder.RecipientAccountId, cancellationToken);
        if (dossier.Status != ComplianceDossierStatus.IN_PREPARATION || dossier.Transport.Booking.Status is BookingStatus.CANCELLED or BookingStatus.REJECTED ||
            dossier.Transport.Booking.RequestedDepartureDate < DateOnly.FromDateTime(now) || missing.Count == 0 || recipient.IsDeleted || recipient.Status != AccountStatus.ACTIVE)
            reminder.CancelledAt = now;
        else if (dossier.Transport.Booking.Customer.AccountId != reminder.RecipientAccountId)
        {
            reminder.CancelledAt = now;
        }
        else
        {
            reminder.LastAttemptAt = now;
            reminder.EmailAttempts++;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            try
            {
                await mail.SendMail(new MailService.MailContent
                {
                    To = recipient.Email,
                    Subject = $"Missing documents reminder — {dossier.DossierCode}",
                    Body = EmailBodies.MissingDocumentsBody(
                        dossierCode: dossier.DossierCode,
                        bookingCode: dossier.Transport.Booking.BookingCode,
                        generalDueDate: reminder.DueDate,
                        note: reminder.Note,
                        actionUrl: null,
                        missingItems: missing.Select(item => new EmailBodies.MissingItem(
                            RequirementName: item.ComplianceRequirement.RequirementName,
                            HorseName: item.Horse?.HorseName,
                            DueDate: item.DueDate)))
                }, timeout.Token);
                reminder.EmailSentAt = ReminderNow();
            }
            catch (Exception error) when (error is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                Console.Error.WriteLine($"MissingDocumentReminder {reminderId}: email pending retry ({error.GetType().Name}).");
            }
        }
        await _db.SaveChangesAsync(CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);
        return reminder.EmailSentAt.HasValue;
    }

    private MissingDocumentReminder QueueMissingReminder(ComplianceDossier dossier, List<DossierRequirement> requirements,
        DateOnly dueDate, string? note, DateOnly? automaticDate, DateTime now)
    {
        var reminder = new MissingDocumentReminder
        {
            MissingDocumentReminderId = Guid.NewGuid(), ComplianceDossierId = dossier.ComplianceDossierId,
            RecipientAccountId = dossier.Transport.Booking.Customer.AccountId, RequirementIds = requirements.Select(item => item.DossierRequirementId).ToArray(),
            DueDate = dueDate, Note = note?.Trim(), AutomaticDate = automaticDate, CreatedAt = now
        };
        _db.MissingDocumentReminders.Add(reminder);
        AddNotification(reminder.RecipientAccountId, dossier, "Nhắc nộp bổ sung giấy tờ còn thiếu", now,
            message: MissingReminderMessage(dossier, requirements, dueDate, note));
        return reminder;
    }

    private IQueryable<DossierRequirement> MissingRequirementsQuery(Guid dossierId) => _db.DossierRequirements.Where(item =>
        item.ComplianceDossierId == dossierId && !item.IsDeleted &&
        (item.Status == DossierRequirementStatus.PENDING || item.Status == DossierRequirementStatus.REQUESTED) &&
        (item.ComplianceDocument == null || !item.ComplianceDocument.DocumentVersions.Any(version => version.FileUrl != null && version.FileUrl.Trim() != "")));

    private async Task<ComplianceDossier> MissingReminderDossierAsync(Guid dossierId, bool forUpdate, CancellationToken cancellationToken)
    {
        if (forUpdate && _db.Database.IsRelational())
            await _db.ComplianceDossiers.Where(item => item.ComplianceDossierId == dossierId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.ComplianceDossierId, item => item.ComplianceDossierId), cancellationToken);
        var query = _db.ComplianceDossiers.AsQueryable();
        query = forUpdate ? query.AsTracking() : query.AsNoTracking();
        var dossier = await query.Include(item => item.Transport).ThenInclude(item => item.Booking).ThenInclude(item => item.Customer)
            .SingleOrDefaultAsync(item => item.ComplianceDossierId == dossierId, cancellationToken)
            ?? throw new NotFoundException("ComplianceDossier not found.", "DOSSIER_NOT_FOUND");
        if (forUpdate)
        {
            await _db.Entry(dossier).ReloadAsync(cancellationToken);
            await _db.Entry(dossier.Transport.Booking).ReloadAsync(cancellationToken);
        }
        return dossier;
    }

    private static void EnsurePreparingForReminder(ComplianceDossier dossier)
    {
        if (dossier.Status != ComplianceDossierStatus.IN_PREPARATION || dossier.Transport.Booking.Status is BookingStatus.CANCELLED or BookingStatus.REJECTED)
            throw new ConflictException("Không thể gửi nhắc nhở khi hồ sơ đã hoàn tất hoặc đã đóng.");
    }

    private static bool AutomaticReminderEligible(ComplianceDossier dossier, DateOnly today)
    {
        var days = dossier.Transport.Booking.RequestedDepartureDate.DayNumber - today.DayNumber;
        return dossier.Status == ComplianceDossierStatus.IN_PREPARATION && dossier.Transport.Booking.Status is not (BookingStatus.CANCELLED or BookingStatus.REJECTED) &&
            days >= 0 && days <= (GetRouteType(dossier) == "DOMESTIC" ? 21 : 60);
    }

    private static string MissingReminderMessage(ComplianceDossier dossier, List<DossierRequirement> requirements, DateOnly dueDate, string? note) =>
        $"Hồ sơ {dossier.DossierCode}: vui lòng nộp bổ sung các giấy tờ còn thiếu.\n" +
        string.Join("\n", requirements.Select(item => $"- {item.ComplianceRequirement.RequirementName}" +
            (item.Horse is null ? "" : $" ({item.Horse.HorseName})") + $"; hạn nộp: {item.DueDate ?? dueDate:yyyy-MM-dd}")) +
        (string.IsNullOrWhiteSpace(note) ? "" : $"\n{note.Trim()}");

    private DateTime ReminderNow() => DateTime.SpecifyKind(_reminderClock.GetUtcNow().UtcDateTime, DateTimeKind.Unspecified);

    private Task<IDbContextTransaction> BeginReminderTransactionAsync(CancellationToken cancellationToken) => _db.Database.IsRelational()
        ? _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
        : _db.Database.BeginTransactionAsync(cancellationToken);

    private static bool ReminderWriteConflict(Exception error) => error is DbUpdateConcurrencyException ||
        error is DbUpdateException { InnerException: PostgresException { SqlState: "23505" or "40001" or "40P01" } } ||
        error is PostgresException { SqlState: "23505" or "40001" or "40P01" };
}
