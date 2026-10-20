using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Pegaxus.Repository;
using Pegaxus.Repository.Enum;
using Pegaxus.Repository.Extensions;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.OutboxService;

using static Pegaxus.Service.Test.HandoverCompletionTests;
using static Pegaxus.Service.Test.HandoverTests;

namespace Pegaxus.Service.Test;

public partial class HandoverPostgresTests
{
    [PostgresFact]
    public async Task Postgres_CompleteFullJourney_CommitsLifecycleNotificationsAndDeliveryEmailOncePerHandover()
    {
        await WithDatabaseAsync(async options =>
        {
            await using var seed = new AppDbContext(options); var f = await Fixture.CreateStartedAsync(seed); var vehicle = AddVehicle(seed, f.First, f.Second); await seed.SaveChangesAsync();
            var audit = new SaveAudit();
            await using (var writer = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(options).AddInterceptors(audit).Options))
            {
                var service = f.CreateService(writer);
                Assert.Equal("IN_TRANSIT", (await service.CompleteAsync(f.Pickup.HandoverRecordId, Request(f.Pickup), f.Escort.AccountId)).Effects!.TransportStatus);
                Assert.Equal(VehicleStatus.IN_USE, (await writer.Vehicles.SingleAsync()).Status);
                // GP2 updates the database while this context still tracks the old segment timestamps.
                var gpsArrival = Fixture.Now.AddMinutes(-5);
                await using (var gps = new AppDbContext(options))
                    await gps.RoutePlanRoutes.Where(segment => segment.RoutePlanRouteId == f.First.RoutePlanRouteId)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(segment => segment.ActualArrivalAt, gpsArrival));
                f.Clock.Now = f.Clock.Now.AddHours(1);
                await service.CompleteAsync(f.Transfer.HandoverRecordId, Request(f.Transfer), f.Receiver.AccountId);
                Assert.Equal(gpsArrival, (await writer.RoutePlanRoutes.SingleAsync(segment => segment.RoutePlanRouteId == f.First.RoutePlanRouteId)).ActualArrivalAt);
                f.Clock.Now = f.Clock.Now.AddHours(1);
                var result = await service.CompleteAsync(f.Delivery.HandoverRecordId, Request(f.Delivery), f.Coordinator.AccountId);
                Assert.Equal(f.Clock.Now.AddHours(24), result.Effects!.ClaimWindowEndsAt); Assert.Equal("COMPLETED", result.Transport.Status);
            }
            Assert.Equal(3, audit.Calls); Assert.Equal((1, 3, 4), audit.Writes); Assert.Empty(f.Realtime.Messages);
            await using var verify = new AppDbContext(options);
            Assert.All(await verify.HandoverRecords.ToArrayAsync(), item => { Assert.Equal(HandoverStatus.COMPLETED, item.Status); Assert.Equal(item.ActualHandoverAt, item.CompletedAt); });
            Assert.Equal(TransportStatus.COMPLETED, (await verify.Transports.SingleAsync()).Status);
            Assert.Equal(RoutePlanStatus.COMPLETED, (await verify.RoutePlans.SingleAsync()).Status); Assert.Equal(TransportPlanStatus.COMPLETED, (await verify.TransportPlans.SingleAsync()).Status);
            Assert.Equal(VehicleStatus.AVAILABLE, (await verify.Vehicles.SingleAsync(item => item.VehicleId == vehicle.VehicleId)).Status);
            Assert.Equal(7, await verify.Notifications.CountAsync()); Assert.Equal(8, await verify.OutboxMessages.CountAsync());
            var email = OutboxQueue.Deserialize<EmailPayload>((await verify.OutboxMessages.SingleAsync(item => item.MessageType == MessageTypes.Email)).Payload);
            Assert.Contains("24 hours", email.Body); Assert.Equal(DateTimeKind.Unspecified, (await verify.Transports.SingleAsync()).CompletedAt!.Value.Kind);
        });
    }

    [PostgresFact]
    public async Task Postgres_DeliveryFailureAfterSave_RollsBackAllEffects()
    {
        await WithDatabaseAsync(async options =>
        {
            await using var seed = new AppDbContext(options); var f = await Fixture.CreateStartedAsync(seed); var vehicle = AddVehicle(seed, f.First, f.Second);
            f.Route.Status = RoutePlanStatus.ACTIVE; f.Route.TransportPlan.Status = TransportPlanStatus.IN_PROGRESS; f.Route.TransportPlan.Transport.Status = TransportStatus.IN_TRANSIT;
            f.First.ActualDepartureAt = f.First.ActualArrivalAt = f.Second.ActualDepartureAt = Fixture.Now.AddHours(-1); vehicle.Status = VehicleStatus.IN_USE; await seed.SaveChangesAsync();
            var failure = new SaveAudit { FailAfterSave = true };
            await using (var writer = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(options).AddInterceptors(failure).Options))
                await Assert.ThrowsAsync<IOException>(() => f.CreateService(writer).CompleteAsync(f.Delivery.HandoverRecordId, Request(f.Delivery), f.Manager.AccountId));
            Assert.Equal(1, failure.Calls); Assert.Equal((1, 3, 4), failure.Writes); Assert.Equal(2, f.Media.Confirmed.Count);
            await using var verify = new AppDbContext(options);
            var record = await verify.HandoverRecords.SingleAsync(item => item.HandoverRecordId == f.Delivery.HandoverRecordId);
            Assert.Equal(HandoverStatus.IN_PROGRESS, record.Status); Assert.Null(record.CompletedAt); Assert.Null(record.SenderSignatureUrl); Assert.Null(record.ReceiverSignatureUrl);
            var transport = await verify.Transports.SingleAsync(); Assert.Equal(TransportStatus.IN_TRANSIT, transport.Status); Assert.Null(transport.CompletedAt); Assert.Null(transport.ActualArrivalAt);
            Assert.Null((await verify.RoutePlanRoutes.SingleAsync(segment => segment.RoutePlanRouteId == f.Second.RoutePlanRouteId)).ActualArrivalAt);
            Assert.Equal(RoutePlanStatus.ACTIVE, (await verify.RoutePlans.SingleAsync()).Status); Assert.Equal(TransportPlanStatus.IN_PROGRESS, (await verify.TransportPlans.SingleAsync()).Status);
            Assert.Equal(VehicleStatus.IN_USE, (await verify.Vehicles.SingleAsync()).Status); Assert.Empty(await verify.Notifications.ToArrayAsync());
            Assert.Empty(await verify.AccountNotifications.ToArrayAsync()); Assert.Empty(await verify.OutboxMessages.ToArrayAsync()); Assert.Empty(f.Realtime.Messages);
        });
    }

    [PostgresFact]
    public async Task Postgres_ErrorAfterSuccessfulCommitKeepsPersistedSignatures()
    {
        await WithDatabaseAsync(async options =>
        {
            await using var seed = new AppDbContext(options); var f = await Fixture.CreateStartedAsync(seed);
            await using (var writer = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(options).AddInterceptors(new CommitFailure()).Options))
                await Assert.ThrowsAsync<IOException>(() => f.CreateService(writer).CompleteAsync(f.Pickup.HandoverRecordId, Request(f.Pickup), f.Manager.AccountId));
            await using var verify = new AppDbContext(options);
            Assert.Equal(HandoverStatus.COMPLETED, (await verify.HandoverRecords.SingleAsync(item => item.HandoverRecordId == f.Pickup.HandoverRecordId)).Status);
            Assert.Equal(TransportStatus.IN_TRANSIT, (await verify.Transports.SingleAsync()).Status); Assert.Equal(3, await verify.OutboxMessages.CountAsync());
            Assert.Equal(2, f.Media.Confirmed.Count);
        });
    }

    [PostgresFact]
    public async Task Postgres_ConcurrentCompletionCommitsOneLifecycleChangeAndOneNotificationPerRecipient()
    {
        await WithDatabaseAsync(async options =>
        {
            await using var seed = new AppDbContext(options); var f = await Fixture.CreateStartedAsync(seed); AddVehicle(seed, f.First); await seed.SaveChangesAsync();
            var saved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var competingLock = new LockSignal();
            var audit = new SaveAudit { AfterSave = async () => { saved.TrySetResult(); await release.Task.WaitAsync(TimeSpan.FromSeconds(15)); } };
            await using var first = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(options).AddInterceptors(audit).Options);
            await using var second = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(options).AddInterceptors(competingLock).Options);
            var winner = f.CreateService(first).CompleteAsync(f.Pickup.HandoverRecordId, Request(f.Pickup), f.Driver.AccountId);
            try
            {
                await saved.Task.WaitAsync(TimeSpan.FromSeconds(15));
                await using (var pending = new AppDbContext(options))
                { Assert.Equal(HandoverStatus.IN_PROGRESS, (await pending.HandoverRecords.SingleAsync(item => item.HandoverRecordId == f.Pickup.HandoverRecordId)).Status); Assert.Empty(await pending.OutboxMessages.ToArrayAsync()); }
                var loser = Record.ExceptionAsync(() => f.CreateService(second).CompleteAsync(f.Pickup.HandoverRecordId, Request(f.Pickup), f.Manager.AccountId));
                await competingLock.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15)); release.TrySetResult();
                Assert.Equal("COMPLETED", (await winner).Status);
                var error = await loser; Assert.NotNull(error);
                Assert.True(PostgresErrors.IsWriteConflict(error) || error.InnerException is { } inner && PostgresErrors.IsWriteConflict(inner), error.ToString());
            }
            finally { release.TrySetResult(); }
            await using var verify = new AppDbContext(options);
            Assert.Equal(TransportStatus.IN_TRANSIT, (await verify.Transports.SingleAsync()).Status); Assert.Equal(VehicleStatus.IN_USE, (await verify.Vehicles.SingleAsync()).Status);
            Assert.Equal(HandoverStatus.COMPLETED, (await verify.HandoverRecords.SingleAsync(item => item.HandoverRecordId == f.Pickup.HandoverRecordId)).Status);
            Assert.Equal(3, await verify.Notifications.CountAsync()); Assert.Equal(3, await verify.OutboxMessages.CountAsync());
            Assert.Equal(3, await verify.AccountNotifications.Select(item => item.AccountId).Distinct().CountAsync()); Assert.Equal(4, f.Media.Confirmed.Count);
            Assert.Equal("INVALID_HANDOVER_STATUS", (await Assert.ThrowsAsync<ConflictException>(() => f.CreateService(verify).CompleteAsync(f.Pickup.HandoverRecordId, Request(f.Pickup), f.Manager.AccountId))).ErrorCode);
        });
    }

    private sealed class CommitFailure : DbTransactionInterceptor
    {
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default) =>
            throw new IOException("Planned failure after a successful commit");
    }
}
