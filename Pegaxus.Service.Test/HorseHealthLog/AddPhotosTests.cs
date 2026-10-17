using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.HorseHealthService.Validator;

using static Pegaxus.Service.Test.HorseHealthLogTests;

using Health = Pegaxus.Service.HorseHealthService;
using HealthLogs = Pegaxus.Service.HorseHealthLogService;

namespace Pegaxus.Service.Test;

public class AddPhotosTests
{
    [Fact]
    public async Task Append_PreservesEvidenceAndCheckFieldsAndReturnsAllPhotosOnEveryRead()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        var request = f.Request(); request.Files = [Photo(), Photo()];
        request.GeneralNote = "Original check"; request.BehaviorNote = "Restless";
        request.VeterinarianRequired = true; request.VeterinarianNote = "Internal vet note";
        var created = (await f.Service.CreateAsync(f.Transport.TransportId, request, f.Escort.AccountId)).Value;
        var log = await db.HorseHealthLogs.SingleAsync();
        var original = log.PhotoUrls.ToArray();
        var alerts = await db.Notifications.CountAsync();
        var response = await f.Service.AddPhotosAsync(f.Transport.TransportId, log.HorseHealthLogId,
            new() { Files = [Photo(), Photo(), Photo()] }, f.Escort.AccountId);
        Assert.Equal(5, response.Photos.Count);
        Assert.Equal(original.Concat(f.Storage.Confirmations.Skip(2).Select(Storage.Url)), log.PhotoUrls);
        Assert.Equal(created.CheckedAt, DbTime.AsUtc(log.CheckedAt));
        Assert.Equal(created.CreatedAt, DbTime.AsUtc(log.CreatedAt));
        Assert.Equal(f.Escort.EmployeeId, log.EmployeeId); Assert.Equal("Original check", log.GeneralNote);
        Assert.Equal("Restless", log.BehaviorNote); Assert.Equal("Internal vet note", log.VeterinarianNote);
        Assert.Equal(alerts, await db.Notifications.CountAsync()); Assert.Empty(f.Storage.Deletes);

        var replay = await f.Service.CreateAsync(f.Transport.TransportId, request, f.Escort.AccountId);
        Assert.False(replay.Created); Assert.Equal(5, replay.Value.Photos.Count); Assert.Equal(5, f.Storage.Confirmations.Count);
        var customerId = await db.Bookings.Where(item => item.BookingId == f.Transport.BookingId).Select(item => item.Customer.AccountId).SingleAsync();
        var viewer = new Health.Service(db, new FixedClock(), new ListRequestValidator(), f.Storage, f.Storage);
        var detail = await viewer.GetAsync(log.HorseHealthLogId, customerId);
        Assert.Equal(5, detail.Photos.Count); Assert.True(detail.HasPhoto); Assert.Null(detail.VeterinarianNote);
        Assert.True(Assert.Single((await viewer.GetLogsAsync(f.Transport.TransportId, f.Horse.HorseId, new(), customerId)).Items).HasPhoto);
        Assert.All(detail.Photos, photo => Assert.Equal(Storage.SignedUrl, photo.PhotoUrl));
        Assert.All(response.Photos, photo =>
        {
            Assert.Equal(Storage.SignedUrl, photo.PhotoUrl); Assert.Null(photo.Caption);
            Assert.Null(photo.Latitude); Assert.Null(photo.Longitude); Assert.Null(photo.CapturedAt);
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public async Task Append_RejectsMissingOrTooManyFilesBeforeUpload(int count)
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        var id = (await f.Service.CreateAsync(f.Transport.TransportId, f.Request(), f.Escort.AccountId)).Value.HorseHealthLogId;
        Assert.Equal("VALIDATION_ERROR", (await Assert.ThrowsAsync<BadRequestException>(() => f.Service.AddPhotosAsync(
            f.Transport.TransportId, id, new() { Files = Enumerable.Range(0, count).Select(_ => Photo()).ToList() }, f.Escort.AccountId))).ErrorCode);
        Assert.Empty(f.Storage.Confirmations); Assert.Empty((await db.HorseHealthLogs.SingleAsync()).PhotoUrls);
    }

    [Fact]
    public async Task Append_RejectsCombinedLimitAndInvalidConfirmedImage()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        var id = (await f.Service.CreateAsync(f.Transport.TransportId, f.Request(), f.Escort.AccountId)).Value.HorseHealthLogId;
        f.Storage.Preview = new("invalid"u8.ToArray(), "image/png");
        Assert.Equal("INVALID_IMAGE", (await Assert.ThrowsAsync<BadRequestException>(() => f.Service.AddPhotosAsync(
            f.Transport.TransportId, id, new() { Files = [Photo(), Photo()] }, f.Escort.AccountId))).ErrorCode);
        Assert.Single(f.Storage.Confirmations); f.Storage.Confirmations.Clear(); f.Storage.Preview = new(Png, "image/png");
        var log = await db.HorseHealthLogs.SingleAsync();
        log.PhotoUrls = Enumerable.Range(0, 4).Select(_ => Storage.Url(Guid.NewGuid())).ToArray(); await db.SaveChangesAsync();
        Assert.Equal("PHOTO_LIMIT_EXCEEDED", (await Assert.ThrowsAsync<UnprocessableEntityException>(() => f.Service.AddPhotosAsync(
            f.Transport.TransportId, id, new() { Files = [Photo(), Photo()] }, f.Escort.AccountId))).ErrorCode);
        Assert.Empty(f.Storage.Confirmations); Assert.Equal(4, log.PhotoUrls.Length);
    }

    [Theory]
    [InlineData("otherAuthor")]
    [InlineData("unassigned")]
    [InlineData("driver")]
    [InlineData("inactive")]
    [InlineData("superseded")]
    [InlineData("outsourced")]
    public async Task Append_RequiresActiveAssignedEscortWhoCreatedTheLog(string scenario)
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        var id = (await f.Service.CreateAsync(f.Transport.TransportId, f.Request(), f.Escort.AccountId)).Value.HorseHealthLogId;
        var accountId = f.Escort.AccountId;
        if (scenario == "otherAuthor")
        {
            var other = await EmployeeAsync(db, EmployeeRole.ESCORT); accountId = other.AccountId;
            f.Segment.EscortEmployeeId = other.EmployeeId;
        }
        if (scenario == "unassigned") f.Segment.EscortEmployeeId = null;
        if (scenario == "driver") f.Escort.Role = EmployeeRole.DRIVER;
        if (scenario == "inactive") f.Escort.IsActive = false;
        if (scenario == "superseded") { f.Segment.SupersededAt = Fixture.Now; f.Segment.ActualDepartureAt = null; }
        if (scenario == "outsourced") f.Segment.TransportProviderId = Guid.NewGuid();
        await db.SaveChangesAsync();
        Func<Task> append = () => f.Service.AddPhotosAsync(f.Transport.TransportId, id, new() { Files = [Photo()] }, accountId);
        if (scenario == "inactive") await Assert.ThrowsAsync<UnauthorizedException>(append);
        else Assert.Equal("FORBIDDEN", (await Assert.ThrowsAsync<ForbiddenException>(append)).ErrorCode);
        Assert.Empty(f.Storage.Confirmations); Assert.Empty(f.Storage.Deletes);
    }

    [Theory]
    [InlineData(TransportStatus.CREATED)]
    [InlineData(TransportStatus.PLANNING)]
    [InlineData(TransportStatus.COMPLETED)]
    [InlineData(TransportStatus.CANCELLED)]
    public async Task Append_RequiresOpenTransport(TransportStatus status)
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        var id = (await f.Service.CreateAsync(f.Transport.TransportId, f.Request(), f.Escort.AccountId)).Value.HorseHealthLogId;
        f.Transport.Status = status; await db.SaveChangesAsync();
        Assert.Equal("INVALID_TRANSPORT_STATUS", (await Assert.ThrowsAsync<ConflictException>(() => f.Service.AddPhotosAsync(
            f.Transport.TransportId, id, new() { Files = [Photo()] }, f.Escort.AccountId))).ErrorCode);
        Assert.Empty(f.Storage.Confirmations);
    }

    [Fact]
    public async Task Append_RejectsForeignLogMissingResourcesAndEmptyIds()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db); var foreign = await Fixture.CreateAsync(db);
        var id = (await foreign.Service.CreateAsync(foreign.Transport.TransportId, foreign.Request(), foreign.Escort.AccountId)).Value.HorseHealthLogId;
        var request = new HealthLogs.Request.AddPhotosRequest { Files = [Photo()] };
        Assert.Equal("HEALTH_LOG_NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(() => f.Service.AddPhotosAsync(
            f.Transport.TransportId, id, request, f.Escort.AccountId))).ErrorCode);
        Assert.Equal("HEALTH_LOG_NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(() => f.Service.AddPhotosAsync(
            f.Transport.TransportId, Guid.NewGuid(), request, f.Escort.AccountId))).ErrorCode);
        Assert.Equal("TRANSPORT_NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(() => f.Service.AddPhotosAsync(
            Guid.NewGuid(), id, request, f.Escort.AccountId))).ErrorCode);
        await Assert.ThrowsAsync<BadRequestException>(() => f.Service.AddPhotosAsync(Guid.Empty, id, request, f.Escort.AccountId));
        await Assert.ThrowsAsync<BadRequestException>(() => f.Service.AddPhotosAsync(f.Transport.TransportId, Guid.Empty, request, f.Escort.AccountId));
        Assert.Empty(f.Storage.Confirmations);
    }

    [Theory]
    [InlineData("secondConfirmation")]
    [InlineData("sign")]
    [InlineData("cancel")]
    public async Task Append_FailuresPreserveUploadedAndExistingEvidence(string failure)
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        var id = (await f.Service.CreateAsync(f.Transport.TransportId, f.Request(), f.Escort.AccountId)).Value.HorseHealthLogId;
        var log = await db.HorseHealthLogs.SingleAsync();
        var original = new[] { Storage.Url(Guid.NewGuid()) }; log.PhotoUrls = original; await db.SaveChangesAsync();
        using var cancellation = new CancellationTokenSource();
        f.Storage.FailAtConfirmation = failure == "secondConfirmation" ? 2 : null;
        f.Storage.FailSign = failure == "sign";
        if (failure == "cancel") f.Storage.AfterConfirmation = () => { cancellation.Cancel(); return Task.CompletedTask; };
        Func<Task> append = () => f.Service.AddPhotosAsync(f.Transport.TransportId, id,
            new() { Files = [Photo(), Photo()] }, f.Escort.AccountId, cancellation.Token);
        if (failure == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(append);
        else await Assert.ThrowsAsync<IOException>(append);
        Assert.Empty(f.Storage.Deletes); Assert.NotEmpty(f.Storage.Confirmations);
        Assert.Equal(original, log.PhotoUrls);
    }

    [Fact]
    public async Task Record_EnforcesCreationAndPanoramaPhotoLimits()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        var request = f.Request(); request.Files = Enumerable.Range(0, 6).Select(_ => Photo()).ToList();
        await Assert.ThrowsAsync<BadRequestException>(() => f.Service.CreateAsync(f.Transport.TransportId, request, f.Escort.AccountId));
        await Assert.ThrowsAsync<FieldValidationException>(() => f.Service.MarkAllNormalAsync(f.Transport.TransportId,
            new() { LogType = "OTHER", Files = [Photo(), Photo()] }, f.Escort.AccountId));
        Assert.Empty(f.Storage.Confirmations); Assert.Empty(db.HorseHealthLogs);
        request.Files.RemoveAt(5);
        Assert.Equal(5, (await f.Service.CreateAsync(f.Transport.TransportId, request, f.Escort.AccountId)).Value.Photos.Count);
        Assert.Equal(5, (await db.HorseHealthLogs.SingleAsync()).PhotoUrls.Length);
    }
}
