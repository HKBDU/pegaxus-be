using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;

namespace Pegaxus.Service.Common;

public static class HandoverRules
{
    public const string Start = "START";
    public const string Complete = "COMPLETE";
    public const string Cancel = "CANCEL";

    /// <summary>LM and RC may always create a handover; DRIVER and ESCORT only when assigned to the from or to segment.</summary>
    public static bool MayCreate(EmployeeRole? role, bool assignedToFromOrToSegment) => role switch
    {
        EmployeeRole.LOGISTICS_MANAGER or EmployeeRole.FLEET_ROUTE_COORDINATOR => true,
        EmployeeRole.DRIVER or EmployeeRole.ESCORT => assignedToFromOrToSegment,
        _ => false
    };

    /// <param name="canOperate">Caller is LM, RC, or a Driver/Escort assigned to the from or to segment.</param>
    /// <param name="isManagerOrCoordinator">Caller is LM or RC.</param>
    public static IReadOnlyList<string> AvailableActions(HandoverRecord record, bool canOperate, bool isManagerOrCoordinator)
    {
        if (record.Status is not (HandoverStatus.PLANNED or HandoverStatus.IN_PROGRESS)) return [];
        var actions = new List<string>(3);
        if (canOperate && record.Status == HandoverStatus.PLANNED) actions.Add(Start);
        if (canOperate) actions.Add(Complete);
        if (isManagerOrCoordinator) actions.Add(Cancel);
        return actions;
    }
}
