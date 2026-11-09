using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Pegaxus.Repository;
using Pegaxus.Service.Exceptions;

using static Pegaxus.Service.Test.RouteChangeTests;

namespace Pegaxus.Service.Test;

/// <summary>
/// A Serializable route plan write aborted by a concurrent writer (GPS ping, another plan booking the same vehicle) is retried on a
/// fresh read instead of surfacing as CONCURRENT_UPDATE; the map provider is asked once, before the transaction.
/// </summary>
public class RoutePlanWriteRetryTests
{
    [Fact]
    public async Task Create_RetriesAfterWriteConflict_AndCallsMapProviderOnce()
    {
        var saves = new FailingSaves();
        await using var db = CreateDb(saves);
        var f = await ArrangeAsync(db);
        var routing = new CountingRouting();
        var routes = CreateRoutes(db, routing);

        saves.FailNext = 2;
        var result = await routes.CreateAsync(f.Coordinator.AccountId, f.Plan.TransportPlanId, Request(f));

        Assert.Equal("DRAFT", result.Status);
        Assert.Equal(125.5m, result.TotalDistanceKm);
        Assert.Equal(1, await db.RoutePlans.CountAsync(item => item.RoutePlanId == result.RoutePlanId));
        Assert.Equal(1, routing.Calls);
    }

    [Fact]
    public async Task Create_ReportsRoutePlanConcurrentUpdate_WhenEveryAttemptConflicts()
    {
        var saves = new FailingSaves();
        await using var db = CreateDb(saves);
        var f = await ArrangeAsync(db);
        var before = await db.RoutePlans.CountAsync();

        saves.FailNext = 3;
        var error = await Assert.ThrowsAsync<ConflictException>(() =>
            CreateRoutes(db, new CountingRouting()).CreateAsync(f.Coordinator.AccountId, f.Plan.TransportPlanId, Request(f)));

        Assert.Equal("ROUTE_PLAN_CONCURRENT_UPDATE", error.ErrorCode);
        Assert.Equal(before, await db.RoutePlans.CountAsync());
    }

    private static async Task<Fixture> ArrangeAsync(AppDbContext db)
    {
        var f = await Fixture.CreateAsync(db);
        f.Transport.Booking.OriginCountry = f.Transport.Booking.DestinationCountry = "FR";
        f.Start.Latitude = 48; f.Start.Longitude = 2;
        f.Origin.Latitude = 49; f.Origin.Longitude = 3;
        await db.SaveChangesAsync();
        return f;
    }

    private static Pegaxus.Service.RoutePlanService.Request.SaveRequest Request(Fixture f) => new()
    {
        RouteType = "RETURN", Action = "SAVE_DRAFT", RouteNote = "Return to origin after road closure",
        Segments = [new() { SequenceNo = 1, FromLocationId = f.Start.LocationId, ToLocationId = f.Origin.LocationId,
            TransportMode = "ROAD", PlannedDepartureAt = Fixture.StartAt, PlannedArrivalAt = Fixture.StartAt.AddHours(2) }]
    };

    private static AppDbContext CreateDb(FailingSaves saves) => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
        .AddInterceptors(saves).Options);

    private static Pegaxus.Service.RoutePlanService.Service CreateRoutes(AppDbContext db, CountingRouting routing)
    {
        var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Geoapify:ApiKey"] = "test-key" }).Build();
        return new(db, TestSupport.CreateNotifications(db), new Pegaxus.Service.RouteService.Service(db, routing, configuration),
            new Pegaxus.Service.RoutePlanService.NoopRoutePlanRealtimeService());
    }

    /// <summary>Stands in for PostgreSQL aborting the Serializable transaction.</summary>
    private sealed class FailingSaves : SaveChangesInterceptor
    {
        public int FailNext { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (FailNext <= 0) return ValueTask.FromResult(result);
            FailNext--;
            throw new DbUpdateConcurrencyException("Simulated serialization failure");
        }
    }

    private sealed class CountingRouting : IHttpClientFactory
    {
        public int Calls { get; private set; }

        public HttpClient CreateClient(string name) => new(new Handler(this));

        private sealed class Handler(CountingRouting owner) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                owner.Calls++;
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                    { Content = new StringContent("{\"results\":[{\"distance\":125500,\"time\":5401}]}") });
            }
        }
    }
}
