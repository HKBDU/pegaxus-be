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
        // A scheduled record must be started before it can be completed.
        if (canOperate) actions.Add(record.Status == HandoverStatus.PLANNED ? Start : Complete);
        if (isManagerOrCoordinator) actions.Add(Cancel);
        return actions;
    }

    /// <summary>
    /// Whether the boundary between two consecutive segments needs a signed handover. The first and last boundary always do (a customer is a party).
    /// A middle boundary is waived only when one internal driver runs both segments; an outsourced segment has no driver, so it never is.
    /// </summary>
    public static bool RequiredBetween(Guid? fromDriverEmployeeId, Guid? toDriverEmployeeId) =>
        !fromDriverEmployeeId.HasValue || fromDriverEmployeeId != toDriverEmployeeId;
}
