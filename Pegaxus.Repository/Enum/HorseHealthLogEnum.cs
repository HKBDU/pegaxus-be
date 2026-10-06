namespace Pegaxus.Repository.Enum;

public enum HorseHealthLogType {
    PRE_TRIP_CHECK = 1,
    ROUTINE_CHECK,
    BORDER_CHECK,
    REST_STOP_CHECK,
    INCIDENT_FOLLOWUP,
    POST_TRIP_CHECK,
    OTHER
}
public enum HorseHealthStatus {
    NORMAL = 1,
    OBSERVATION_REQUIRED,
    AT_RISK,
    CRITICAL
}
public enum HydrationStatus {
    NORMAL = 1,
    MILD_DEHYDRATION,
    MODERATE_DEHYDRATION,
    SEVERE_DEHYDRATION
}
