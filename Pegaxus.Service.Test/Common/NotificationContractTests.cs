using System.Security.Claims;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pegaxus.API.Extensions;
using Pegaxus.API.Hubs;
using Pegaxus.Service.Common;
using Pegaxus.Service.NotificationService;

namespace Pegaxus.Service.Test;

public class NotificationContractTests
{
    [Fact]
    public async Task RealtimeNotification_PreservesActionUrl()
    {
        await using var db = TestSupport.CreateDb();
        var realtime = new RecordingRealtime();
        var service = new Pegaxus.Service.NotificationService.Service(db, realtime);
        service.Queue(new() { AccountId = Guid.NewGuid(), ActionUrl = "/bookings/test", CreatedAt = DbTime.UtcNow() });
        await service.FlushAsync();
        Assert.Equal("/bookings/test", Assert.Single(realtime.Notifications).ActionUrl);
    }

    [Fact]
    public async Task AcknowledgeAnotherAccountsNotification_ReturnsSafeNotFoundHubError()
    {
        await using var db = TestSupport.CreateDb();
        var service = TestSupport.CreateNotifications(db);
        service.Queue(new() { AccountId = Guid.NewGuid(), CreatedAt = DbTime.UtcNow() });
        await db.SaveChangesAsync();
        var notification = await db.AccountNotifications.SingleAsync();
        using var hub = new NotificationHub(service) { Context = new CallerContext(Guid.NewGuid()) };

        var error = await Assert.ThrowsAsync<HubException>(() => hub.AcknowledgeDelivered(notification.NotificationId));

        Assert.Equal("NOTIFICATION_NOT_FOUND: Notification not found", error.Message);
        Assert.Null(notification.DeliveredAt);
    }

    [Theory]
    [InlineData("https://one.example; https://two.example")]
    [InlineData("https://one.example, https://two.example")]
    public void Cors_UsesConfiguredOriginsWithoutAllowingOtherSites(string origins)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ALLOWED_ORIGINS"] = origins
        }).Build();
        using var services = new ServiceCollection().AddFrontendCors(configuration).BuildServiceProvider();
        var policy = services.GetRequiredService<IOptions<CorsOptions>>().Value.GetPolicy("Frontend")!;
        Assert.True(policy.IsOriginAllowed("https://one.example"));
        Assert.True(policy.IsOriginAllowed("https://two.example"));
        Assert.False(policy.IsOriginAllowed("https://other.example"));
        Assert.True(policy.SupportsCredentials);
    }

    private sealed class RecordingRealtime : INotificationRealtimeService
    {
        public List<NotificationRealtimeDto> Notifications { get; } = [];
        public Task SendToAccountAsync(Guid accountId, NotificationRealtimeDto notification, CancellationToken cancellationToken = default)
        {
            Notifications.Add(notification);
            return Task.CompletedTask;
        }
    }

    private sealed class CallerContext(Guid accountId) : HubCallerContext
    {
        public override string ConnectionId => "test";
        public override string UserIdentifier => accountId.ToString();
        public override ClaimsPrincipal User => new();
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override Microsoft.AspNetCore.Http.Features.IFeatureCollection Features { get; } = new Microsoft.AspNetCore.Http.Features.FeatureCollection();
        public override CancellationToken ConnectionAborted => CancellationToken.None;
        public override void Abort() { }
    }
}
