using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Common;
using Pegaxus.Repository.Extensions;
using Pegaxus.Service.MediaService;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

using static Pegaxus.Service.Common.DbTime;

using NotificationService = Pegaxus.Service.NotificationService;

namespace Pegaxus.Service.ComplianceDossierService;

public sealed partial class Service : IComplianceDossierService
{
    private const long MaxProofFileSize = 2 * 1024 * 1024;

    private static readonly HashSet<string> AuditActionTypes =
    [
        "DOSSIER_INITIALIZED", "DOSSIER_STATUS_CHANGED", "DOSSIER_REQUIREMENT_ADDED",
        "DOSSIER_REQUIREMENT_REMOVED", "DOCUMENT_VERSION_SUBMITTED", "DOCUMENT_VERSION_UPDATED",
        "DOCUMENT_METADATA_UPDATED", "DOCUMENT_REVIEW_APPROVED",
        "DOCUMENT_REVIEW_REJECTED", "DOCUMENT_REVIEW_REVISION_REQUIRED", "DOCUMENT_REVIEW_REVOKED", "DOCUMENT_REQUEST_CREATED", "DOCUMENT_REQUEST_COMMENTED",
        "DOSSIER_REQUIREMENT_UPDATED", "DOSSIER_REQUIREMENT_WAIVED", "DOSSIER_REQUIREMENT_REMINDED",
        "AUTHORITY_SUBMITTED", "AUTHORITY_DECISION_UPDATED", "QUARANTINE_PROOF_SUBMITTED",
        "QUARANTINE_RESULT_FINALIZED", "CUSTOMS_PROOF_SUBMITTED", "CUSTOMS_RESULT_FINALIZED"
    ];

    private readonly AppDbContext _db;
    private readonly MediaService.IMediaService _mediaService;
    private readonly IValidator<Request.InitializeRequest> _initializeValidator;
    private readonly NotificationService.INotificationService _notificationService;
    private readonly DocumentStorageService.IDocumentStorageService _documentStorage;
    private readonly ILogger<Service> _logger;
    private readonly MailService.IMailService? _mailService;
    private readonly TimeProvider _reminderClock;

    public Service(
        AppDbContext db,
        MediaService.IMediaService mediaService,
        IValidator<Request.InitializeRequest> initializeValidator,
        NotificationService.INotificationService notificationService,
        DocumentStorageService.IDocumentStorageService documentStorage,
        ILogger<Service> logger,
        MailService.IMailService? mailService = null,
        TimeProvider? timeProvider = null)
    {
        _db = db;
        _mediaService = mediaService;
        _initializeValidator = initializeValidator;
        _notificationService = notificationService;
        _documentStorage = documentStorage;
        _logger = logger;
        _mailService = mailService;
        _reminderClock = timeProvider ?? TimeProvider.System;
    }

    /// <param name="role">The recipient employee role, or <c>null</c> for the booking customer.</param>
    private void AddNotification(
        Guid accountId,
        EmployeeRole? role,
        ComplianceDossier dossier,
        string title,
        string message,
        DateTime now,
        NotificationPriority priority = NotificationPriority.NORMAL)
    {
        _notificationService.Queue(new NotificationService.NotificationCreateRequest
        {
            AccountId = accountId,
            TransportId = dossier.TransportId,
            Type = NotificationType.COMPLIANCE,
            Priority = priority,
            Title = title,
            Message = message,
            ReferenceType = "ComplianceDossier",
            ReferenceId = dossier.ComplianceDossierId,
            ActionUrl = NotificationLinks.Dossier(role, dossier.ComplianceDossierId),
            CreatedAt = now
        });
    }

    private Task<string> NextDossierCodeAsync(DateTime now, CancellationToken cancellationToken) =>
        CodeGeneration.NextAsync("CD", now, async code =>
            _db.ComplianceDossiers.Local.Any(item => item.DossierCode == code) ||
            await _db.ComplianceDossiers.AnyAsync(item => item.DossierCode == code, cancellationToken));

    private Task<string> NextDocumentCodeAsync(DateTime now, CancellationToken cancellationToken) =>
        CodeGeneration.NextAsync("DOC", now, async code =>
            _db.ComplianceDocuments.Local.Any(item => item.DocumentCode == code) ||
            await _db.ComplianceDocuments.AnyAsync(item => item.DocumentCode == code, cancellationToken));

    private Task<string> NextRequestCodeAsync(DateTime now, CancellationToken cancellationToken) =>
        CodeGeneration.NextAsync("DREQ", now, async code =>
            _db.DocumentRequests.Local.Any(item => item.RequestCode == code) ||
            await _db.DocumentRequests.AnyAsync(item => item.RequestCode == code, cancellationToken));

    private Task QueueDossierUpdateMailAsync(Guid customerAccountId, ComplianceDossier dossier,
        MailService.EmailBodies.DossierUpdateKind kind, string? subject, string? detail, DateTime now, CancellationToken cancellationToken) =>
        MailService.MailRecipients.QueueAsync(_db, [customerAccountId], now, recipient => MailService.MailRecipients.Mail(recipient,
            $"{MailService.EmailBodies.DossierUpdateTitle(kind)} — {dossier.DossierCode}",
            MailService.EmailBodies.DossierUpdateBody(recipient.FullName, dossier.DossierCode, kind, subject, detail,
                NotificationLinks.Dossier(null, dossier.ComplianceDossierId))), cancellationToken);

    private Task QueueAuthorityMailAsync(IEnumerable<Guid> accountIds, ComplianceDossier dossier, string authorityName,
        MailService.EmailBodies.AuthorityMailKind kind, string? reason, DateTime now, CancellationToken cancellationToken) =>
        MailService.MailRecipients.QueueAsync(_db, accountIds, now, recipient => MailService.MailRecipients.Mail(recipient,
            $"{MailService.EmailBodies.AuthorityTitle(kind)} — {dossier.DossierCode}",
            MailService.EmailBodies.AuthorityBody(recipient.FullName, dossier.DossierCode, authorityName, kind, reason,
                NotificationLinks.Dossier(recipient.Role, dossier.ComplianceDossierId))), cancellationToken);

    private Task QueueClearanceMailAsync(IEnumerable<Guid> accountIds, ComplianceDossier dossier,
        MailService.EmailBodies.ClearanceKind kind, string place, MailService.EmailBodies.ClearanceOutcome outcome, string? detail,
        DateTime now, CancellationToken cancellationToken) =>
        MailService.MailRecipients.QueueAsync(_db, accountIds, now, recipient => MailService.MailRecipients.Mail(recipient,
            $"{MailService.EmailBodies.ClearanceTitle(kind, outcome)} — {dossier.DossierCode}",
            MailService.EmailBodies.ClearanceBody(recipient.FullName, dossier.DossierCode, kind, place, outcome, detail,
                NotificationLinks.Dossier(recipient.Role, dossier.ComplianceDossierId))), cancellationToken);

    private async Task<IReadOnlyList<Guid>> NotifyEmployeesAsync(
        ComplianceDossier dossier,
        EmployeeRole[] roles,
        string title,
        string message,
        NotificationPriority priority,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var recipients = await _db.Employees
            .Active()
            .Where(employee => roles.Contains(employee.Role))
            .Select(employee => new { employee.AccountId, employee.Role })
            .ToListAsync(cancellationToken);

        foreach (var recipient in recipients)
            AddNotification(recipient.AccountId, recipient.Role, dossier, title, message, now, priority);
        return recipients.Select(item => item.AccountId).ToList();
    }
}
