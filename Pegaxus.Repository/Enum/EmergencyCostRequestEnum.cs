namespace Pegaxus.Repository.Enum;

public enum EmergencyCostCategory {
    VETERINARY = 1,
    MEDICATION,
    VEHICLE_REPAIR,
    TOWING,
    REPLACEMENT_VEHICLE,
    EMERGENCY_STABLE,
    ROUTE_CHANGE,
    ACCOMMODATION,
    OTHER
}
public enum EmergencyCostStatus {
    PENDING = 1,
    APPROVED,
    REJECTED,
    CANCELLED
}

public enum EmergencyReceiptStatus {
    NOT_REQUIRED = 1,
    PENDING,
    SUBMITTED,
    APPROVED,
    REJECTED
}
