using Microsoft.EntityFrameworkCore;
using Npgsql;
using Npgsql.NameTranslation;
using Pegaxus.Repository;
using Pegaxus.Repository.Enum;

namespace Pegaxus.Service.Test;

[Collection("PostgresSharedDatabase")]
public class JourneyProgressPostgresTests
{
    [PostgresFact]
    public async Task PostgreSql_TranslatesAllJourneyQueriesAndPreservesReadOnlyCustomerVisibility()
    {
        var settings = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("PEGAXUS_TEST_CONNECTION_STRING")!)
        {
            Database = $"pegaxus_journey_test_{Guid.NewGuid():N}"
        };
        var builder = new NpgsqlDataSourceBuilder(settings.ConnectionString);
        var translator = new EnumTranslator();
        foreach (var type in typeof(AccountStatus).Assembly.GetTypes().Where(type => type.IsEnum && type.Namespace == typeof(AccountStatus).Namespace))
            builder.MapEnum(type, translator.TranslateTypeName(type.Name), translator);
        await using var source = builder.Build();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseTestNpgsql(source).Options);
        try
        {
            await db.Database.EnsureCreatedAsync();
            var f = await JourneyProgressTests.Fixture.CreateAsync(db);
            db.ChangeTracker.Clear();
            var ongoing = await f.Service.GetOngoingAsync(f.Manager.AccountId, new() { Search = "tEsT", HasActiveIncident = true, PageSize = 1 });
            Assert.Equal(1, ongoing.TotalItems);
            Assert.Equal(f.Transport.TransportId, Assert.Single(ongoing.Items).TransportId);
            Assert.Empty((await f.Service.GetOngoingAsync(f.Manager.AccountId, new() { PageIndex = 1, SortBy = "departureAt:desc" })).Items);
            Assert.Single((await f.Service.GetOngoingAsync(f.Manager.AccountId, new() { SortBy = "transportCode:asc" })).Items);
            Assert.Equal("ESCORT", Assert.Single((await f.Service.GetAssignedAsync(f.Escort.AccountId, new())).Items).MyRole);
            var overview = await f.Service.GetOverviewAsync(f.Customer.AccountId, f.Transport.TransportId);
            Assert.Equal(1, overview.HorseHealthSummary.Critical);
            Assert.Equal(1, overview.HorseHealthSummary.NoData);
            Assert.Empty(overview.CurrentSegment!.Vehicles);
            Assert.Null(overview.CurrentSegment.Driver?.Phone);
            Assert.All(overview.ActiveIncidents, item => Assert.Null(item.Description));
            var segments = await f.Service.GetSegmentsAsync(f.Driver.AccountId, f.Transport.TransportId);
            Assert.Equal(3, segments.Segments.Count);
            Assert.Equal(4, segments.HandoverBoundaries.Count);
            Assert.Single(segments.Segments[1].Vehicles);
            Assert.NotNull(segments.HandoverBoundaries[0].ExistingHandover);
            Assert.Empty(db.ChangeTracker.Entries());
            Assert.Equal(2, await db.HandoverRecords.CountAsync());
            Assert.Empty(await db.Notifications.ToListAsync());
            Assert.Empty(await db.OutboxMessages.ToListAsync());
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    private sealed class EnumTranslator : INpgsqlNameTranslator
    {
        public string TranslateTypeName(string name) => new NpgsqlSnakeCaseNameTranslator().TranslateTypeName(name);
        public string TranslateMemberName(string name) => name;
    }
}
