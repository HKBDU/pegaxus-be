using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Npgsql.NameTranslation;
using Pegaxus.Repository;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;

namespace Pegaxus.Service.Test;

[Collection("PostgresSharedDatabase")]
public class HorseHealthPostgresTests
{
    [PostgresFact]
    public async Task PostgreSql_TranslatesLatestChecksFiltersAuthorizationAndEffectiveEscortWithoutWrites()
    {
        var settings = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("PEGAXUS_TEST_CONNECTION_STRING")!)
        {
            Database = $"pegaxus_horse_health_test_{Guid.NewGuid():N}"
        };
        var builder = new NpgsqlDataSourceBuilder(settings.ConnectionString);
        var translator = new EnumTranslator();
        foreach (var type in typeof(AccountStatus).Assembly.GetTypes().Where(type => type.IsEnum && type.Namespace == typeof(AccountStatus).Namespace))
            builder.MapEnum(type, translator.TranslateTypeName(type.Name), translator);
        await using var source = builder.Build();
        var commands = new ReadCommands();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseTestNpgsql(source).AddInterceptors(commands).Options);
        try
        {
            await db.Database.EnsureCreatedAsync();
            var f = await HorseHealthTests.Fixture.CreateAsync(db);
            var (foreign, _) = await TestSupport.AddCustomerAsync(db);
            f.AddAlternative(RouteType.BACKUP, RoutePlanStatus.ACTIVE);
            db.Add(PersonnelAssignmentTests.Segment(f.RoutePlan, 2));
            await db.SaveChangesAsync(); db.ChangeTracker.Clear(); commands.Sql.Clear();
            var overview = await f.Service.GetOverviewAsync(f.Transport.TransportId, f.Customer.AccountId);
            Assert.Equal(f.Driver.EmployeeId, overview.CurrentEscort!.EmployeeId); Assert.Null(overview.CurrentEscort.Phone);
            var horse = overview.Horses.Single(item => item.Horse.HorseId == f.Horse.HorseId);
            Assert.Equal(4, horse.LogCount); Assert.Equal(f.Latest.HorseHealthLogId, horse.LatestLog!.HorseHealthLogId);
            Assert.Equal("Restless", horse.LatestLog.LatestNote);
            Assert.Null(overview.Horses.Single(item => item.Horse.HorseId == f.UncheckedHorse.HorseId).LatestLog);
            var logs = await f.Service.GetLogsAsync(f.Transport.TransportId, f.Horse.HorseId,
                new() { LogType = "ROUTINE_CHECK,REST_STOP_CHECK", HealthStatus = "AT_RISK,CRITICAL", PageSize = 1, PageIndex = 1 }, f.Driver.AccountId);
            Assert.Equal(2, logs.TotalItems); Assert.Equal(2, logs.TotalPages);
            Assert.Equal(f.Logs[2].HorseHealthLogId, Assert.Single(logs.Items).HorseHealthLogId);
            var detail = await f.Service.GetAsync(f.Latest.HorseHealthLogId, f.Customer.AccountId);
            Assert.Null(detail.VeterinarianNote); Assert.Equal(f.Recorder.EmployeeId, detail.CheckedBy.EmployeeId);
            Assert.Equal(HorseHealthTests.Storage.SignedUrl, Assert.Single(detail.Photos).PhotoUrl);
            Assert.Equal("HEALTH_LOG_NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(() =>
                f.Service.GetAsync(f.Latest.HorseHealthLogId, foreign.AccountId))).ErrorCode);
            Assert.Single(f.Storage.Reads);
            Assert.NotEmpty(commands.Sql); Assert.All(commands.Sql, sql => Assert.StartsWith("SELECT", sql));
            Assert.Contains(commands.Sql, sql => sql.Contains("LIMIT") && sql.Contains("OFFSET"));
            Assert.Empty(db.ChangeTracker.Entries());
            Assert.Equal(4, await db.HorseHealthLogs.CountAsync());
            Assert.Equal(0, await db.Notifications.CountAsync()); Assert.Equal(0, await db.OutboxMessages.CountAsync());
        }
        finally { await db.Database.EnsureDeletedAsync(); }
    }

    private sealed class EnumTranslator : INpgsqlNameTranslator
    {
        public string TranslateTypeName(string name) => new NpgsqlSnakeCaseNameTranslator().TranslateTypeName(name);
        public string TranslateMemberName(string name) => name;
    }

    private sealed class ReadCommands : DbCommandInterceptor
    {
        internal List<string> Sql { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Sql.Add(command.CommandText); return ValueTask.FromResult(result);
        }
    }
}
