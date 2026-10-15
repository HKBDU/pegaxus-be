using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Service.MailService;
using Pegaxus.Service.NotificationService;

namespace Pegaxus.Service.OutboxService;

public static class MessageTypes
{
    public const string Email = "EMAIL";
    public const string SignalRAccount = "SIGNALR_ACCOUNT";
}

public sealed class EmailPayload
{
    public required string To { get; init; }
    public required string Subject { get; init; }
    public required string Body { get; init; }
}

public sealed class SignalRAccountPayload
{
    public Guid AccountId { get; init; }
    public NotificationRealtimeDto Notification { get; init; } = new();
}

public static class OutboxQueue
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static bool IsEnabled(AppDbContext db) => db.Database.IsRelational();

    public static void AddEmail(AppDbContext db, MailContent mail, DateTime now)
    {
        db.OutboxMessages.Add(new OutboxMessage
        {
            OutboxMessageId = Guid.NewGuid(),
            MessageType = MessageTypes.Email,
            Payload = JsonSerializer.Serialize(new EmailPayload
            {
                To = mail.To,
                Subject = mail.Subject,
                Body = mail.Body
            }, JsonOptions),
            Status = "PENDING",
            AvailableAt = now,
            CreatedAt = now,
            UpdatedAt = now
        });
    }

    public static void AddSignalRAccount(
        AppDbContext db,
        Guid accountId,
        NotificationRealtimeDto notification,
        DateTime now)
    {
        db.OutboxMessages.Add(new OutboxMessage
        {
            OutboxMessageId = Guid.NewGuid(),
            MessageType = MessageTypes.SignalRAccount,
            Payload = JsonSerializer.Serialize(new SignalRAccountPayload
            {
                AccountId = accountId,
                Notification = notification
            }, JsonOptions),
            Status = "PENDING",
            AvailableAt = now,
            CreatedAt = now,
            UpdatedAt = now
        });
    }

    public static T Deserialize<T>(string payload) =>
        JsonSerializer.Deserialize<T>(payload, JsonOptions)
        ?? throw new InvalidOperationException($"Cannot deserialize outbox payload as {typeof(T).Name}.");
}
