using System.Buffers.Binary;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Repository.Extensions;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.MediaService;

using static Pegaxus.Service.Common.DbTime;

namespace Pegaxus.Service.HandoverService;

public sealed partial class Service
{
    public async Task<Response.DetailResponse> CompleteAsync(Guid handoverRecordId, Request.CompleteRequest request, Guid accountId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.SenderSignature?.PublicId) || string.IsNullOrWhiteSpace(request.ReceiverSignature?.PublicId))
            throw new BadRequestException("Both sender and receiver signatures are required", "SIGNATURE_REQUIRED");
        Validation.Validate(request, completeValidator);
        var senderUrl = await ConfirmSignatureAsync(accountId, request.SenderSignature, cancellationToken);
        var receiverUrl = await ConfirmSignatureAsync(accountId, request.ReceiverSignature, cancellationToken);
        await using var transaction = await db.Database.BeginSerializableAsync(cancellationToken);
        var actor = await ActorAsync(accountId, cancellationToken);
        var item = await LockAsync(handoverRecordId, cancellationToken);
        if (!await CanOperateAsync(item, actor, cancellationToken)) throw Forbidden();
        // A scheduled record must be started first, so completion always follows a deliberate start.
        if (item.Status != HandoverStatus.IN_PROGRESS) throw InvalidStatus(item);
        if (Truncate(request.LastUpdatedAt!.Value) != Truncate(item.UpdatedAt))
            throw new ConflictException("The handover record was just modified. Please reload the page", "HANDOVER_MODIFIED");
        var (transport, segments) = await LockJourneyAsync(item.TransportId, cancellationToken);
        var ordered = EffectiveSegments(segments.Where(segment => EligibleStatuses.Contains(segment.RoutePlan.Status))
            .Select(segment => new SegmentData(segment.RoutePlanRouteId, segment.RoutePlanId, segment.RoutePlan.Status, segment.RoutePlan.UpdatedAt,
                segment.SequenceNo, segment.RouteType, segment.SupersededAt, segment.ActualDepartureAt, "", "")).ToArray(), transport.Status);
        var effective = ordered.Select(entry => segments.Single(segment => segment.RoutePlanRouteId == entry.Id)).ToArray();
        var customerId = await db.Bookings.Where(booking => booking.BookingId == transport.BookingId).Select(booking => booking.CustomerId).SingleAsync(cancellationToken);
        ValidateBoundary(item, segments, effective, customerId);
        // Coordinates only prove the device is at the meeting point; they are discarded. A location without coordinates cannot be checked.
        if (tracking.Value.EnforceHandoverRadius && await GeofenceAsync(item, cancellationToken) is { } geofence &&
            Geo.DistanceMeters(request.Latitude!.Value, request.Longitude!.Value, geofence.Latitude, geofence.Longitude) > geofence.RadiusMeters)
            throw new UnprocessableEntityException("You are not within the handover radius of the handover point", "NOT_IN_HANDOVER_RADIUS");
        var now = Truncate(clock.GetUtcNow());
        ApplyCompletion(item, request, senderUrl, receiverUrl, now);
        var effects = await ApplyEffectsAsync(item, transport, segments, effective, now, cancellationToken);
        await QueueCompletionAsync(item, transport, segments, now, effects, cancellationToken);
        var result = (await DetailAsync(item, (await HeaderAsync(item.TransportId, cancellationToken)) with { Status = transport.Status }, actor, cancellationToken))
            with { Effects = effects };
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await notifications.FlushAsync(cancellationToken);
        if (item.HandoverType == HandoverType.DELIVERY) await EnsureFinancialReportAsync(transport.TransportId, cancellationToken);
        return result;
    }

    /// <summary>R27: the report is created after the commit, so a failure here is only logged and never undoes the signed handover.</summary>
    private async Task EnsureFinancialReportAsync(Guid transportId, CancellationToken cancellationToken)
    {
        try { await financialReports.EnsureAsync(transportId, cancellationToken); }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            logger.LogWarning(error, "Financial report for transport {TransportId} could not be created after delivery; the first view will create it", transportId);
        }
    }

    private async Task<string> ConfirmSignatureAsync(Guid accountId, UploadService.Request.UploadedAssetReference? reference, CancellationToken cancellationToken)
    {
        ConfirmedUploadResult asset;
        try { asset = await media.ConfirmUploadAsync(accountId, reference!.PublicId, cancellationToken); }
        catch (Pegaxus.Service.Exceptions.Exceptions error) when (error.ErrorCode == "FILE_TOO_LARGE") { throw InvalidImage(); }
        if (asset.MimeType != "image/png" || asset.SizeBytes is < 45 or > 200 * 1024) throw InvalidImage();
        var content = await media.DownloadPreviewAsync(asset.FileUrl, cancellationToken);
        if (content.ContentType != "image/png" || content.Content.Length != asset.SizeBytes) throw InvalidImage();
        ValidatePng(content.Content);
        return asset.FileUrl;
    }

    private static void ValidatePng(byte[] bytes)
    {
        if (bytes.Length < 45) throw InvalidImage();
        if (!bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) ||
            !bytes.AsSpan(8, 8).SequenceEqual(new byte[] { 0, 0, 0, 13, 73, 72, 68, 82 }) ||
            !bytes.AsSpan(bytes.Length - 12).SequenceEqual(new byte[] { 0, 0, 0, 0, 73, 69, 78, 68, 174, 66, 96, 130 }) ||
            BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16)) == 0 || BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20)) == 0) throw InvalidImage();
        var offset = 8;
        var hasImageData = false;
        while (offset <= bytes.Length - 12)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset));
            if (length > bytes.Length - offset - 12) throw InvalidImage();
            if (bytes.AsSpan(offset + 4, 4).SequenceEqual("IDAT"u8) && length > 0) hasImageData = true;
            offset += (int)length + 12;
        }
        if (offset != bytes.Length || !hasImageData) throw InvalidImage();
    }

    private static BadRequestException InvalidImage() => new("Signatures must be PNG images up to 200 KB", "INVALID_IMAGE");

    private static void ApplyCompletion(HandoverRecord item, Request.CompleteRequest request, string senderUrl, string receiverUrl, DateTime now)
    {
        item.Status = HandoverStatus.COMPLETED;
        item.ActualHandoverAt = item.CompletedAt = item.UpdatedAt = now;
        item.HorseConditionNote = request.HorseConditionNote!.Trim();
        item.DocumentHandoverNote = request.DocumentHandoverNote!.Trim();
        item.EquipmentHandoverNote = request.EquipmentHandoverNote!.Trim();
        item.DiscrepancyNote = Text.EmptyToNull(request.DiscrepancyNote);
        item.SenderSignatureUrl = senderUrl; item.ReceiverSignatureUrl = receiverUrl;
        if (request.LocationName is not null) item.LocationName = Text.EmptyToNull(request.LocationName);
        if (request.Address is not null) item.Address = Text.EmptyToNull(request.Address);
        if (request.Country is not null) item.Country = Text.EmptyToNull(request.Country);
        if (request.FromPartyName is not null) item.FromPartyName = Text.EmptyToNull(request.FromPartyName);
        if (request.FromContactName is not null) item.FromContactName = Text.EmptyToNull(request.FromContactName);
        if (request.FromContactPhone is not null) item.FromContactPhone = Text.EmptyToNull(request.FromContactPhone);
        if (request.ToPartyName is not null) item.ToPartyName = Text.EmptyToNull(request.ToPartyName);
        if (request.ToContactName is not null) item.ToContactName = Text.EmptyToNull(request.ToContactName);
        if (request.ToContactPhone is not null) item.ToContactPhone = Text.EmptyToNull(request.ToContactPhone);
    }
}
