using System.Buffers.Binary;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Repository.Extensions;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;

using static Pegaxus.Service.Common.DbTime;

namespace Pegaxus.Service.HandoverService;

public sealed partial class Service
{
    public async Task<Response.DetailResponse> CompleteAsync(Guid handoverRecordId, Request.CompleteRequest request, Guid accountId, CancellationToken cancellationToken = default)
    {
        if (request.SenderSignature is null || request.ReceiverSignature is null)
            throw new BadRequestException("Both sender and receiver signatures are required", "SIGNATURE_REQUIRED");
        Validation.Validate(request, completeValidator);
        await ValidateSignatureAsync(request.SenderSignature, cancellationToken);
        await ValidateSignatureAsync(request.ReceiverSignature, cancellationToken);
        var uploaded = new List<string>();
        string? senderUrl = null, receiverUrl = null;
        var commitStarted = false;
        Response.DetailResponse result;
        try
        {
            await using var transaction = await db.Database.BeginSerializableAsync(cancellationToken);
            var actor = await ActorAsync(accountId, cancellationToken);
            var item = await LockAsync(handoverRecordId, cancellationToken);
            if (!await CanOperateAsync(item, actor, cancellationToken)) throw Forbidden();
            if (item.Status is not (HandoverStatus.PLANNED or HandoverStatus.IN_PROGRESS)) throw InvalidStatus(item);
            if (Truncate(request.LastUpdatedAt!.Value) != Truncate(item.UpdatedAt))
                throw new ConflictException("The handover record was just modified. Please reload the page", "HANDOVER_MODIFIED");
            var (transport, segments) = await LockJourneyAsync(item.TransportId, cancellationToken);
            var ordered = EffectiveSegments(segments.Where(segment => EligibleStatuses.Contains(segment.RoutePlan.Status))
                .Select(segment => new SegmentData(segment.RoutePlanRouteId, segment.RoutePlanId, segment.RoutePlan.Status, segment.RoutePlan.UpdatedAt,
                    segment.SequenceNo, segment.RouteType, segment.SupersededAt, segment.ActualDepartureAt, "", "")).ToArray(), transport.Status);
            var effective = ordered.Select(entry => segments.Single(segment => segment.RoutePlanRouteId == entry.Id)).ToArray();
            var customerId = await db.Bookings.Where(booking => booking.BookingId == transport.BookingId).Select(booking => booking.CustomerId).SingleAsync(cancellationToken);
            ValidateBoundary(item, segments, effective, customerId);
            var sender = await media.UploadFileAsync(request.SenderSignature); uploaded.Add(sender.PublicId);
            var receiver = await media.UploadFileAsync(request.ReceiverSignature); uploaded.Add(receiver.PublicId);
            senderUrl = sender.FileUrl; receiverUrl = receiver.FileUrl;
            var now = Truncate(clock.GetUtcNow());
            ApplyCompletion(item, request, sender.FileUrl, receiver.FileUrl, now);
            var effects = await ApplyEffectsAsync(item, transport, segments, effective, now, cancellationToken);
            await QueueCompletionAsync(item, transport, segments, now, cancellationToken);
            result = (await DetailAsync(item, (await HeaderAsync(item.TransportId, cancellationToken)) with { Status = transport.Status }, actor, cancellationToken))
                with { Effects = effects };
            await db.SaveChangesAsync(cancellationToken);
            commitStarted = true;
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            // A failed commit can have succeeded remotely; do not delete a persisted signature.
            var keepFiles = false;
            if (commitStarted)
            {
                try { keepFiles = await db.HandoverRecords.AsNoTracking().AnyAsync(item => item.HandoverRecordId == handoverRecordId &&
                    item.Status == HandoverStatus.COMPLETED && item.SenderSignatureUrl == senderUrl && item.ReceiverSignatureUrl == receiverUrl, CancellationToken.None); }
                catch (Exception error) { keepFiles = true; logger.LogWarning(error, "Could not verify handover {HandoverRecordId} after a failed commit; retaining signatures", handoverRecordId); }
            }
            if (!keepFiles)
                foreach (var publicId in uploaded)
                    try { await media.DeleteFileAsync(publicId); }
                    catch (Exception error) { logger.LogWarning(error, "Could not delete orphaned handover signature {PublicId}", publicId); }
            throw;
        }
        await notifications.FlushAsync(cancellationToken);
        return result;
    }

    private static async Task ValidateSignatureAsync(IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length is < 45 or > 200 * 1024 || !string.Equals(file.ContentType, "image/png", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetExtension(file.FileName), ".png", StringComparison.OrdinalIgnoreCase)) throw InvalidImage();
        await using var stream = file.OpenReadStream();
        var bytes = new byte[file.Length];
        try { await stream.ReadExactlyAsync(bytes, cancellationToken); }
        catch (EndOfStreamException) { throw InvalidImage(); }
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
