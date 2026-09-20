using System;
using System.Collections.Generic;

namespace Pegaxus.Repository.Entities;

/// <summary>
/// HandoverRecord: Một lần bàn giao trong Transport. from_type/to_type xác định bên giao/nhận là CUSTOMER hay EMPLOYEE; from/to_route_plan_route_id xác định chặng vừa kết thúc và chặng tiếp theo.
/// </summary>
public partial class HandoverRecord
{
    public Guid HandoverRecordId { get; set; }

    public Guid TransportId { get; set; }

    public string HandoverCode { get; set; } = null!;

    public int SequenceNo { get; set; }

    public DateTime? PlannedHandoverAt { get; set; }

    public DateTime? ActualHandoverAt { get; set; }

    public Guid? FromCustomerId { get; set; }

    public Guid? FromEmployeeId { get; set; }

    public Guid? ToCustomerId { get; set; }

    public Guid? ToEmployeeId { get; set; }

    public Guid? FromRoutePlanRouteId { get; set; }

    public Guid? ToRoutePlanRouteId { get; set; }

    public string? FromPartyName { get; set; }

    public string? FromContactName { get; set; }

    public string? FromContactPhone { get; set; }

    public string? ToPartyName { get; set; }

    public string? ToContactName { get; set; }

    public string? ToContactPhone { get; set; }

    public string? LocationName { get; set; }

    public string? Address { get; set; }

    public string? Country { get; set; }

    public string? HorseConditionNote { get; set; }

    public string? DocumentHandoverNote { get; set; }

    public string? EquipmentHandoverNote { get; set; }

    public string? DiscrepancyNote { get; set; }

    public string? SenderSignatureUrl { get; set; }

    public string? ReceiverSignatureUrl { get; set; }

    public DateTime? CompletedAt { get; set; }

    public DateTime? CancelledAt { get; set; }

    public string? CancellationReason { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual ICollection<Claim> Claims { get; set; } = new List<Claim>();

    public virtual Customer? FromCustomer { get; set; }

    public virtual Employee? FromEmployee { get; set; }

    public virtual RoutePlanRoute? FromRoutePlanRoute { get; set; }

    public virtual Customer? ToCustomer { get; set; }

    public virtual Employee? ToEmployee { get; set; }

    public virtual RoutePlanRoute? ToRoutePlanRoute { get; set; }

    public virtual Transport Transport { get; set; } = null!;
}
