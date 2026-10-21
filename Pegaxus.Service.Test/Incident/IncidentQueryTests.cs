using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.IncidentService.Validator;

using static Pegaxus.Service.Test.HorseHealthLogTests;
using static Pegaxus.Service.Test.IncidentLifecycleTests;
using static Pegaxus.Service.Test.VehicleIncidentTests;

using Incidents = Pegaxus.Service.IncidentService;

namespace Pegaxus.Service.Test;

public class IncidentQueryTests
{
    [Theory]
    [InlineData(EmployeeRole.LOGISTICS_MANAGER, 3)]
    [InlineData(EmployeeRole.FLEET_ROUTE_COORDINATOR, 3)]
    [InlineData(EmployeeRole.TRANSPORT_SPECIALIST, 3)]
    [InlineData(EmployeeRole.DRIVER, 2)]
    [InlineData(EmployeeRole.ESCORT, 1)]
    public async Task Reads_EnforceScope_IncludeOwnReportsAfterAssignmentEnds_NeverAcceptIncidents(EmployeeRole role, int expected)
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db); var other = await Scenario.CreateAsync(db);
        AddIncident(db, f); AddIncident(db, other, reporter: f.Driver); var hidden = AddIncident(db, other); await db.SaveChangesAsync();
        var actor = new[] { f.Manager, f.Coordinator, f.Specialist, f.Driver, f.Escort }.Single(employee => employee.Role == role);
        var page = await f.Service.ListAsync(new(), actor.AccountId); Assert.Equal(expected, page.TotalItems); Assert.Equal(expected, page.Counts.Reported);
        foreach (var item in page.Items)
            Assert.Equal("REPORTED", Assert.IsType<Incidents.Response.Incident>(await f.Service.GetAsync(item.IncidentReportId, actor.AccountId)).Status);
        if (role is EmployeeRole.DRIVER or EmployeeRole.ESCORT)
            Assert.Equal("INCIDENT_NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(() => f.Service.GetAsync(hidden.IncidentReportId, actor.AccountId))).ErrorCode);
        Assert.All(db.IncidentReports, item => Assert.Equal(IncidentStatus.REPORTED, item.Status)); Assert.Empty(db.Notifications);
    }

    [Fact]
    public async Task List_FiltersAndPaginates_WhileCountsCoverEntireVisibleScope()
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db);
        var low = AddIncident(db, f, severity: IncidentSeverity.LOW); low.EmergencySettlementRequired = false;
        var high = AddIncident(db, f, IncidentStatus.PENDING); high.Category = IncidentCategory.HORSE_HEALTH;
        high.OccurredAt = Scenario.Now.Date; AddCost(db, high, f.Driver, EmergencyCostStatus.PENDING);
        var critical = AddIncident(db, f, severity: IncidentSeverity.CRITICAL); critical.OccurredAt = Scenario.Now.Date.AddDays(1).AddTicks(-10);
        AddCost(db, critical, f.Driver, EmergencyCostStatus.APPROVED);
        AddIncident(db, f, IncidentStatus.RESOLVED); AddIncident(db, f, IncidentStatus.CANCELLED); await db.SaveChangesAsync();
        var request = new Incidents.Request.ListRequest { Status = " REPORTED, PENDING ", TransportId = f.Transport.TransportId,
            OccurredFrom = DateOnly.FromDateTime(Scenario.Now), OccurredTo = DateOnly.FromDateTime(Scenario.Now), PageSize = 1 };
        var first = await f.Service.ListAsync(request, f.Manager.AccountId);
        Assert.Equal(new Incidents.Response.Counts(2, 1, 1, 2), first.Counts); Assert.Equal(3, first.TotalItems); Assert.Equal(3, first.TotalPages);
        Assert.Equal(critical.IncidentReportId, Assert.Single(first.Items).IncidentReportId);
        request.PageIndex = 2; var second = await f.Service.ListAsync(request, f.Manager.AccountId);
        Assert.Equal(high.IncidentReportId, Assert.Single(second.Items).IncidentReportId); Assert.Equal(first.Counts, second.Counts);
        request.PageIndex = 1; request.HasPendingCost = true;
        Assert.Equal(high.IncidentReportId, Assert.Single((await f.Service.ListAsync(request, f.Manager.AccountId)).Items).IncidentReportId);
        request.HasPendingCost = false; request.Severity = "CRITICAL";
        var approved = Assert.Single((await f.Service.ListAsync(request, f.Manager.AccountId)).Items); Assert.Equal(700, approved.ApprovedTotal);
        request.Severity = null; request.Category = "HORSE_HEALTH";
        Assert.Empty((await f.Service.ListAsync(request, f.Manager.AccountId)).Items);
        request.Category = null; request.EmergencySettlementRequired = false;
        Assert.Equal(low.IncidentReportId, Assert.Single((await f.Service.ListAsync(request, f.Manager.AccountId)).Items).IncidentReportId);
        request.PageIndex = 50; var empty = await f.Service.ListAsync(request, f.Manager.AccountId);
        Assert.Empty(empty.Items); Assert.Equal(1, empty.TotalItems); Assert.Equal(first.Counts, empty.Counts);
    }

    [Theory]
    [InlineData("incidentCode")]
    [InlineData("title")]
    [InlineData("transportCode")]
    [InlineData("reporter")]
    [InlineData("location")]
    [InlineData("licensePlate")]
    public async Task List_SearchesAllContractFieldsIgnoringCaseAndOuterSpaces(string field)
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db);
        var created = (await f.Service.ReportVehicleAsync(f.Report(), f.Driver.AccountId)).Value;
        var text = field switch { "incidentCode" => created.IncidentCode, "title" => created.Title,
            "transportCode" => f.Transport.TransportCode, "reporter" => f.Driver.FullName, "location" => f.Location.LocationName, _ => f.Vehicle.LicensePlate! };
        var page = await f.Service.ListAsync(new() { Search = "  " + text.ToUpperInvariant() + "  " }, f.Specialist.AccountId);
        Assert.Equal(created.IncidentReportId, Assert.Single(page.Items).IncidentReportId);
    }

    [Theory]
    [InlineData("severity:asc", false)]
    [InlineData("severity:desc", true)]
    [InlineData("occurredAt:asc", false)]
    [InlineData("occurredAt:desc", true)]
    [InlineData("status:asc", false)]
    [InlineData("status:desc", true)]
    [InlineData("updatedAt:asc", false)]
    [InlineData("updatedAt:desc", true)]
    public async Task List_SortsAllSupportedFields(string sort, bool descending)
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db);
        var first = AddIncident(db, f, severity: IncidentSeverity.LOW);
        var second = AddIncident(db, f, IncidentStatus.PENDING, IncidentSeverity.CRITICAL); second.OccurredAt = second.UpdatedAt = Scenario.Now.AddMinutes(1);
        await db.SaveChangesAsync(); var page = await f.Service.ListAsync(new() { SortBy = sort }, f.Manager.AccountId);
        Assert.Equal(descending ? second.IncidentReportId : first.IncidentReportId, page.Items[0].IncidentReportId);
    }

    [Fact]
    public async Task Detail_ActionsMatchLifecyclePermissions_AndCustomersCannotList()
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db); var item = AddIncident(db, f); await db.SaveChangesAsync();
        var manager = Assert.IsType<Incidents.Response.Incident>(await f.Service.GetAsync(item.IncidentReportId, f.Manager.AccountId));
        Assert.True(manager.CanResolve); Assert.Contains("RESOLVE", manager.AvailableActions); Assert.Contains("CANCEL_INCIDENT", manager.AvailableActions);
        var crew = Assert.IsType<Incidents.Response.Incident>(await f.Service.GetAsync(item.IncidentReportId, f.Escort.AccountId));
        Assert.Contains("SELF_RESOLVE", crew.AvailableActions); Assert.Contains("RAISE_SEVERITY", crew.AvailableActions);
        var specialist = Assert.IsType<Incidents.Response.Incident>(await f.Service.GetAsync(item.IncidentReportId, f.Specialist.AccountId));
        Assert.Empty(specialist.AvailableActions); Assert.False(specialist.CanResolve);
        AddCost(db, item, f.Driver, EmergencyCostStatus.PENDING); await db.SaveChangesAsync();
        manager = Assert.IsType<Incidents.Response.Incident>(await f.Service.GetAsync(item.IncidentReportId, f.Manager.AccountId));
        crew = Assert.IsType<Incidents.Response.Incident>(await f.Service.GetAsync(item.IncidentReportId, f.Driver.AccountId));
        Assert.False(manager.CanResolve); Assert.DoesNotContain("RESOLVE", manager.AvailableActions); Assert.DoesNotContain("SELF_RESOLVE", crew.AvailableActions);
        Assert.Equal("FORBIDDEN", (await Assert.ThrowsAsync<ForbiddenException>(() => f.Service.ListAsync(new(), f.CustomerAccountId))).ErrorCode);
    }

    [Theory]
    [InlineData("status")]
    [InlineData("category")]
    [InlineData("severity")]
    [InlineData("transportId")]
    [InlineData("search")]
    [InlineData("dateRange")]
    [InlineData("pageIndex")]
    [InlineData("pageSize")]
    [InlineData("overflow")]
    [InlineData("sortBy")]
    [InlineData("nullSort")]
    public void List_RejectsInvalidFilters(string field)
    {
        var request = new Incidents.Request.ListRequest();
        switch (field)
        {
            case "status": request.Status = "REPORTED,UNKNOWN"; break;
            case "category": request.Category = "UNKNOWN"; break;
            case "severity": request.Severity = "2"; break;
            case "transportId": request.TransportId = Guid.Empty; break;
            case "search": request.Search = new string('x', 201); break;
            case "dateRange": request.OccurredFrom = new(2026, 10, 21); request.OccurredTo = new(2026, 10, 20); break;
            case "pageIndex": request.PageIndex = 0; break;
            case "pageSize": request.PageSize = 101; break;
            case "overflow": request.PageIndex = int.MaxValue; break;
            case "nullSort": request.SortBy = null!; break;
            default: request.SortBy = "severity:desc,unknown:asc"; break;
        }
        Assert.False(new ListRequestValidator().Validate(request).IsValid);
    }
}
