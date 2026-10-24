using System.IO.Compression;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pegaxus.Repository;
using Pegaxus.Repository.Enum;
using static Pegaxus.Service.Test.AuditSupport;

namespace Pegaxus.Service.Test;

[Collection("PostgresSharedDatabase")]
public class AuditPostgresTests
{
    [PostgresFact]
    public async Task Postgres_TheArchiveTheAuditAndTheExportTranslateToSql()
    {
        var settings = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("PEGAXUS_TEST_CONNECTION_STRING"))
        { Database = "pegaxus_audit_test_" + Guid.NewGuid().ToString("N") };
        var builder = new NpgsqlDataSourceBuilder(settings.ConnectionString); var translator = new FinancialReportPostgresTests.EnumTranslator();
        foreach (var type in typeof(InvoiceStatus).Assembly.GetTypes().Where(x => x.IsEnum && x.Namespace == typeof(InvoiceStatus).Namespace))
            builder.MapEnum(type, translator.TranslateTypeName(type.Name), translator);
        await using var source = builder.Build();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseTestNpgsql(source).Options;
        await using var seed = new AppDbContext(options);
        try
        {
            await seed.Database.EnsureCreatedAsync();
            var f = await Fixture.CreateAsync(seed);
            var cancelled = f.R.AddTransport(null, status: TransportStatus.CANCELLED); cancelled.CancelledAt = D("2026-10-18 10:00");
            await seed.SaveChangesAsync();
            await using var db = new AppDbContext(options);
            var service = f.For(db);

            foreach (var text in new[] { "985120", "fr-2020", "pegasus", f.Transport.TransportCode.ToLower(), f.Transport.Booking.BookingCode[3..12], "test customer" })
                Assert.Equal(f.Transport.TransportCode, Assert.Single((await service.GetArchiveAsync(f.ManagerId, new() { Search = text })).Items).TransportCode);
            var archive = await service.GetArchiveAsync(f.ManagerId, new() { IncludeCancelled = true, HasClaim = false, OriginCountry = "france", CompletedFrom = new(2026, 10, 1), CompletedTo = new(2026, 10, 31), SortBy = "completedAt:asc" });
            Assert.Equal(new[] { f.Transport.TransportCode, cancelled.TransportCode }, archive.Items.Select(item => item.TransportCode));
            var withClaim = Assert.Single((await service.GetArchiveAsync(f.ManagerId, new() { HasClaim = true })).Items);
            Assert.Equal((1, 2), (withClaim.ClaimCount, withClaim.IncidentCount));

            var full = await service.GetAuditAsync(f.ManagerId, f.Transport.TransportId, new());
            Assert.Equal((2, 2, 3, 3, 2), (full.Booking!.Invoices.Count, full.Routing!.Primary!.Segments.Count, full.Operations!.Handovers.Count, full.Finance!.Invoices.Count, full.Compliance!.Dossiers[0].Requirements.Count));
            Assert.Equal("Slight fever, monitor", full.Horses![0].HealthLogs[1].VeterinarianNote);
            var customer = await service.GetAuditAsync(f.CustomerId, f.Transport.TransportId, new());
            Assert.Equal((1, 2, 1, 1), (customer.Booking!.Invoices.Count, customer.Operations!.Handovers.Count, customer.Operations.Incidents.Count, customer.Compliance!.Dossiers[0].Requirements.Count));
            Assert.Null(customer.Finance);

            var queued = await service.RequestExportAsync(f.ManagerId, f.Transport.TransportId, new() { Format = "ZIP", Mode = "INSURANCE" });
            await service.ProcessExportAsync(queued.ExportId);
            var status = await service.GetExportAsync(f.ManagerId, queued.ExportId);
            Assert.Equal("READY", status.Status);
            using (var zip = new ZipArchive(new MemoryStream(f.Files.Exports.Values.Last()))) Assert.Contains(zip.Entries, entry => entry.FullName == "claims.json");
            var pdf = await service.RequestExportAsync(f.ManagerId, f.Transport.TransportId, new() { Format = "PDF", Mode = "STANDARD" });
            await service.ProcessExportAsync(pdf.ExportId);
            Assert.Equal("READY", (await service.GetExportAsync(f.ManagerId, pdf.ExportId)).Status);
        }
        finally { await seed.Database.EnsureDeletedAsync(); }
    }
}
