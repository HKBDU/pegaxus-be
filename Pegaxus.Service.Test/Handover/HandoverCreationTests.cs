using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;

using static Pegaxus.Service.Test.HandoverTests;

using Handovers = Pegaxus.Service.HandoverService;

namespace Pegaxus.Service.Test;

public class HandoverCreationTests
{
    [Fact]
    public async Task Creation_InfersPartiesSnapshotsAndSequenceWithoutStartingJourney()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        f.Pickup.Status = HandoverStatus.CANCELLED; await db.SaveChangesAsync();
        var result = await f.Service.CreateAsync(f.TransportId, new() { HandoverType = "PICKUP", ToRoutePlanRouteId = f.First.RoutePlanRouteId,
            FromType = "EMPLOYEE", FromEmployeeId = Guid.NewGuid(), LocationName = "  Actual stable  " }, f.Escort.AccountId);
        Assert.Equal("IN_PROGRESS", result.Status); Assert.Equal(4, result.SequenceNo);
        Assert.Equal(f.CustomerId, result.From.CustomerId); Assert.Equal(f.Driver.EmployeeId, result.To.EmployeeId);
        Assert.Equal("Actual stable", result.Location.LocationName); Assert.Matches("^HO-20261016-[0-9]{4}$", result.HandoverCode);
        Assert.Equal(TransportStatus.READY, f.Route.TransportPlan.Transport.Status); Assert.Null(f.First.ActualDepartureAt);
        Assert.Empty(db.Notifications); Assert.Empty(db.OutboxMessages);
        var duplicate = await Assert.ThrowsAsync<ConflictException>(() => f.Service.CreateAsync(f.TransportId,
            new() { HandoverType = "PICKUP", ToRoutePlanRouteId = f.First.RoutePlanRouteId }, f.Manager.AccountId));
        Assert.Equal("HANDOVER_ALREADY_EXISTS", duplicate.ErrorCode); Assert.NotNull(duplicate.Value);
    }

    [Fact]
    public async Task Scheduling_QueuesDistinctEmployeeNotificationsAndValidatesPermissionAndBoundary()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        f.Route.TransportPlan.Transport.Status = TransportStatus.IN_TRANSIT; f.Transfer.Status = HandoverStatus.CANCELLED; await db.SaveChangesAsync();
        var request = new Handovers.Request.CreateRequest { HandoverType = "TRANSFER", FromRoutePlanRouteId = f.First.RoutePlanRouteId,
            ToRoutePlanRouteId = f.Second.RoutePlanRouteId, StartNow = false, PlannedHandoverAt = f.Clock.GetUtcNow().AddHours(1) };
        await Assert.ThrowsAsync<ForbiddenException>(() => f.Service.CreateAsync(f.TransportId, request, f.Escort.AccountId));
        var result = await f.Service.CreateAsync(f.TransportId, request, f.Coordinator.AccountId);
        Assert.Equal("PLANNED", result.Status); Assert.Equal(f.Driver.EmployeeId, result.From.EmployeeId); Assert.Equal(f.Receiver.EmployeeId, result.To.EmployeeId);
        Assert.Equal(2, await db.Notifications.CountAsync()); Assert.Equal(2, f.Realtime.Messages.Count);
        request.StartNow = true; request.FromRoutePlanRouteId = f.Second.RoutePlanRouteId; request.ToRoutePlanRouteId = f.First.RoutePlanRouteId;
        Assert.Equal("INVALID_SEGMENT_PAIR", (await Assert.ThrowsAsync<UnprocessableEntityException>(() => f.Service.CreateAsync(f.TransportId, request, f.Manager.AccountId))).ErrorCode);
        request.HandoverType = "CARRIER_HANDOVER";
        Assert.Equal("HANDOVER_TYPE_NOT_SUPPORTED", (await Assert.ThrowsAsync<UnprocessableEntityException>(() => f.Service.CreateAsync(f.TransportId, request, f.Manager.AccountId))).ErrorCode);
    }

    [Fact]
    public async Task Creation_ValidatesOtherPartyXorStatusAndSchedulingInput()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        await Assert.ThrowsAsync<BadRequestException>(() => f.Service.CreateAsync(f.TransportId, new() { HandoverType = "OTHER", StartNow = false }, f.Manager.AccountId));
        Assert.Equal("INVALID_TRANSPORT_STATUS", (await Assert.ThrowsAsync<ConflictException>(() => f.Service.CreateAsync(f.TransportId,
            new() { HandoverType = "DELIVERY", FromRoutePlanRouteId = f.Second.RoutePlanRouteId }, f.Manager.AccountId))).ErrorCode);
        f.Route.TransportPlan.Transport.Status = TransportStatus.IN_TRANSIT; await db.SaveChangesAsync();
        var request = new Handovers.Request.CreateRequest { HandoverType = "OTHER", FromType = "CUSTOMER", FromCustomerId = f.CustomerId,
            FromEmployeeId = f.Driver.EmployeeId, ToType = "EMPLOYEE", ToEmployeeId = f.Receiver.EmployeeId };
        Assert.Equal("INVALID_SEGMENT_PAIR", (await Assert.ThrowsAsync<UnprocessableEntityException>(() => f.Service.CreateAsync(f.TransportId, request, f.Manager.AccountId))).ErrorCode);
        request.FromEmployeeId = null;
        Assert.Equal("OTHER", (await f.Service.CreateAsync(f.TransportId, request, f.Manager.AccountId)).HandoverType);
        request.FromType = "PROVIDER";
        Assert.Equal("HANDOVER_TYPE_NOT_SUPPORTED", (await Assert.ThrowsAsync<UnprocessableEntityException>(() =>
            f.Service.CreateAsync(f.TransportId, request, f.Manager.AccountId))).ErrorCode);
    }
}

public partial class HandoverHttpTests
{
    [Fact]
    public async Task Http_CreateUsesJsonReturns201AndEnforcesOperatorPolicy()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        f.Pickup.Status = HandoverStatus.CANCELLED; await db.SaveChangesAsync();
        await using var host = await StartAsync(db, f); using var client = Client(host, f.Manager.AccountId, EmployeeRole.LOGISTICS_MANAGER);
        var url = $"/api/v1/transports/{f.TransportId}/handover-records";
        var request = new { handoverType = "PICKUP", toRoutePlanRouteId = f.First.RoutePlanRouteId };
        using var response = await client.PostAsJsonAsync(url, request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(); Assert.True(body.GetProperty("isSuccess").GetBoolean());
        Assert.Equal("IN_PROGRESS", body.GetProperty("value").GetProperty("status").GetString());
        using var customer = Client(host, f.CustomerAccountId);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.PostAsJsonAsync(url, request)).StatusCode);
        using var multipart = new MultipartFormDataContent();
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await client.PostAsync(url, multipart)).StatusCode);
    }
}

public partial class HandoverPostgresTests
{
    [PostgresFact]
    public async Task Postgres_ConcurrentCreationRetriesThenReturnsExistingBoundary()
    {
        await WithDatabaseAsync(async options =>
        {
            await using var seed = new AppDbContext(options); var f = await Fixture.CreateAsync(seed);
            f.Pickup.Status = HandoverStatus.CANCELLED; await seed.SaveChangesAsync();
            var saved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var competing = new LockSignal();
            var audit = new SaveAudit { AfterSave = async () => { saved.TrySetResult(); await release.Task.WaitAsync(TimeSpan.FromSeconds(15)); } };
            await using var first = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(options).AddInterceptors(audit).Options);
            await using var second = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(options).AddInterceptors(competing).Options);
            var request = new Handovers.Request.CreateRequest { HandoverType = "PICKUP", ToRoutePlanRouteId = f.First.RoutePlanRouteId };
            var winner = f.CreateService(first).CreateAsync(f.TransportId, request, f.Manager.AccountId);
            try
            {
                await saved.Task.WaitAsync(TimeSpan.FromSeconds(15));
                var loser = Record.ExceptionAsync(() => f.CreateService(second).CreateAsync(f.TransportId, request, f.Coordinator.AccountId));
                await competing.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
                release.TrySetResult(); Assert.Equal(4, (await winner).SequenceNo);
                Assert.Equal("HANDOVER_ALREADY_EXISTS", Assert.IsType<ConflictException>(await loser).ErrorCode);
            }
            finally { release.TrySetResult(); }
            await using var verify = new AppDbContext(options);
            Assert.Equal(4, await verify.HandoverRecords.CountAsync());
            Assert.Single(await verify.HandoverRecords.Where(item => item.HandoverType == HandoverType.PICKUP && item.Status != HandoverStatus.CANCELLED).ToArrayAsync());
        });
    }

    [PostgresFact]
    public async Task Postgres_CreateRollsBackNotificationsAndRecordWhenSaveFails()
    {
        await WithDatabaseAsync(async options =>
        {
            await using var seed = new AppDbContext(options); var f = await Fixture.CreateAsync(seed);
            f.Pickup.Status = HandoverStatus.CANCELLED; await seed.SaveChangesAsync();
            var audit = new SaveAudit { FailAfterSave = true };
            await using (var writer = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(options).AddInterceptors(audit).Options))
                await Assert.ThrowsAsync<IOException>(() => f.CreateService(writer).CreateAsync(f.TransportId, new()
                {
                    HandoverType = "PICKUP", ToRoutePlanRouteId = f.First.RoutePlanRouteId, StartNow = false,
                    PlannedHandoverAt = f.Clock.GetUtcNow().AddHours(1)
                }, f.Manager.AccountId));
            await using var verify = new AppDbContext(options);
            Assert.Equal(3, await verify.HandoverRecords.CountAsync()); Assert.Empty(await verify.Notifications.ToListAsync());
            Assert.Empty(await verify.OutboxMessages.ToListAsync()); Assert.Equal(TransportStatus.READY, (await verify.Transports.SingleAsync()).Status);
            var created = await f.CreateService(verify).CreateAsync(f.TransportId,
                new() { HandoverType = "PICKUP", ToRoutePlanRouteId = f.First.RoutePlanRouteId }, f.Driver.AccountId);
            Assert.Equal(4, created.SequenceNo); Assert.Equal("IN_PROGRESS", created.Status);
        });
    }
}
