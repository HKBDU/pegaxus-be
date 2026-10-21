using Pegaxus.Repository.Enum;
using Pegaxus.Service.Common;

namespace Pegaxus.Service.Test;

public class RouteRestRulesTests
{
    [Theory]
    [InlineData(4.5, 0, true)]
    [InlineData(4.51, 0.74, false)]
    [InlineData(4.51, 0.75, true)]
    [InlineData(8, 0.75, true)]
    [InlineData(8.01, 1, false)]
    public void Driving_ChecksEightHourAndFortyFiveMinuteBoundaries(double hours, double restHours, bool passed)
    {
        var start = new DateTime(2026, 10, 22); var arrival = start.AddHours(hours);
        var checks = RouteRestRules.Evaluate([new(start, arrival, TransportMode.ROAD, null, null),
            new(arrival.AddHours(restHours), arrival.AddHours(restHours + 1), TransportMode.AIR, null, null)]);
        Assert.Equal(passed, checks[0].Passed); Assert.True(checks[1].Passed);
    }

    [Theory]
    [InlineData(22, false, true)]
    [InlineData(23, true, false)]
    [InlineData(24, false, false)]
    [InlineData(24, true, true)]
    public void LongTrip_RequiresTwentyFourHoursAtRestStop(double gapHours, bool restStop, bool passed)
    {
        var start = new DateTime(2026, 10, 22); var arrival = start.AddHours(1);
        var checks = RouteRestRules.Evaluate([new(start, arrival, TransportMode.ROAD, null, restStop ? LocationType.REST_STOP : null),
            new(arrival.AddHours(gapHours), arrival.AddHours(gapHours + 1), TransportMode.ROAD, null, null)]);
        Assert.True(checks[0].Passed); Assert.Equal(passed, checks[1].Passed);
    }

    [Fact]
    public void FinalLongRoadLeg_StillRequiresRest()
    {
        var start = new DateTime(2026, 10, 22);
        Assert.False(RouteRestRules.Evaluate([new(start, start.AddHours(5), TransportMode.ROAD, null, null)])[0].Passed);
        Assert.All(RouteRestRules.Evaluate([]), check => Assert.False(check.Passed));
    }
}
