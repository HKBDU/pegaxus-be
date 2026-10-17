using System.Collections.Concurrent;

namespace Pegaxus.Service.LocationTrackingService;

public sealed class TrackingState
{
    // shortcut: state belongs to one API process; use shared ephemeral state and a SignalR backplane before scaling out.
    private readonly ConcurrentDictionary<Guid, Entry> transports = new();

    public Entry For(Guid transportId) => transports.GetOrAdd(transportId, _ => new());

    public sealed class Entry
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
        private Response.LocationUpdate? latest;
        public Response.LocationUpdate? Latest { get => Volatile.Read(ref latest); set => Volatile.Write(ref latest, value); }
        public Dictionary<Guid, DateTimeOffset> PermissionAlerts { get; } = [];
    }
}

public sealed class TrackingOptions
{
    public double GeofenceRadiusMeters { get; set; } = 500;
}
