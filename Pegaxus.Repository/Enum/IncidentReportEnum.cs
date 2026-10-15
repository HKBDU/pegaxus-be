namespace Pegaxus.Repository.Enum;

public enum IncidentCategory {
    VEHICLE = 1,
    HORSE_HEALTH,
    BOTH,
    ROUTE_CHANGE
}
public enum IncidentSeverity {
    LOW = 1,
    MEDIUM,
    HIGH,
    CRITICAL
}
public enum IncidentStatus {
    REPORTED = 1,
    PENDING,
    RESOLVED,
    CANCELLED
}
