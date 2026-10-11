namespace Pegaxus.Repository.Enum;

public enum RoutePlanStatus {
    DRAFT = 1,
    CONFIRMED,
    ACTIVE,
    COMPLETED,
    CANCELLED,
    PENDING_LEGAL_REVIEW,
    PENDING_MANAGER_APPROVAL,
    REJECTED_BY_MANAGER,
    APPROVED
}
