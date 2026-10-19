using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.OutboxService;

using static Pegaxus.Service.Common.DbTime;

namespace Pegaxus.Service.NotificationService;

public sealed class Service : INotificationService
{
    private readonly AppDbContext _db;
    private readonly INotificationRealtimeService _realtimeService;
    private readonly ILogger<Service> _logger;
    private readonly List<(Guid AccountId, NotificationRealtimeDto Notification)> _pendingPushes = [];

    public Service(AppDbContext db, INotificationRealtimeService realtimeService, ILogger<Service>? logger = null)
    {
        _db = db;
        _realtimeService = realtimeService;
        _logger = logger ?? NullLogger<Service>.Instance;
    }

    public void Queue(NotificationCreateRequest request)
    {
        var notificationId = Guid.NewGuid();
        var notification = new Notification
        {
            NotificationId = notificationId,
            TransportId = request.TransportId,
            NotificationType = request.Type,
            Priority = request.Priority,
            Title = request.Title,
            Message = request.Message,
            ReferenceType = request.ReferenceType,
            ReferenceId = request.ReferenceId,
            ActionUrl = request.ActionUrl,
            CreatedAt = request.CreatedAt
        };

        _db.Notifications.Add(notification);
        _db.AccountNotifications.Add(new AccountNotification
        {
            AccountId = request.AccountId,
            NotificationId = notificationId,
            Status = RecipientStatus.PENDING,
            CreatedAt = request.CreatedAt
        });

        var realtimeNotification = new NotificationRealtimeDto
        {
            Id = notificationId,
            Type = request.Type.ToString(),
            Title = request.Title,
            Message = request.Message,
            Priority = request.Priority.ToString(),
            Status = RecipientStatus.PENDING.ToString(),
            CreatedAt = AsUtc(request.CreatedAt),
            ReferenceType = request.ReferenceType,
            ReferenceId = request.ReferenceId,
            ActionUrl = request.ActionUrl
        };

        if (OutboxQueue.IsEnabled(_db))
            OutboxQueue.AddSignalRAccount(_db, request.AccountId, realtimeNotification, request.CreatedAt);
        else
            _pendingPushes.Add((request.AccountId, realtimeNotification));
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        // Relational deployments deliver these messages through the outbox worker
        // after the business transaction commits.
        if (OutboxQueue.IsEnabled(_db))
            return;

        if (_pendingPushes.Count == 0)
            return;

        var pushes = _pendingPushes.ToArray();
        _pendingPushes.Clear();

        foreach (var push in pushes)
        {
            try
            {
                await _realtimeService.SendToAccountAsync(
                    push.AccountId,
                    push.Notification,
                    cancellationToken);
            }
            catch (Exception exception)
            {
                // Realtime delivery failure must not roll back a committed notification.
                // An offline client remains PENDING until it acknowledges delivery.
                _logger.LogWarning("SignalR notification push failed: {Message}", exception.Message);
            }
        }
    }

    public async Task<Response.NotificationListResponse> GetAsync(
        Guid accountId,
        Request.NotificationListRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidatePaging(request.PageIndex, request.PageSize);

        var query = _db.AccountNotifications
            .AsNoTracking()
            .Where(item => item.AccountId == accountId)
            .Include(item => item.Notification)
            .AsQueryable();

        if (request.UnreadOnly == true)
            query = query.Where(item => item.Status != RecipientStatus.READ);

        var totalItems = await query.CountAsync(cancellationToken);
        var totalPages = totalItems == 0 ? 0 : Paging.TotalPages(totalItems, request.PageSize);
        var items = await query
            .OrderByDescending(item => item.CreatedAt)
            .Skip(Paging.Offset(request.PageIndex, request.PageSize))
            .Take(request.PageSize)
            .Select(item => new Response.NotificationItemResponse
            {
                Id = item.NotificationId,
                Type = item.Notification.NotificationType.ToString(),
                Title = item.Notification.Title,
                Message = item.Notification.Message,
                Priority = item.Notification.Priority.ToString(),
                Status = item.Status.ToString(),
                CreatedAt = AsUtc(item.Notification.CreatedAt),
                DeliveredAt = AsUtc(item.DeliveredAt),
                ReadAt = AsUtc(item.ReadAt),
                ReferenceType = item.Notification.ReferenceType,
                ReferenceId = item.Notification.ReferenceId,
                ActionUrl = item.Notification.ActionUrl
            })
            .ToListAsync(cancellationToken);

        return new Response.NotificationListResponse
        {
            Items = items,
            PageIndex = request.PageIndex,
            PageSize = request.PageSize,
            TotalItems = totalItems,
            TotalPages = totalPages
        };
    }

    public Task<int> GetUnreadCountAsync(Guid accountId, CancellationToken cancellationToken = default) =>
        _db.AccountNotifications.CountAsync(
            item => item.AccountId == accountId && item.Status != RecipientStatus.READ,
            cancellationToken);

    public async Task MarkReadAsync(
        Guid accountId,
        Guid notificationId,
        CancellationToken cancellationToken = default)
    {
        var recipient = await GetRecipientAsync(accountId, notificationId, cancellationToken);
        if (recipient.Status == RecipientStatus.READ)
            return;

        recipient.Status = RecipientStatus.READ;
        recipient.ReadAt = UtcNow();
        if (recipient.DeliveredAt is null)
            recipient.DeliveredAt = recipient.ReadAt;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkAllReadAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var recipients = await _db.AccountNotifications
            .Where(item => item.AccountId == accountId && item.Status != RecipientStatus.READ)
            .ToListAsync(cancellationToken);
        var now = UtcNow();
        foreach (var recipient in recipients)
        {
            recipient.Status = RecipientStatus.READ;
            recipient.ReadAt = now;
            recipient.DeliveredAt ??= now;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task AcknowledgeDeliveredAsync(
        Guid accountId,
        Guid notificationId,
        CancellationToken cancellationToken = default)
    {
        var recipient = await GetRecipientAsync(accountId, notificationId, cancellationToken);
        if (recipient.Status != RecipientStatus.PENDING)
            return;

        recipient.Status = RecipientStatus.DELIVERED;
        recipient.DeliveredAt = UtcNow();
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task<AccountNotification> GetRecipientAsync(
        Guid accountId,
        Guid notificationId,
        CancellationToken cancellationToken)
    {
        var recipient = await _db.AccountNotifications.SingleOrDefaultAsync(
            item => item.AccountId == accountId && item.NotificationId == notificationId,
            cancellationToken);
        if (recipient is null)
            throw new NotFoundException("Notification not found", "NOTIFICATION_NOT_FOUND");

        return recipient;
    }

    private static void ValidatePaging(int pageIndex, int pageSize)
    {
        if (pageIndex < 1 || pageSize is < 1 or > 100)
            throw new BadRequestException("PageIndex must be at least 1 and PageSize must be between 1 and 100");
    }


}
