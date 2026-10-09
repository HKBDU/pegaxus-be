using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Npgsql.NameTranslation;
using Pegaxus.API.Controllers;
using Pegaxus.API.Extensions;
using Pegaxus.API.Middleware;
using Pegaxus.API.Services;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Repository.Migrations;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.JwtService;
using Pegaxus.Service.NotificationService;
using Pegaxus.Service.Common;
using Outsourcing = Pegaxus.Service.RouteOutsourcingService;
using Claim = System.Security.Claims.Claim;
using ClaimTypes = System.Security.Claims.ClaimTypes;

namespace Pegaxus.Service.Test;

public class RouteOutsourcingTests
{
    [Fact]
    public async Task AssignSwitchAndRemove_PersistSegmentMetadataClearResourcesAndMaintainPlanLinks()
    {
        await using var db = TestSupport.CreateDb();
        var (actor, segment, provider) = await SeedAsync(db);
        var (_, driver) = await TestSupport.AddLogisticsManagerAsync(db);
        driver.Role = EmployeeRole.DRIVER;
        var (_, escort) = await TestSupport.AddLogisticsManagerAsync(db);
        escort.Role = EmployeeRole.ESCORT;
        segment.DriverEmployee = driver;
        segment.EscortEmployee = escort;
        var vehicle = new Vehicle { VehicleId = Guid.NewGuid(), VehicleCode = "VEH-1", TransportMode = TransportMode.AIR };
        db.RoutePlanRouteVehicles.Add(new() { RoutePlanRoute = segment, Vehicle = vehicle });
        await db.SaveChangesAsync();
        var service = Service(db);
        var result = await service.SetAsync(actor, segment.RoutePlanId, segment.RoutePlanRouteId, Assign(segment, provider));
        Assert.Equal("OUTSOURCED", result.Segment.ExecutionType);
        Assert.Equal("FULLY_ASSIGNED", result.PersonnelStatus);
        Assert.True(result.ClearedPersonnel);
        Assert.True(result.ClearedVehicles);
        Assert.Empty(result.Segment.Vehicles);
        Assert.Null(result.Segment.Driver);
        Assert.Null(result.Segment.Escort);
        Assert.Empty(await db.RoutePlanRouteVehicles.ToListAsync());
        Assert.Null(segment.DriverEmployeeId);
        Assert.Null(segment.EscortEmployeeId);
        Assert.Equal("AIR_CHARTER", segment.ServiceType);
        Assert.Equal("AWB-123", segment.ProviderReference);
        var link = await db.TransportPlanProviders.SingleAsync();
        Assert.Equal(provider.TransportProviderId, link.TransportProviderId);
        Assert.Equal("AIR_CHARTER", link.ServiceType);
        Assert.Equal("Flight note", link.Note);
        Assert.Equal(segment.UpdatedAt, link.AssignedAt);
        Assert.Equal(TimeSpan.Zero, result.UpdatedAt.Offset);
        Assert.Single(await db.Notifications.ToListAsync());

        await service.SetAsync(actor, segment.RoutePlanId, segment.RoutePlanRouteId, Assign(segment, provider));
        Assert.Single(await db.CatalogAuditLogs.ToListAsync());
        Assert.Single(await db.Notifications.ToListAsync());
        var otherRoute = new RoutePlan { RoutePlanId = Guid.NewGuid(), RoutePlanCode = "BACKUP", TransportPlan = segment.RoutePlan.TransportPlan,
            CreatedAt = segment.CreatedAt, UpdatedAt = segment.UpdatedAt };
        var other = new RoutePlanRoute { RoutePlanRouteId = Guid.NewGuid(), RoutePlan = otherRoute, Route = segment.Route, SequenceNo = 1,
            TransportProvider = provider, ServiceType = "AIR_FEEDER", CreatedAt = segment.CreatedAt, UpdatedAt = segment.UpdatedAt };
        db.RoutePlanRoutes.Add(other);
        await db.SaveChangesAsync();
        await service.SetAsync(actor, segment.RoutePlanId, segment.RoutePlanRouteId, new() { Outsourced = false, LastUpdatedAt = Offset(segment.RoutePlan.UpdatedAt) });
        Assert.Single(await db.TransportPlanProviders.ToListAsync());
        Assert.Null(segment.TransportProviderId);
        Assert.Null(segment.ServiceType);
        Assert.Null(segment.ProviderReference);
        var internalResult = await service.SetAsync(actor, other.RoutePlanId, other.RoutePlanRouteId,
            new() { Outsourced = false, LastUpdatedAt = Offset(otherRoute.UpdatedAt) });
        Assert.Equal("INTERNAL", internalResult.Segment.ExecutionType);
        Assert.Equal("NOT_ASSIGNED", internalResult.PersonnelStatus);
        Assert.Null(internalResult.Segment.Outsourcing);
        Assert.Empty(await db.TransportPlanProviders.ToListAsync());

        await service.SetAsync(actor, segment.RoutePlanId, segment.RoutePlanRouteId, Assign(segment, provider));
        var replacement = new TransportProvider { TransportProviderId = Guid.NewGuid(), ProviderCode = "PRV-2", ProviderName = "Replacement", SupportedModes = "AIR", Status = ProviderStatus.ACTIVE };
        db.TransportProviders.Add(replacement);
        await db.SaveChangesAsync();
        await service.SetAsync(actor, segment.RoutePlanId, segment.RoutePlanRouteId, Assign(segment, replacement));
        Assert.Equal(replacement.TransportProviderId, (await db.TransportPlanProviders.SingleAsync()).TransportProviderId);
        Assert.Equal(replacement.TransportProviderId, segment.TransportProviderId);
    }

    [Theory]
    [InlineData(RoutePlanStatus.DRAFT, RoutePlanStatus.DRAFT)]
    [InlineData(RoutePlanStatus.REJECTED_BY_MANAGER, RoutePlanStatus.REJECTED_BY_MANAGER)]
    [InlineData(RoutePlanStatus.PENDING_LEGAL_REVIEW, RoutePlanStatus.PENDING_LEGAL_REVIEW)]
    [InlineData(RoutePlanStatus.PENDING_MANAGER_APPROVAL, RoutePlanStatus.PENDING_LEGAL_REVIEW)]
    [InlineData(RoutePlanStatus.APPROVED, RoutePlanStatus.PENDING_LEGAL_REVIEW)]
    [InlineData(RoutePlanStatus.CONFIRMED, RoutePlanStatus.PENDING_LEGAL_REVIEW)]
    public async Task AssignAndRemove_InvalidateReviewWhenRequired(RoutePlanStatus initial, RoutePlanStatus expected)
    {
        await using var db = TestSupport.CreateDb();
        var (actor, segment, provider) = await SeedAsync(db);
        segment.RoutePlan.Status = initial;
        await db.SaveChangesAsync();
        var result = await Service(db).SetAsync(actor, segment.RoutePlanId, segment.RoutePlanRouteId, Assign(segment, provider));
        Assert.Equal(expected.ToString(), result.RoutePlanStatus);
        segment.RoutePlan.Status = initial;
        await db.SaveChangesAsync();
        result = await Service(db).SetAsync(actor, segment.RoutePlanId, segment.RoutePlanRouteId,
            new() { Outsourced = false, LastUpdatedAt = Offset(segment.RoutePlan.UpdatedAt) });
        Assert.Equal(expected.ToString(), result.RoutePlanStatus);
    }

    [Theory]
    [InlineData(RoutePlanStatus.ACTIVE)]
    [InlineData(RoutePlanStatus.COMPLETED)]
    [InlineData(RoutePlanStatus.CANCELLED)]
    public async Task LockedRoute_RejectsAssignmentAndRemoval(RoutePlanStatus status)
    {
        await using var db = TestSupport.CreateDb();
        var (actor, segment, provider) = await SeedAsync(db);
        segment.RoutePlan.Status = status;
        await db.SaveChangesAsync();
        foreach (var request in new[] { Assign(segment, provider), new Outsourcing.Request { Outsourced = false, LastUpdatedAt = Offset(segment.RoutePlan.UpdatedAt) } })
            Assert.Equal("ROUTE_LOCKED", (await Assert.ThrowsAsync<ConflictException>(() =>
                Service(db).SetAsync(actor, segment.RoutePlanId, segment.RoutePlanRouteId, request))).ErrorCode);
        Assert.Empty(await db.TransportPlanProviders.ToListAsync());
        Assert.Empty(await db.CatalogAuditLogs.ToListAsync());
    }

    [Fact]
    public async Task ProviderValidation_RejectsMissingDeletedInactiveAndModeMismatchWithoutWrites()
    {
        await using var db = TestSupport.CreateDb();
        var (actor, segment, provider) = await SeedAsync(db);
        var request = Assign(segment, provider);
        request.TransportProviderId = null;
        Assert.Equal("PROVIDER_REQUIRED", (await Assert.ThrowsAsync<BadRequestException>(() => Service(db).SetAsync(actor, segment.RoutePlanId, segment.RoutePlanRouteId, request))).ErrorCode);
        request.TransportProviderId = Guid.NewGuid();
        Assert.Equal("PROVIDER_REQUIRED", (await Assert.ThrowsAsync<BadRequestException>(() => Service(db).SetAsync(actor, segment.RoutePlanId, segment.RoutePlanRouteId, request))).ErrorCode);
        request.TransportProviderId = provider.TransportProviderId;
        provider.IsDeleted = true;
        await db.SaveChangesAsync();
        Assert.Equal("PROVIDER_REQUIRED", (await Assert.ThrowsAsync<BadRequestException>(() => Service(db).SetAsync(actor, segment.RoutePlanId, segment.RoutePlanRouteId, request))).ErrorCode);
        provider.IsDeleted = false;
        foreach (var status in new[] { ProviderStatus.INACTIVE, ProviderStatus.SUSPENDED })
        {
            provider.Status = status;
            await db.SaveChangesAsync();
            Assert.Equal("PROVIDER_INACTIVE", (await Assert.ThrowsAsync<ConflictException>(() => Service(db).SetAsync(actor, segment.RoutePlanId, segment.RoutePlanRouteId, request))).ErrorCode);
        }
        provider.Status = ProviderStatus.ACTIVE;
        provider.SupportedModes = "ROAD,SEA,CHAIR";
        await db.SaveChangesAsync();
        Assert.Equal("PROVIDER_MODE_MISMATCH", (await Assert.ThrowsAsync<UnprocessableEntityException>(() => Service(db).SetAsync(actor, segment.RoutePlanId, segment.RoutePlanRouteId, request))).ErrorCode);
        Assert.Null(segment.TransportProviderId);
        Assert.Empty(await db.TransportPlanProviders.ToListAsync());
        Assert.Empty(await db.CatalogAuditLogs.ToListAsync());
        Assert.Empty(await db.Notifications.ToListAsync());
    }

    [Fact]
    public async Task SecurityOwnershipAndVersion_AreEnforced()
    {
        await using var db = TestSupport.CreateDb();
        var (actor, segment, provider) = await SeedAsync(db);
        var (customer, _) = await TestSupport.AddCustomerAsync(db);
        await Assert.ThrowsAsync<ForbiddenException>(() => Service(db).SetAsync(customer.AccountId, segment.RoutePlanId, segment.RoutePlanRouteId, Assign(segment, provider)));
        Assert.Equal("ROUTE_PLAN_NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(() => Service(db).SetAsync(actor, Guid.NewGuid(), segment.RoutePlanRouteId, Assign(segment, provider)))).ErrorCode);
        Assert.Equal("SEGMENT_NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(() => Service(db).SetAsync(actor, segment.RoutePlanId, Guid.NewGuid(), Assign(segment, provider)))).ErrorCode);
        var stale = Assign(segment, provider);
        await Service(db).SetAsync(actor, segment.RoutePlanId, segment.RoutePlanRouteId, stale);
        Assert.Equal("ROUTE_PLAN_MODIFIED", (await Assert.ThrowsAsync<ConflictException>(() => Service(db).SetAsync(actor, segment.RoutePlanId, segment.RoutePlanRouteId, stale))).ErrorCode);
        var account = await db.Accounts.SingleAsync(item => item.AccountId == actor);
        account.Status = AccountStatus.INACTIVE;
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ForbiddenException>(() => Service(db).SetAsync(actor, segment.RoutePlanId, segment.RoutePlanRouteId, Assign(segment, provider)));
    }

    [Fact]
    public async Task ExistingOutsourcing_RepairsMissingPlanLinkAndRejectsForeignSegment()
    {
        await using var db = TestSupport.CreateDb();
        var (actor, segment, provider) = await SeedAsync(db);
        segment.TransportProviderId = provider.TransportProviderId;
        segment.ServiceType = "AIR_CHARTER";
        segment.ProviderReference = "AWB-123";
        segment.Note = "Flight note";
        var foreign = new RoutePlanRoute
        {
            RoutePlanRouteId = Guid.NewGuid(), Route = segment.Route, SequenceNo = 1,
            RoutePlan = new RoutePlan { RoutePlanId = Guid.NewGuid(), RoutePlanCode = "OTHER", TransportPlan = segment.RoutePlan.TransportPlan,
                UpdatedAt = segment.UpdatedAt, CreatedAt = segment.CreatedAt }, CreatedAt = segment.CreatedAt, UpdatedAt = segment.UpdatedAt
        };
        db.RoutePlanRoutes.Add(foreign);
        await db.SaveChangesAsync();
        Assert.Equal("SEGMENT_NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(() =>
            Service(db).SetAsync(actor, segment.RoutePlanId, foreign.RoutePlanRouteId, Assign(segment, provider)))).ErrorCode);
        await Service(db).SetAsync(actor, segment.RoutePlanId, segment.RoutePlanRouteId, Assign(segment, provider));
        Assert.Equal(provider.TransportProviderId, (await db.TransportPlanProviders.SingleAsync()).TransportProviderId);
    }

    [Fact]
    public void Validation_RequiresExplicitSelectionTimestampServiceAndBoundedFields()
    {
        var validator = new Outsourcing.Validator.RequestValidator();
        Assert.False(validator.Validate(new Outsourcing.Request()).IsValid);
        Assert.True(validator.Validate(new Outsourcing.Request { Outsourced = false, LastUpdatedAt = DateTimeOffset.UtcNow }).IsValid);
        Assert.False(validator.Validate(new Outsourcing.Request { Outsourced = false, TransportProviderId = Guid.NewGuid(), LastUpdatedAt = DateTimeOffset.UtcNow }).IsValid);
        Assert.False(validator.Validate(new Outsourcing.Request { Outsourced = true, TransportProviderId = Guid.Empty, LastUpdatedAt = DateTimeOffset.UtcNow }).IsValid);
        Assert.False(validator.Validate(new Outsourcing.Request { Outsourced = true, TransportProviderId = Guid.NewGuid(), LastUpdatedAt = DateTimeOffset.UtcNow, ServiceType = " " }).IsValid);
        Assert.False(validator.Validate(new Outsourcing.Request { Outsourced = true, TransportProviderId = Guid.NewGuid(), LastUpdatedAt = DateTimeOffset.UtcNow, ServiceType = new string('x', 101), ProviderReference = new string('x', 256) }).IsValid);
    }

    [Fact]
    public async Task HttpContract_EnforcesJwtAndReturnsExpectedEnvelopeAndErrorCodes()
    {
        await using var db = TestSupport.CreateDb();
        var (actor, segment, provider) = await SeedAsync(db);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["JwtOptions:SecretKey"] = new string('x', 32), ["JwtOptions:Issuer"] = "uc42-tests",
            ["JwtOptions:Audience"] = "uc42-tests", ["JwtOptions:ExpirationMinutes"] = "15"
        });
        builder.Services.AddControllers().AddApplicationPart(typeof(RouteOutsourcingController).Assembly);
        builder.Services.AddJwtServices(builder.Configuration);
        builder.Services.AddSingleton(db);
        builder.Services.AddScoped<Outsourcing.IService>(_ => Service(db));
        builder.Services.AddTransient<GlobalExceptionHandlerMiddleware>();
        await using var app = builder.Build();
        app.UseMiddleware<GlobalExceptionHandlerMiddleware>();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        await app.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
        var path = $"/api/v1/route-plans/{segment.RoutePlanId}/segments/{segment.RoutePlanRouteId}/outsourcing";
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PutAsJsonAsync(path, Assign(segment, provider))).StatusCode);
        void SignIn(string role) => client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            new JwtServices(builder.Configuration).GenerateAccessToken([new Claim(ClaimTypes.NameIdentifier, actor.ToString()), new Claim(ClaimTypes.Role, "Employee"), new Claim(ClaimTypes.Role, role)]));
        SignIn("FleetRouteCoordinator");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync(path, Assign(segment, provider))).StatusCode);
        SignIn("LogisticsManager");
        async Task Error(Outsourcing.Request request, HttpStatusCode status, string code)
        {
            var response = await client.PutAsJsonAsync(path, request);
            Assert.Equal(status, response.StatusCode);
            Assert.Equal(code, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errorCode").GetString());
        }
        await Error(new() { Outsourced = true, LastUpdatedAt = Offset(segment.RoutePlan.UpdatedAt), ServiceType = "AIR_CHARTER" }, HttpStatusCode.BadRequest, "PROVIDER_REQUIRED");
        provider.SupportedModes = "ROAD";
        await db.SaveChangesAsync();
        await Error(Assign(segment, provider), HttpStatusCode.UnprocessableEntity, "PROVIDER_MODE_MISMATCH");
        provider.SupportedModes = "AIR";
        provider.Status = ProviderStatus.INACTIVE;
        await db.SaveChangesAsync();
        await Error(Assign(segment, provider), HttpStatusCode.Conflict, "PROVIDER_INACTIVE");
        provider.Status = ProviderStatus.ACTIVE;
        await db.SaveChangesAsync();
        var request = Assign(segment, provider);
        var success = await client.PutAsJsonAsync(path, request);
        Assert.Equal(HttpStatusCode.OK, success.StatusCode);
        var body = await success.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("isSuccess").GetBoolean());
        var value = body.GetProperty("value");
        Assert.Equal("OUTSOURCED", value.GetProperty("segment").GetProperty("executionType").GetString());
        Assert.Equal("FULLY_ASSIGNED", value.GetProperty("personnelStatus").GetString());
        Assert.Equal(1500.25m, value.GetProperty("financialPlan").GetProperty("routes")[0].GetProperty("totalOutsourcingCost").GetDecimal());
        Assert.Equal("EUR", value.GetProperty("segment").GetProperty("outsourcing").GetProperty("currency").GetString());
        Assert.Equal(provider.TransportProviderId, value.GetProperty("segment").GetProperty("outsourcing").GetProperty("provider").GetProperty("transportProviderId").GetGuid());
        Assert.Equal(JsonValueKind.Null, value.GetProperty("segment").GetProperty("driver").ValueKind);
        Assert.Equal(0, value.GetProperty("segment").GetProperty("vehicles").GetArrayLength());
        await Error(request, HttpStatusCode.Conflict, "ROUTE_PLAN_MODIFIED");
        var removed = await client.PutAsJsonAsync(path, new Outsourcing.Request { Outsourced = false, LastUpdatedAt = value.GetProperty("updatedAt").GetDateTimeOffset() });
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        var removedSegment = (await removed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("value").GetProperty("segment");
        Assert.Equal("INTERNAL", removedSegment.GetProperty("executionType").GetString());
        Assert.Equal(JsonValueKind.Null, removedSegment.GetProperty("outsourcing").ValueKind);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync(path.Replace(segment.RoutePlanRouteId.ToString(), Guid.NewGuid().ToString()), Assign(segment, provider))).StatusCode);
        await app.StopAsync();
    }

    [Fact]
    public void PersonnelStatus_OutsourcedSegmentsNeedOnlyProvider_InternalSegmentsNeedDriverAndEscort()
    {
        var outsourced = new RoutePlanRoute { TransportProviderId = Guid.NewGuid() };
        var internalSegment = new RoutePlanRoute();
        Assert.Equal("NOT_ASSIGNED", RoutePersonnel.GetStatus([]));
        Assert.Equal("NOT_ASSIGNED", RoutePersonnel.GetStatus([internalSegment]));
        Assert.Equal("FULLY_ASSIGNED", RoutePersonnel.GetStatus([outsourced]));
        Assert.Equal("PARTIALLY_ASSIGNED", RoutePersonnel.GetStatus([outsourced, internalSegment]));
        internalSegment.DriverEmployeeId = Guid.NewGuid();
        Assert.Equal("PARTIALLY_ASSIGNED", RoutePersonnel.GetStatus([internalSegment]));
        internalSegment.EscortEmployeeId = Guid.NewGuid();
        Assert.Equal("FULLY_ASSIGNED", RoutePersonnel.GetStatus([outsourced, internalSegment]));
        outsourced.TransportProviderId = null;
        Assert.Equal("PARTIALLY_ASSIGNED", RoutePersonnel.GetStatus([outsourced, internalSegment]));
    }

    [Fact]
    public async Task FinancialPlan_RecalculatesCostsSeparatelyPerRouteAndCurrencyOnEveryChange()
    {
        await using var db = TestSupport.CreateDb();
        var (actor, first, provider) = await SeedAsync(db);
        var second = new RoutePlanRoute { RoutePlanRouteId = Guid.NewGuid(), RoutePlan = first.RoutePlan, Route = first.Route, SequenceNo = 2,
            CreatedAt = first.CreatedAt, UpdatedAt = first.UpdatedAt };
        var backup = new RoutePlanRoute { RoutePlanRouteId = Guid.NewGuid(), Route = first.Route, SequenceNo = 1,
            RoutePlan = new RoutePlan { RoutePlanId = Guid.NewGuid(), RoutePlanCode = "BACKUP", TransportPlan = first.RoutePlan.TransportPlan,
                CreatedAt = first.CreatedAt, UpdatedAt = first.UpdatedAt }, CreatedAt = first.CreatedAt, UpdatedAt = first.UpdatedAt };
        var cancelled = new RoutePlanRoute { RoutePlanRouteId = Guid.NewGuid(), Route = first.Route, SequenceNo = 1, TransportProvider = provider,
            OutsourcingCostAmount = 999m, OutsourcingCurrency = "EUR",
            RoutePlan = new RoutePlan { RoutePlanId = Guid.NewGuid(), RoutePlanCode = "CANCELLED", Status = RoutePlanStatus.CANCELLED,
                TransportPlan = first.RoutePlan.TransportPlan, CreatedAt = first.CreatedAt, UpdatedAt = first.UpdatedAt }, CreatedAt = first.CreatedAt, UpdatedAt = first.UpdatedAt };
        db.AddRange(second, backup, cancelled);
        await db.SaveChangesAsync();
        var service = Service(db);
        var result = await service.SetAsync(actor, first.RoutePlanId, first.RoutePlanRouteId, Assign(first, provider));
        Assert.Equal("PARTIALLY_ASSIGNED", result.PersonnelStatus);
        Assert.Equal(1500.25m, Assert.Single(result.FinancialPlan.Routes).TotalOutsourcingCost);
        Assert.Equal(first.RoutePlan.TransportPlanId, result.FinancialPlan.TransportPlanId);
        var request = Assign(second, provider);
        request.CostAmount = 100.50m;
        result = await service.SetAsync(actor, second.RoutePlanId, second.RoutePlanRouteId, request);
        Assert.Equal("FULLY_ASSIGNED", result.PersonnelStatus);
        Assert.Equal(1600.75m, Assert.Single(result.FinancialPlan.Routes).TotalOutsourcingCost);
        Assert.Equal("EUR", second.OutsourcingCurrency);
        request = Assign(backup, provider);
        request.Currency = "USD";
        request.CostAmount = 75m;
        result = await service.SetAsync(actor, backup.RoutePlanId, backup.RoutePlanRouteId, request);
        Assert.Equal(2, result.FinancialPlan.Routes.Count);
        Assert.Equal(75m, result.FinancialPlan.Routes.Single(item => item.RoutePlanId == backup.RoutePlanId).TotalOutsourcingCost);
        Assert.DoesNotContain(result.FinancialPlan.Routes, item => item.RoutePlanId == cancelled.RoutePlanId);
        request = Assign(second, provider);
        request.Currency = "USD";
        request.CostAmount = 40m;
        result = await service.SetAsync(actor, second.RoutePlanId, second.RoutePlanRouteId, request);
        Assert.Equal(3, result.FinancialPlan.Routes.Count);
        Assert.Equal(1500.25m, result.FinancialPlan.Routes.Single(item => item.RoutePlanId == first.RoutePlanId && item.Currency == "EUR").TotalOutsourcingCost);
        Assert.Equal(40m, result.FinancialPlan.Routes.Single(item => item.RoutePlanId == first.RoutePlanId && item.Currency == "USD").TotalOutsourcingCost);
        result = await service.SetAsync(actor, second.RoutePlanId, second.RoutePlanRouteId,
            new() { Outsourced = false, LastUpdatedAt = Offset(second.RoutePlan.UpdatedAt) });
        Assert.Equal("PARTIALLY_ASSIGNED", result.PersonnelStatus);
        Assert.Null(second.OutsourcingCostAmount);
        Assert.Null(second.OutsourcingCurrency);
        Assert.Null(result.Segment.Outsourcing);
        Assert.Equal(2, result.FinancialPlan.Routes.Count);
        result = await service.SetAsync(actor, first.RoutePlanId, first.RoutePlanRouteId,
            new() { Outsourced = false, LastUpdatedAt = Offset(first.RoutePlan.UpdatedAt) });
        Assert.Equal(backup.RoutePlanId, Assert.Single(result.FinancialPlan.Routes).RoutePlanId);
        Assert.Equal("NOT_ASSIGNED", result.PersonnelStatus);
        backup.OutsourcingCostAmount = null;
        backup.OutsourcingCurrency = null;
        await db.SaveChangesAsync();
        result = await service.SetAsync(actor, first.RoutePlanId, first.RoutePlanRouteId,
            new() { Outsourced = false, LastUpdatedAt = Offset(first.RoutePlan.UpdatedAt) });
        Assert.Null(Assert.Single(result.FinancialPlan.Routes).TotalOutsourcingCost);
        Assert.Equal(1, Assert.Single(result.FinancialPlan.Routes).UnpricedSegments);
    }

    [Theory]
    [InlineData(null, "EUR")]
    [InlineData("-1", "EUR")]
    [InlineData("1.001", "EUR")]
    [InlineData("10000000000000000", "EUR")]
    [InlineData("1", null)]
    [InlineData("1", "ZZZ")]
    [InlineData("1", "EURO")]
    public void CostValidation_RejectsMissingInvalidAndUnrepresentableMoney(string? amount, string? currency)
    {
        var request = new Outsourcing.Request { Outsourced = true, TransportProviderId = Guid.NewGuid(), ServiceType = "AIR_CHARTER",
            CostAmount = amount is null ? null : decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture), Currency = currency,
            LastUpdatedAt = DateTimeOffset.UtcNow };
        Assert.False(new Outsourcing.Validator.RequestValidator().Validate(request).IsValid);
        request.CostAmount = 0m;
        request.Currency = "EUR";
        Assert.True(new Outsourcing.Validator.RequestValidator().Validate(request).IsValid);
    }

    private static Outsourcing.Service Service(AppDbContext db) => new(db, new Outsourcing.Validator.RequestValidator(),
        new Pegaxus.Service.NotificationService.Service(db, new NoopNotificationRealtimeService()), NullLogger<Outsourcing.Service>.Instance);

    [OutsourcingPostgreSqlFact]
    public async Task PostgreSql_MigrationConcurrencyAndRollback_PreserveAggregateAndOutbox()
    {
        var schema = "uc42_" + Guid.NewGuid().ToString("N");
        var connection = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("PEGAXUS_TEST_CONNECTION_STRING")) { SearchPath = schema };
        await using var admin = new NpgsqlConnection(connection.ConnectionString);
        await admin.OpenAsync();
        await new NpgsqlCommand($"CREATE SCHEMA {schema}", admin).ExecuteNonQueryAsync();
        try
        {
            var sourceBuilder = new NpgsqlDataSourceBuilder(connection.ConnectionString);
            var translator = new EnumTranslator();
            foreach (var type in typeof(ProviderStatus).Assembly.GetTypes().Where(type => type.IsEnum && type.Namespace == typeof(ProviderStatus).Namespace))
                sourceBuilder.MapEnum(type, translator.TranslateTypeName(type.Name), translator);
            await using var source = sourceBuilder.Build();
            var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(source).Options;
            await using var db = new AppDbContext(options);
            var script = db.Database.GenerateCreateScript();
            var oldScript = script.Replace("'CANCELLED', 'PENDING_LEGAL_REVIEW', 'PENDING_MANAGER_APPROVAL', 'REJECTED_BY_MANAGER', 'APPROVED'", "'CANCELLED'");
            Assert.NotEqual(script, oldScript);
            await new NpgsqlCommand(oldScript, admin).ExecuteNonQueryAsync();
            await new NpgsqlCommand("ALTER TABLE \"RoutePlanRoute\" DROP COLUMN service_type, DROP COLUMN provider_reference, DROP COLUMN outsourcing_cost_amount, DROP COLUMN outsourcing_currency", admin).ExecuteNonQueryAsync();
            var generator = db.GetService<IMigrationsSqlGenerator>();
            async Task Apply(IReadOnlyList<Microsoft.EntityFrameworkCore.Migrations.Operations.MigrationOperation> operations)
            {
                foreach (var command in generator.Generate(operations, db.Model))
                    await new NpgsqlCommand(command.CommandText, admin).ExecuteNonQueryAsync();
            }
            await Apply(new AddSegmentOutsourcing().UpOperations);
            await Apply(new AddSegmentOutsourcing().DownOperations);
            await Apply(new AddSegmentOutsourcing().UpOperations);
            var (actor, segment, provider) = await SeedAsync(db);
            var secondRoute = new RoutePlan { RoutePlanId = Guid.NewGuid(), RoutePlanCode = "RP-2", TransportPlan = segment.RoutePlan.TransportPlan,
                UpdatedAt = segment.UpdatedAt, CreatedAt = segment.CreatedAt, Status = RoutePlanStatus.APPROVED };
            var other = new RoutePlanRoute { RoutePlanRouteId = Guid.NewGuid(), RoutePlan = secondRoute, Route = segment.Route,
                SequenceNo = 1, UpdatedAt = segment.UpdatedAt, CreatedAt = segment.CreatedAt };
            db.RoutePlanRoutes.Add(other);
            await db.SaveChangesAsync();
            await using var first = new AppDbContext(options);
            await using var second = new AppDbContext(options);
            await Task.WhenAll(Service(first).SetAsync(actor, segment.RoutePlanId, segment.RoutePlanRouteId, Assign(segment, provider)),
                Service(second).SetAsync(actor, other.RoutePlanId, other.RoutePlanRouteId, Assign(other, provider)));
            Assert.Single(await db.TransportPlanProviders.ToListAsync());
            Assert.Equal(2, await db.OutboxMessages.CountAsync());
            Assert.Equal(2, await db.CatalogAuditLogs.CountAsync());
            Assert.Equal(RoutePlanStatus.PENDING_LEGAL_REVIEW, (await db.RoutePlans.AsNoTracking().SingleAsync(item => item.RoutePlanId == secondRoute.RoutePlanId)).Status);

            await db.Entry(segment.RoutePlan).ReloadAsync();
            var remove = new Outsourcing.Request { Outsourced = false, LastUpdatedAt = Offset(segment.RoutePlan.UpdatedAt) };
            await using var third = new AppDbContext(options);
            await using var fourth = new AppDbContext(options);
            async Task<bool> Remove(AppDbContext context)
            {
                try { await Service(context).SetAsync(actor, segment.RoutePlanId, segment.RoutePlanRouteId, remove); return true; }
                catch (ConflictException error) { Assert.Equal("ROUTE_PLAN_MODIFIED", error.ErrorCode); return false; }
            }
            Assert.Single((await Task.WhenAll(Remove(third), Remove(fourth))).Where(success => success));
            Assert.Single(await db.TransportPlanProviders.ToListAsync());
            Assert.Equal(3, await db.OutboxMessages.CountAsync());
            Assert.Equal(3, await db.CatalogAuditLogs.CountAsync());
            var retained = await db.RoutePlanRoutes.AsNoTracking().SingleAsync(item => item.RoutePlanRouteId == other.RoutePlanRouteId);
            Assert.Equal(1500.25m, retained.OutsourcingCostAmount);
            Assert.Equal("EUR", retained.OutsourcingCurrency);

            await db.Entry(secondRoute).ReloadAsync();
            await new NpgsqlCommand("""
                CREATE FUNCTION reject_outsourcing_audit() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'UC42 rollback check'; END; $$;
                CREATE TRIGGER reject_outsourcing_audit BEFORE INSERT ON "CatalogAuditLog"
                FOR EACH ROW EXECUTE FUNCTION reject_outsourcing_audit();
                """, admin).ExecuteNonQueryAsync();
            await using (var failedDb = new AppDbContext(options))
                await Assert.ThrowsAsync<DbUpdateException>(() => Service(failedDb).SetAsync(actor, other.RoutePlanId, other.RoutePlanRouteId,
                    new() { Outsourced = false, LastUpdatedAt = Offset(secondRoute.UpdatedAt) }));
            Assert.Equal(provider.TransportProviderId, (await db.RoutePlanRoutes.AsNoTracking().SingleAsync(item => item.RoutePlanRouteId == other.RoutePlanRouteId)).TransportProviderId);
            Assert.Equal(1500.25m, (await db.RoutePlanRoutes.AsNoTracking().SingleAsync(item => item.RoutePlanRouteId == other.RoutePlanRouteId)).OutsourcingCostAmount);
            Assert.Single(await db.TransportPlanProviders.ToListAsync());
            Assert.Equal(3, await db.OutboxMessages.CountAsync());
            Assert.Equal(3, await db.Notifications.CountAsync());
            Assert.Equal(3, await db.CatalogAuditLogs.CountAsync());
            await new NpgsqlCommand("DROP TRIGGER reject_outsourcing_audit ON \"CatalogAuditLog\"; DROP FUNCTION reject_outsourcing_audit()", admin).ExecuteNonQueryAsync();
            await using var finalDb = new AppDbContext(options);
            await Service(finalDb).SetAsync(actor, other.RoutePlanId, other.RoutePlanRouteId,
                new() { Outsourced = false, LastUpdatedAt = Offset(secondRoute.UpdatedAt) });
            Assert.Empty(await db.TransportPlanProviders.ToListAsync());
            Assert.Equal(0, await db.DocumentVersions.Where(item => item.ComplianceDocumentId == Guid.Empty)
                .OrderByDescending(item => item.VersionNo).Take(1)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.DocumentVersionId, item => item.DocumentVersionId)));
            await CheckOutboxClaimsAsync(options);
            await CheckRequirementReferenceRaceAsync(options, actor);
        }
        finally { await new NpgsqlCommand($"DROP SCHEMA {schema} CASCADE", admin).ExecuteNonQueryAsync(); }
    }

    private static async Task CheckOutboxClaimsAsync(DbContextOptions<AppDbContext> options)
    {
        await using var db = new AppDbContext(options);
        await db.OutboxMessages.ExecuteDeleteAsync();
        var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        for (var index = 0; index < 26; index++)
        {
            Pegaxus.Service.OutboxService.OutboxQueue.AddEmail(db,
                new() { To = "outbox@example.com", Subject = index.ToString(), Body = "Test" }, now);
            var message = db.ChangeTracker.Entries<OutboxMessage>().Last().Entity;
            message.Status = index switch { 20 or 23 => "PROCESSING", 21 => "FAILED", 24 => "DEAD", 25 => "PROCESSED", _ => "PENDING" };
            if (index is 20 or 23) message.LockedAt = index == 20 ? now.AddMinutes(-11) : now;
            if (index == 22) message.AvailableAt = now.AddHours(1);
        }
        await db.SaveChangesAsync();
        var mail = new ConcurrentMail();
        await using var services = new ServiceCollection().AddScoped(_ => new AppDbContext(options))
            .AddSingleton<Pegaxus.Service.MailService.IService>(mail).BuildServiceProvider();
        var batch = typeof(TransactionalOutboxWorker).GetMethod("ProcessBatchAsync",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        async Task RunBatchAsync()
        {
            using var worker = new TransactionalOutboxWorker(services.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<TransactionalOutboxWorker>.Instance);
            await (Task)batch.Invoke(worker, [CancellationToken.None])!;
        }
        await RunBatchAsync();
        Assert.Equal(20, mail.Subjects.Count);
        await Task.WhenAll(RunBatchAsync(), RunBatchAsync());
        Assert.Equal(22, mail.Subjects.Count);
        Assert.Equal(22, mail.Subjects.Distinct().Count());
        Assert.DoesNotContain("22", mail.Subjects);
        Assert.DoesNotContain("23", mail.Subjects);
        Assert.DoesNotContain("24", mail.Subjects);
        Assert.DoesNotContain("25", mail.Subjects);
        Assert.Equal(23, await db.OutboxMessages.CountAsync(message => message.Status == "PROCESSED"));
    }

    private static async Task CheckRequirementReferenceRaceAsync(DbContextOptions<AppDbContext> options, Guid actorId)
    {
        await using var seed = new AppDbContext(options);
        var requirement = new ComplianceRequirement
        {
            ComplianceRequirementId = Guid.NewGuid(), RequirementCode = "RACE", RequirementName = "Race check",
            Status = ComplianceRequirementStatus.ACTIVE, Category = ComplianceRequirementCategory.Other
        };
        var template = new DossierTemplate { DossierTemplateId = Guid.NewGuid(), TemplateCode = "RACE", TemplateName = "Race check" };
        seed.AddRange(requirement, template);
        await seed.SaveChangesAsync();
        await using var writer = new AppDbContext(options);
        await using var writeTransaction = await writer.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        Assert.True(await writer.ComplianceRequirements.AnyAsync(item => item.ComplianceRequirementId == requirement.ComplianceRequirementId && !item.IsDeleted));
        writer.DossierTemplateItems.Add(new DossierTemplateItem
        {
            DossierTemplateItemId = Guid.NewGuid(), DossierTemplateId = template.DossierTemplateId,
            ComplianceRequirementId = requirement.ComplianceRequirementId, SequenceNo = 1
        });
        await writer.SaveChangesAsync();
        await using var deleter = new AppDbContext(options);
        var deleteError = await Record.ExceptionAsync(() =>
            new Pegaxus.Service.ComplianceRequirementService.Service(deleter, null!, null!, null!)
                .DeleteAsync(requirement.ComplianceRequirementId, actorId));
        var writeError = await Record.ExceptionAsync(() => writeTransaction.CommitAsync());
        Assert.True(deleteError is not null || writeError is not null, "Deletion and reference insertion must not both commit.");
        var conflict = deleteError ?? writeError;
        Assert.Equal(PostgresErrorCodes.SerializationFailure,
            Assert.IsType<PostgresException>(conflict is DbUpdateException update ? update.InnerException : conflict).SqlState);
        await using var check = new AppDbContext(options);
        Assert.False(await check.DossierTemplateItems.AnyAsync(item =>
            item.ComplianceRequirementId == requirement.ComplianceRequirementId && item.ComplianceRequirement.IsDeleted));
    }

    [Theory]
    [InlineData(PostgresErrorCodes.SerializationFailure, false)]
    [InlineData(PostgresErrorCodes.SerializationFailure, true)]
    [InlineData(PostgresErrorCodes.DeadlockDetected, false)]
    [InlineData(PostgresErrorCodes.DeadlockDetected, true)]
    public async Task PostgreSqlWriteConflicts_Return409WithoutDatabaseDetails(string sqlState, bool wrapped)
    {
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var postgres = new PostgresException("Database internals", "ERROR", "ERROR", sqlState);
        Exception error = wrapped ? new DbUpdateException("Database internals", postgres) : postgres;
        await new GlobalExceptionHandlerMiddleware(NullLogger<GlobalExceptionHandlerMiddleware>.Instance)
            .InvokeAsync(context, _ => Task.FromException(error));
        Assert.Equal(409, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        var response = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.Contains("CONCURRENT_UPDATE", response);
        Assert.DoesNotContain("Database internals", response);
    }

    private sealed class ConcurrentMail : Pegaxus.Service.MailService.IService
    {
        public System.Collections.Concurrent.ConcurrentBag<string> Subjects { get; } = [];
        public Task SendMail(Pegaxus.Service.MailService.MailContent mailContent, CancellationToken cancellationToken = default)
        {
            Subjects.Add(mailContent.Subject);
            return Task.CompletedTask;
        }
    }

    private sealed class EnumTranslator : INpgsqlNameTranslator
    {
        public string TranslateTypeName(string name) => new NpgsqlSnakeCaseNameTranslator().TranslateTypeName(name);
        public string TranslateMemberName(string name) => name;
    }

    private static DateTimeOffset Offset(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static Outsourcing.Request Assign(RoutePlanRoute segment, TransportProvider provider) => new()
    {
        Outsourced = true, TransportProviderId = provider.TransportProviderId, ServiceType = " AIR_CHARTER ",
        ProviderReference = " AWB-123 ", Note = "Flight note", LastUpdatedAt = Offset(segment.RoutePlan.UpdatedAt),
        CostAmount = 1500.25m, Currency = " eur "
    };

    private static async Task<(Guid Actor, RoutePlanRoute Segment, TransportProvider Provider)> SeedAsync(AppDbContext db)
    {
        var (manager, _) = await TestSupport.AddLogisticsManagerAsync(db);
        var (_, coordinator) = await TestSupport.AddLogisticsManagerAsync(db);
        coordinator.Role = EmployeeRole.FLEET_ROUTE_COORDINATOR;
        var (_, customer) = await TestSupport.AddCustomerAsync(db);
        var (booking, _) = await TestSupport.AddPendingBookingAsync(db, customer);
        var now = new DateTime(DateTime.UtcNow.Ticks / 10 * 10, DateTimeKind.Unspecified);
        var plan = new TransportPlan
        {
            TransportPlanId = Guid.NewGuid(), PlanCode = "TP-1", CreatedAt = now, UpdatedAt = now,
            Transport = new Transport { TransportId = Guid.NewGuid(), TransportCode = "TR-1", Booking = booking, CreatedAt = now, UpdatedAt = now }
        };
        var routePlan = new RoutePlan { RoutePlanId = Guid.NewGuid(), RoutePlanCode = "RP-1", TransportPlan = plan, CreatedAt = now, UpdatedAt = now };
        var segment = new RoutePlanRoute
        {
            RoutePlanRouteId = Guid.NewGuid(), RoutePlan = routePlan, SequenceNo = 1, CreatedAt = now, UpdatedAt = now,
            Route = new Route
            {
                RouteId = Guid.NewGuid(), RouteCode = "AIR-1", TransportMode = TransportMode.AIR, CreatedAt = now, UpdatedAt = now,
                FromLocation = new Location { LocationId = Guid.NewGuid(), LocationName = "Frankfurt", Country = "DE", CreatedAt = now, UpdatedAt = now },
                ToLocation = new Location { LocationId = Guid.NewGuid(), LocationName = "London", Country = "GB", CreatedAt = now, UpdatedAt = now }
            }
        };
        var provider = new TransportProvider { TransportProviderId = Guid.NewGuid(), ProviderCode = "PRV-1", ProviderName = "Lufthansa Cargo",
            SupportedModes = "road, air", Status = ProviderStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
        db.AddRange(segment, provider, new EmployeeTransportPlan { TransportPlan = plan, Employee = coordinator, AssignedAt = now });
        await db.SaveChangesAsync();
        return (manager.AccountId, segment, provider);
    }
}

public sealed class OutsourcingPostgreSqlFactAttribute : FactAttribute
{
    public OutsourcingPostgreSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PEGAXUS_TEST_CONNECTION_STRING")))
            Skip = "Set PEGAXUS_TEST_CONNECTION_STRING to run the isolated PostgreSQL schema check.";
    }
}
