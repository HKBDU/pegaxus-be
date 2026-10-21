namespace Pegaxus.Service.IncidentService;

public interface IIncidentService
{
    Task<Response.RerouteContext> GetRerouteContextAsync(Guid incidentReportId, Guid accountId, CancellationToken cancellationToken = default);
    Task<Response.EscalationResult> EscalateRerouteAsync(Guid incidentReportId, Request.RerouteEscalationRequest request, Guid accountId, CancellationToken cancellationToken = default);
    Task<Response.HoldResult> HoldRouteAsync(Guid incidentReportId, Request.RouteHoldRequest request, Guid accountId, CancellationToken cancellationToken = default);
    Task<int> QueueExpiredHoldsAsync(CancellationToken cancellationToken = default);
    Task<Response.ListResult> ListAsync(Request.ListRequest request, Guid accountId, CancellationToken cancellationToken = default);
    Task<object> GetAsync(Guid incidentReportId, Guid accountId, CancellationToken cancellationToken = default);
    Task<Response.Incident> ResolveAsync(Guid incidentReportId, Request.ResolveRequest request, Guid accountId, CancellationToken cancellationToken = default);
    Task<Response.Incident> CancelAsync(Guid incidentReportId, Request.CancelRequest request, Guid accountId, CancellationToken cancellationToken = default);
    Task<Response.Incident> SelfResolveAsync(Guid incidentReportId, Request.SelfResolveRequest request, Guid accountId, CancellationToken cancellationToken = default);
    Task<Response.SeverityResult> RaiseSeverityAsync(Guid incidentReportId, Request.RaiseSeverityRequest request, Guid accountId, CancellationToken cancellationToken = default);
    Task<Response.CreateResult> ReportVehicleAsync(Request.ReportVehicleRequest request, Guid accountId, CancellationToken cancellationToken = default);
    Task<Response.Incident> UpdateVehicleAsync(Guid incidentReportId, Request.UpdateVehicleRequest request, Guid accountId, CancellationToken cancellationToken = default);
    Task<Response.EmergencyContacts> GetEmergencyContactsAsync(Guid transportId, Guid accountId, CancellationToken cancellationToken = default);
    Task<Response.CreateResult> ReportHorseHealthAsync(Request.ReportHorseHealthRequest request, Guid accountId, CancellationToken cancellationToken = default);
    Task<Response.Incident> AddHorseDetailsAsync(Guid incidentReportId, Request.AddHorseDetailsRequest request, Guid accountId, CancellationToken cancellationToken = default);
    Task<Response.Incident> FollowUpHorseAsync(Guid incidentReportId, Guid horseHealthIncidentDetailId, Request.FollowUpHorseRequest request, Guid accountId, CancellationToken cancellationToken = default);
}
