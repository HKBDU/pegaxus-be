using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Common;

using static Pegaxus.Service.Common.TransportItinerary;

namespace Pegaxus.Service.Test;

public class TransportItineraryTests
{
    private static readonly DateTime T0 = new(2030, 1, 1, 8, 0, 0, DateTimeKind.Unspecified);

    private static RoutePlanRoute Route(int sequenceNo, RouteType type = RouteType.PRIMARY, bool departed = false, bool arrived = false,
        bool superseded = false, bool outsourced = false) => new()
    {
        RoutePlanRouteId = Guid.NewGuid(), SequenceNo = sequenceNo, RouteType = type,
        PlannedDepartureAt = T0.AddHours(sequenceNo), PlannedArrivalAt = T0.AddHours(sequenceNo + 1),
        ActualDepartureAt = departed || arrived ? T0.AddHours(sequenceNo) : null,
        ActualArrivalAt = arrived ? T0.AddHours(sequenceNo + 1) : null,
        SupersededAt = superseded ? T0 : null,
        TransportProviderId = outsourced ? Guid.NewGuid() : null
    };

    private static RoutePlan Plan(RoutePlanStatus status, params RoutePlanRoute[] routes) => new()
    {
        RoutePlanId = Guid.NewGuid(), Status = status, CreatedAt = T0, UpdatedAt = T0, RoutePlanRoutes = routes
    };

    private static int[] Sequences(IReadOnlyList<Segment> segments) => segments.Select(item => item.Route.SequenceNo).ToArray();

    [Theory]
    [InlineData(false, false, RouteSegmentStatus.PLANNED)]
    [InlineData(true, false, RouteSegmentStatus.IN_TRANSIT)]
    [InlineData(true, true, RouteSegmentStatus.COMPLETED)]
    public void StatusOf_IsDerivedFromActualTimes(bool departed, bool arrived, RouteSegmentStatus expected) =>
        Assert.Equal(expected, StatusOf(Route(1, departed: departed, arrived: arrived)));

    [Fact]
    public void Primary_NumbersSegmentsFromOneInSequenceOrder()
    {
        var segments = Effective([Plan(RoutePlanStatus.CONFIRMED, Route(3), Route(1), Route(2))]);
        Assert.Equal([1, 2, 3], Sequences(segments));
        Assert.Equal([1, 2, 3], segments.Select(item => item.DisplayOrder).ToArray());
    }

    [Theory]
    [InlineData(RoutePlanStatus.DRAFT)]
    [InlineData(RoutePlanStatus.CANCELLED)]
    public void PrimaryThatIsNotConfirmed_HasNoItinerary(RoutePlanStatus status) =>
        Assert.Empty(Effective([Plan(status, Route(1))]));

    [Fact]
    public void SupersededSegments_AreHiddenUnlessTheyAlreadyDeparted()
    {
        var plan = Plan(RoutePlanStatus.ACTIVE, Route(1, arrived: true, superseded: true), Route(2, departed: true, superseded: true),
            Route(3, superseded: true), Route(4));
        Assert.Equal([1, 2, 4], Sequences(Effective([plan])));
    }

    [Fact]
    public void ActiveBackup_KeepsDepartedPrimarySegmentsAndContinuesFromTheBranchPoint()
    {
        var primary = Plan(RoutePlanStatus.ACTIVE, Route(1, arrived: true), Route(2, departed: true, superseded: true), Route(3, superseded: true));
        var backupBranch = Route(2, RouteType.BACKUP);
        var backupTail = Route(3);
        var backup = Plan(RoutePlanStatus.ACTIVE, Route(1), backupBranch, backupTail);

        var segments = Effective([primary, backup]);

        Assert.Equal(4, segments.Count);
        Assert.Equal([1, 2, 3, 4], segments.Select(item => item.DisplayOrder).ToArray());
        Assert.Equal(primary.RoutePlanRoutes.Take(2).Select(item => item.RoutePlanRouteId), segments.Take(2).Select(item => item.Route.RoutePlanRouteId));
        Assert.Equal([backupBranch.RoutePlanRouteId, backupTail.RoutePlanRouteId], segments.Skip(2).Select(item => item.Route.RoutePlanRouteId).ToArray());
        Assert.Equal([RouteSegmentStatus.COMPLETED, RouteSegmentStatus.IN_TRANSIT, RouteSegmentStatus.PLANNED, RouteSegmentStatus.PLANNED],
            segments.Select(item => item.Status).ToArray());
    }

    [Fact]
    public void Backup_ThatIsOnlyConfirmed_DoesNotChangeTheItinerary()
    {
        var primary = Plan(RoutePlanStatus.ACTIVE, Route(1), Route(2));
        var backup = Plan(RoutePlanStatus.CONFIRMED, Route(1), Route(2, RouteType.BACKUP));
        Assert.Equal([1, 2], Sequences(Effective([primary, backup])));
        Assert.All(Effective([primary, backup]), item => Assert.Contains(item.Route, primary.RoutePlanRoutes));
    }

    [Fact]
    public void CompletedBackup_StillShapesTheItineraryOfAFinishedTransport()
    {
        var primary = Plan(RoutePlanStatus.COMPLETED, Route(1, arrived: true), Route(2, arrived: true, superseded: true));
        var backup = Plan(RoutePlanStatus.COMPLETED, Route(1, arrived: true), Route(2, RouteType.BACKUP, arrived: true));
        var segments = Effective([primary, backup]);
        Assert.Equal(3, segments.Count);
        Assert.All(segments, item => Assert.Equal(RouteSegmentStatus.COMPLETED, item.Status));
    }

    [Fact]
    public void ActiveReturn_KeepsEveryDepartedSegmentAndAppendsTheReturnSegments()
    {
        var primary = Plan(RoutePlanStatus.ACTIVE, Route(1, arrived: true), Route(2, departed: true, superseded: true), Route(3, superseded: true));
        var returning = Plan(RoutePlanStatus.ACTIVE, Route(1, RouteType.RETURN), Route(2, RouteType.RETURN));
        var segments = Effective([primary, returning]);
        Assert.Equal(4, segments.Count);
        Assert.Equal(returning.RoutePlanRoutes.Select(item => item.RoutePlanRouteId), segments.Skip(2).Select(item => item.Route.RoutePlanRouteId));
    }

    [Fact]
    public void BranchPoint_IsTheFirstBackupSegment()
    {
        var backup = Plan(RoutePlanStatus.CONFIRMED, Route(1), Route(4, RouteType.BACKUP), Route(3, RouteType.BACKUP), Route(2));
        Assert.Equal(3, BranchSequenceNo(backup));
        Assert.Equal(3, BranchSegment(backup)!.SequenceNo);
        Assert.Null(BranchSequenceNo(Plan(RoutePlanStatus.CONFIRMED, Route(1))));
        Assert.Null(BranchSegment(Plan(RoutePlanStatus.CONFIRMED, Route(1))));
    }

    [Fact]
    public void PlanType_IsDerivedFromItsSegments()
    {
        Assert.True(IsPrimaryPlan(Plan(RoutePlanStatus.DRAFT, Route(1), Route(2))));
        Assert.False(IsPrimaryPlan(Plan(RoutePlanStatus.DRAFT)));
        var mixed = Plan(RoutePlanStatus.DRAFT, Route(1), Route(2, RouteType.BACKUP));
        Assert.True(IsBackupPlan(mixed));
        Assert.False(IsPrimaryPlan(mixed));
        Assert.True(IsReturnPlan(Plan(RoutePlanStatus.DRAFT, Route(1, RouteType.RETURN))));
    }

    [Fact]
    public void Current_FollowsTheTransportStatusWhenNothingIsInTransit()
    {
        var planned = Effective([Plan(RoutePlanStatus.CONFIRMED, Route(1), Route(2))]);
        Assert.Equal(1, Current(planned, TransportStatus.READY)!.Route.SequenceNo);
        Assert.Equal(2, Current(planned, TransportStatus.COMPLETED)!.Route.SequenceNo);
        Assert.Null(Current(planned, TransportStatus.IN_TRANSIT));
        Assert.Null(Current([], TransportStatus.READY));

        var moving = Effective([Plan(RoutePlanStatus.ACTIVE, Route(1, arrived: true), Route(2, departed: true), Route(3))]);
        Assert.Equal(2, Current(moving, TransportStatus.IN_TRANSIT)!.Route.SequenceNo);
    }

    [Fact]
    public void DepartureAndEta_PreferActualDepartureAndUseTheLastPlannedArrival()
    {
        var segments = Effective([Plan(RoutePlanStatus.CONFIRMED, Route(1), Route(2))]);
        Assert.Equal(T0.AddHours(1), DepartureAt(new Transport(), segments));
        Assert.Equal(T0.AddHours(5), DepartureAt(new Transport { ActualDepartureAt = T0.AddHours(5) }, segments));
        Assert.Equal(T0.AddHours(3), EtaAt(segments));
        Assert.Null(EtaAt([]));
    }

    [Fact]
    public void Boundaries_AreOnePickupNMinusOneTransfersAndOneDelivery()
    {
        var segments = Effective([Plan(RoutePlanStatus.CONFIRMED, Route(1), Route(2), Route(3))]);
        var boundaries = Boundaries(segments, []);

        Assert.Equal([HandoverType.PICKUP, HandoverType.TRANSFER, HandoverType.TRANSFER, HandoverType.DELIVERY],
            boundaries.Select(item => item.Type).ToArray());
        Assert.Equal([BoundaryPosition.Start, BoundaryPosition.Middle, BoundaryPosition.Middle, BoundaryPosition.End],
            boundaries.Select(item => item.Position).ToArray());
        Assert.Null(boundaries[0].From);
        Assert.Equal(segments[0], boundaries[0].To);
        Assert.Equal(segments[0], boundaries[1].From);
        Assert.Equal(segments[1], boundaries[1].To);
        Assert.Equal(segments[2], boundaries[3].From);
        Assert.Null(boundaries[3].To);
        Assert.Empty(Boundaries([], []));
    }

    [Fact]
    public void Boundaries_AttachTheNewestNonCancelledHandoverOfTheSamePair()
    {
        var segments = Effective([Plan(RoutePlanStatus.ACTIVE, Route(1), Route(2))]);
        var first = segments[0].Route.RoutePlanRouteId;
        var second = segments[1].Route.RoutePlanRouteId;
        HandoverRecord Record(int sequenceNo, HandoverStatus status, Guid? from, Guid? to) =>
            new() { HandoverRecordId = Guid.NewGuid(), SequenceNo = sequenceNo, Status = status, FromRoutePlanRouteId = from, ToRoutePlanRouteId = to };
        var cancelled = Record(3, HandoverStatus.CANCELLED, first, second);
        var older = Record(1, HandoverStatus.COMPLETED, first, second);
        var newer = Record(2, HandoverStatus.IN_PROGRESS, first, second);
        var pickup = Record(4, HandoverStatus.PLANNED, null, first);

        var boundaries = Boundaries(segments, [cancelled, older, newer, pickup]);

        Assert.Equal(pickup, boundaries[0].ExistingHandover);
        Assert.Equal(newer, boundaries[1].ExistingHandover);
        Assert.Null(boundaries[2].ExistingHandover);
    }

    [Fact]
    public void Boundary_KnowsWhenAnOutsourcedSegmentIsInvolved()
    {
        var segments = Effective([Plan(RoutePlanStatus.CONFIRMED, Route(1), Route(2, outsourced: true), Route(3))]);
        var boundaries = Boundaries(segments, []);
        Assert.False(boundaries[0].InvolvesOutsourcedSegment);
        Assert.True(boundaries[1].InvolvesOutsourcedSegment);
        Assert.True(boundaries[2].InvolvesOutsourcedSegment);
        Assert.False(boundaries[3].InvolvesOutsourcedSegment);
    }

    [Theory]
    [InlineData(0, TransportStatus.READY, true, true)]
    [InlineData(0, TransportStatus.IN_TRANSIT, true, false)]
    [InlineData(1, TransportStatus.IN_TRANSIT, true, true)]
    [InlineData(1, TransportStatus.READY, true, false)]
    [InlineData(2, TransportStatus.IN_TRANSIT, true, true)]
    [InlineData(1, TransportStatus.IN_TRANSIT, false, false)]
    public void CanCreate_DependsOnRoleAndTransportStatus(int index, TransportStatus status, bool callerMayCreate, bool expected)
    {
        var segments = Effective([Plan(RoutePlanStatus.CONFIRMED, Route(1), Route(2))]);
        Assert.Equal(expected, CanCreate(Boundaries(segments, [])[index], status, callerMayCreate));
    }

    [Fact]
    public void CanCreate_IsFalseOnceAHandoverExists()
    {
        var segments = Effective([Plan(RoutePlanStatus.CONFIRMED, Route(1))]);
        var existing = new HandoverRecord { Status = HandoverStatus.PLANNED, ToRoutePlanRouteId = segments[0].Route.RoutePlanRouteId };
        Assert.False(CanCreate(Boundaries(segments, [existing])[0], TransportStatus.READY, true));
    }
}
