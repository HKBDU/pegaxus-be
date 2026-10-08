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
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
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
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var messages = await db.OutboxMessages
            .FromSqlRaw("""
                SELECT *
                FROM "OutboxMessage"
                WHERE (status = 'PENDING' OR status = 'FAILED'
                    OR (status = 'PROCESSING' AND locked_at < (CURRENT_TIMESTAMP AT TIME ZONE 'UTC' - INTERVAL '10 minutes')))
                  AND available_at <= (CURRENT_TIMESTAMP AT TIME ZONE 'UTC')
                ORDER BY created_at
                LIMIT 20
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(cancellationToken);

        foreach (var message in messages)
        {
            message.Status = "PROCESSING";
            message.LockedAt = now;
            message.UpdatedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        foreach (var message in messages)
            await ProcessMessageAsync(message.OutboxMessageId, cancellationToken);
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
