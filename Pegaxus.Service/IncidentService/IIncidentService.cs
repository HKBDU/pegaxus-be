namespace Pegaxus.Service.IncidentService;

public interface IIncidentService
{
    Task<Response.CreateResult> ReportVehicleAsync(Request.ReportVehicleRequest request, Guid accountId, CancellationToken cancellationToken = default);
    Task<Response.Incident> UpdateVehicleAsync(Guid incidentReportId, Request.UpdateVehicleRequest request, Guid accountId, CancellationToken cancellationToken = default);
    Task<Response.EmergencyContacts> GetEmergencyContactsAsync(Guid transportId, Guid accountId, CancellationToken cancellationToken = default);
}
