using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.IncidentService.Validator;
using Pegaxus.Service.OutboxService;

using static Pegaxus.Service.Test.HorseHealthLogTests;

using Incidents = Pegaxus.Service.IncidentService;

namespace Pegaxus.Service.Test;

public class VehicleIncidentTests
{
    [Fact]
    public async Task Report_PersistsDetailsPhotosVehicleAndAlerts_WithoutExposingPrivateStorage()
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db); var request = f.Report();
        request.VehicleOperable = false; request.ReplacementVehicleRequired = true; request.RepairRequired = true; request.TowingRequired = true;
        request.EstimatedRepairCost = 15_000_000; request.EstimatedRepairHours = 6.5m;
        request.DriverSafetyStatus = " Safe "; request.CargoConditionNote = " Calm horses ";
        request.VehicleCondition = " Engine stopped "; request.DamageDescription = " Engine damage "; request.ImmediateAction = " Warning triangle ";
        request.PhotoFiles = [Photo()]; request.EmergencySettlementRequired = true;
        var result = await f.Service.ReportVehicleAsync(request, f.Driver.AccountId); var value = result.Value;
        Assert.True(result.Created); Assert.Matches(@"^INC-\d{8}-\d{4}$", value.IncidentCode);
        Assert.Equal("REPORTED", value.Status); Assert.Equal("VEHICLE", value.Category); Assert.True(value.EmergencySettlementRequired);
        Assert.Equal(f.Driver.EmployeeId, value.ReportedBy.EmployeeId); Assert.Equal(f.Transport.TransportId, value.Transport!.TransportId);
        Assert.Equal("Engine stopped", value.VehicleDetail!.VehicleCondition); Assert.Equal("Engine damage", value.VehicleDetail.DamageDescription);
        Assert.Equal(15_000_000, value.VehicleDetail.EstimatedRepairCost); Assert.Equal(6.5m, value.VehicleDetail.EstimatedRepairHours);
        Assert.Equal("Safe", value.VehicleDetail.DriverSafetyStatus); Assert.Equal("Calm horses", value.VehicleDetail.CargoConditionNote);
        Assert.Null(value.VehicleDetail.RepairNote); Assert.False(value.VehicleDetail.VehicleOperable);
        Assert.True(value.VehicleDetail.RepairRequired && value.VehicleDetail.TowingRequired && value.VehicleDetail.ReplacementVehicleRequired);
        Assert.Equal("OUT_OF_SERVICE", value.VehicleDetail.Vehicle.Status); Assert.Equal(VehicleStatus.OUT_OF_SERVICE, f.Vehicle.Status);
        Assert.Equal("Warning triangle", value.ImmediateAction); Assert.Equal("VND", value.CostSummary.Currency);
        Assert.Equal(Storage.SignedUrl, Assert.Single(value.Photos).PhotoUrl); Assert.Equal(1, value.PhotoCount);
        var photo = await db.IncidentPhotos.SingleAsync(); Assert.Equal(value.VehicleDetail.VehicleIncidentDetailId, photo.VehicleIncidentDetailId);
        Assert.Equal(value.IncidentReportId, photo.IncidentReportId); Assert.Equal(1, photo.SortOrder);
        Assert.NotEqual(photo.PhotoUrl, value.Photos[0].PhotoUrl); Assert.Equal(request.OccurredAt, value.OccurredAt);
        Assert.Equal(DateTimeKind.Unspecified, (await db.IncidentReports.SingleAsync()).OccurredAt.Kind);
        Assert.True(value.Effects!.VehicleStatusChanged && value.Effects.EmailQueued); Assert.Equal("URGENT", value.Effects.NotifiedPriority);
        Assert.False(value.Effects.CanAddHorseDetails); Assert.NotEmpty(value.Safety!.Message);
        var contacts = await f.Service.GetEmergencyContactsAsync(f.Transport.TransportId, f.Driver.AccountId);
        Assert.Equal(contacts.Contacts, value.Safety.EmergencyContacts); Assert.Equal(4, contacts.Contacts.Count);
        Assert.Equal(4, await db.Notifications.CountAsync()); Assert.Equal(4, await db.AccountNotifications.CountAsync());
        var mail = OutboxQueue.Deserialize<EmailPayload>((await db.OutboxMessages.SingleAsync()).Payload);
        Assert.Equal(f.Manager.Account.Email, mail.To); Assert.Contains(value.IncidentCode, mail.Body);
        var customer = await db.AccountNotifications.Include(item => item.Notification).SingleAsync(item => item.AccountId == f.CustomerAccountId);
        Assert.Equal(NotificationPriority.HIGH, customer.Notification.Priority);
        Assert.Equal($"/bookings/{f.Transport.BookingId}/transports/{f.Transport.TransportId}/incidents/{value.IncidentReportId}", customer.Notification.ActionUrl);
        foreach (var secret in new[] { request.Title, request.Description, request.IncidentType, f.Vehicle.VehicleCode, f.Driver.FullName, "15000000" })
            Assert.DoesNotContain(secret, customer.Notification.Title + customer.Notification.Message);
    }

    [Theory]
    [InlineData("LOW", "ENGINE_FAILURE", "VEHICLE", "HIGH", false, false)]
    [InlineData("MEDIUM", "ENGINE_FAILURE", "VEHICLE", "HIGH", false, false)]
    [InlineData("HIGH", "ENGINE_FAILURE", "VEHICLE", "URGENT", true, true)]
    [InlineData("CRITICAL", "ACCIDENT", "VEHICLE", "URGENT", true, true)]
    [InlineData("LOW", "TEMPERATURE_CONTROL_FAILURE", "VEHICLE", "URGENT", true, false)]
    [InlineData("LOW", "ENGINE_FAILURE", "BOTH", "URGENT", true, true)]
    public async Task Report_UsesSeverityPriorityAndSanitizedCustomerPolicy(string severity, string type, string category, string priority, bool email, bool customer)
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db); var request = f.Report();
        request.Severity = severity; request.IncidentType = type; request.Category = category;
        var extraManager = await EmployeeAsync(db, EmployeeRole.LOGISTICS_MANAGER);
        var inactive = await EmployeeAsync(db, EmployeeRole.LOGISTICS_MANAGER); inactive.IsActive = false; await db.SaveChangesAsync();
        var result = await f.Service.ReportVehicleAsync(request, f.Escort.AccountId);
        Assert.Equal(category == "BOTH" ? "CRITICAL" : severity, result.Value.Severity);
        Assert.Equal(f.Escort.EmployeeId, result.Value.ReportedBy.EmployeeId);
        Assert.Equal(priority, result.Value.Effects!.NotifiedPriority); Assert.Equal(email, result.Value.Effects.EmailQueued);
        Assert.Equal(category == "BOTH", result.Value.Effects.CanAddHorseDetails);
        var recipients = await db.AccountNotifications.Include(item => item.Notification).ToListAsync();
        foreach (var employee in new[] { f.Manager, extraManager, f.Coordinator, f.Specialist })
            Assert.Equal(priority, Assert.Single(recipients.Where(item => item.AccountId == employee.AccountId)).Notification.Priority.ToString());
        Assert.DoesNotContain(recipients, item => item.AccountId == inactive.AccountId);
        Assert.Equal(customer ? 1 : 0, recipients.Count(item => item.AccountId == f.CustomerAccountId));
        Assert.Equal(category == "BOTH" ? 1 : 0, recipients.Count(item => item.AccountId == f.Escort.AccountId));
        Assert.Equal(email ? 2 : 0, await db.OutboxMessages.CountAsync());
    }

    [Fact]
    public async Task Report_ReplayPreservesReportAndHasNoRepeatedPhotosNotificationsOrMail()
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db); var request = f.Report();
        request.Category = "BOTH"; request.PhotoFiles = [Photo()]; var service = f.Service;
        var first = await service.ReportVehicleAsync(request, f.Driver.AccountId);
        request.Title = "Changed during offline replay"; request.PhotoFiles = [Photo()];
        f.Transport.Status = TransportStatus.COMPLETED; await db.SaveChangesAsync();
        var replay = await service.ReportVehicleAsync(request, f.Driver.AccountId);
        Assert.False(replay.Created); Assert.Equal(first.Value.IncidentReportId, replay.Value.IncidentReportId);
        Assert.Equal(first.Value.Title, replay.Value.Title); Assert.Equal(new Incidents.Response.Effects(false, false, false, false), replay.Value.Effects);
        Assert.Single(db.IncidentReports); Assert.Single(db.VehicleIncidentDetails); Assert.Single(db.IncidentPhotos);
        Assert.Single(f.Storage.Confirmations); Assert.Equal(5, await db.Notifications.CountAsync()); Assert.Single(db.OutboxMessages);
    }

    [Theory]
    [InlineData("vehicleOperable")][InlineData("transportId")][InlineData("vehicleId")][InlineData("category")]
    [InlineData("severity")][InlineData("incidentType")][InlineData("description")][InlineData("title")]
    [InlineData("location")][InlineData("pair")][InlineData("precision")][InlineData("zero")][InlineData("range")]
    [InlineData("future")][InlineData("moneyDecimal")][InlineData("moneyNegative")][InlineData("moneyOverflow")]
    [InlineData("hours")][InlineData("hoursPrecision")][InlineData("safetyLength")][InlineData("photoCount")][InlineData("photoNull")][InlineData("photoDuplicate")]
    public async Task Report_RejectsInvalidInputBeforeStorageOrWrites(string field)
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db); var request = f.Report();
        switch (field)
        {
            case "vehicleOperable": request.VehicleOperable = null; break;
            case "transportId": request.TransportId = Guid.Empty; break;
            case "vehicleId": request.VehicleId = Guid.Empty; break;
            case "category": request.Category = "HORSE_HEALTH"; break;
            case "severity": request.Severity = "1"; break;
            case "incidentType": request.IncidentType = "UNKNOWN"; break;
            case "description": request.Description = "  short  "; break;
            case "title": request.Title = " "; break;
            case "location": request.LocationId = null; break;
            case "pair": request.Latitude = 50; break;
            case "precision": request.Latitude = 50.12345m; request.Longitude = 3; break;
            case "zero": request.Latitude = 0; request.Longitude = 0; break;
            case "range": request.Latitude = 91; request.Longitude = 3; break;
            case "future": request.OccurredAt = DbTime.AsUtc(Scenario.Now.AddMinutes(6)); break;
            case "moneyDecimal": request.EstimatedRepairCost = 1000.0m; break;
            case "moneyNegative": request.EstimatedRepairCost = -1; break;
            case "moneyOverflow": request.EstimatedRepairCost = 10_000_000_000_000_000m; break;
            case "hours": request.EstimatedRepairHours = 1000; break;
            case "hoursPrecision": request.EstimatedRepairHours = 1.25m; break;
            case "safetyLength": request.DriverSafetyStatus = new string('x', 501); break;
            case "photoCount": request.PhotoFiles = Enumerable.Range(0, 6).Select(_ => Photo()).ToList(); break;
            case "photoNull": request.PhotoFiles = [null!]; break;
            case "photoDuplicate": var photo = Photo(); request.PhotoFiles = [photo, photo]; break;
        }
        Assert.Equal(field == "photoCount" ? "TOO_MANY_IMAGES" : "VALIDATION_ERROR",
            (await Assert.ThrowsAnyAsync<BadRequestException>(() => f.Service.ReportVehicleAsync(request, f.Driver.AccountId))).ErrorCode);
        Assert.Empty(db.IncidentReports); Assert.Empty(db.Notifications); Assert.Empty(f.Storage.Confirmations);
    }

    [Fact]
    public async Task Report_EnforcesRoleAssignmentStateAndVehicleMembership()
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db); var request = f.Report();
        foreach (var actor in new[] { f.Manager, f.Coordinator, f.Specialist, await EmployeeAsync(db, EmployeeRole.DRIVER) })
            await Assert.ThrowsAsync<ForbiddenException>(() => f.Service.ReportVehicleAsync(request, actor.AccountId));
        await Assert.ThrowsAsync<ForbiddenException>(() => f.Service.ReportVehicleAsync(request, f.CustomerAccountId));
        request.TransportId = Guid.NewGuid(); Assert.Equal("TRANSPORT_NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(() => f.Service.ReportVehicleAsync(request, f.Driver.AccountId))).ErrorCode);
        request.TransportId = f.Transport.TransportId; request.VehicleId = Guid.NewGuid();
        Assert.Equal("VEHICLE_NOT_IN_TRANSPORT", (await Assert.ThrowsAsync<UnprocessableEntityException>(() => f.Service.ReportVehicleAsync(request, f.Driver.AccountId))).ErrorCode);
        request.VehicleId = f.Vehicle.VehicleId; f.Transport.Status = TransportStatus.READY; await db.SaveChangesAsync();
        Assert.Equal("INVALID_TRANSPORT_STATUS", (await Assert.ThrowsAsync<ConflictException>(() => f.Service.ReportVehicleAsync(request, f.Driver.AccountId))).ErrorCode);
        Assert.Empty(db.IncidentReports); Assert.Empty(db.Notifications);
    }

    [Fact]
    public async Task Location_UsesExistingWithin100MetersOtherwiseCreatesOther_RejectsDeletedOrMissing()
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db);
        f.Location.Latitude = 50.6292m; f.Location.Longitude = 3.0573m; await db.SaveChangesAsync();
        var request = f.Report(); request.LocationId = null; request.Latitude = 50.6293m; request.Longitude = 3.0573m;
        var near = await f.Service.ReportVehicleAsync(request, f.Driver.AccountId); Assert.Equal(f.Location.LocationId, near.Value.Location!.LocationId);
        request.OccurredAt = request.OccurredAt!.Value.AddSeconds(-1); request.Latitude = 40; request.Longitude = 2; request.LocationDescription = " Road marker "; request.Country = " ES ";
        var far = await f.Service.ReportVehicleAsync(request, f.Driver.AccountId); Assert.Equal("OTHER", far.Value.Location!.LocationType);
        Assert.Equal("ES", far.Value.Location.Country); Assert.Equal("Road marker", (await db.Locations.SingleAsync(item => item.LocationId == far.Value.Location.LocationId)).Address);
        request.OccurredAt = request.OccurredAt!.Value.AddSeconds(-1); request.LocationId = Guid.NewGuid();
        Assert.Equal("LOCATION_NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(() => f.Service.ReportVehicleAsync(request, f.Driver.AccountId))).ErrorCode);
        request.LocationId = f.Location.LocationId; f.Location.IsDeleted = true; await db.SaveChangesAsync();
        await Assert.ThrowsAsync<NotFoundException>(() => f.Service.ReportVehicleAsync(request, f.Driver.AccountId));
    }

    [Fact]
    public async Task Update_AfterCompletionChangesOnlySuppliedFields_AppendsPhotos_UsesIncidentVersionAndNormalAlerts()
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db); var report = f.Report();
        report.VehicleOperable = false; report.ReplacementVehicleRequired = true; report.PhotoFiles = [Photo()]; report.EstimatedRepairHours = 6.5m;
        var created = (await f.Service.ReportVehicleAsync(report, f.Driver.AccountId)).Value;
        f.Transport.Status = TransportStatus.COMPLETED; await db.SaveChangesAsync();
        Assert.Equal(created.UpdatedAt, DbTime.AsUtc((await db.IncidentReports.AsNoTracking().SingleAsync()).UpdatedAt));
        var request = new Incidents.Request.UpdateVehicleRequest { LastUpdatedAt = created.UpdatedAt, VehicleOperable = true,
            ReplacementVehicleRequired = false, RepairNote = " Fixed engine ", EstimatedRepairCost = 250000, DriverSafetyStatus = " All safe ", PhotoFiles = [Photo()] };
        var updated = await f.Service.UpdateVehicleAsync(created.IncidentReportId, request, f.Driver.AccountId);
        Assert.Equal("Fixed engine", updated.VehicleDetail!.RepairNote); Assert.Equal(250000, updated.VehicleDetail.EstimatedRepairCost);
        Assert.Equal(6.5m, updated.VehicleDetail.EstimatedRepairHours); Assert.Equal("All safe", updated.VehicleDetail.DriverSafetyStatus);
        Assert.True(updated.VehicleDetail.VehicleOperable); Assert.False(updated.VehicleDetail.ReplacementVehicleRequired);
        Assert.Equal("OUT_OF_SERVICE", updated.VehicleDetail.Vehicle.Status); Assert.Equal(created.Description, updated.Description);
        Assert.Equal(created.Severity, updated.Severity); Assert.Equal(2, updated.PhotoCount);
        Assert.Equal(new[] { 1, 2 }, updated.Photos.Select(photo => photo.SortOrder)); Assert.True(updated.UpdatedAt > created.UpdatedAt);
        Assert.Equal(updated.UpdatedAt, updated.VehicleDetail.UpdatedAt); Assert.Null(updated.Effects); Assert.Null(updated.Safety);
        Assert.Equal(3, await db.Notifications.CountAsync(item => item.Priority == NotificationPriority.NORMAL));
        Assert.Single(await db.AccountNotifications.Where(item => item.AccountId == f.CustomerAccountId).ToArrayAsync()); Assert.Single(db.OutboxMessages);
        Assert.Equal("INCIDENT_MODIFIED", (await Assert.ThrowsAsync<ConflictException>(() => f.Service.UpdateVehicleAsync(created.IncidentReportId, request, f.Driver.AccountId))).ErrorCode);
        Assert.Equal(2, await db.IncidentPhotos.CountAsync());
    }

    [Theory]
    [InlineData("manager", true)][InlineData("coordinator", true)][InlineData("reporter", true)]
    [InlineData("escort", false)][InlineData("specialist", false)][InlineData("unassignedCoordinator", false)][InlineData("formerReporter", false)]
    public async Task Update_EnforcesDatabaseRoleReporterAndAssignment(string role, bool allowed)
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db);
        var incident = (await f.Service.ReportVehicleAsync(f.Report(), f.Driver.AccountId)).Value;
        var actor = role switch { "manager" => f.Manager, "coordinator" => f.Coordinator, "specialist" => f.Specialist, "escort" => f.Escort,
            "unassignedCoordinator" => await EmployeeAsync(db, EmployeeRole.FLEET_ROUTE_COORDINATOR), _ => f.Driver };
        if (role == "formerReporter") { f.Segment.DriverEmployeeId = null; await db.SaveChangesAsync(); }
        var request = new Incidents.Request.UpdateVehicleRequest { LastUpdatedAt = incident.UpdatedAt, RepairRequired = false };
        if (allowed) Assert.False((await f.Service.UpdateVehicleAsync(incident.IncidentReportId, request, actor.AccountId)).VehicleDetail!.RepairRequired);
        else await Assert.ThrowsAsync<ForbiddenException>(() => f.Service.UpdateVehicleAsync(incident.IncidentReportId, request, actor.AccountId));
    }

    [Theory]
    [InlineData("RESOLVED", "INVALID_INCIDENT_STATUS")][InlineData("CANCELLED", "INVALID_INCIDENT_STATUS")]
    [InlineData("HORSE_HEALTH", "INCIDENT_CATEGORY_MISMATCH")][InlineData("missingDetail", "INCIDENT_CATEGORY_MISMATCH")]
    public async Task Update_RejectsClosedOrIncompatibleIncidents(string state, string code)
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db);
        var value = (await f.Service.ReportVehicleAsync(f.Report(), f.Driver.AccountId)).Value; var incident = await db.IncidentReports.SingleAsync();
        if (state == "HORSE_HEALTH") incident.Category = IncidentCategory.HORSE_HEALTH;
        else if (state == "missingDetail") db.VehicleIncidentDetails.Remove(await db.VehicleIncidentDetails.SingleAsync());
        else incident.Status = Enum.Parse<IncidentStatus>(state);
        await db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<UnprocessableEntityException>(() => f.Service.UpdateVehicleAsync(value.IncidentReportId,
            new() { LastUpdatedAt = value.UpdatedAt, RepairNote = "Repair progress" }, f.Manager.AccountId));
        Assert.Equal(code, error.ErrorCode);
    }

    [Theory]
    [InlineData("owner")][InlineData("pdf")][InlineData("large")][InlineData("empty")][InlineData("bytes")][InlineData("metadata")]
    public async Task Photos_RejectUnownedInvalidOrMismatchedUploads_KeepPresignedEvidence(string scenario)
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db); var request = f.Report(); request.PhotoFiles = [Photo()];
        switch (scenario)
        {
            case "owner": f.Storage.OwnerAccountId = f.Manager.AccountId; break;
            case "pdf": f.Storage.ConfirmedMimeType = "application/pdf"; break;
            case "large": f.Storage.ConfirmedSize = 5 * 1024 * 1024 + 1; break;
            case "empty": f.Storage.ConfirmedSize = 0; break;
            case "bytes": f.Storage.Preview = new("not an image"u8.ToArray(), "image/png"); break;
            case "metadata": f.Storage.ConfirmedSize = Png.Length + 1; break;
        }
        if (scenario == "owner") await Assert.ThrowsAsync<ForbiddenException>(() => f.Service.ReportVehicleAsync(request, f.Driver.AccountId));
        else Assert.Equal("INVALID_IMAGE", (await Assert.ThrowsAsync<BadRequestException>(() => f.Service.ReportVehicleAsync(request, f.Driver.AccountId))).ErrorCode);
        Assert.Empty(db.IncidentReports); Assert.Empty(db.IncidentPhotos); Assert.Empty(db.Notifications); Assert.Empty(f.Storage.Deletes);
    }

    [Theory]
    [InlineData(TransportStatus.READY)][InlineData(TransportStatus.IN_TRANSIT)][InlineData(TransportStatus.COMPLETED)]
    public async Task Contacts_OnlyActiveManagersAssignedCoordinatorAndCurrentCrew_NoNextCrewDuringHandover(TransportStatus status)
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db); f.Transport.Status = status;
        var nextDriver = await EmployeeAsync(db, EmployeeRole.DRIVER); var nextEscort = await EmployeeAsync(db, EmployeeRole.ESCORT);
        var next = PersonnelAssignmentTests.Segment(f.Segment.RoutePlan, 2); next.DriverEmployeeId = nextDriver.EmployeeId; next.EscortEmployeeId = nextEscort.EmployeeId;
        if (status == TransportStatus.READY) f.Segment.ActualDepartureAt = null;
        if (status == TransportStatus.COMPLETED) { f.Segment.ActualArrivalAt = Scenario.Now; next.ActualDepartureAt = Scenario.Now; next.ActualArrivalAt = Scenario.Now; }
        db.Add(next); var inactive = await EmployeeAsync(db, EmployeeRole.LOGISTICS_MANAGER); inactive.Account.Status = AccountStatus.INACTIVE;
        await db.SaveChangesAsync();
        var result = await f.Service.GetEmergencyContactsAsync(f.Transport.TransportId, f.Coordinator.AccountId);
        Assert.Equal(4, result.Contacts.Count); Assert.Equal(DbTime.AsUtc(Scenario.Now), result.ServerTime);
        Assert.Equal(new[] { "LOGISTICS_MANAGER", "FLEET_ROUTE_COORDINATOR", "DRIVER", "ESCORT" }, result.Contacts.Select(item => item.Role));
        var ids = result.Contacts.Select(item => item.EmployeeId).ToArray();
        Assert.Contains(status == TransportStatus.COMPLETED ? nextDriver.EmployeeId : f.Driver.EmployeeId, ids);
        Assert.Contains(status == TransportStatus.COMPLETED ? nextEscort.EmployeeId : f.Escort.EmployeeId, ids);
        Assert.DoesNotContain(status == TransportStatus.COMPLETED ? f.Driver.EmployeeId : nextDriver.EmployeeId, ids);
        Assert.DoesNotContain(f.Specialist.EmployeeId, ids); Assert.DoesNotContain(inactive.EmployeeId, ids);
        await Assert.ThrowsAsync<ForbiddenException>(() => f.Service.GetEmergencyContactsAsync(f.Transport.TransportId, f.Specialist.AccountId));
        await Assert.ThrowsAsync<ForbiddenException>(() => f.Service.GetEmergencyContactsAsync(f.Transport.TransportId, f.CustomerAccountId));
        var outsider = await EmployeeAsync(db, EmployeeRole.DRIVER);
        await Assert.ThrowsAsync<ForbiddenException>(() => f.Service.GetEmergencyContactsAsync(f.Transport.TransportId, outsider.AccountId));
        f.Transport.Status = TransportStatus.CANCELLED; await db.SaveChangesAsync();
        Assert.Equal("INVALID_TRANSPORT_STATUS", (await Assert.ThrowsAsync<ConflictException>(() => f.Service.GetEmergencyContactsAsync(f.Transport.TransportId, f.Manager.AccountId))).ErrorCode);
    }

    [Fact]
    public async Task Update_ReturnsExistingMedicalCostAndRouteData_ExcludesCancelledCostsAndScopesResolution()
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db); var request = f.Report(); request.Category = "BOTH";
        var created = (await f.Service.ReportVehicleAsync(request, f.Driver.AccountId)).Value;
        Assert.False(created.CanResolve); var incident = await db.IncidentReports.SingleAsync();
        var horse = await db.Horses.FirstAsync();
        db.Add(new HorseHealthIncidentDetail { HorseHealthIncidentDetailId = Guid.NewGuid(), IncidentReport = incident, Horse = horse,
            IncidentType = HorseIncidentType.STRESS, ConditionDescription = "Original clinical record", BodyTemperatureCelsius = 38.2m,
            IsolationRequired = true, CreatedAt = Scenario.Now, UpdatedAt = Scenario.Now });
        foreach (var (status, amount) in new[] { (EmergencyCostStatus.CANCELLED, 100), (EmergencyCostStatus.PENDING, 200), (EmergencyCostStatus.APPROVED, 300) })
        {
            var cost = new EmergencyCostRequest { EmergencyCostRequestId = Guid.NewGuid(), IncidentReport = incident, RequestCode = "ECR-" + Guid.NewGuid().ToString("N"),
                RequestedByEmployeeId = f.Driver.EmployeeId, Category = EmergencyCostCategory.VEHICLE_REPAIR, RequestedAmount = amount,
                ApprovedAmount = status == EmergencyCostStatus.APPROVED ? 250 : null, Status = status, Currency = "VND", Description = "Repair the engine",
                Justification = "The engine cannot start", RequestedAt = Scenario.Now, CreatedAt = Scenario.Now, UpdatedAt = Scenario.Now, InvoiceRequired = true };
            if (status == EmergencyCostStatus.APPROVED) cost.CostApprovalDecision = new() { CostApprovalDecisionId = Guid.NewGuid(),
                EmployeeId = f.Manager.EmployeeId, Decision = ApprovalDecision.APPROVED, ApprovedAmount = 250, DecidedAt = Scenario.Now, CreatedAt = Scenario.Now };
            db.Add(cost);
        }
        var backup = new RoutePlan { RoutePlanId = Guid.NewGuid(), TransportPlanId = f.Segment.RoutePlan.TransportPlanId, PrimaryRoutePlanId = f.Segment.RoutePlanId,
            RoutePlanCode = "RP-BACKUP", Status = RoutePlanStatus.DRAFT, CreatedAt = Scenario.Now, UpdatedAt = Scenario.Now };
        var backupSegment = PersonnelAssignmentTests.Segment(backup, 1); backupSegment.RouteType = RouteType.BACKUP; db.Add(backupSegment);
        await db.SaveChangesAsync();
        var updated = await f.Service.UpdateVehicleAsync(created.IncidentReportId, new() { LastUpdatedAt = created.UpdatedAt, RepairNote = "Repair in progress" }, f.Manager.AccountId);
        Assert.False(updated.CanResolve); Assert.Equal(3, updated.CostRequestCount); Assert.Equal(500, updated.CostSummary.RequestedTotal);
        Assert.Equal(250, updated.CostSummary.ApprovedTotal); Assert.Equal(1, updated.PendingCostCount);
        Assert.All(updated.CostRequests, cost => { Assert.Equal(f.Driver.EmployeeId, cost.RequestedBy.EmployeeId); Assert.True(cost.InvoiceRequired); });
        Assert.Equal(f.Manager.EmployeeId, updated.CostRequests.Single(cost => cost.Status == "APPROVED").Decision!.DecidedBy.EmployeeId);
        Assert.Equal("Original clinical record", Assert.Single(updated.HorseDetails).ConditionDescription);
        Assert.Equal(38.2m, updated.HorseDetails[0].BodyTemperatureCelsius); Assert.True(updated.HorseDetails[0].IsolationRequired);
        Assert.Equal("BACKUP", Assert.Single(updated.RouteChanges).RouteType);
        var pending = await db.EmergencyCostRequests.SingleAsync(cost => cost.Status == EmergencyCostStatus.PENDING); pending.Status = EmergencyCostStatus.CANCELLED;
        await db.SaveChangesAsync();
        var finished = await f.Service.UpdateVehicleAsync(created.IncidentReportId, new() { LastUpdatedAt = updated.UpdatedAt, RepairRequired = false }, f.Manager.AccountId);
        Assert.True(finished.CanResolve); Assert.Equal(300, finished.CostSummary.RequestedTotal); Assert.Equal(0, finished.PendingCostCount);
    }

    internal sealed class Scenario(AppDbContext db)
    {
        internal static DateTime Now => Fixture.Now;
        internal required Employee Manager { get; init; }
        internal required Employee Coordinator { get; init; }
        internal required Employee Specialist { get; init; }
        internal required Employee Driver { get; init; }
        internal required Employee Escort { get; init; }
        internal required Transport Transport { get; init; }
        internal required RoutePlanRoute Segment { get; init; }
        internal required Vehicle Vehicle { get; init; }
        internal required Location Location { get; init; }
        internal required Guid CustomerAccountId { get; init; }
        internal Storage Storage { get; } = new();
        internal Incidents.Service Service => CreateService(db, Storage);
        internal Incidents.Request.ReportVehicleRequest Report() => new()
        {
            TransportId = Transport.TransportId, VehicleId = Vehicle.VehicleId, IncidentType = "ENGINE_FAILURE", Severity = "HIGH",
            VehicleOperable = true, Title = "Engine failure on motorway", Description = "Engine overheated and stopped completely.",
            LocationId = Location.LocationId, OccurredAt = DbTime.AsUtc(Now.AddMinutes(-1))
        };
        internal static async Task<Scenario> CreateAsync(AppDbContext db)
        {
            var journey = await Fixture.CreateAsync(db); var driver = await EmployeeAsync(db, EmployeeRole.DRIVER);
            var specialist = await EmployeeAsync(db, EmployeeRole.TRANSPORT_SPECIALIST);
            journey.Transport.Status = TransportStatus.IN_TRANSIT; journey.RoutePlan.Status = RoutePlanStatus.ACTIVE;
            journey.Segment.DriverEmployeeId = driver.EmployeeId; journey.Segment.ActualDepartureAt = Now.AddHours(-1);
            db.Add(new EmployeeTransportPlan { Employee = specialist, TransportPlan = journey.RoutePlan.TransportPlan, AssignedAt = Now });
            var vehicle = new Vehicle { VehicleId = Guid.NewGuid(), VehicleCode = "VH-" + Guid.NewGuid().ToString("N"),
                VehicleName = "Horse truck", LicensePlate = "AB-123-CD", TransportMode = TransportMode.ROAD, Status = VehicleStatus.IN_USE, CreatedAt = Now, UpdatedAt = Now };
            db.Add(new RoutePlanRouteVehicle { RoutePlanRoute = journey.Segment, Vehicle = vehicle, AssignedAt = Now });
            await db.SaveChangesAsync();
            return new(db) { Manager = journey.Manager, Coordinator = journey.Coordinator, Specialist = specialist, Driver = driver, Escort = journey.Escort,
                Transport = journey.Transport, Segment = journey.Segment, Vehicle = vehicle, Location = journey.Segment.Route.FromLocation,
                CustomerAccountId = journey.Transport.Booking.Customer.AccountId };
        }
    }

    internal static Incidents.Service CreateService(AppDbContext db, Storage storage)
    {
        var fields = new VehicleFieldsValidator(); var clock = new FixedClock();
        return new(db, clock, new ReportVehicleRequestValidator(clock, fields), new UpdateVehicleRequestValidator(fields), storage, storage, TestSupport.CreateNotifications(db));
    }
}
