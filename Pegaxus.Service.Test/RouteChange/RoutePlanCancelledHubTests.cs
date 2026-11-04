using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Pegaxus.API.Hubs;
using Pegaxus.Service.RoutePlanService;

namespace Pegaxus.Service.Test;

public class RoutePlanCancelledHubTests
{
    private sealed class FakeClient(bool fail = false) : INotificationClient
    {
        public List<Response.RoutePlanCancelledEvent> Received { get; } = [];
        public Task ReceiveNotification(Pegaxus.Service.NotificationService.NotificationRealtimeDto notification) => Task.CompletedTask;

        public Task RoutePlanCancelled(Response.RoutePlanCancelledEvent routePlanCancelled)
        {
            if (fail) throw new InvalidOperationException("The connection is gone");
            Received.Add(routePlanCancelled);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeClients(FakeClient client) : IHubClients<INotificationClient>
    {
        public List<string> AskedFor { get; } = [];
        public INotificationClient All => client;
        public INotificationClient AllExcept(IReadOnlyList<string> excludedConnectionIds) => client;
        public INotificationClient Client(string connectionId) => client;
        INotificationClient IHubClients<INotificationClient>.Clients(IReadOnlyList<string> connectionIds) => client;
        public INotificationClient Group(string groupName) => client;
        public INotificationClient GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => client;
        public INotificationClient Groups(IReadOnlyList<string> groupNames) => client;
        public INotificationClient User(string userId) => client;

        public INotificationClient Users(IReadOnlyList<string> userIds)
        {
            AskedFor.AddRange(userIds);
            return client;
        }
    }

    private sealed class FakeHub(FakeClients clients) : IHubContext<NotificationHub, INotificationClient>
    {
        public IHubClients<INotificationClient> Clients => clients;
        public IGroupManager Groups => throw new NotSupportedException();
    }

    private static readonly Response.RoutePlanCancelledEvent Event =
        new(Guid.NewGuid(), Guid.NewGuid(), "BACKUP", new DateTimeOffset(2026, 11, 4, 8, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Publish_SendsTheEventToTheRecipientAccountsOnly()
    {
        var client = new FakeClient();
        var clients = new FakeClients(client);
        var specialist = Guid.NewGuid();

        await new SignalRRoutePlanRealtimeService(new FakeHub(clients), NullLogger<SignalRRoutePlanRealtimeService>.Instance).PublishCancelledAsync([specialist], Event);

        Assert.Equal([specialist.ToString()], clients.AskedFor);
        Assert.Same(Event, Assert.Single(client.Received));
    }

    [Fact]
    public async Task Publish_WithoutRecipients_SendsNothing()
    {
        var client = new FakeClient();
        var clients = new FakeClients(client);

        await new SignalRRoutePlanRealtimeService(new FakeHub(clients), NullLogger<SignalRRoutePlanRealtimeService>.Instance).PublishCancelledAsync([], Event);

        Assert.Empty(clients.AskedFor);
        Assert.Empty(client.Received);
    }

    [Fact]
    public async Task Publish_ThatFails_DoesNotFailTheCancelThatAlreadyCommitted()
    {
        var clients = new FakeClients(new FakeClient(fail: true));

        await new SignalRRoutePlanRealtimeService(new FakeHub(clients), NullLogger<SignalRRoutePlanRealtimeService>.Instance).PublishCancelledAsync([Guid.NewGuid()], Event);
    }

    [Fact]
    public void RoutePlanCancelled_SerializesWithTheAgreedFieldNames()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(Event, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));

        using var document = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal(["routePlanId", "transportPlanId", "routeType", "cancelledAt"], document.RootElement.EnumerateObject().Select(item => item.Name).ToArray());
    }
}
