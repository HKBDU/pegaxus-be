using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.NotificationService;
using Pegaxus.Service.RouteChangeService.Validator;

using RouteChange = Pegaxus.Service.RouteChangeService;
using Request = Pegaxus.Service.RouteChangeService.Request;

namespace Pegaxus.Service.Test;

public class RouteChangeTests
{
    [Fact]
    public async Task Backup_SummaryReportsTheBranchPoint_AndNoReplacementRange()
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        var backup = await f.AlternateAsync();
        db.ChangeTracker.Clear();
        var result = await TestSupport.CreateRoutePlans(db).GetListAsync(f.Manager.AccountId, f.Plan.TransportPlanId, new());
        var summary = Assert.Single(result.Items, item => item.RoutePlanId == backup.RoutePlanId);
        Assert.Equal("BACKUP", summary.RouteType);
        Assert.Equal(2, summary.BranchSequenceNo);
        Assert.Equal(f.Start.LocationId, summary.BranchLocation!.LocationId);
        Assert.Null(Assert.Single(result.Items, item => item.RoutePlanId == f.Plan.RoutePlanId).BranchSequenceNo);
    }

    [Fact]
    public async Task Return_ApprovedDraftCannotBeCancelled_LeavesBothDossiersIntact()
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        var returning = await f.AlternateAsync(RouteType.RETURN);
        var decision = await f.Service.DecideAsync(returning.RoutePlanId, f.Manager.AccountId,
            new() { Decision = "APPROVE", LastUpdatedAt = Fixture.Version(returning) });
        var error = await Assert.ThrowsAsync<ConflictException>(() => TestSupport.CreateRoutePlans(db).CancelAsync(
            f.Coordinator.AccountId, returning.RoutePlanId, new() { LastUpdatedAt = decision.UpdatedAt, Reason = "Cancel return itinerary" }));
        Assert.Equal("RETURN_ALREADY_APPROVED", error.ErrorCode);
        Assert.Equal(RoutePlanStatus.DRAFT, returning.Status);
        Assert.Equal(ComplianceDossierStatus.APPROVED, f.Transport.MainDossier!.Status);
        Assert.Equal(ComplianceDossierStatus.DRAFT, f.Transport.ExtraDossier!.Status);
    }

    [Fact]
    public async Task Return_UnapprovedDraftCanStillBeCancelled()
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        var returning = await f.AlternateAsync(RouteType.RETURN);
        var result = await TestSupport.CreateRoutePlans(db).CancelAsync(f.Coordinator.AccountId, returning.RoutePlanId,
            new() { LastUpdatedAt = Fixture.Version(returning), Reason = "Cancel return itinerary" });
        Assert.Equal("CANCELLED", result.Status);
        Assert.Equal(ComplianceDossierStatus.APPROVED, f.Transport.MainDossier!.Status);
    }

    [Fact]
    public async Task Return_AutoCreatedRoadRoutePersistsProviderDistance_AndPlanTotal()
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        f.Transport.Booking.OriginCountry = f.Transport.Booking.DestinationCountry = "FR";
        f.Start.Latitude = 48; f.Start.Longitude = 2;
        f.Origin.Latitude = 49; f.Origin.Longitude = 3;
        await db.SaveChangesAsync();
        var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Geoapify:ApiKey"] = "test-key" }).Build();
        var routes = new Pegaxus.Service.RoutePlanService.Service(db, TestSupport.CreateNotifications(db),
            new Pegaxus.Service.RouteService.Service(db, new RoutingClientFactory(), configuration));
        var result = await routes.CreateAsync(f.Coordinator.AccountId, f.Plan.TransportPlanId, new()
        {
            RouteType = "RETURN", Action = "SAVE_DRAFT", RouteNote = "Return to origin after road closure",
            Segments = [new() { SequenceNo = 1, FromLocationId = f.Start.LocationId, ToLocationId = f.Origin.LocationId,
                TransportMode = "ROAD", PlannedDepartureAt = Fixture.StartAt, PlannedArrivalAt = Fixture.StartAt.AddHours(2) }]
        });
        Assert.Equal(125.5m, result.TotalDistanceKm);
        Assert.Equal(125.5m, Assert.Single(result.Segments).Route.DistanceKm);
        var route = await db.Routes.SingleAsync(item => item.RouteId == result.Segments[0].Route.RouteId);
        Assert.Equal(125.5m, route.DistanceKm);
        Assert.Equal(91, route.EstimatedDurationMinutes);
    }

    private sealed class RoutingClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new RoutingHandler());
        private sealed class RoutingHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
                Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                { Content = new StringContent("{\"results\":[{\"distance\":125500,\"time\":5401}]}") });
        }
    }

    [Fact]
    public void RouteChangeRequests_AreServiceDtos_NotDatabaseEntities()
    {
        using var db = TestSupport.CreateDb();
        using var relationalDb = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=unused").Options);
        var snapshot = relationalDb.GetService<IMigrationsAssembly>().ModelSnapshot!.Model;
        var requests = new[] { typeof(Request.RouteChangeRequest), typeof(Request.CreateRequest), typeof(Request.DecisionRequest),
            typeof(Request.ActivationRequest), typeof(Request.DriverNoticeRequest) };
        foreach (var type in requests)
        {
            Assert.Equal(typeof(RouteChange.Service).Assembly, type.Assembly);
            Assert.Null(db.Model.FindEntityType(type));
        }
        foreach (var model in new[] { db.Model, snapshot })
        {
            Assert.DoesNotContain(model.GetEntityTypes(), entity => entity.GetTableName() == "RouteChangeRequest");
            Assert.Null(model.FindEntityType(typeof(RoutePlanRoute).FullName!)!.FindProperty("SupersededByChangeRequestId"));
        }
    }

    [Theory]
    [InlineData(15, 20, false)]
    [InlineData(49.99, 59, false)]
    [InlineData(50, 0, true)]
    [InlineData(0, 60, true)]
    public async Task BR036_UsesStrictExemptionBoundaries(decimal extraDistance, int delay, bool needsApproval)
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        f.Alternative.DistanceKm = 100 + extraDistance;
        var backup = await f.AlternateAsync();
        if (needsApproval)
        {
            var result = await f.Service.CreateAsync(backup.RoutePlanId, f.Coordinator.AccountId, f.Proposal(delay));
            Assert.Equal(backup.RoutePlanId, result.RoutePlanId); Assert.Equal("CONFIRMED", result.Status);
            var notification = Assert.Single(db.Notifications);
            Assert.Equal(NotificationPriority.URGENT, notification.Priority); Assert.Equal("RoutePlan", notification.ReferenceType);
            Assert.Equal(backup.RoutePlanId, notification.ReferenceId);
            Assert.Contains("1,800 VND", notification.Message);
            Assert.Contains(FormattableString.Invariant($"ETA: {f.Proposal(delay).NewEstimatedArrivalAt.UtcDateTime:yyyy-MM-dd HH:mm} UTC"), notification.Message);
        }
        else
            Assert.Equal("APPROVAL_NOT_REQUIRED", (await Assert.ThrowsAsync<UnprocessableEntityException>(() =>
                f.Service.CreateAsync(backup.RoutePlanId, f.Coordinator.AccountId, f.Proposal(delay)))).ErrorCode);
        Assert.Empty(db.EmergencyCostRequests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BR036_NewBcpOr24HourRestRequireApproval(bool rest)
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        f.Alternative.DistanceKm = 100;
        var backup = await f.AlternateAsync();
        var middle = new Location { LocationId = Guid.NewGuid(), LocationName = "Middle", Country = "FR",
            LocationType = rest ? LocationType.REST_STOP : LocationType.BORDER_GATE };
        var first = f.NewRoute(f.Start, middle, 50);
        var second = f.NewRoute(middle, f.End, 50);
        var row = await db.RoutePlanRoutes.SingleAsync(x => x.RoutePlanId == backup.RoutePlanId && x.RouteType == RouteType.BACKUP);
        row.Route = first; row.RouteId = first.RouteId; row.PlannedArrivalAt = Fixture.StartAt.AddHours(1).UtcDateTime;
        db.Add(new RoutePlanRoute { RoutePlanRouteId = Guid.NewGuid(), RoutePlanId = backup.RoutePlanId, Route = second,
            SequenceNo = 3, RouteType = RouteType.BACKUP, PlannedDepartureAt = Fixture.StartAt.AddHours(rest ? 25 : 1).UtcDateTime,
            PlannedArrivalAt = Fixture.StartAt.AddHours(rest ? 26 : 2).UtcDateTime });
        await db.SaveChangesAsync();
        Assert.Equal("CONFIRMED", (await f.Service.CreateAsync(backup.RoutePlanId, f.Coordinator.AccountId, f.Proposal(0))).Status);
    }

    [Fact]
    public async Task Backup_ApproveKeepsDepartedPrimarySegments_SupersedesTheRest_AndRejectsASecondBackup()
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        var finish = new Location { LocationId = Guid.NewGuid(), LocationName = "Finish", Country = "DE" };
        var suffixRoute = f.NewRoute(f.End, finish, 20);
        var suffix = new RoutePlanRoute { RoutePlanRouteId = Guid.NewGuid(), RoutePlan = f.Plan, Route = suffixRoute,
            SequenceNo = 3, PlannedDepartureAt = Fixture.StartAt.AddHours(3).UtcDateTime, PlannedArrivalAt = Fixture.StartAt.AddHours(4).UtcDateTime };
        db.Add(suffix);
        db.RoutePlanRouteVehicles.Add(new() { RoutePlanRouteId = suffix.RoutePlanRouteId,
            VehicleId = await db.Vehicles.Select(x => x.VehicleId).SingleAsync(), AssignedAt = DateTime.SpecifyKind(Fixture.StartAt.UtcDateTime, DateTimeKind.Unspecified) });
        await db.SaveChangesAsync();
        var backup = await f.AlternateAsync();
        var overlap = await f.AlternateAsync();
        // A second confirmed fallback uses a different vehicle; only active alternatives reserve the replacement range.
        db.RoutePlanRouteVehicles.RemoveRange(await db.RoutePlanRouteVehicles.Where(x => x.RoutePlanRoute.RoutePlanId == overlap.RoutePlanId).ToListAsync());
        await db.SaveChangesAsync();
        var result = await f.Service.DecideAsync(backup.RoutePlanId, f.Manager.AccountId,
            new() { Decision = "APPROVE", LastUpdatedAt = Fixture.Version(backup) });
        Assert.Equal("ACTIVE", result.Status); Assert.Null(result.ComplianceDossier);
        Assert.Equal((2, f.Start.LocationId), (result.BranchSequenceNo, result.BranchLocation!.LocationId));
        Assert.Equal(RoutePlanStatus.ACTIVE, f.Plan.Status);
        Assert.Null(f.Completed.SupersededAt); Assert.NotNull(suffix.SupersededAt); Assert.NotNull(f.Current.SupersededAt);
        var detail = await TestSupport.CreateRoutePlans(db).GetByIdAsync(f.Manager.AccountId, f.Plan.RoutePlanId);
        Assert.Equal(new[] { f.Completed.RouteId, f.Alternative.RouteId, suffix.RouteId }, detail.Segments.Select(x => x.Route.RouteId));
        Assert.Equal(f.Driver.EmployeeId, detail.Segments[1].Driver!.EmployeeId);
        Assert.Equal(205m, detail.TotalDistanceKm); Assert.Equal(420, detail.EstimatedDurationMinutes);
        Assert.Equal("SEGMENT_LOCKED", (await Assert.ThrowsAsync<ConflictException>(() =>
            f.Service.DecideAsync(overlap.RoutePlanId, f.Manager.AccountId, new() { Decision = "APPROVE", LastUpdatedAt = Fixture.Version(overlap) }))).ErrorCode);
        Assert.Equal(3, await db.Notifications.CountAsync());
        Assert.DoesNotContain(await db.AccountNotifications.ToListAsync(), x => x.AccountId ==
            db.Employees.Single(e => e.Role == EmployeeRole.TRANSPORT_SPECIALIST).AccountId);
        Assert.Empty(db.IncidentReports); Assert.Empty(db.EmergencyCostRequests);
    }

    [Theory]
    [InlineData(RouteType.BACKUP)]
    [InlineData(RouteType.RETURN)]
    public async Task Reject_KeepsBackupConfirmed_CancelsReturn_WithCoordinatorNotice(RouteType type)
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db); var plan = await f.AlternateAsync(type);
        var result = await f.Service.DecideAsync(plan.RoutePlanId, f.Manager.AccountId, new() {
            Decision = "REJECT", LastUpdatedAt = Fixture.Version(plan), Note = "Choose a safer itinerary" });
        Assert.Equal(type == RouteType.BACKUP ? "CONFIRMED" : "CANCELLED", result.Status);
        Assert.Equal(ComplianceDossierStatus.APPROVED, f.Transport.MainDossier!.Status);
        if (type == RouteType.RETURN) Assert.Equal("Choose a safer itinerary", plan.RouteNote);
        Assert.Equal(f.Coordinator.AccountId, Assert.Single(db.AccountNotifications).AccountId);
        Assert.Null(f.Current.SupersededAt);
    }

    [Fact]
    public async Task Return_Lifecycle_CreatesExtraDossier_RequiresTsReview_AndApprovedExtra_ActivatesOwnInUseVehicle_NotifiesDrivers()
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db); var plan = await f.AlternateAsync(RouteType.RETURN);
        var row = await db.RoutePlanRoutes.SingleAsync(x => x.RoutePlanId == plan.RoutePlanId);
        await Assert.ThrowsAsync<ConflictException>(() => f.Service.ActivateReturnAsync(plan.RoutePlanId, f.Manager.AccountId,
            new() { LastUpdatedAt = Fixture.Version(plan) }));
        var decision = await f.Service.DecideAsync(plan.RoutePlanId, f.Manager.AccountId,
            new() { Decision = "APPROVE", LastUpdatedAt = Fixture.Version(plan) });
        Assert.Equal("DRAFT", decision.Status);
        Assert.Equal(("EXTRA", "DRAFT"), (decision.ComplianceDossier!.DossierType, decision.ComplianceDossier.Status));
        Assert.Equal(0, decision.ComplianceDossier.InheritedRequirementCount);
        Assert.Equal(ComplianceDossierStatus.APPROVED, f.Transport.MainDossier!.Status);
        Assert.Single(db.AuditDossiers); Assert.Equal(2, await db.Notifications.CountAsync());
        Assert.Contains(await EmailOutboxWiringTests.EmailsAsync(db), mail => mail.Subject.StartsWith("Extra dossier ready for the return route — "));
        var specialist = await db.Employees.SingleAsync(x => x.Role == EmployeeRole.TRANSPORT_SPECIALIST);
        var reviewed = await TestSupport.CreateRoutePlans(db).ReviewLegalAsync(specialist.AccountId, plan.RoutePlanId,
            new() { LastUpdatedAt = decision.UpdatedAt, IsFeasible = true });
        Assert.Equal("CONFIRMED", reviewed.Status); Assert.True(reviewed.CustomerUploadEnabled);
        Assert.False(reviewed.PersonnelAssignmentPhase2Enabled);
        Assert.Equal(("EXTRA", "IN_PREPARATION"), (reviewed.ComplianceDossier!.DossierType, reviewed.ComplianceDossier.Status));
        Assert.Empty(reviewed.ConfirmedBackupRoutePlanIds);
        Assert.Equal(ComplianceDossierStatus.APPROVED, f.Transport.MainDossier!.Status);
        Assert.Equal(ComplianceDossierStatus.IN_PREPARATION, f.Transport.ExtraDossier!.Status);
        Assert.Equal(TransportStatus.IN_TRANSIT, f.Transport.Status);
        var blocked = await Assert.ThrowsAsync<UnprocessableEntityException>(() => f.Service.ActivateReturnAsync(plan.RoutePlanId, f.Manager.AccountId,
            new() { LastUpdatedAt = reviewed.UpdatedAt }));
        Assert.Equal("RETURN_DOSSIER_NOT_APPROVED", blocked.ErrorCode);
        f.Transport.ExtraDossier!.Status = ComplianceDossierStatus.APPROVED; await db.SaveChangesAsync();
        var vehicle = await db.Vehicles.SingleAsync(); vehicle.Status = VehicleStatus.IN_USE; await db.SaveChangesAsync();
        var result = await f.Service.ActivateReturnAsync(plan.RoutePlanId, f.Manager.AccountId, new() {
            LastUpdatedAt = reviewed.UpdatedAt, Segments = [new() { RoutePlanRouteId = row.RoutePlanRouteId, VehicleIds = [vehicle.VehicleId] }] });
        Assert.Equal("ACTIVE", result.Status); Assert.Equal(f.Driver.EmployeeId, result.Segments[0].Driver!.EmployeeId);
        Assert.False(result.Segments[0].IsOutsourced);
        Assert.Equal(vehicle.VehicleId, Assert.Single(result.Segments[0].Vehicles).VehicleId);
        Assert.Equal(VehicleStatus.IN_USE, vehicle.Status);
        Assert.NotNull(f.Current.SupersededAt); Assert.Null(f.Completed.SupersededAt);
        Assert.Equal(RoutePlanStatus.ACTIVE, f.Plan.Status); Assert.Equal(TransportStatus.IN_TRANSIT, f.Transport.Status);
        var notice = await f.Service.NotifyDriversAsync(plan.RoutePlanId, f.Coordinator.AccountId, new() { Message = "Dossier approved, return now" });
        Assert.Equal(f.Driver.EmployeeId, Assert.Single(notice.NotifiedDrivers).EmployeeId);
        var detail = await TestSupport.CreateRoutePlans(db).GetByIdAsync(f.Manager.AccountId, f.Plan.RoutePlanId);
        Assert.Equal(new[] { f.Completed.RouteId, row.RouteId }, detail.Segments.Select(x => x.Route.RouteId));
        Assert.Empty(db.EmergencyCostRequests);
    }

    [Fact]
    public async Task Return_Rp1AndRp4_RequireLmApprovalBeforeTsSubmission()
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        f.Transport.Booking.OriginCountry = f.Transport.Booking.DestinationCountry = "FR";
        var back = f.NewRoute(f.Start, f.Origin, 70); await db.SaveChangesAsync();
        var routes = TestSupport.CreateRoutePlans(db);
        var (_, unrelated) = await TestSupport.AddLogisticsManagerAsync(db);
        unrelated.Role = EmployeeRole.FLEET_ROUTE_COORDINATOR; await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ForbiddenException>(() => routes.CreateAsync(unrelated.AccountId, f.Plan.TransportPlanId, new() {
            RouteType = "RETURN", Action = "SAVE_DRAFT", RouteNote = "Road closed, return to origin",
            Segments = [new() { SequenceNo = 1, RouteId = back.RouteId,
                PlannedDepartureAt = Fixture.StartAt, PlannedArrivalAt = Fixture.StartAt.AddHours(2) }] }));
        var draft = await routes.CreateAsync(f.Coordinator.AccountId, f.Plan.TransportPlanId, new() {
            RouteType = "RETURN", Action = "SAVE_DRAFT", RouteNote = "Road closed, return to origin",
            Segments = [new() { SequenceNo = 1, RouteId = back.RouteId,
                PlannedDepartureAt = Fixture.StartAt, PlannedArrivalAt = Fixture.StartAt.AddHours(2) }] });
        Assert.Equal("DRAFT", draft.Status);
        Assert.Equal(ComplianceDossierStatus.APPROVED, f.Transport.MainDossier!.Status);
        var specialist = await db.Employees.SingleAsync(x => x.Role == EmployeeRole.TRANSPORT_SPECIALIST);
        Assert.DoesNotContain(await db.AccountNotifications.ToArrayAsync(), x => x.AccountId == specialist.AccountId);
        var change = new Pegaxus.Service.RoutePlanService.Request.UpdateRequest {
            LastUpdatedAt = draft.UpdatedAt, UpdateReason = "Updated return schedule", Action = "SUBMIT_FOR_LEGAL_REVIEW",
            RouteNote = "Road closed, return to origin",
            Segments = [new() { SequenceNo = 1, RouteId = back.RouteId,
                PlannedDepartureAt = Fixture.StartAt, PlannedArrivalAt = Fixture.StartAt.AddHours(2) }] };
        Assert.Equal("RETURN_NOT_APPROVED", (await Assert.ThrowsAsync<ConflictException>(() =>
            routes.UpdateAsync(f.Coordinator.AccountId, draft.RoutePlanId, change))).ErrorCode);
        var approved = await f.Service.DecideAsync(draft.RoutePlanId, f.Manager.AccountId,
            new() { Decision = "APPROVE", LastUpdatedAt = draft.UpdatedAt });
        change.LastUpdatedAt = approved.UpdatedAt;
        var updated = await routes.UpdateAsync(f.Coordinator.AccountId, draft.RoutePlanId, change);
        Assert.Equal("DRAFT", updated.Status); Assert.True(updated.UpdatedAt > approved.UpdatedAt);
        Assert.Contains(await db.Notifications.ToListAsync(), notification =>
            notification.Title == "Route plan needs another legal review" && notification.Priority == NotificationPriority.HIGH);
        await routes.ReviewLegalAsync(specialist.AccountId, draft.RoutePlanId,
            new() { LastUpdatedAt = updated.UpdatedAt, IsFeasible = true });
        Assert.Equal(ComplianceDossierStatus.APPROVED, f.Transport.MainDossier.Status);
        Assert.Equal(ComplianceDossierStatus.IN_PREPARATION, f.Transport.ExtraDossier!.Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Return_ActiveProvider_ReplacesVehiclesAndPersonnelWithoutProviderNotice(bool unmeasured)
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db); var plan = await f.AlternateAsync(RouteType.RETURN, RoutePlanStatus.CONFIRMED);
        await f.AddExtraAsync();
        var row = await db.RoutePlanRoutes.SingleAsync(x => x.RoutePlanId == plan.RoutePlanId);
        if (unmeasured) row.Route.DistanceKm = null;
        var provider = new TransportProvider { TransportProviderId = Guid.NewGuid(), ProviderCode = "P1", ProviderName = "Provider",
            Status = ProviderStatus.ACTIVE, SupportedModes = "ROAD", Email = "provider@example.com" };
        db.Add(provider); await db.SaveChangesAsync();
        var result = await f.Service.ActivateReturnAsync(plan.RoutePlanId, f.Manager.AccountId, new() {
            LastUpdatedAt = Fixture.Version(plan), Segments = [new() { RoutePlanRouteId = row.RoutePlanRouteId,
                Outsourcing = new() { TransportProviderId = provider.TransportProviderId, Note = "Emergency transport" } }] });
        var segment = Assert.Single(result.Segments);
        if (unmeasured) Assert.Null(segment.Route.DistanceKm);
        Assert.True(segment.IsOutsourced); Assert.Null(segment.Driver); Assert.Null(segment.Escort); Assert.Empty(segment.Vehicles);
        Assert.Equal(provider.TransportProviderId, segment.Outsourcing!.TransportProviderId);
        Assert.Equal(provider.TransportProviderId, Assert.Single(db.TransportPlanProviders).TransportProviderId);
        Assert.DoesNotContain(await db.OutboxMessages.ToListAsync(), x => x.Payload.Contains("provider@example.com"));
    }

    [Theory]
    [InlineData("provider", "PROVIDER_INACTIVE")]
    [InlineData("provider-mode", "PROVIDER_MODE_MISMATCH")]
    [InlineData("vehicle-mode", "VEHICLE_MODE_MISMATCH")]
    [InlineData("capacity", "VEHICLE_CAPACITY_EXCEEDED")]
    [InlineData("unavailable", "VEHICLE_UNAVAILABLE")]
    [InlineData("conflict", "SCHEDULE_CONFLICT")]
    [InlineData("outsourced", "OUTSOURCED_SEGMENT")]
    public async Task Return_InvalidResourcesFailWithoutChangingRoute(string invalid, string code)
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db); var plan = await f.AlternateAsync(RouteType.RETURN, RoutePlanStatus.CONFIRMED);
        await f.AddExtraAsync();
        var row = await db.RoutePlanRoutes.SingleAsync(x => x.RoutePlanId == plan.RoutePlanId);
        var vehicle = await db.Vehicles.SingleAsync();
        var change = new Request.SegmentRequest { RoutePlanRouteId = row.RoutePlanRouteId, VehicleIds = [vehicle.VehicleId] };
        if (invalid.StartsWith("provider") || invalid == "outsourced")
        {
            var provider = new TransportProvider { TransportProviderId = Guid.NewGuid(), ProviderCode = "PROVIDER", ProviderName = "Provider",
                SupportedModes = invalid == "provider-mode" ? "AIR" : "ROAD", Status = invalid == "provider" ? ProviderStatus.INACTIVE : ProviderStatus.ACTIVE };
            db.Add(provider);
            change.Outsourcing = new() { TransportProviderId = provider.TransportProviderId };
            if (invalid != "outsourced") change.VehicleIds = null;
        }
        if (invalid == "vehicle-mode") vehicle.TransportMode = TransportMode.AIR;
        if (invalid == "capacity") vehicle.CapacityHorses = 0;
        if (invalid == "unavailable") vehicle.Status = VehicleStatus.MAINTENANCE;
        if (invalid == "conflict")
        {
            var otherTransportPlan = new TransportPlan { TransportPlanId = Guid.NewGuid(), TransportId = f.Transport.TransportId, PlanCode = "TP2", Status = TransportPlanStatus.APPROVED };
            db.Add(otherTransportPlan);
            var other = new RoutePlan { RoutePlanId = Guid.NewGuid(), TransportPlanId = otherTransportPlan.TransportPlanId,
                RoutePlanCode = "OTHER", Status = RoutePlanStatus.CONFIRMED };
            var busy = new RoutePlanRoute { RoutePlanRouteId = Guid.NewGuid(), RoutePlan = other, RouteId = f.Current.RouteId,
                PlannedDepartureAt = f.Current.PlannedDepartureAt, PlannedArrivalAt = f.Current.PlannedArrivalAt };
            db.Add(busy); db.RoutePlanRouteVehicles.Add(new() { RoutePlanRouteId = busy.RoutePlanRouteId, VehicleId = vehicle.VehicleId });
        }
        await db.SaveChangesAsync();
        var error = await Record.ExceptionAsync(() => f.Service.ActivateReturnAsync(plan.RoutePlanId, f.Manager.AccountId,
            new() { LastUpdatedAt = Fixture.Version(plan), Segments = [change] }));
        Assert.Equal(code, Assert.IsAssignableFrom<Pegaxus.Service.Exceptions.Exceptions>(error).ErrorCode);
        Assert.Equal(RoutePlanStatus.CONFIRMED, plan.Status); Assert.Null(f.Current.SupersededAt); Assert.Empty(db.Notifications);
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(true, "VEHICLE_SCHEDULE_CONFLICT")]
    public async Task Return_Rp1_VehicleConflictOnlyAcrossTransportPlans_AndOwnInUseVehicleAllowed(bool otherPlan, string? code)
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db);
        f.Transport.Booking.OriginCountry = f.Transport.Booking.DestinationCountry = "FR";
        var back = f.NewRoute(f.Start, f.Origin, 70);
        var vehicle = await db.Vehicles.SingleAsync(); vehicle.Status = VehicleStatus.IN_USE;
        var planId = f.Plan.TransportPlanId;
        if (otherPlan)
        {
            var other = new TransportPlan { TransportPlanId = Guid.NewGuid(), TransportId = f.Transport.TransportId, PlanCode = "TP2", Status = TransportPlanStatus.APPROVED };
            db.Add(other); planId = other.TransportPlanId;
        }
        var busy = new RoutePlanRoute { RoutePlanRouteId = Guid.NewGuid(), RoutePlan = new() { RoutePlanId = Guid.NewGuid(), TransportPlanId = planId, RoutePlanCode = "BUSY", Status = RoutePlanStatus.CONFIRMED },
            RouteId = back.RouteId, PlannedDepartureAt = DateTime.SpecifyKind(Fixture.StartAt.UtcDateTime, DateTimeKind.Unspecified), PlannedArrivalAt = DateTime.SpecifyKind(Fixture.StartAt.AddHours(2).UtcDateTime, DateTimeKind.Unspecified) };
        db.Add(busy); db.RoutePlanRouteVehicles.Add(new() { RoutePlanRouteId = busy.RoutePlanRouteId, VehicleId = vehicle.VehicleId });
        await db.SaveChangesAsync();

        var create = () => TestSupport.CreateRoutePlans(db).CreateAsync(f.Coordinator.AccountId, f.Plan.TransportPlanId, new() {
            RouteType = "RETURN", Action = "SAVE_DRAFT", RouteNote = "Road closed, return to origin",
            Segments = [new() { SequenceNo = 1, RouteId = back.RouteId, VehicleIds = [vehicle.VehicleId],
                PlannedDepartureAt = Fixture.StartAt, PlannedArrivalAt = Fixture.StartAt.AddHours(2) }] });

        if (code is null) Assert.Equal("DRAFT", (await create()).Status);
        else Assert.Equal(code, (await Assert.ThrowsAsync<ConflictException>(create)).ErrorCode);
    }

    [Fact]
    public async Task Return_SameTransportPlanVehicleOverlapIsNotAConflict()
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db); var plan = await f.AlternateAsync(RouteType.RETURN, RoutePlanStatus.CONFIRMED);
        await f.AddExtraAsync();
        var row = await db.RoutePlanRoutes.SingleAsync(x => x.RoutePlanId == plan.RoutePlanId);
        var vehicle = await db.Vehicles.SingleAsync();
        var sibling = new RoutePlan { RoutePlanId = Guid.NewGuid(), TransportPlanId = f.Plan.TransportPlanId, RoutePlanCode = "SIBLING", Status = RoutePlanStatus.CONFIRMED };
        var busy = new RoutePlanRoute { RoutePlanRouteId = Guid.NewGuid(), RoutePlan = sibling, RouteId = f.Current.RouteId,
            PlannedDepartureAt = f.Current.PlannedDepartureAt, PlannedArrivalAt = f.Current.PlannedArrivalAt };
        db.Add(busy); db.RoutePlanRouteVehicles.Add(new() { RoutePlanRouteId = busy.RoutePlanRouteId, VehicleId = vehicle.VehicleId });
        await db.SaveChangesAsync();

        var result = await f.Service.ActivateReturnAsync(plan.RoutePlanId, f.Manager.AccountId,
            new() { LastUpdatedAt = Fixture.Version(plan), Segments = [new() { RoutePlanRouteId = row.RoutePlanRouteId, VehicleIds = [vehicle.VehicleId] }] });

        Assert.Equal(vehicle.VehicleId, Assert.Single(Assert.Single(result.Segments).Vehicles).VehicleId);
    }

    [Fact]
    public async Task RolesVersionsAndTransportStateProtectAllActions()
    {
        await using var db = TestSupport.CreateDb();
        var f = await Fixture.CreateAsync(db); var backup = await f.AlternateAsync();
        var (_, unrelated) = await TestSupport.AddLogisticsManagerAsync(db); unrelated.Role = EmployeeRole.FLEET_ROUTE_COORDINATOR; await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ForbiddenException>(() => f.Service.CreateAsync(backup.RoutePlanId, unrelated.AccountId, f.Proposal()));
        await Assert.ThrowsAsync<ForbiddenException>(() => f.Service.DecideAsync(backup.RoutePlanId, f.Coordinator.AccountId,
            new() { Decision = "APPROVE", LastUpdatedAt = Fixture.Version(backup) }));
        Assert.Equal("ROUTE_PLAN_MODIFIED", (await Assert.ThrowsAsync<ConflictException>(() => f.Service.DecideAsync(backup.RoutePlanId, f.Manager.AccountId,
            new() { Decision = "APPROVE", LastUpdatedAt = Fixture.Version(backup).AddSeconds(1) }))).ErrorCode);
        f.Transport.Status = TransportStatus.READY; await db.SaveChangesAsync();
        Assert.Equal("TRANSPORT_NOT_IN_TRANSIT", (await Assert.ThrowsAsync<ConflictException>(() =>
            f.Service.CreateAsync(backup.RoutePlanId, f.Coordinator.AccountId, f.Proposal()))).ErrorCode);
        Assert.Empty(db.Notifications);
    }

    [Fact]
    public void Validators_RequireContractFieldsAndRejectLegacyDecisions()
    {
        Assert.False(new CreateRequestValidator().Validate(new Request.CreateRequest { Reason = "short" }).IsValid);
        Assert.False(new DecisionRequestValidator().Validate(new Request.DecisionRequest { Decision = "EMERGENCY_RETURN", LastUpdatedAt = Fixture.StartAt }).IsValid);
        Assert.False(new DecisionRequestValidator().Validate(new Request.DecisionRequest { Decision = "REJECT", LastUpdatedAt = Fixture.StartAt, Note = "short" }).IsValid);
        Assert.False(new ActivationRequestValidator().Validate(new Request.ActivationRequest()).IsValid);
    }

    internal sealed class Fixture(AppDbContext db)
    {
        internal static readonly DateTimeOffset StartAt = new(2030, 1, 2, 10, 0, 0, TimeSpan.Zero);
        internal Account Coordinator = null!, Manager = null!;
        internal Employee Driver = null!, Escort = null!;
        internal Location Origin = null!, Start = null!, End = null!;
        internal Route Alternative = null!;
        internal RoutePlan Plan = null!;
        internal RoutePlanRoute Current = null!, Completed = null!;
        internal Transport Transport = null!;
        internal RouteChange.Service Service = new(db, new Pegaxus.Service.NotificationService.Service(db, new NoopNotificationRealtimeService()), new FixedClock(),
            TestSupport.CreateRoutePlans(db));
        internal static async Task<Fixture> CreateAsync(AppDbContext db)
        {
            var f = new Fixture(db);
            var (managerAccount, _) = await TestSupport.AddLogisticsManagerAsync(db);
            f.Manager = managerAccount;
            var (rcAccount, rc) = await TestSupport.AddLogisticsManagerAsync(db);
            rc.Role = EmployeeRole.FLEET_ROUTE_COORDINATOR;
            f.Coordinator = rcAccount;
            var (_, driver) = await TestSupport.AddLogisticsManagerAsync(db);
            driver.Role = EmployeeRole.DRIVER;
            f.Driver = driver;
            var (_, escort) = await TestSupport.AddLogisticsManagerAsync(db);
            escort.Role = EmployeeRole.ESCORT;
            f.Escort = escort;
            var (_, specialist) = await TestSupport.AddLogisticsManagerAsync(db);
            specialist.Role = EmployeeRole.TRANSPORT_SPECIALIST;
            var (_, customer) = await TestSupport.AddCustomerAsync(db);
            var (booking, _) = await TestSupport.AddPendingBookingAsync(db, customer, true);
            f.Origin = new() { LocationId = Guid.NewGuid(), LocationName = "Origin", Country = "FR", LocationType = LocationType.STABLE };
            f.Start = new() { LocationId = Guid.NewGuid(), LocationName = "Safe stop", Country = "FR" };
            f.End = new() { LocationId = Guid.NewGuid(), LocationName = "Destination", Country = "DE" };
            db.AddRange(f.Origin, f.Start, f.End);
            var completedRoute = f.NewRoute(f.Origin, f.Start, 70);
            var currentRoute = f.NewRoute(f.Start, f.End, 100);
            f.Alternative = f.NewRoute(f.Start, f.End, 115);
            f.Transport = new() { TransportId = Guid.NewGuid(), TransportCode = "TR1", BookingId = booking.BookingId, Status = TransportStatus.IN_TRANSIT };
            booking.Status = BookingStatus.APPROVED;
            var transportPlan = new TransportPlan { TransportPlanId = Guid.NewGuid(), TransportId = f.Transport.TransportId, PlanCode = "TP1", Status = TransportPlanStatus.APPROVED, PlannedEndAt = DbTime(StartAt.AddHours(2)) };
            f.Plan = new() { RoutePlanId = Guid.NewGuid(), TransportPlanId = transportPlan.TransportPlanId, RoutePlanCode = "RP1", Status = RoutePlanStatus.ACTIVE, CreatedAt = DbTime(StartAt), UpdatedAt = DbTime(StartAt) };
            f.Completed = new() { RoutePlanRouteId = Guid.NewGuid(), RoutePlanId = f.Plan.RoutePlanId, RouteId = completedRoute.RouteId, SequenceNo = 1,
                PlannedDepartureAt = DbTime(StartAt.AddHours(-3)), PlannedArrivalAt = DbTime(StartAt.AddHours(-1)),
                ActualDepartureAt = DbTime(StartAt.AddHours(-3)), ActualArrivalAt = DbTime(StartAt.AddHours(-1)) };
            f.Current = new() { RoutePlanRouteId = Guid.NewGuid(), RoutePlanId = f.Plan.RoutePlanId, RouteId = currentRoute.RouteId, SequenceNo = 2,
                DriverEmployeeId = driver.EmployeeId, EscortEmployeeId = escort.EmployeeId, PlannedDepartureAt = DbTime(StartAt), PlannedArrivalAt = DbTime(StartAt.AddHours(2)) };
            var vehicle = new Vehicle { VehicleId = Guid.NewGuid(), VehicleCode = "V1", CapacityHorses = 5, TransportMode = TransportMode.ROAD,
                RegistrationExpiryDate = new(2031, 1, 1), InsuranceExpiryDate = new(2031, 1, 1) };
            db.AddRange(f.Transport, transportPlan, f.Plan, f.Completed, f.Current, vehicle,
                new RoutePlanRouteVehicle { RoutePlanRouteId = f.Current.RoutePlanRouteId, VehicleId = vehicle.VehicleId, AssignedAt = DbTime(StartAt) },
                new EmployeeTransportPlan { TransportPlanId = transportPlan.TransportPlanId, EmployeeId = specialist.EmployeeId, Responsibility = "TRANSPORT_SPECIALIST", AssignedAt = DbTime(StartAt), IsPrimary = true },
                new EmployeeTransportPlan { TransportPlanId = transportPlan.TransportPlanId, EmployeeId = rc.EmployeeId, Responsibility = "FLEET_ROUTE_COORDINATOR", AssignedAt = DbTime(StartAt), IsPrimary = true },
                new ComplianceDossier { ComplianceDossierId = Guid.NewGuid(), TransportId = f.Transport.TransportId, DossierCode = "CD1", Status = ComplianceDossierStatus.APPROVED, CreatedAt = DbTime(StartAt), UpdatedAt = DbTime(StartAt) });
            await db.SaveChangesAsync();
            return f;
        }
        internal Route NewRoute(Location from, Location to, decimal distance)
        {
            var route = new Route { RouteId = Guid.NewGuid(), RouteCode = "R-" + Guid.NewGuid().ToString("N"),
                FromLocationId = from.LocationId, ToLocationId = to.LocationId, FromLocation = from, ToLocation = to, DistanceKm = distance,
                EstimatedDurationMinutes = 120, TransportMode = TransportMode.ROAD };
            db.Add(route);
            return route;
        }
        internal static DateTimeOffset Version(RoutePlan plan) => new(DateTime.SpecifyKind(plan.UpdatedAt, DateTimeKind.Utc));
        internal async Task<RoutePlan> AlternateAsync(RouteType type = RouteType.BACKUP, RoutePlanStatus? status = null)
        {
            var plan = new RoutePlan { RoutePlanId = Guid.NewGuid(), TransportPlanId = Plan.TransportPlanId,
                PrimaryRoutePlanId = Plan.RoutePlanId, RoutePlanCode = type + "-" + Guid.NewGuid().ToString("N"),
                Status = status ?? (type == RouteType.RETURN ? RoutePlanStatus.DRAFT : RoutePlanStatus.CONFIRMED),
                CreatedAt = DbTime(StartAt), UpdatedAt = DbTime(StartAt), RouteNote = "Emergency route planning" };
            if (type == RouteType.RETURN)
            {
                db.Add(new RoutePlanRoute { RoutePlanRouteId = Guid.NewGuid(), RoutePlan = plan, Route = NewRoute(Start, Origin, 70), SequenceNo = 1,
                    RouteType = type, PlannedDepartureAt = DbTime(StartAt), PlannedArrivalAt = DbTime(StartAt.AddHours(2)) });
            }
            else
            {
                var primary = await db.RoutePlanRoutes.Include(x => x.Route).Include(x => x.RoutePlanRouteVehicles)
                    .Where(x => x.RoutePlanId == Plan.RoutePlanId).OrderBy(x => x.SequenceNo).ToListAsync();
                foreach (var source in primary)
                {
                    var isDetour = source.RoutePlanRouteId == Current.RoutePlanRouteId;
                    var row = new RoutePlanRoute { RoutePlanRouteId = Guid.NewGuid(), RoutePlan = plan, Route = isDetour ? Alternative : source.Route,
                        SequenceNo = source.SequenceNo, RouteType = isDetour ? RouteType.BACKUP : RouteType.PRIMARY,
                        PlannedDepartureAt = source.PlannedDepartureAt, PlannedArrivalAt = source.PlannedArrivalAt };
                    db.Add(row);
                    foreach (var vehicle in source.RoutePlanRouteVehicles)
                        db.RoutePlanRouteVehicles.Add(new() { RoutePlanRouteId = row.RoutePlanRouteId, VehicleId = vehicle.VehicleId, AssignedAt = DbTime(StartAt) });
                }
            }
            await db.SaveChangesAsync();
            return plan;
        }
        internal async Task<ComplianceDossier> AddExtraAsync(ComplianceDossierStatus status = ComplianceDossierStatus.APPROVED)
        {
            var extra = new ComplianceDossier { ComplianceDossierId = Guid.NewGuid(), TransportId = Transport.TransportId, DossierCode = "CD-EXTRA",
                DossierType = DossierType.EXTRA, Status = status, CreatedAt = DbTime(StartAt), UpdatedAt = DbTime(StartAt) };
            db.Add(extra);
            await db.SaveChangesAsync();
            return extra;
        }
        internal Request.CreateRequest Proposal(int delay = 60) => new()
        {
            Reason = "Road blocked by severe weather", NewEstimatedArrivalAt = StartAt.AddHours(2).AddMinutes(delay),
            EstimatedExtraCost = 1800
        };
        private static DateTime DbTime(DateTimeOffset value) => DateTime.SpecifyKind(value.UtcDateTime, DateTimeKind.Unspecified);
        internal sealed class FixedClock : TimeProvider { public override DateTimeOffset GetUtcNow() => StartAt.AddHours(-1); }
    }
}
