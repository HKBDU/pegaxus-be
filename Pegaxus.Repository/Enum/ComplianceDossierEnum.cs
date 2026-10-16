namespace Pegaxus.Repository.Enum;

public enum DossierType {
    MAIN = 1,
    EXTRA
}

public enum ComplianceDossierStatus {
    DRAFT = 1,
    IN_PREPARATION,
    READY_FOR_SUBMISSION,
    SUBMITTED,
    PENDING,
    APPROVED,
    REJECTED,
    CLOSED
}
