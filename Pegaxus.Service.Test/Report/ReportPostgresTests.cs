using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pegaxus.Repository;
using Pegaxus.Repository.Enum;

namespace Pegaxus.Service.Test;

[Collection("PostgresSharedDatabase")]
public class ReportPostgresTests
{
    [PostgresFact]
    public async Task Postgres_EveryReportAndEveryFilterTranslatesToSql()
    {
        var settings = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("PEGAXUS_TEST_CONNECTION_STRING"))
        { Database = "pegaxus_report_test_" + Guid.NewGuid().ToString("N") };
        var builder = new NpgsqlDataSourceBuilder(settings.ConnectionString); var translator = new FinancialReportPostgresTests.EnumTranslator();
        foreach (var type in typeof(InvoiceStatus).Assembly.GetTypes().Where(x => x.IsEnum && x.Namespace == typeof(InvoiceStatus).Namespace))
            builder.MapEnum(type, translator.TranslateTypeName(type.Name), translator);
        await using var source = builder.Build();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseTestNpgsql(source).Options;
        await using var seed = new AppDbContext(options);
        try
        {
            await seed.Database.EnsureCreatedAsync();
            var s = await ReportTests.SeedOnTimeAsync(seed);
            var incidents = await ReportTests.SeedIncidentsOnAsync(s.F);
            await using var db = new AppDbContext(options);
            var service = s.F.For(db); var manager = s.F.ManagerAccount.AccountId;

            var onTime = await service.GetOnTimeAsync(manager, ReportTests.OnTime());
            // The six trips of the scenario plus the on-time trip added for the incident data.
            Assert.Equal((7L, 2), (onTime.Kpi.TotalTransports.Value, onTime.Items.Count));
            Assert.Equal(71.4m, onTime.Kpi.OnTimeRate.Value);
            Assert.Equal(("Eurotunnel", 1), (onTime.ByProvider[0].ProviderName, onTime.ByProvider[0].Total));
            Assert.Equal(new[] { ("UNKNOWN", 1), ("VEHICLE_INCIDENT", 1) }, onTime.DelayCauses.Select(item => (item.Cause, item.Count)));
            var filtered = ReportTests.OnTime(); filtered.OriginCountry = "france"; filtered.DestinationCountry = "GERMANY"; filtered.TransportProviderId = s.Provider.TransportProviderId;
            filtered.DriverEmployeeId = s.Driver.EmployeeId;
            Assert.Equal(0L, (await service.GetOnTimeAsync(manager, filtered)).Kpi.TotalTransports.Value);
            filtered.DriverEmployeeId = null;
            Assert.Equal(1L, (await service.GetOnTimeAsync(manager, filtered)).Kpi.TotalTransports.Value);
            var byRoute = ReportTests.OnTime(); byRoute.RouteId = s.OtherRoute.RouteId; byRoute.Bucket = "ON_TIME";
            Assert.Equal(s.OnTime60.TransportCode, Assert.Single((await service.GetOnTimeAsync(manager, byRoute)).Items).TransportCode);

            var report = await service.GetIncidentClaimAsync(manager, ReportTests.IncidentClaim());
            Assert.Equal((3L, 1L), (report.Kpi.TotalIncidents.Value, report.Kpi.TotalClaims.Value));
            Assert.Equal(700_000L, report.Kpi.TotalEmergencyCost.Value);
            var narrowed = ReportTests.IncidentClaim(); narrowed.IncidentCategory = "BOTH"; narrowed.Severity = "HIGH"; narrowed.VehicleId = s.F.Vehicle.VehicleId; narrowed.HorseId = s.F.Horse.HorseId;
            narrowed.ClaimType = "DAMAGE"; narrowed.OriginCountry = "France"; narrowed.TransportProviderId = null;
            Assert.Equal(incidents.IncidentCode, Assert.Single((await service.GetIncidentClaimAsync(manager, narrowed)).Items).IncidentCode);
            narrowed.RouteId = s.F.Route.RouteId;
            Assert.Equal(0, (await service.GetIncidentClaimAsync(manager, narrowed)).Items.Count);

            var transit = ReportTests.Transit(); transit.DelayFilter = "EARLY";
            var transitReport = await service.GetTransitTimeAsync(manager, transit);
            Assert.Equal((1L, 0), (transitReport.Kpi.TotalTransports.Value, transitReport.Items.Count));

            foreach (var type in new[] { "on-time-delivery", "incident-claim-frequency", "transit-time" })
            {
                Assert.NotEmpty((await service.ExportAsync(manager, type, new() { Format = "xlsx", DateFrom = new(2026, 10, 1), DateTo = new(2026, 10, 31) })).Content);
                Assert.NotEmpty((await service.ExportAsync(manager, type, new() { Format = "pdf", DateFrom = new(2026, 10, 1), DateTo = new(2026, 10, 31) })).Content);
            }
        }
        finally { await seed.Database.EnsureDeletedAsync(); }
    }
}
