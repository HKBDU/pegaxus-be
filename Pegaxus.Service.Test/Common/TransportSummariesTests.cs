using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Common;

namespace Pegaxus.Service.Test;

public class TransportSummariesTests
{
    private static readonly DateTime T0 = new(2030, 1, 1, 8, 0, 0, DateTimeKind.Unspecified);

    private static HorseHealthLog Log(Guid transportId, Guid horseId, HorseHealthStatus status, int minutes, int createdSeconds = 0) => new()
    {
        HorseHealthLogId = Guid.NewGuid(), TransportId = transportId, HorseId = horseId, EmployeeId = Guid.NewGuid(),
        HealthStatus = status, LogType = HorseHealthLogType.ROUTINE_CHECK,
        CheckedAt = T0.AddMinutes(minutes), CreatedAt = T0.AddSeconds(createdSeconds), UpdatedAt = T0
    };

    [Fact]
    public async Task LatestPerHorse_KeepsTheNewestLogOfEachHorseOfTheTransport()
    {
        await using var db = TestSupport.CreateDb();
        var transport = Guid.NewGuid();
        var (horseA, horseB) = (Guid.NewGuid(), Guid.NewGuid());
        var oldA = Log(transport, horseA, HorseHealthStatus.NORMAL, 0);
        var newA = Log(transport, horseA, HorseHealthStatus.CRITICAL, 30);
        var onlyB = Log(transport, horseB, HorseHealthStatus.AT_RISK, 10);
        var tieOld = Log(transport, Guid.NewGuid(), HorseHealthStatus.NORMAL, 5, createdSeconds: 1);
        var tieNew = Log(transport, tieOld.HorseId, HorseHealthStatus.OBSERVATION_REQUIRED, 5, createdSeconds: 9);
        var otherTransport = Log(Guid.NewGuid(), horseA, HorseHealthStatus.NORMAL, 99);
        db.AddRange(oldA, newA, onlyB, tieOld, tieNew, otherTransport);
        await db.SaveChangesAsync();

        var latest = await db.HorseHealthLogs.Where(log => log.TransportId == transport).LatestPerHorse().ToListAsync();

        Assert.Equal(3, latest.Count);
        Assert.Contains(latest, log => log.HorseHealthLogId == newA.HorseHealthLogId);
        Assert.Contains(latest, log => log.HorseHealthLogId == onlyB.HorseHealthLogId);
        Assert.Contains(latest, log => log.HorseHealthLogId == tieNew.HorseHealthLogId);
    }

    [Fact]
    public void Summarize_CountsLatestStatusesAndHorsesWithoutAnyLog()
    {
        var summary = TransportSummaries.Summarize(
            [HorseHealthStatus.NORMAL, HorseHealthStatus.NORMAL, HorseHealthStatus.AT_RISK, HorseHealthStatus.CRITICAL], 6);
        Assert.Equal(new TransportSummaries.HealthSummary(2, 0, 1, 1, 2, 6), summary);
        Assert.Equal(new TransportSummaries.HealthSummary(0, 0, 0, 0, 3, 3), TransportSummaries.Summarize([], 3));
    }

    [Fact]
    public async Task ActiveIncidents_AreReportedOrPendingOfTheTransport()
    {
        await using var db = TestSupport.CreateDb();
        var transport = Guid.NewGuid();
        IncidentReport Incident(Guid? transportId, IncidentStatus status) => new()
        {
            IncidentReportId = Guid.NewGuid(), TransportId = transportId, Status = status, IncidentCode = Guid.NewGuid().ToString("N"),
            Title = "t", Description = "d", OccurredAt = T0, ReportedAt = T0, CreatedAt = T0, UpdatedAt = T0
        };
        var reported = Incident(transport, IncidentStatus.REPORTED);
        var pending = Incident(transport, IncidentStatus.PENDING);
        db.AddRange(reported, pending, Incident(transport, IncidentStatus.RESOLVED), Incident(transport, IncidentStatus.CANCELLED),
            Incident(Guid.NewGuid(), IncidentStatus.REPORTED), Incident(null, IncidentStatus.REPORTED));
        await db.SaveChangesAsync();

        var active = await db.IncidentReports.Active(transport).Select(item => item.IncidentReportId).ToListAsync();

        Assert.Equal(new[] { reported.IncidentReportId, pending.IncidentReportId }.Order(), active.Order());
    }

    [Fact]
    public void LatestNote_PrefersTheGeneralNoteThenTheBehaviorNote()
    {
        Assert.Null(TransportSummaries.LatestNote(null));
        Assert.Equal("general", TransportSummaries.LatestNote(new() { GeneralNote = "general", BehaviorNote = "behavior" }));
        Assert.Equal("behavior", TransportSummaries.LatestNote(new() { GeneralNote = " ", BehaviorNote = "behavior" }));
        Assert.Null(TransportSummaries.LatestNote(new()));
    }

    [Theory]
    [InlineData(EmployeeRole.LOGISTICS_MANAGER, false, true)]
    [InlineData(EmployeeRole.FLEET_ROUTE_COORDINATOR, false, true)]
    [InlineData(EmployeeRole.DRIVER, true, true)]
    [InlineData(EmployeeRole.ESCORT, false, false)]
    [InlineData(EmployeeRole.TRANSPORT_SPECIALIST, true, false)]
    [InlineData(null, true, false)]
    public void MayCreate_AllowsManagersAndAssignedCrewOnly(EmployeeRole? role, bool assigned, bool expected) =>
        Assert.Equal(expected, HandoverRules.MayCreate(role, assigned));

    [Theory]
    [InlineData(HandoverStatus.PLANNED, true, true, "START,COMPLETE,CANCEL")]
    [InlineData(HandoverStatus.IN_PROGRESS, true, true, "COMPLETE,CANCEL")]
    [InlineData(HandoverStatus.PLANNED, true, false, "START,COMPLETE")]
    [InlineData(HandoverStatus.IN_PROGRESS, false, false, "")]
    [InlineData(HandoverStatus.COMPLETED, true, true, "")]
    [InlineData(HandoverStatus.CANCELLED, true, true, "")]
    public void AvailableActions_FollowStatusAndRole(HandoverStatus status, bool canOperate, bool manager, string expected) =>
        Assert.Equal(expected, string.Join(',', HandoverRules.AvailableActions(new() { Status = status }, canOperate, manager)));
}
