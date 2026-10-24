using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Common;

using static Pegaxus.Service.Common.DbTime;

namespace Pegaxus.Service.AuditService;

public sealed partial class Service
{
    private static string Pretty(string name) => char.ToUpperInvariant(name[0]) + name[1..].ToLowerInvariant().Replace('_', ' ');

    // ------------------------------------------------------------------ rows shared by the operations block and the timeline

    private sealed record HandoverRow(Guid Id, string Code, HandoverType Type, HandoverStatus Status, DateTime? CompletedAt, DateTime At);
    private sealed record IncidentRow(Guid Id, string Code, string Title, IncidentCategory Category, IncidentSeverity Severity, IncidentStatus Status, DateTime OccurredAt);
    private sealed record AuditRow(Guid Id, string ActionType, string TargetEntity, string? Summary, AuditVisibility Visibility, DateTime CreatedAt);

    /// <summary>A customer only sees pick-up and delivery records (flow4 HO2).</summary>
    private Task<List<HandoverRow>> HandoverRowsAsync(Guid transportId, Actor actor, CancellationToken cancellationToken)
    {
        var query = db.HandoverRecords.AsNoTracking().Where(item => item.TransportId == transportId);
        if (!actor.IsManager) query = query.Where(item => item.HandoverType == HandoverType.PICKUP || item.HandoverType == HandoverType.DELIVERY);
        return query.OrderBy(item => item.SequenceNo).ThenBy(item => item.HandoverRecordId)
            .Select(item => new HandoverRow(item.HandoverRecordId, item.HandoverCode, item.HandoverType, item.Status, item.CompletedAt,
                item.CompletedAt ?? item.CancelledAt ?? item.ActualHandoverAt ?? item.PlannedHandoverAt ?? item.CreatedAt)).ToListAsync(cancellationToken);
    }

    /// <summary>A customer only sees live HIGH and CRITICAL incidents, and never their content.</summary>
    private Task<List<IncidentRow>> IncidentRowsAsync(Guid transportId, Actor actor, CancellationToken cancellationToken)
    {
        var query = db.IncidentReports.AsNoTracking().Where(item => item.TransportId == transportId);
        if (!actor.IsManager)
            query = query.Where(item => item.Status != IncidentStatus.CANCELLED && (item.Severity == IncidentSeverity.HIGH || item.Severity == IncidentSeverity.CRITICAL));
        return query.OrderBy(item => item.OccurredAt).ThenBy(item => item.IncidentReportId)
            .Select(item => new IncidentRow(item.IncidentReportId, item.IncidentCode, item.Title, item.Category, item.Severity, item.Status, item.OccurredAt)).ToListAsync(cancellationToken);
    }

    /// <summary>INTERNAL audit records are for Logistics Managers only.</summary>
    private Task<List<AuditRow>> AuditRowsAsync(Guid transportId, Actor actor, CancellationToken cancellationToken)
    {
        var query = db.AuditDossiers.AsNoTracking().Where(item => item.ComplianceDossier.TransportId == transportId);
        if (!actor.IsManager) query = query.Where(item => item.Visibility == AuditVisibility.Public);
        return query.OrderBy(item => item.CreatedAt).ThenBy(item => item.AuditDossierId)
            .Select(item => new AuditRow(item.AuditDossierId, item.ActionType, item.TargetEntity, item.Summary, item.Visibility, item.CreatedAt)).ToListAsync(cancellationToken);
    }

    // ------------------------------------------------------------------ booking

    private async Task<Response.BookingBlock> BookingBlockAsync(TransportRow transport, Actor actor, CancellationToken cancellationToken)
    {
        var booking = await db.Bookings.AsNoTracking().Where(item => item.BookingId == transport.BookingId)
            .Select(item => new { item.BookingId, item.BookingCode, item.Status, item.CustomerId, CustomerName = item.Customer.FullName, item.OriginCountry, item.DestinationCountry })
            .SingleAsync(cancellationToken);
        // The customer gets no internal quotation: only invoices that were really issued.
        var invoices = db.Invoices.AsNoTracking().Where(item => item.BookingId == transport.BookingId && item.Status != InvoiceStatus.DRAFT);
        if (!actor.IsManager) invoices = invoices.Where(item => item.Type != InvoiceType.QUOTATION);
        var invoiceRows = await invoices.OrderBy(item => item.CreatedAt).ThenBy(item => item.InvoiceId)
            .Select(item => new { item.InvoiceId, item.InvoiceCode, item.Type, item.Status, item.TotalAmount, item.Currency }).ToListAsync(cancellationToken);
        var claimRows = await db.Claims.AsNoTracking().Where(item => item.HandoverRecord.Transport.BookingId == transport.BookingId)
            .OrderBy(item => item.SubmittedAt).ThenBy(item => item.ClaimId).Select(item => new { item.ClaimId, item.ClaimCode, item.Status }).ToListAsync(cancellationToken);
        return new(booking.BookingId, booking.BookingCode, booking.Status.ToString(), new(booking.CustomerId, booking.CustomerName), booking.OriginCountry, booking.DestinationCountry,
            invoiceRows.Select(item => new Response.InvoiceRef(item.InvoiceId, item.InvoiceCode, item.Type.ToString(), item.Status.ToString(), Vnd.ToLong(item.TotalAmount), item.Currency)).ToList(),
            claimRows.Select(item => new Response.ClaimRef(item.ClaimId, item.ClaimCode, item.Status.ToString())).ToList());
    }

    // ------------------------------------------------------------------ horses

    private async Task<List<Response.HorseItem>> HorsesBlockAsync(TransportRow transport, Actor actor, CancellationToken cancellationToken)
    {
        var horses = await db.BookingHorses.AsNoTracking().Where(link => link.BookingId == transport.BookingId).OrderBy(link => link.Horse.HorseName).ThenBy(link => link.HorseId)
            .Select(link => new { link.Horse.HorseId, link.Horse.HorseName, link.Horse.MicrochipNumber, link.Horse.PassportNumber, link.Horse.WeightKg, link.Horse.HeightCm })
            .ToListAsync(cancellationToken);
        // Same view as flow4: a customer reads the whole health log of their horses, only without the veterinarian's own note.
        var logs = (await db.HorseHealthLogs.AsNoTracking().Where(item => item.TransportId == transport.TransportId).OrderBy(item => item.CheckedAt).ThenBy(item => item.HorseHealthLogId)
                .Select(item => new
                {
                    item.HorseId, item.HorseHealthLogId, item.CheckedAt, item.LogType, item.HealthStatus, item.TemperatureCelsius, item.HeartRateBpm, item.RespiratoryRateBpm,
                    item.SymptomNote, item.InjuryNote, item.TreatmentProvided, item.MedicationGiven, item.VeterinarianRequired, item.VeterinarianNote, item.GeneralNote, item.PhotoUrls
                }).ToListAsync(cancellationToken)).ToLookup(item => item.HorseId);
        return horses.Select(horse => new Response.HorseItem(horse.HorseId, horse.HorseName, horse.MicrochipNumber, horse.PassportNumber, horse.WeightKg, horse.HeightCm,
            logs[horse.HorseId].Select(log => new Response.HealthLogItem(log.HorseHealthLogId, AsUtc(log.CheckedAt), log.LogType.ToString(), log.HealthStatus.ToString(),
                log.TemperatureCelsius, log.HeartRateBpm, log.RespiratoryRateBpm, log.SymptomNote, log.InjuryNote, log.TreatmentProvided, log.MedicationGiven, log.VeterinarianRequired,
                actor.IsManager ? log.VeterinarianNote : null, log.GeneralNote, log.PhotoUrls.Length)).ToList())).ToList();
    }

    // ------------------------------------------------------------------ routing

    private async Task<Response.RoutingBlock> RoutingBlockAsync(Guid transportId, Actor actor, CancellationToken cancellationToken)
    {
        var plans = await db.RoutePlans.AsNoTracking().Where(item => item.TransportPlan.TransportId == transportId)
            .Select(item => new { item.RoutePlanId, item.RoutePlanCode, item.PrimaryRoutePlanId, item.Status, item.TotalDistanceKm, item.EstimatedDurationMinutes, item.CreatedAt })
            .ToListAsync(cancellationToken);
        var planIds = plans.Select(item => item.RoutePlanId).ToArray();
        var segments = await db.RoutePlanRoutes.AsNoTracking().Where(item => planIds.Contains(item.RoutePlanId)).OrderBy(item => item.SequenceNo).ThenBy(item => item.RoutePlanRouteId)
            .Select(item => new
            {
                item.RoutePlanRouteId, item.RoutePlanId, item.SequenceNo, item.RouteType, item.Status, From = item.Route.FromLocation.LocationName, To = item.Route.ToLocation.LocationName,
                item.Route.TransportMode, item.TransportProviderId, Provider = item.TransportProvider == null ? null : item.TransportProvider.ProviderName,
                item.OutsourcingCostAmount, item.OutsourcingCurrency, Driver = item.DriverEmployee == null ? null : item.DriverEmployee.FullName,
                Escort = item.EscortEmployee == null ? null : item.EscortEmployee.FullName, item.PlannedDepartureAt, item.PlannedArrivalAt, item.ActualDepartureAt, item.ActualArrivalAt, item.SupersededAt
            }).ToListAsync(cancellationToken);
        var segmentIds = segments.Select(item => item.RoutePlanRouteId).ToArray();
        var vehicles = (await db.RoutePlanRouteVehicles.AsNoTracking().Where(item => segmentIds.Contains(item.RoutePlanRouteId)).OrderBy(item => item.AssignedAt)
                .Select(item => new { item.RoutePlanRouteId, item.Vehicle.VehicleCode, item.Vehicle.LicensePlate }).ToListAsync(cancellationToken))
            .GroupBy(item => item.RoutePlanRouteId).ToDictionary(group => group.Key, group => group.First());

        // A customer sees what really happened on each leg, not the plan, the partner's price, the vehicle or the people.
        Response.RoutePlanItem Map(Guid planId)
        {
            var plan = plans.Single(item => item.RoutePlanId == planId);
            var legs = segments.Where(item => item.RoutePlanId == planId).Select(item =>
            {
                vehicles.TryGetValue(item.RoutePlanRouteId, out var vehicle);
                return new Response.SegmentItem(item.RoutePlanRouteId, item.SequenceNo, item.RouteType.ToString(), item.Status.ToString(), item.From, item.To, item.TransportMode.ToString(),
                    item.TransportProviderId.HasValue ? "OUTSOURCED" : "INTERNAL",
                    actor.IsManager ? item.Provider : null, actor.IsManager ? item.OutsourcingCostAmount : null, actor.IsManager ? item.OutsourcingCurrency : null,
                    actor.IsManager ? vehicle?.VehicleCode : null, actor.IsManager ? vehicle?.LicensePlate : null, actor.IsManager ? item.Driver : null, actor.IsManager ? item.Escort : null,
                    actor.IsManager ? AsUtc(item.PlannedDepartureAt) : null, actor.IsManager ? AsUtc(item.PlannedArrivalAt) : null,
                    AsUtc(item.ActualDepartureAt), AsUtc(item.ActualArrivalAt), actor.IsManager ? AsUtc(item.SupersededAt) : null);
            }).ToList();
            return new(plan.RoutePlanId, plan.RoutePlanCode, plan.Status.ToString(), plan.TotalDistanceKm, plan.EstimatedDurationMinutes, legs);
        }

        // The primary plan is the newest one that is not cancelled; every other plan is an alternative kept for the record.
        var primary = plans.Where(item => item.PrimaryRoutePlanId == null).OrderBy(item => item.Status == RoutePlanStatus.CANCELLED)
            .ThenByDescending(item => item.CreatedAt).ThenBy(item => item.RoutePlanId).FirstOrDefault();
        return new(primary is null ? null : Map(primary.RoutePlanId),
            plans.Where(item => item.RoutePlanId != primary?.RoutePlanId).OrderBy(item => item.CreatedAt).ThenBy(item => item.RoutePlanId).Select(item => Map(item.RoutePlanId)).ToList());
    }
}
