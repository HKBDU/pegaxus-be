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

public class HorseIncidentTests
{
    [Theory]
    [InlineData("LOW", "AT_RISK", false)]
    [InlineData("MEDIUM", "AT_RISK", false)]
    [InlineData("HIGH", "AT_RISK", true)]
    [InlineData("CRITICAL", "CRITICAL", true)]
    public async Task Report_CreatesMedicalRecordsLogsPhotosAndUrgentAlertsAtomically(string severity, string health, bool customerNotified)
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db); var request = f.Report();
        request.Severity = severity; request.Horses.Add(f.HorseRequest(f.SecondHorseId)); request.PhotoFiles = [Photo()];
        request.Horses[0].IsolationRequired = true; request.Horses[0].BodyTemperatureCelsius = 39.8m;
        request.Horses[0].HeartRateBpm = 68; request.Horses[0].RespiratoryRateBpm = 32;
        request.Horses[0].MedicationGiven = " Electrolytes "; request.Horses[0].VeterinarianInstruction = " Keep walking ";
        request.Horses[0].VeterinarianContactedAt = DbTime.AsUtc(Scenario.Now.AddMinutes(-1));
        request.EmergencySettlementRequired = true;
        var result = await f.Service.ReportHorseHealthAsync(request, f.Driver.AccountId); var value = result.Value;
        Assert.True(result.Created); Assert.Matches(@"^INC-\d{8}-\d{4}$", value.IncidentCode);
        Assert.Equal("HORSE_HEALTH", value.Category); Assert.Equal("REPORTED", value.Status); Assert.Equal(severity, value.Severity);
        Assert.Equal(f.Driver.EmployeeId, value.ReportedBy.EmployeeId); Assert.Equal(f.Transport.TransportId, value.Transport!.TransportId);
        Assert.Equal("Walk the horse", value.ImmediateAction); Assert.True(value.EmergencySettlementRequired);
        Assert.Contains("[SỰ CỐ Y TẾ]", value.Title); Assert.Equal(2, value.HorseDetails.Count);
        var detail = value.HorseDetails.Single(horse => horse.Horse.HorseId == f.Horse.HorseId);
        Assert.Equal(new[] { "BODY_TEMPERATURE_OUTSIDE_NORMAL", "HEART_RATE_OUTSIDE_NORMAL", "RESPIRATORY_RATE_OUTSIDE_NORMAL" }, detail.VitalWarnings);
        Assert.True(detail.IsolationRequired); Assert.Equal("Electrolytes", detail.MedicationGiven); Assert.Equal("Keep walking", detail.VeterinarianInstruction);
        Assert.Null(detail.HorseTransportable); Assert.Equal(f.Horse.MicrochipNumber, detail.Horse.MicrochipNumber);
        Assert.Empty(value.HorseDetails.Single(horse => horse.Horse.HorseId == f.SecondHorseId).VitalWarnings!);
        Assert.Equal(Storage.SignedUrl, Assert.Single(value.Photos).PhotoUrl); Assert.Equal(1, value.PhotoCount);
        Assert.Null(value.Photos[0].HorseHealthIncidentDetailId);
        Assert.Equal(2, value.Effects!.HealthLogsCreated); Assert.Equal("URGENT", value.Effects.NotifiedPriority); Assert.True(value.Effects.EmailQueued);
        Assert.Equal(new[] { "COLIC" }, value.Effects.FirstAidGuide); Assert.NotEmpty(value.Safety!.Message);
        Assert.Equal(4, value.Safety.EmergencyContacts.Count); Assert.Null(value.VehicleDetail);
        var logs = await db.HorseHealthLogs.ToListAsync(); Assert.Equal(2, logs.Count);
        Assert.All(logs, log =>
        {
            Assert.Equal(HorseHealthLogType.INCIDENT_FOLLOWUP, log.LogType); Assert.Equal(health, log.HealthStatus.ToString());
            Assert.Equal(f.Driver.EmployeeId, log.EmployeeId); Assert.Equal(DbTime.Truncate(request.OccurredAt!.Value), log.CheckedAt);
            Assert.Equal(DateTimeKind.Unspecified, log.CheckedAt.Kind);
        });
        var logPhoto = Assert.Single(logs[0].PhotoUrls); Assert.NotEqual(Storage.SignedUrl, logPhoto); Assert.Equal(logPhoto, Assert.Single(logs[1].PhotoUrls));
        var mail = OutboxQueue.Deserialize<EmailPayload>((await db.OutboxMessages.SingleAsync()).Payload);
        Assert.Equal(f.Manager.Account.Email, mail.To); Assert.Contains("Horse health", mail.Subject);
        var recipients = await db.AccountNotifications.Include(item => item.Notification).ToArrayAsync();
        foreach (var employee in new[] { f.Manager, f.Coordinator, f.Specialist })
        {
            var alerts = recipients.Where(item => item.AccountId == employee.AccountId).ToArray(); Assert.Single(alerts);
            Assert.Contains(alerts, item => item.Notification.Priority == NotificationPriority.URGENT && item.Notification.NotificationType == NotificationType.HORSE_HEALTH);
        }
        var customer = recipients.Where(item => item.AccountId == f.CustomerAccountId).ToArray(); Assert.Equal(customerNotified ? 1 : 0, customer.Length);
        if (customerNotified)
        {
            var notification = customer[0].Notification;
            Assert.Equal(NotificationPriority.HIGH, notification.Priority); Assert.Equal(NotificationType.INCIDENT, notification.NotificationType);
            Assert.Equal($"/bookings/{f.Transport.BookingId}/transports/{f.Transport.TransportId}/incidents/{value.IncidentReportId}", notification.ActionUrl);
            foreach (var secret in new[] { "COLIC", f.Horse.HorseName, f.Driver.FullName, "39.8", "Electrolytes" })
                Assert.DoesNotContain(secret, notification.Title + notification.Message);
        }
    }

    [Fact]
    public async Task Report_ReplayIgnoresMutableTextAndLaterAddedHorses_WithoutRepeatedSideEffects()
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db); var request = f.Report();
        request.PhotoFiles = [Photo()]; var created = await f.Service.ReportHorseHealthAsync(request, f.Escort.AccountId);
        var added = await f.Service.AddHorseDetailsAsync(created.Value.IncidentReportId,
            new() { Horses = [f.HorseRequest(f.SecondHorseId)], PhotoFiles = [Photo()] }, f.Escort.AccountId);
        var notifications = await db.Notifications.CountAsync(); var mail = await db.OutboxMessages.CountAsync();
        request.Title = "Offline edited title"; request.Horses[0].ConditionDescription = "This must never overwrite the record";
        request.Severity = "CRITICAL"; f.Transport.Status = TransportStatus.COMPLETED; await db.SaveChangesAsync();
        var replay = await f.Service.ReportHorseHealthAsync(request, f.Escort.AccountId);
        Assert.False(replay.Created); Assert.Equal(created.Value.IncidentReportId, replay.Value.IncidentReportId);
        Assert.Equal(created.Value.Title, replay.Value.Title); Assert.Equal("HIGH", replay.Value.Severity); Assert.Equal(2, replay.Value.HorseDetails.Count);
        Assert.Equal(new Incidents.Response.Effects(false, false, HealthLogsCreated: 0, FirstAidGuide: []), replay.Value.Effects);
        Assert.Equal(added.UpdatedAt, replay.Value.UpdatedAt); Assert.Equal(notifications, await db.Notifications.CountAsync());
        Assert.Equal(mail, await db.OutboxMessages.CountAsync()); Assert.Equal(2, await db.IncidentPhotos.CountAsync());
        Assert.Equal(2, f.Storage.Confirmations.Count); Assert.Equal(2, await db.HorseHealthLogs.CountAsync()); Assert.Single(db.IncidentReports);
    }

    [Fact]
    public async Task Add_AcceptsBothAfterCompletion_KeepsSeverityAndVehicle_RejectsDuplicatesAtomically()
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db);
        var incident = await f.SeedIncidentAsync(IncidentCategory.BOTH); f.Transport.Status = TransportStatus.COMPLETED;
        incident.Severity = IncidentSeverity.MEDIUM; await db.SaveChangesAsync();
        var request = new Incidents.Request.AddHorseDetailsRequest { Horses = [f.HorseRequest(f.Horse.HorseId)], PhotoFiles = [Photo()] };
        var added = await f.Service.AddHorseDetailsAsync(incident.IncidentReportId, request, f.Escort.AccountId);
        Assert.Equal("MEDIUM", added.Severity); Assert.Equal(1, added.Effects!.HealthLogsCreated); Assert.Null(added.Safety);
        Assert.Equal(IncidentStatus.REPORTED, incident.Status); Assert.True(added.UpdatedAt > DbTime.AsUtc(Scenario.Now));
        Assert.Equal(3, await db.Notifications.CountAsync()); Assert.Single(db.OutboxMessages);
        request.Horses.Add(f.HorseRequest(f.SecondHorseId));
        var duplicate = await Assert.ThrowsAsync<ConflictException>(() => f.Service.AddHorseDetailsAsync(incident.IncidentReportId, request, f.Escort.AccountId));
        Assert.Equal("HORSE_DETAIL_ALREADY_EXISTS", duplicate.ErrorCode); Assert.NotNull(duplicate.Value);
        Assert.Single(db.HorseHealthIncidentDetails); Assert.Single(db.HorseHealthLogs); Assert.Single(f.Storage.Confirmations);
        Assert.Empty(db.Vehicles); Assert.Empty(db.VehicleIncidentDetails);
    }

    [Fact]
    public async Task FollowUp_AppendsWithAuthorAndTime_PreservesMedicalLogs_TracksExplicitNull()
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db); var request = f.Report();
        request.Horses[0].MedicationGiven = "Original medication"; request.Horses[0].HorseTransportable = false;
        var incident = (await f.Service.ReportHorseHealthAsync(request, f.Driver.AccountId)).Value; var original = Assert.Single(incident.HorseDetails);
        f.Transport.Status = TransportStatus.COMPLETED; await db.SaveChangesAsync();
        var followUp = new Incidents.Request.FollowUpHorseRequest { LastUpdatedAt = incident.UpdatedAt.AddTicks(9), OutcomeNote = " Improved ",
            MedicationGiven = "Flunixin 500 mg", VeterinarianInstruction = "Rest", VeterinarianContactedAt = DbTime.AsUtc(Scenario.Now), HorseTransportable = true };
        var updated = await f.Service.FollowUpHorseAsync(incident.IncidentReportId, original.HorseHealthIncidentDetailId, followUp, f.Manager.AccountId);
        var detail = Assert.Single(updated.HorseDetails); Assert.True(detail.HorseTransportable);
        Assert.StartsWith("Original medication\n[2026-10-16T12:00:00Z · " + f.Manager.FullName + "] Flunixin 500 mg", detail.MedicationGiven);
        Assert.Contains("] Improved", detail.OutcomeNote); Assert.Contains("] Rest", detail.VeterinarianInstruction);
        Assert.Equal(original.ConditionDescription, detail.ConditionDescription); Assert.Equal(original.FirstAidProvided, detail.FirstAidProvided);
        Assert.Null(detail.VitalWarnings); Assert.True(updated.UpdatedAt > incident.UpdatedAt); Assert.Null(updated.Effects);
        Assert.Equal("Original medication", (await db.HorseHealthLogs.SingleAsync()).MedicationGiven);
        var reset = await f.Service.FollowUpHorseAsync(incident.IncidentReportId, detail.HorseHealthIncidentDetailId,
            new() { LastUpdatedAt = updated.UpdatedAt, HorseTransportable = null }, f.Coordinator.AccountId);
        Assert.Null(reset.HorseDetails[0].HorseTransportable); Assert.Equal(detail.MedicationGiven, reset.HorseDetails[0].MedicationGiven);
        var preserved = await f.Service.FollowUpHorseAsync(incident.IncidentReportId, detail.HorseHealthIncidentDetailId,
            new() { LastUpdatedAt = reset.UpdatedAt, OutcomeNote = "Second examination" }, f.Escort.AccountId);
        Assert.Null(preserved.HorseDetails[0].HorseTransportable); Assert.Contains("] Improved\n[", preserved.HorseDetails[0].OutcomeNote);
        Assert.Equal("INCIDENT_MODIFIED", (await Assert.ThrowsAsync<ConflictException>(() => f.Service.FollowUpHorseAsync(incident.IncidentReportId,
            detail.HorseHealthIncidentDetailId, followUp, f.Driver.AccountId))).ErrorCode);
        Assert.Equal("IMMUTABLE_MEDICAL_RECORD", (await Assert.ThrowsAsync<UnprocessableEntityException>(() => f.Service.FollowUpHorseAsync(incident.IncidentReportId,
            detail.HorseHealthIncidentDetailId, new() { LastUpdatedAt = preserved.UpdatedAt, VeterinarianContactedAt = DbTime.AsUtc(Scenario.Now) }, f.Manager.AccountId))).ErrorCode);
        Assert.Single(db.HorseHealthLogs); Assert.Single(db.OutboxMessages); Assert.Single((await db.AccountNotifications.ToListAsync()).Where(item => item.AccountId == f.CustomerAccountId));
    }

    [Theory]
    [InlineData("transport")][InlineData("severity")][InlineData("empty")][InlineData("nullHorses")][InlineData("nullHorse")]
    [InlineData("duplicate")][InlineData("horseId")][InlineData("type")][InlineData("description")][InlineData("temperaturePrecision")]
    [InlineData("location")][InlineData("pair")][InlineData("coordinates")][InlineData("zero")][InlineData("future")]
    [InlineData("title")][InlineData("notes")][InlineData("photoCount")][InlineData("nullPhotos")][InlineData("duplicatePhotos")]
    public async Task Report_RejectsInvalidInputBeforeStorageOrWrites(string scenario)
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db); var request = f.Report();
        switch (scenario)
        {
            case "transport": request.TransportId = Guid.Empty; break;
            case "severity": request.Severity = "1"; break;
            case "empty": request.Horses = []; break;
            case "nullHorses": request.Horses = null!; break;
            case "nullHorse": request.Horses = [null!]; break;
            case "duplicate": request.Horses.Add(request.Horses[0]); break;
            case "horseId": request.Horses[0].HorseId = Guid.Empty; break;
            case "type": request.Horses[0].IncidentType = "1"; break;
            case "description": request.Horses[0].ConditionDescription = " short "; break;
            case "temperaturePrecision": request.Horses[0].BodyTemperatureCelsius = 38.25m; break;
            case "location": request.LocationId = null; break;
            case "pair": request.Latitude = 50; break;
            case "coordinates": request.Latitude = 50.12345m; request.Longitude = 3; break;
            case "zero": request.Latitude = request.Longitude = 0; break;
            case "future": request.OccurredAt = DbTime.AsUtc(Scenario.Now.AddMinutes(6)); break;
            case "title": request.Title = new('x', 201); break;
            case "notes": request.Horses[0].Symptoms = new('x', 2001); break;
            case "photoCount": request.PhotoFiles = Enumerable.Range(0, 6).Select(_ => Photo()).ToList(); break;
            case "nullPhotos": request.PhotoFiles = null!; break;
            case "duplicatePhotos": var photo = Photo(); request.PhotoFiles = [photo, photo]; break;
        }
        Assert.Equal(scenario == "photoCount" ? "TOO_MANY_IMAGES" : "VALIDATION_ERROR",
            (await Assert.ThrowsAnyAsync<BadRequestException>(() => f.Service.ReportHorseHealthAsync(request, f.Escort.AccountId))).ErrorCode);
        Assert.Empty(db.IncidentReports); Assert.Empty(db.HorseHealthLogs); Assert.Empty(db.Notifications); Assert.Empty(f.Storage.Confirmations);
    }

    [Theory]
    [InlineData(34.9, 38, 12)][InlineData(43.1, 38, 12)][InlineData(38.2, 9, 12)]
    [InlineData(38.2, 201, 12)][InlineData(38.2, 38, 1)][InlineData(38.2, 38, 101)]
    public async Task Vitals_RejectBiologicallyImpossibleValuesOnReportAndAddition(decimal temperature, int heartRate, int respiration)
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db); var request = f.Report();
        var horse = request.Horses[0]; horse.BodyTemperatureCelsius = temperature; horse.HeartRateBpm = heartRate; horse.RespiratoryRateBpm = respiration;
        Assert.Equal("VITAL_SIGNS_OUT_OF_RANGE", (await Assert.ThrowsAsync<UnprocessableEntityException>(() => f.Service.ReportHorseHealthAsync(request, f.Escort.AccountId))).ErrorCode);
        var incident = await f.SeedIncidentAsync();
        Assert.Equal("VITAL_SIGNS_OUT_OF_RANGE", (await Assert.ThrowsAsync<UnprocessableEntityException>(() => f.Service.AddHorseDetailsAsync(incident.IncidentReportId,
            new() { Horses = request.Horses }, f.Escort.AccountId))).ErrorCode);
        Assert.Empty(db.HorseHealthIncidentDetails); Assert.Empty(db.HorseHealthLogs);
    }

    [Fact]
    public async Task Report_EnforcesDatabaseRoleLiveAssignmentTransportStatusAndHorseMembership()
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db); var request = f.Report();
        foreach (var actor in new[] { f.Manager, f.Coordinator, f.Specialist, await EmployeeAsync(db, EmployeeRole.DRIVER) })
            await Assert.ThrowsAsync<ForbiddenException>(() => f.Service.ReportHorseHealthAsync(request, actor.AccountId));
        await Assert.ThrowsAsync<ForbiddenException>(() => f.Service.ReportHorseHealthAsync(request, f.CustomerAccountId));
        request.TransportId = Guid.NewGuid(); Assert.Equal("TRANSPORT_NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(() => f.Service.ReportHorseHealthAsync(request, f.Driver.AccountId))).ErrorCode);
        request.TransportId = f.Transport.TransportId; request.Horses[0].HorseId = Guid.NewGuid();
        Assert.Equal("HORSE_NOT_IN_TRANSPORT", (await Assert.ThrowsAsync<UnprocessableEntityException>(() => f.Service.ReportHorseHealthAsync(request, f.Driver.AccountId))).ErrorCode);
        request.Horses[0].HorseId = f.Horse.HorseId; f.Transport.Status = TransportStatus.READY; await db.SaveChangesAsync();
        Assert.Equal("INVALID_TRANSPORT_STATUS", (await Assert.ThrowsAsync<ConflictException>(() => f.Service.ReportHorseHealthAsync(request, f.Driver.AccountId))).ErrorCode);
        f.Transport.Status = TransportStatus.IN_TRANSIT; f.Segment.SupersededAt = Scenario.Now; f.Segment.ActualDepartureAt = null; await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ForbiddenException>(() => f.Service.ReportHorseHealthAsync(request, f.Driver.AccountId));
        Assert.Empty(db.IncidentReports); Assert.Empty(db.HorseHealthLogs);
    }

    [Theory]
    [InlineData("manager", true)][InlineData("coordinator", true)][InlineData("escort", true)][InlineData("driver", true)]
    [InlineData("otherDriver", false)][InlineData("otherEscort", false)][InlineData("otherCoordinator", false)][InlineData("specialist", false)]
    public async Task FollowUp_RequiresAssignedEscortOrReportingDriverOrAssignedCoordinatorOrManager(string role, bool allowed)
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db);
        var incident = (await f.Service.ReportHorseHealthAsync(f.Report(), f.Driver.AccountId)).Value;
        var actor = role switch { "manager" => f.Manager, "coordinator" => f.Coordinator, "escort" => f.Escort, "driver" => f.Driver,
            "otherDriver" => await EmployeeAsync(db, EmployeeRole.DRIVER), "otherEscort" => await EmployeeAsync(db, EmployeeRole.ESCORT),
            "otherCoordinator" => await EmployeeAsync(db, EmployeeRole.FLEET_ROUTE_COORDINATOR), _ => f.Specialist };
        if (role == "otherDriver") f.Segment.DriverEmployeeId = actor.EmployeeId;
        if (role == "otherCoordinator")
        {
            var old = new TransportPlan { TransportPlanId = Guid.NewGuid(), TransportId = f.Transport.TransportId, PlanCode = "TP-OLD",
                Status = TransportPlanStatus.CANCELLED, CreatedAt = Scenario.Now.AddDays(1), UpdatedAt = Scenario.Now };
            db.Add(new EmployeeTransportPlan { Employee = actor, TransportPlan = old });
        }
        await db.SaveChangesAsync();
        var update = new Incidents.Request.FollowUpHorseRequest { LastUpdatedAt = incident.UpdatedAt, OutcomeNote = "Examination complete" };
        if (allowed) Assert.Contains("] Examination complete", (await f.Service.FollowUpHorseAsync(incident.IncidentReportId, incident.HorseDetails[0].HorseHealthIncidentDetailId, update, actor.AccountId)).HorseDetails[0].OutcomeNote);
        else await Assert.ThrowsAsync<ForbiddenException>(() => f.Service.FollowUpHorseAsync(incident.IncidentReportId, incident.HorseDetails[0].HorseHealthIncidentDetailId, update, actor.AccountId));
    }

    [Theory]
    [InlineData(IncidentCategory.HORSE_HEALTH, IncidentStatus.RESOLVED, "INVALID_INCIDENT_STATUS")]
    [InlineData(IncidentCategory.BOTH, IncidentStatus.CANCELLED, "INVALID_INCIDENT_STATUS")]
    [InlineData(IncidentCategory.VEHICLE, IncidentStatus.REPORTED, "INCIDENT_CATEGORY_MISMATCH")]
    public async Task AddAndFollowUp_RejectClosedOrIncompatibleIncidents(IncidentCategory category, IncidentStatus status, string code)
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db); var incident = await f.SeedIncidentAsync(category);
        incident.Status = status; await db.SaveChangesAsync();
        Assert.Equal(code, (await Assert.ThrowsAsync<UnprocessableEntityException>(() => f.Service.AddHorseDetailsAsync(incident.IncidentReportId,
            new() { Horses = [f.HorseRequest(f.Horse.HorseId)] }, f.Escort.AccountId))).ErrorCode);
        Assert.Equal(code, (await Assert.ThrowsAsync<UnprocessableEntityException>(() => f.Service.FollowUpHorseAsync(incident.IncidentReportId,
            Guid.NewGuid(), new() { LastUpdatedAt = DbTime.AsUtc(incident.UpdatedAt), OutcomeNote = "Examination complete" }, f.Manager.AccountId))).ErrorCode);
    }

    [Fact]
    public async Task Report_ReplayUsesUnorderedHorseAndTypePairs()
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db); var request = f.Report();
        request.Horses.Add(f.HorseRequest(f.SecondHorseId)); var original = await f.Service.ReportHorseHealthAsync(request, f.Driver.AccountId);
        request.Horses.Reverse(); var replay = await f.Service.ReportHorseHealthAsync(request, f.Driver.AccountId);
        Assert.False(replay.Created); Assert.Equal(original.Value.IncidentReportId, replay.Value.IncidentReportId);
        request.Horses[0].IncidentType = "STRESS"; var different = await f.Service.ReportHorseHealthAsync(request, f.Driver.AccountId);
        Assert.True(different.Created); Assert.NotEqual(original.Value.IncidentReportId, different.Value.IncidentReportId);
    }

    [Theory]
    [InlineData("owner", 403, "FORBIDDEN")][InlineData("type", 400, "INVALID_FILE_TYPE")]
    [InlineData("large", 413, "FILE_TOO_LARGE")][InlineData("empty", 400, "INVALID_IMAGE")]
    [InlineData("bytes", 400, "INVALID_IMAGE")][InlineData("metadata", 400, "INVALID_IMAGE")]
    public async Task Photos_RejectUnownedInvalidOrMismatchedUploads_KeepPresignedEvidence(string scenario, int status, string code)
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db); var request = f.Report(); request.PhotoFiles = [Photo()];
        switch (scenario)
        {
            case "owner": f.Storage.OwnerAccountId = f.Manager.AccountId; break;
            case "type": f.Storage.ConfirmedMimeType = "application/pdf"; break;
            case "large": f.Storage.ConfirmedSize = 5 * 1024 * 1024 + 1; break;
            case "empty": f.Storage.ConfirmedSize = 0; break;
            case "bytes": f.Storage.Preview = new("not an image"u8.ToArray(), "image/png"); break;
            case "metadata": f.Storage.ConfirmedSize = Png.Length + 1; break;
        }
        var error = await Assert.ThrowsAnyAsync<Pegaxus.Service.Exceptions.Exceptions>(() => f.Service.ReportHorseHealthAsync(request, f.Driver.AccountId));
        Assert.Equal(status, error.StatusCode); Assert.Equal(code, error.ErrorCode);
        Assert.Empty(db.IncidentReports); Assert.Empty(db.HorseHealthIncidentDetails); Assert.Empty(db.HorseHealthLogs);
        Assert.Empty(db.Notifications); Assert.Empty(f.Storage.Deletes);
    }

    [Fact]
    public async Task FollowUp_RejectsEmptyInputAndDetailsFromAnotherIncident()
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db);
        var first = (await f.Service.ReportHorseHealthAsync(f.Report(), f.Driver.AccountId)).Value;
        var otherRequest = f.Report(); otherRequest.OccurredAt = otherRequest.OccurredAt!.Value.AddSeconds(1);
        var other = (await f.Service.ReportHorseHealthAsync(otherRequest, f.Driver.AccountId)).Value;
        await Assert.ThrowsAsync<FieldValidationException>(() => f.Service.FollowUpHorseAsync(first.IncidentReportId, first.HorseDetails[0].HorseHealthIncidentDetailId,
            new() { LastUpdatedAt = first.UpdatedAt, OutcomeNote = "  " }, f.Manager.AccountId));
        Assert.Equal("HORSE_DETAIL_NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(() => f.Service.FollowUpHorseAsync(first.IncidentReportId,
            other.HorseDetails[0].HorseHealthIncidentDetailId, new() { LastUpdatedAt = first.UpdatedAt, OutcomeNote = "Cross-incident edit" }, f.Manager.AccountId))).ErrorCode);
        Assert.All(await db.HorseHealthIncidentDetails.ToListAsync(), detail => Assert.Null(detail.OutcomeNote));
    }

    [Fact]
    public async Task Location_ReusesNearbyOrCreatesOther_RejectsMissingAndDeleted()
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db);
        f.Location.Latitude = 50.6292m; f.Location.Longitude = 3.0573m; await db.SaveChangesAsync();
        var request = f.Report(); request.LocationId = null; request.Latitude = 50.6293m; request.Longitude = 3.0573m;
        var near = await f.Service.ReportHorseHealthAsync(request, f.Escort.AccountId); Assert.Equal(f.Location.LocationId, near.Value.Location!.LocationId);
        request.OccurredAt = request.OccurredAt!.Value.AddSeconds(1); request.Latitude = 10; request.Longitude = 20;
        request.LocationDescription = "Safe stop"; request.Country = "France";
        var far = await f.Service.ReportHorseHealthAsync(request, f.Escort.AccountId); Assert.NotEqual(f.Location.LocationId, far.Value.Location!.LocationId);
        var location = await db.Locations.SingleAsync(item => item.LocationId == far.Value.Location.LocationId);
        Assert.Equal(LocationType.OTHER, location.LocationType); Assert.Equal("Safe stop", location.Address); Assert.Equal("France", location.Country);
        request.OccurredAt = request.OccurredAt.Value.AddSeconds(1); request.LocationId = Guid.NewGuid();
        Assert.Equal("LOCATION_NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(() => f.Service.ReportHorseHealthAsync(request, f.Escort.AccountId))).ErrorCode);
        request.LocationId = f.Location.LocationId; f.Location.IsDeleted = true; await db.SaveChangesAsync();
        Assert.Equal("LOCATION_NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(() => f.Service.ReportHorseHealthAsync(request, f.Escort.AccountId))).ErrorCode);
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
        internal required Horse Horse { get; init; }
        internal required Guid SecondHorseId { get; init; }
        internal required Location Location { get; init; }
        internal required Guid CustomerAccountId { get; init; }
        internal Storage Storage { get; } = new();
        internal Incidents.Service Service => CreateIncidentService(db, Storage);
        internal Incidents.Request.HorseDetail HorseRequest(Guid id) => new() { HorseId = id, IncidentType = "COLIC",
            ConditionDescription = "Sweating and repeatedly looking at the belly", FirstAidProvided = " Walk the horse ", VeterinarianRequired = true };
        internal Incidents.Request.ReportHorseHealthRequest Report() => new() { TransportId = Transport.TransportId, Severity = "HIGH",
            LocationId = Location.LocationId, OccurredAt = DbTime.AsUtc(Now.AddMinutes(-2)), Horses = [HorseRequest(Horse.HorseId)] };
        internal async Task<IncidentReport> SeedIncidentAsync(IncidentCategory category = IncidentCategory.HORSE_HEALTH)
        {
            var incident = new IncidentReport { IncidentReportId = Guid.NewGuid(), TransportId = Transport.TransportId, EmployeeId = Driver.EmployeeId,
                LocationId = Location.LocationId, IncidentCode = "INC-" + Guid.NewGuid().ToString("N"), Category = category, Severity = IncidentSeverity.CRITICAL,
                Title = "Combined incident", Description = "Vehicle and horse affected", Status = IncidentStatus.REPORTED,
                OccurredAt = Now, ReportedAt = Now, CreatedAt = Now, UpdatedAt = Now };
            db.Add(incident); await db.SaveChangesAsync(); return incident;
        }
        internal static async Task<Scenario> CreateAsync(AppDbContext db)
        {
            var journey = await Fixture.CreateAsync(db); var driver = await EmployeeAsync(db, EmployeeRole.DRIVER);
            var specialist = await EmployeeAsync(db, EmployeeRole.TRANSPORT_SPECIALIST);
            journey.Transport.Status = TransportStatus.IN_TRANSIT; journey.RoutePlan.Status = RoutePlanStatus.ACTIVE;
            journey.Segment.DriverEmployeeId = driver.EmployeeId; journey.Segment.ActualDepartureAt = Now.AddHours(-1);
            db.Add(new EmployeeTransportPlan { Employee = specialist, TransportPlan = journey.RoutePlan.TransportPlan });
            await db.SaveChangesAsync();
            var otherHorse = await db.BookingHorses.Where(item => item.BookingId == journey.Transport.BookingId && item.HorseId != journey.Horse.HorseId).Select(item => item.HorseId).SingleAsync();
            return new(db) { Manager = journey.Manager, Coordinator = journey.Coordinator, Specialist = specialist, Driver = driver, Escort = journey.Escort,
                Transport = journey.Transport, Segment = journey.Segment, Horse = journey.Horse, SecondHorseId = otherHorse, Location = journey.Segment.Route.FromLocation,
                CustomerAccountId = journey.Transport.Booking.Customer.AccountId };
        }
    }

    internal static Incidents.Service CreateIncidentService(AppDbContext db, Storage storage)
    {
        var clock = new FixedClock(); var horse = new HorseDetailValidator(clock); var details = new AddHorseDetailsRequestValidator(horse);
        var fields = new VehicleFieldsValidator();
        return new(db, clock, new ReportVehicleRequestValidator(clock, fields), new UpdateVehicleRequestValidator(fields),
            new ReportHorseHealthRequestValidator(clock, details), details, new FollowUpHorseRequestValidator(clock),
            new ListRequestValidator(), new ResolveRequestValidator(new SelfResolveRequestValidator()), new CancelRequestValidator(),
            new SelfResolveRequestValidator(), new RaiseSeverityRequestValidator(),
            new RerouteEscalationRequestValidator(), new RouteHoldRequestValidator(),
            storage, storage, TestSupport.CreateNotifications(db));
    }
}
