namespace Pegaxus.Service.IncidentService;

public interface IIncidentService
{
    Task<Response.CreateResult> ReportVehicleAsync(Request.ReportVehicleRequest request, Guid accountId, CancellationToken cancellationToken = default);
    Task<Response.Incident> UpdateVehicleAsync(Guid incidentReportId, Request.UpdateVehicleRequest request, Guid accountId, CancellationToken cancellationToken = default);
    Task<Response.EmergencyContacts> GetEmergencyContactsAsync(Guid transportId, Guid accountId, CancellationToken cancellationToken = default);
    Task<Response.CreateResult> ReportHorseHealthAsync(Request.ReportHorseHealthRequest request, Guid accountId, CancellationToken cancellationToken = default);
    Task<Response.Incident> AddHorseDetailsAsync(Guid incidentReportId, Request.AddHorseDetailsRequest request, Guid accountId, CancellationToken cancellationToken = default);
    Task<Response.Incident> FollowUpHorseAsync(Guid incidentReportId, Guid horseHealthIncidentDetailId, Request.FollowUpHorseRequest request, Guid accountId, CancellationToken cancellationToken = default);
}
