namespace Pegaxus.Repository.Enum;

public enum QuarantineProofStatus
{
    PendingVerification = 1,
    Passed,
    PassedWithConditions,
    Holdback,
    Failed,
    Rejected
}
