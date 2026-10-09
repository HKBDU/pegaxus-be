using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Service.MailService;
using Pegaxus.Service.NotificationService;
using Pegaxus.Service.OutboxService;

namespace Pegaxus.API.Services;

public sealed class TransactionalOutboxWorker(
    IServiceScopeFactory scopes,
    ILogger<TransactionalOutboxWorker> logger) : BackgroundService
{
    private const int MaxAttempts = 8;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        do
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception error)
            {
                logger.LogError(error, "Transactional outbox cycle failed; next cycle will retry.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task ProcessBatchAsync(CancellationToken cancellationToken)
    {
        await using var claimScope = scopes.CreateAsyncScope();
        var db = claimScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = UtcNow();
        var staleBefore = now.AddMinutes(-10);
        var eligible = db.OutboxMessages.Where(message =>
            (message.Status == "PENDING" || message.Status == "FAILED" ||
                (message.Status == "PROCESSING" && message.LockedAt < staleBefore)) &&
            message.AvailableAt <= now);
        var candidates = await eligible.OrderBy(message => message.CreatedAt).ThenBy(message => message.OutboxMessageId)
            .Select(message => message.OutboxMessageId).Take(20)
            .ToListAsync(cancellationToken);
        var claimed = new List<Guid>();
        foreach (var messageId in candidates)
        {
            // Recheck eligibility atomically so competing workers cannot claim the same message.
            if (await eligible.Where(message => message.OutboxMessageId == messageId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(message => message.Status, "PROCESSING")
                    .SetProperty(message => message.LockedAt, now)
                    .SetProperty(message => message.UpdatedAt, now), cancellationToken) == 1)
                claimed.Add(messageId);
        }
        foreach (var messageId in claimed)
            await ProcessMessageAsync(messageId, cancellationToken);
    }

    private async Task ProcessMessageAsync(Guid messageId, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var message = await db.OutboxMessages.SingleOrDefaultAsync(
                item => item.OutboxMessageId == messageId && item.Status == "PROCESSING",
                cancellationToken);
            if (message is null)
                return;

            switch (message.MessageType)
            {
                case MessageTypes.Email:
                    var mail = scope.ServiceProvider.GetRequiredService<Pegaxus.Service.MailService.IService>();
                    var email = OutboxQueue.Deserialize<EmailPayload>(message.Payload);
                    await mail.SendMail(new MailContent
                    {
                        To = email.To,
                        Subject = email.Subject,
                        Body = email.Body
                    }, cancellationToken);
                    break;

                case MessageTypes.SignalRAccount:
                    var realtime = scope.ServiceProvider.GetRequiredService<INotificationRealtimeService>();
                    var signalR = OutboxQueue.Deserialize<SignalRAccountPayload>(message.Payload);
                    await realtime.SendToAccountAsync(signalR.AccountId, signalR.Notification, cancellationToken);
                    break;

                default:
                    throw new InvalidOperationException($"Unsupported outbox message type '{message.MessageType}'.");
            }

            message.Status = "PROCESSED";
            message.ProcessedAt = UtcNow();
            message.UpdatedAt = message.ProcessedAt.Value;
            message.LastError = null;
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception error) when (error is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            await MarkFailedAsync(messageId, error, cancellationToken);
        }
    }

    private async Task MarkFailedAsync(Guid messageId, Exception error, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var message = await db.OutboxMessages.SingleOrDefaultAsync(
            item => item.OutboxMessageId == messageId,
            cancellationToken);
        if (message is null)
            return;

        message.Attempts++;
        message.Status = message.Attempts >= MaxAttempts ? "DEAD" : "FAILED";
        message.AvailableAt = UtcNow().AddSeconds(Math.Min(300, Math.Pow(2, message.Attempts) * 5));
        message.LastError = error.ToString()[..Math.Min(4000, error.ToString().Length)];
        message.UpdatedAt = UtcNow();
        await db.SaveChangesAsync(cancellationToken);
        logger.LogError(error, "Outbox message {OutboxMessageId} failed on attempt {Attempt}.", messageId, message.Attempts);
    }

    private static DateTime UtcNow() =>
        DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
}
