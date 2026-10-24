using Microsoft.EntityFrameworkCore;
using Npgsql;
using Npgsql.NameTranslation;
using Pegaxus.Repository;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;

namespace Pegaxus.Service.Test;

[Collection("PostgresSharedDatabase")]
public class FinancialReportPostgresTests
{
    [PostgresFact]
    public async Task Postgres_TheQueriesTranslate_AndConcurrentFirstViewsLeaveOneReport()
    {
        var settings = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("PEGAXUS_TEST_CONNECTION_STRING"))
        { Database = "pegaxus_financial_test_" + Guid.NewGuid().ToString("N") };
        var builder = new NpgsqlDataSourceBuilder(settings.ConnectionString); var translator = new EnumTranslator();
        foreach (var type in typeof(InvoiceStatus).Assembly.GetTypes().Where(x => x.IsEnum && x.Namespace == typeof(InvoiceStatus).Namespace))
            builder.MapEnum(type, translator.TranslateTypeName(type.Name), translator);
        await using var source = builder.Build();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseTestNpgsql(source).Options;
        await using var seed = new AppDbContext(options);
        try
        {
            await seed.Database.EnsureCreatedAsync();
            var f = await FinancialReportTests.Fixture.CreateAsync(seed);
            var final = f.AddInvoice(InvoiceType.FINAL, InvoiceStatus.PAID, 1_000); f.AddReceipt(final, 900);
            f.AddInvoice(InvoiceType.SUPPLEMENTARY, InvoiceStatus.ISSUED, 200);
            f.AddEmergencyCost("INC-20261016-0001", 500); f.AddDossier();
            await seed.SaveChangesAsync();

            // Several first views at the same time: the unique transport key keeps a single report and nobody fails.
            var views = await Task.WhenAll(Enumerable.Range(0, 3).Select(async _ =>
            {
                await using var context = new AppDbContext(options);
                return await f.For(context).GetAsync(f.ManagerAccount.AccountId, f.Transport.TransportId, false);
            }));
            Assert.Single(views.Select(view => view.ReportCode).Distinct());
            await using (var verify = new AppDbContext(options)) Assert.Single(await verify.FinancialReports.ToListAsync());
            var report = views[0];
            Assert.Equal((1_200L, 900L, 500L, 300L), (report.TotalInvoicedAmount, report.TotalPaidAmount, report.TotalEmergencyCost, report.OutstandingAmount));
            Assert.Equal("RECEIPT_AMOUNT_MISMATCH", Assert.Single(report.Reconciliation.Items).Issue); Assert.Equal(10, report.SuggestedItems.Count);

            // Two notes saved with the same version: one wins, the other is told the report changed.
            var outcomes = await Task.WhenAll(new[] { "first note", "second note" }.Select(async note =>
            {
                await using var context = new AppDbContext(options);
                try { await f.For(context).UpdateNoteAsync(f.ManagerAccount.AccountId, f.Transport.TransportId, new() { LastUpdatedAt = report.UpdatedAt, SummaryNote = note }); return (Exception?)null; }
                catch (Exception error) { return error; }
            }));
            Assert.Single(outcomes, error => error is null);
            Assert.Equal("FINANCIAL_REPORT_MODIFIED", Assert.IsType<ConflictException>(Assert.Single(outcomes, error => error is not null)).ErrorCode);
            await using var final2 = new AppDbContext(options);
            var after = await f.For(final2).RefreshAsync(f.ManagerAccount.AccountId, f.Transport.TransportId);
            Assert.NotNull(after.SummaryNote); Assert.Equal(report.ReportCode, after.ReportCode);
        }
        finally { await seed.Database.EnsureDeletedAsync(); }
    }

    private sealed class EnumTranslator : INpgsqlNameTranslator
    {
        public string TranslateTypeName(string name) => new NpgsqlSnakeCaseNameTranslator().TranslateTypeName(name);
        public string TranslateMemberName(string name) => name;
    }
}
