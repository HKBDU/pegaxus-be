using System.Text;
using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Repository.Extensions;
using Pegaxus.Service.Common;
using Pegaxus.Service.MediaService;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

using static Pegaxus.Service.Common.DbTime;

using NotificationService = Pegaxus.Service.NotificationService;

namespace Pegaxus.Service.ComplianceDossierService;

public sealed partial class Service : IService
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
    private readonly MediaService.IService _mediaService;
    private readonly IValidator<Request.InitializeRequest> _initializeValidator;
    private readonly NotificationService.IService _notificationService;
    private readonly DocumentStorageService.IService _documentStorage;
    private readonly ILogger<Service> _logger;
    private readonly MailService.IService? _mailService;
    private readonly TimeProvider _reminderClock;

    public Service(
        AppDbContext db,
        MediaService.IService mediaService,
        IValidator<Request.InitializeRequest> initializeValidator,
        NotificationService.IService notificationService,
        DocumentStorageService.IService documentStorage,
        ILogger<Service> logger,
        MailService.IService? mailService = null,
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

    private void AddNotification(
        Guid accountId,
        ComplianceDossier dossier,
        string title,
        DateTime now,
        NotificationPriority priority = NotificationPriority.NORMAL,
        string? message = null)
    {
        _notificationService.Queue(new NotificationService.NotificationCreateRequest
        {
            AccountId = accountId,
            TransportId = dossier.TransportId,
            Type = NotificationType.COMPLIANCE,
            Priority = priority,
            Title = title,
            Message = message ?? $"ComplianceDossier {dossier.DossierCode} has been submitted to an authority.",
            ReferenceType = "ComplianceDossier",
            ReferenceId = dossier.ComplianceDossierId,
            ActionUrl = $"/compliance-dossiers/{dossier.ComplianceDossierId}",
            CreatedAt = now
        });
    }

    private async Task NotifyEmployeesAsync(
        ComplianceDossier dossier,
        EmployeeRole[] roles,
        string title,
        NotificationPriority priority,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var accountIds = await _db.Employees
            .Active()
            .Where(employee => roles.Contains(employee.Role))
            .Select(employee => employee.AccountId)
            .ToListAsync(cancellationToken);

        foreach (var accountId in accountIds)
            AddNotification(accountId, dossier, title, now, priority);
    }
}
