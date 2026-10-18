using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;

using static Pegaxus.Service.Common.DbTime;

namespace Pegaxus.Service.HandoverService;

public sealed partial class Service
{
    private async Task<Response.DetailResponse> DetailAsync(HandoverRecord item, Header transport, Actor actor, CancellationToken cancellationToken)
    {
        var segments = await db.RoutePlanRoutes.AsNoTracking().Where(segment => segment.RoutePlan.TransportPlan.TransportId == item.TransportId)
            .Select(segment => new SegmentData(segment.RoutePlanRouteId, segment.RoutePlanId, segment.RoutePlan.Status, segment.RoutePlan.UpdatedAt,
                segment.SequenceNo, segment.RouteType, segment.SupersededAt, segment.ActualDepartureAt,
                segment.Route.FromLocation.LocationName, segment.Route.ToLocation.LocationName)).ToArrayAsync(cancellationToken);
        var ordered = EffectiveSegments(segments.Where(segment => EligibleStatuses.Contains(segment.PlanStatus)).ToArray(), transport.Status);
        Response.SegmentBrief? Brief(Guid? id)
        {
            var segment = segments.SingleOrDefault(segment => segment.Id == id);
            if (segment is null) return null;
            var index = Array.FindIndex(ordered, entry => entry.Id == segment.Id);
            return new(segment.Id, index >= 0 ? index + 1 : segment.SequenceNo, segment.From, segment.To);
        }
        var senderSignatureUrl = await ReadUrlAsync(item.SenderSignatureUrl, cancellationToken);
        var receiverSignatureUrl = await ReadUrlAsync(item.ReceiverSignatureUrl, cancellationToken);
        var actions = new List<string>();
        if (item.Status is HandoverStatus.PLANNED or HandoverStatus.IN_PROGRESS)
        {
            if (await CanOperateAsync(item, actor, cancellationToken))
            {
                if (item.Status == HandoverStatus.PLANNED) actions.Add("START");
                actions.Add("COMPLETE");
            }
            if (actor.IsManager) actions.Add("CANCEL");
        }
        return new(item.HandoverRecordId, item.HandoverCode, new(transport.Id, transport.Code, transport.Status.ToString()), transport.BookingId,
            item.SequenceNo, item.HandoverType.ToString(), item.Status.ToString(), AsUtc(item.PlannedHandoverAt), AsUtc(item.ActualHandoverAt), AsUtc(item.CompletedAt),
            new(item.FromType.ToString(), item.FromCustomerId, item.FromEmployeeId, null, item.FromPartyName, item.FromContactName,
                actor.IsCustomer && item.FromType == HandoverPartyType.EMPLOYEE ? null : item.FromContactPhone),
            new(item.ToType.ToString(), item.ToCustomerId, item.ToEmployeeId, null, item.ToPartyName, item.ToContactName,
                actor.IsCustomer && item.ToType == HandoverPartyType.EMPLOYEE ? null : item.ToContactPhone),
            Brief(item.FromRoutePlanRouteId), Brief(item.ToRoutePlanRouteId), new(item.LocationName, item.Address, item.Country),
            item.HorseConditionNote, item.DocumentHandoverNote, item.EquipmentHandoverNote, item.DiscrepancyNote, senderSignatureUrl, receiverSignatureUrl,
            AsUtc(item.CancelledAt), item.CancellationReason, actions, AsUtc(item.CreatedAt), AsUtc(item.UpdatedAt));
    }

    private async Task<string?> ReadUrlAsync(string? fileUrl, CancellationToken cancellationToken) =>
        string.IsNullOrWhiteSpace(fileUrl) ? null : (await media.CreateReadUrlAsync(fileUrl, cancellationToken)).Url;

    private static SegmentData[] EffectiveSegments(SegmentData[] segments, TransportStatus status)
    {
        var plans = segments.GroupBy(segment => segment.PlanId).OrderByDescending(group => group.First().PlanUpdatedAt).ThenBy(group => group.Key).ToArray();
        var alternative = plans.FirstOrDefault(group => group.Any(segment => segment.Type != RouteType.PRIMARY) &&
            (group.First().PlanStatus == RoutePlanStatus.ACTIVE || status == TransportStatus.COMPLETED && group.First().PlanStatus == RoutePlanStatus.COMPLETED));
        var selected = alternative ?? plans.OrderByDescending(group => group.First().PlanStatus == RoutePlanStatus.ACTIVE)
            .FirstOrDefault(group => group.All(segment => segment.Type == RouteType.PRIMARY));
        if (selected is null) return [];
        var branch = alternative is not null && alternative.Any(segment => segment.Type == RouteType.BACKUP)
            ? alternative.Where(segment => segment.Type == RouteType.BACKUP).Min(segment => segment.SequenceNo) : 1;
        return segments.Where(segment => segment.PlanId != selected.Key && segment.DepartureAt.HasValue)
            .OrderBy(segment => segment.DepartureAt).ThenBy(segment => segment.PlanId).ThenBy(segment => segment.SequenceNo)
            .Concat(selected.Where(segment => segment.SequenceNo >= branch && (segment.SupersededAt is null || segment.DepartureAt.HasValue))
                .OrderBy(segment => segment.SequenceNo).ThenBy(segment => segment.Id)).ToArray();
    }

    private sealed record SegmentData(Guid Id, Guid PlanId, RoutePlanStatus PlanStatus, DateTime PlanUpdatedAt, int SequenceNo,
        RouteType Type, DateTime? SupersededAt, DateTime? DepartureAt, string From, string To);
}
