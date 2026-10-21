using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Repository.Extensions;
using Pegaxus.Service.Exceptions;

namespace Pegaxus.Service.Common;

public static class IncidentLinks
{
    /// <summary>
    /// Links a route change to the incident that caused it. The incident must be open on the same transport; the first action taken on it moves it to PENDING (O-F5-05).
    /// Call inside the caller's transaction, after its own locks.
    /// </summary>
    public static async Task<IncidentReport> AttachAsync(AppDbContext db, Guid incidentReportId, Guid transportId, DateTime now, CancellationToken cancellationToken)
    {
        await db.LockRowsAsync(db.IncidentReports.Where(x => x.IncidentReportId == incidentReportId), x => x.IncidentReportId, cancellationToken);
        var incident = await db.IncidentReports.SingleOrDefaultAsync(x => x.IncidentReportId == incidentReportId, cancellationToken)
            ?? throw new NotFoundException("Incident report not found", "INCIDENT_NOT_FOUND");
        if (incident.TransportId != transportId || incident.Status is not (IncidentStatus.REPORTED or IncidentStatus.PENDING))
            throw new UnprocessableEntityException("The incident is not open on this transport", "INVALID_INCIDENT_STATUS");
        if (incident.Status == IncidentStatus.REPORTED)
        {
            incident.Status = IncidentStatus.PENDING;
            incident.UpdatedAt = now;
        }
        return incident;
    }
}
