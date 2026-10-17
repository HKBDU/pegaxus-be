using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.MediaService;
using Pegaxus.Service.OutboxService;

using static Pegaxus.Service.Test.HandoverTests;

using Handovers = Pegaxus.Service.HandoverService;

namespace Pegaxus.Service.Test;

public class HandoverCompletionTests
{
    internal static byte[] PngBytes => Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/lS8AAAAASUVORK5CYII=");
    internal static IFormFile Png() => new FormFile(new MemoryStream(PngBytes), 0, PngBytes.Length, "signature", "signature.png") { Headers = new HeaderDictionary(), ContentType = "image/png" };
    internal static Handovers.Request.CompleteRequest Request(HandoverRecord record) => new()
    {
        LastUpdatedAt = DbTime.AsUtc(record.UpdatedAt), HorseConditionNote = "  Healthy horses  ", DocumentHandoverNote = "  Passports checked  ",
        EquipmentHandoverNote = "  All equipment checked  ", SenderSignature = Png(), ReceiverSignature = Png()
    };
    internal static Vehicle AddVehicle(AppDbContext db, params RoutePlanRoute[] segments)
    {
        var vehicle = new Vehicle { VehicleId = Guid.NewGuid(), VehicleCode = "VEH-" + Guid.NewGuid().ToString("N"), TransportMode = TransportMode.ROAD,
            Status = VehicleStatus.ASSIGNED, CreatedAt = Fixture.Now, UpdatedAt = Fixture.Now };
        foreach (var segment in segments) db.Add(new RoutePlanRouteVehicle { Vehicle = vehicle, RoutePlanRoute = segment, AssignedAt = Fixture.Now });
        return vehicle;
    }

    [Fact]
    public async Task Complete_FullJourney_UpdatesLifecycleVehiclesNotesNotificationsAndDeliveryEmail()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db); var vehicle = AddVehicle(db, f.First, f.Second); await db.SaveChangesAsync();
        var pickupRequest = Request(f.Pickup); pickupRequest.LocationName = "  Actual stable  "; pickupRequest.FromContactName = "  Actual sender  ";
        pickupRequest.ToContactPhone = "  +33123456789  "; pickupRequest.DiscrepancyNote = "  Minor scratch  ";
        var pickup = await f.Service.CompleteAsync(f.Pickup.HandoverRecordId, pickupRequest, f.Escort.AccountId);
        Assert.Equal("COMPLETED", pickup.Status); Assert.Empty(pickup.AvailableActions);
        Assert.Equal("IN_TRANSIT", pickup.Transport.Status); Assert.Equal("ACTIVE", pickup.Effects!.RoutePlanStatus); Assert.Equal("IN_PROGRESS", pickup.Effects.TransportPlanStatus);
        Assert.Equal(f.First.RoutePlanRouteId, pickup.Effects.ActivatedRoutePlanRouteId); Assert.Null(pickup.Effects.ClosedRoutePlanRouteId); Assert.Null(pickup.Effects.ClaimWindowEndsAt);
        Assert.Equal(Fixture.Now, f.First.ActualDepartureAt); Assert.Equal(Fixture.Now, f.Route.TransportPlan.Transport.ActualDepartureAt); Assert.Equal(VehicleStatus.IN_USE, vehicle.Status);
        Assert.Equal("Healthy horses", pickup.HorseConditionNote); Assert.Equal("Actual stable", pickup.Location.LocationName); Assert.Equal("Snapshot address", pickup.Location.Address);
        Assert.Equal("Actual sender", pickup.From.ContactName); Assert.Equal("+33123456789", pickup.To.ContactPhone); Assert.Equal("Minor scratch", pickup.DiscrepancyNote);
        Assert.Equal(3, await db.Notifications.CountAsync()); Assert.Empty(await db.OutboxMessages.ToListAsync());
        Assert.Equal(new[] { f.CustomerAccountId, f.Manager.AccountId, f.Coordinator.AccountId }.Order(), (await db.AccountNotifications.Select(item => item.AccountId).ToArrayAsync()).Order());
        Assert.All(await db.Notifications.ToArrayAsync(), item => Assert.Equal(NotificationType.TRANSPORT, item.NotificationType));
        f.Clock.Now = f.Clock.Now.AddHours(1);
        var transfer = await f.Service.CompleteAsync(f.Transfer.HandoverRecordId, Request(f.Transfer), f.Receiver.AccountId);
        Assert.Equal(f.Clock.Now.UtcDateTime.Ticks, f.First.ActualArrivalAt!.Value.Ticks); Assert.Equal(f.First.ActualArrivalAt, f.Second.ActualDepartureAt);
        Assert.Equal(f.First.RoutePlanRouteId, transfer.Effects!.ClosedRoutePlanRouteId); Assert.Equal(f.Second.RoutePlanRouteId, transfer.Effects.ActivatedRoutePlanRouteId);
        Assert.Null(transfer.DiscrepancyNote); Assert.Equal(4, await db.Notifications.CountAsync());
        var nextCrew = await db.Notifications.SingleAsync(item => item.NotificationType == NotificationType.ROUTE);
        Assert.Equal(NotificationPriority.HIGH, nextCrew.Priority); Assert.Equal($"/crew/transports/{f.TransportId}", nextCrew.ActionUrl);
        f.Clock.Now = f.Clock.Now.AddHours(1);
        var delivery = await f.Service.CompleteAsync(f.Delivery.HandoverRecordId, Request(f.Delivery), f.Coordinator.AccountId);
        Assert.Equal("COMPLETED", delivery.Transport.Status); Assert.Equal("COMPLETED", delivery.Effects!.TransportStatus);
        Assert.Equal("COMPLETED", delivery.Effects.RoutePlanStatus); Assert.Equal("COMPLETED", delivery.Effects.TransportPlanStatus);
        Assert.Equal(f.Clock.Now, delivery.CompletedAt); Assert.Equal(delivery.CompletedAt, delivery.ActualHandoverAt); Assert.Equal(delivery.CompletedAt, delivery.UpdatedAt);
        Assert.Equal(f.Clock.Now.AddHours(24), delivery.Effects.ClaimWindowEndsAt); Assert.Equal(VehicleStatus.AVAILABLE, vehicle.Status);
        Assert.Equal(f.Second.ActualArrivalAt, f.Route.TransportPlan.Transport.ActualArrivalAt); Assert.Equal(f.Second.ActualArrivalAt, f.Route.TransportPlan.Transport.CompletedAt);
        Assert.Equal(7, await db.Notifications.CountAsync()); Assert.Equal(7, f.Realtime.Messages.Count); Assert.Equal(6, f.Media.Uploaded.Count); Assert.Empty(f.Media.Deleted);
        var email = OutboxQueue.Deserialize<EmailPayload>((await db.OutboxMessages.SingleAsync()).Payload);
        Assert.Contains(f.Route.TransportPlan.Transport.TransportCode, email.Subject); Assert.Contains("24 hours", email.Body);
        Assert.Equal((await db.Accounts.SingleAsync(account => account.AccountId == f.CustomerAccountId)).Email, email.To);
    }

    [Theory]
    [InlineData(HandoverType.TRANSFER)]
    [InlineData(HandoverType.BORDER_HANDOVER)]
    public async Task Complete_PreservesGpsArrivalAndExistingDepartureTimes(HandoverType type)
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        f.Transfer.HandoverType = type; f.Transfer.Status = HandoverStatus.IN_PROGRESS;
        f.First.ActualArrivalAt = Fixture.Now.AddMinutes(-20); f.Second.ActualDepartureAt = Fixture.Now.AddMinutes(-10);
        f.Second.ActualArrivalAt = Fixture.Now.AddMinutes(-5); f.Route.TransportPlan.Transport.ActualArrivalAt = Fixture.Now.AddMinutes(-4);
        f.Route.Status = RoutePlanStatus.ACTIVE; f.Route.TransportPlan.Status = TransportPlanStatus.IN_PROGRESS; f.Route.TransportPlan.Transport.Status = TransportStatus.IN_TRANSIT;
        await db.SaveChangesAsync();
        await f.Service.CompleteAsync(f.Transfer.HandoverRecordId, Request(f.Transfer), f.Driver.AccountId);
        await f.Service.CompleteAsync(f.Delivery.HandoverRecordId, Request(f.Delivery), f.Manager.AccountId);
        Assert.Equal(Fixture.Now.AddMinutes(-20), f.First.ActualArrivalAt); Assert.Equal(Fixture.Now.AddMinutes(-10), f.Second.ActualDepartureAt);
        Assert.Equal(Fixture.Now.AddMinutes(-5), f.Second.ActualArrivalAt); Assert.Equal(Fixture.Now.AddMinutes(-4), f.Route.TransportPlan.Transport.ActualArrivalAt);
    }

    [Fact]
    public async Task Complete_DoesNotReleaseVehicleWithAnotherUnfinishedTransport()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db); var vehicle = AddVehicle(db, f.First, f.Second);
        var (other, segment) = await PersonnelAssignmentTests.RouteAsync(db, RoutePlanStatus.ACTIVE); other.TransportPlan.Transport.Status = TransportStatus.IN_TRANSIT;
        db.Add(new RoutePlanRouteVehicle { RoutePlanRoute = segment, Vehicle = vehicle }); await db.SaveChangesAsync();
        await f.Service.CompleteAsync(f.Pickup.HandoverRecordId, Request(f.Pickup), f.Manager.AccountId);
        await f.Service.CompleteAsync(f.Transfer.HandoverRecordId, Request(f.Transfer), f.Manager.AccountId);
        await f.Service.CompleteAsync(f.Delivery.HandoverRecordId, Request(f.Delivery), f.Manager.AccountId);
        Assert.Equal(VehicleStatus.IN_USE, vehicle.Status);
    }

    [Fact]
    public async Task Complete_NotifiesAllActiveManagersAndCoordinators()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        var otherCoordinator = await PersonnelAssignmentTests.EmployeeAsync(db, EmployeeRole.FLEET_ROUTE_COORDINATOR);
        var inactive = await PersonnelAssignmentTests.EmployeeAsync(db, EmployeeRole.LOGISTICS_MANAGER); inactive.IsActive = false; await db.SaveChangesAsync();
        await f.Service.CompleteAsync(f.Pickup.HandoverRecordId, Request(f.Pickup), f.Manager.AccountId);
        var recipients = await db.AccountNotifications.Select(item => item.AccountId).ToArrayAsync();
        Assert.Equal(4, recipients.Length); Assert.Contains(otherCoordinator.AccountId, recipients); Assert.DoesNotContain(inactive.AccountId, recipients);
    }

    [Theory]
    [InlineData("missing-sender", "SIGNATURE_REQUIRED")]
    [InlineData("missing-receiver", "SIGNATURE_REQUIRED")]
    [InlineData("missing-version", "VALIDATION_ERROR")]
    [InlineData("horse-note", "VALIDATION_ERROR")]
    [InlineData("document-note", "VALIDATION_ERROR")]
    [InlineData("equipment-note", "VALIDATION_ERROR")]
    [InlineData("oversize-field", "VALIDATION_ERROR")]
    [InlineData("jpeg", "INVALID_IMAGE")]
    [InlineData("fake-png", "INVALID_IMAGE")]
    [InlineData("oversize-file", "INVALID_IMAGE")]
    [InlineData("truncated-png", "INVALID_IMAGE")]
    [InlineData("header-only-png", "INVALID_IMAGE")]
    public async Task Complete_RejectsInvalidInputBeforeUploading(string invalid, string code)
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db); var request = Request(f.Pickup);
        switch (invalid)
        {
            case "missing-sender": request.SenderSignature = null; break;
            case "missing-receiver": request.ReceiverSignature = null; break;
            case "missing-version": request.LastUpdatedAt = null; break;
            case "horse-note": request.HorseConditionNote = "  "; break;
            case "document-note": request.DocumentHandoverNote = "  "; break;
            case "equipment-note": request.EquipmentHandoverNote = "  "; break;
            case "oversize-field": request.ToContactPhone = new string('1', 31); break;
            default:
                var bytes = invalid == "oversize-file" ? new byte[200 * 1024 + 1] : invalid == "truncated-png" ? PngBytes[..33] :
                    invalid == "header-only-png" ? PngBytes[..33].Concat(PngBytes[^12..]).ToArray() : new byte[70];
                request.ReceiverSignature = new FormFile(new MemoryStream(bytes), 0, bytes.Length, "receiverSignature", invalid == "jpeg" ? "signature.jpg" : "signature.png")
                    { Headers = new HeaderDictionary(), ContentType = invalid == "jpeg" ? "image/jpeg" : "image/png" }; break;
        }
        var error = await Assert.ThrowsAnyAsync<Pegaxus.Service.Exceptions.Exceptions>(() => f.Service.CompleteAsync(f.Pickup.HandoverRecordId, request, f.Manager.AccountId));
        Assert.Equal(code, error.ErrorCode); Assert.Empty(f.Media.Uploaded); Assert.Equal(HandoverStatus.PLANNED, f.Pickup.Status); Assert.Empty(db.Notifications);
    }

    [Theory]
    [InlineData(HandoverStatus.COMPLETED)]
    [InlineData(HandoverStatus.CANCELLED)]
    public async Task Complete_TerminalRecordsAreImmutable(HandoverStatus status)
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db); f.Pickup.Status = status; await db.SaveChangesAsync();
        Assert.Equal("INVALID_HANDOVER_STATUS", (await Assert.ThrowsAsync<ConflictException>(() => f.Service.CompleteAsync(f.Pickup.HandoverRecordId, Request(f.Pickup), f.Manager.AccountId))).ErrorCode);
        Assert.Empty(f.Media.Uploaded); Assert.Empty(db.Notifications);
    }

    [Fact]
    public async Task Complete_StaleVersionIsRejectedBeforeUploading()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db); var request = Request(f.Pickup); request.LastUpdatedAt = request.LastUpdatedAt!.Value.AddSeconds(-1);
        Assert.Equal("HANDOVER_MODIFIED", (await Assert.ThrowsAsync<ConflictException>(() => f.Service.CompleteAsync(f.Pickup.HandoverRecordId, request, f.Manager.AccountId))).ErrorCode);
        Assert.Empty(f.Media.Uploaded);
    }

    [Theory]
    [InlineData("customer")]
    [InlineData("specialist")]
    [InlineData("unassigned")]
    [InlineData("other-segment")]
    public async Task Complete_EnforcesActualRoleAndLinkedSegmentAssignment(string actor)
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        var accountId = actor switch { "customer" => f.CustomerAccountId, "specialist" => (await PersonnelAssignmentTests.EmployeeAsync(db, EmployeeRole.TRANSPORT_SPECIALIST)).AccountId,
            "unassigned" => (await PersonnelAssignmentTests.EmployeeAsync(db, EmployeeRole.DRIVER)).AccountId, _ => f.Receiver.AccountId };
        await Assert.ThrowsAsync<ForbiddenException>(() => f.Service.CompleteAsync(f.Pickup.HandoverRecordId, Request(f.Pickup), accountId)); Assert.Empty(f.Media.Uploaded);
    }

    [Theory]
    [InlineData("nonadjacent", "INVALID_SEGMENT_PAIR")]
    [InlineData("superseded", "INVALID_SEGMENT_PAIR")]
    [InlineData("party-changed", "INVALID_SEGMENT_PAIR")]
    [InlineData("xor", "INVALID_SEGMENT_PAIR")]
    [InlineData("foreign", "INVALID_SEGMENT_PAIR")]
    [InlineData("carrier", "HANDOVER_TYPE_NOT_SUPPORTED")]
    [InlineData("outsourced", "OUTSOURCED_SEGMENT")]
    public async Task Complete_RevalidatesBoundaryAndParties(string invalid, string code)
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        switch (invalid)
        {
            case "nonadjacent": f.Transfer.ToRoutePlanRoute = f.First; break;
            case "superseded": f.Second.SupersededAt = Fixture.Now; break;
            case "party-changed": f.Second.DriverEmployeeId = f.Driver.EmployeeId; break;
            case "xor": f.Transfer.ToCustomerId = f.CustomerId; break;
            case "foreign": f.Transfer.ToRoutePlanRoute = (await PersonnelAssignmentTests.RouteAsync(db)).Segment; break;
            case "carrier": f.Transfer.HandoverType = HandoverType.CARRIER_HANDOVER; break;
            case "outsourced": f.Second.TransportProvider = PersonnelAssignmentTests.Provider(); db.Add(f.Second.TransportProvider); break;
        }
        await db.SaveChangesAsync();
        Assert.Equal(code, (await Assert.ThrowsAsync<UnprocessableEntityException>(() => f.Service.CompleteAsync(f.Transfer.HandoverRecordId, Request(f.Transfer), f.Manager.AccountId))).ErrorCode);
        Assert.Empty(f.Media.Uploaded); Assert.Equal(HandoverStatus.PLANNED, f.Transfer.Status);
    }

    [Fact]
    public async Task Complete_BackupBoundaryMayCrossRoutePlansAndSkipsCopiedPrimaryPrefix()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        f.First.ActualDepartureAt = Fixture.Now.AddHours(-1); f.Second.SupersededAt = Fixture.Now;
        var backup = new RoutePlan { RoutePlanId = Guid.NewGuid(), RoutePlanCode = "BACKUP", TransportPlan = f.Route.TransportPlan, Status = RoutePlanStatus.ACTIVE, UpdatedAt = Fixture.Now };
        var copied = PersonnelAssignmentTests.Segment(backup, 1);
        var branch = PersonnelAssignmentTests.Segment(backup, 3); branch.RouteType = RouteType.BACKUP; branch.DriverEmployeeId = f.Receiver.EmployeeId;
        var tail = PersonnelAssignmentTests.Segment(backup, 4); db.AddRange(copied, branch, tail); f.Transfer.ToRoutePlanRoute = branch; await db.SaveChangesAsync();
        var detail = await f.Service.CompleteAsync(f.Transfer.HandoverRecordId, Request(f.Transfer), f.Driver.AccountId);
        Assert.Equal(branch.RoutePlanRouteId, detail.Effects!.ActivatedRoutePlanRouteId); Assert.Equal(Fixture.Now, branch.ActualDepartureAt);
        Assert.Null(copied.ActualDepartureAt); Assert.Null(f.Second.ActualDepartureAt);
    }

    [Fact]
    public async Task Complete_OtherHasNoJourneyEffectsOrNotifications()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db); f.Transfer.HandoverType = HandoverType.OTHER; await db.SaveChangesAsync();
        var result = await f.Service.CompleteAsync(f.Transfer.HandoverRecordId, Request(f.Transfer), f.Manager.AccountId);
        Assert.Equal("COMPLETED", result.Status); Assert.Equal("READY", result.Effects!.TransportStatus);
        Assert.Null(result.Effects.ActivatedRoutePlanRouteId); Assert.Null(result.Effects.ClosedRoutePlanRouteId); Assert.Null(result.Effects.ClaimWindowEndsAt);
        Assert.Null(f.First.ActualArrivalAt); Assert.Null(f.Second.ActualDepartureAt); Assert.Empty(db.Notifications); Assert.Empty(db.OutboxMessages);
    }

    [Fact]
    public async Task Complete_SecondUploadFailureDeletesFirstSignatureWithoutChangingDatabase()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db); f.Media.FailUpload = 2;
        await Assert.ThrowsAsync<IOException>(() => f.Service.CompleteAsync(f.Pickup.HandoverRecordId, Request(f.Pickup), f.Manager.AccountId));
        Assert.Single(f.Media.Uploaded); Assert.Equal(f.Media.Uploaded, f.Media.Deleted); Assert.Equal(HandoverStatus.PLANNED, f.Pickup.Status);
        Assert.Null(f.First.ActualDepartureAt); Assert.Empty(db.Notifications); Assert.Empty(db.OutboxMessages);
    }

    internal sealed class SignatureMedia : IMediaService
    {
        internal List<string> Uploaded { get; } = [];
        internal List<string> Deleted { get; } = [];
        internal int FailUpload { get; set; }
        public Task<(string FileUrl, string PublicId)> UploadFileAsync(IFormFile file)
        {
            if (Uploaded.Count + 1 == FailUpload) throw new IOException("Planned signature upload failure");
            var id = "handover/" + Guid.NewGuid().ToString("N") + ".png"; Uploaded.Add(id); return Task.FromResult(("https://storage.example.com/" + id, id));
        }
        public Task DeleteFileAsync(string publicId) { Deleted.Add(publicId); return Task.CompletedTask; }
        public Task<string> UploadImageAsync(IFormFile file) => throw new NotSupportedException();
        public Task<string> UploadPrivateImageAsync(Guid imageId, PreviewFile image, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeletePrivateImageAsync(Guid imageId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SignedUploadResult> CreateSignedUploadAsync(Guid accountId, string entity, string contentType, long sizeBytes) => throw new NotSupportedException();
        public Task<ConfirmedUploadResult> ConfirmUploadAsync(Guid accountId, string publicId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Pegaxus.Service.DocumentStorageService.SignedDocumentUrl> CreateReadUrlAsync(string fileUrl, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PreviewFile> DownloadPreviewAsync(string fileUrl, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
