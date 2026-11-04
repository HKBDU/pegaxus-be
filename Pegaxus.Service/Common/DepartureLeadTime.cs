namespace Pegaxus.Service.Common;

/// <summary>The earliest departure a booking made on a given day may have: 3 weeks for a domestic trip, 2 months across a border. There is no latest date.</summary>
public static class DepartureLeadTime
{
    public static DateOnly Earliest(DateOnly madeOn, bool domestic) => domestic ? madeOn.AddDays(21) : madeOn.AddMonths(2);
}
