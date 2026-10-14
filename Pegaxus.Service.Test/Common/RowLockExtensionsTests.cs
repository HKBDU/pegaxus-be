using Microsoft.EntityFrameworkCore;
using Npgsql;
using Npgsql.NameTranslation;
using Pegaxus.Repository;
using Pegaxus.Repository.Enum;
using Pegaxus.Repository.Extensions;

namespace Pegaxus.Service.Test;

[Collection("PostgresSharedDatabase")]
public class RowLockExtensionsTests
{
    [Fact]
    public async Task LockRowsAsync_DoesNothingOnNonRelationalProviders()
    {
        await using var db = TestSupport.CreateDb();
        var (account, _) = await TestSupport.AddCustomerAsync(db);

        await db.LockRowsAsync(db.Accounts.Where(item => item.AccountId == account.AccountId), item => item.AccountId);

        Assert.Equal(1, await db.Accounts.CountAsync());
    }

    [PostgresFact]
    public async Task LockRowsAsync_HoldsTheRowLockUntilTheTransactionEnds()
    {
        var settings = new NpgsqlConnectionStringBuilder(
            Environment.GetEnvironmentVariable("PEGAXUS_TEST_CONNECTION_STRING"))
        {
            Database = "pegaxus_rowlock_test_" + Guid.NewGuid().ToString("N")
        };
        var builder = new NpgsqlDataSourceBuilder(settings.ConnectionString);
        var translator = new EnumTranslator();
        foreach (var type in typeof(AccountStatus).Assembly.GetTypes()
                     .Where(item => item.IsEnum && item.Namespace == typeof(AccountStatus).Namespace))
            builder.MapEnum(type, translator.TranslateTypeName(type.Name), translator);
        await using var source = builder.Build();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseTestNpgsql(source).Options;
        await using var seed = new AppDbContext(options);
        try
        {
            await seed.Database.EnsureCreatedAsync();
            var (account, _) = await TestSupport.AddCustomerAsync(seed);
            var probe = $"SELECT 1 FROM \"Account\" WHERE account_id = '{account.AccountId}' FOR UPDATE NOWAIT";

            await using var holder = new AppDbContext(options);
            await using var transaction = await holder.Database.BeginTransactionAsync();
            await holder.LockRowsAsync(
                holder.Accounts.Where(item => item.AccountId == account.AccountId), item => item.AccountId);

            await using var competitor = new AppDbContext(options);
            var blocked = await Assert.ThrowsAnyAsync<Exception>(() => competitor.Database.ExecuteSqlRawAsync(probe));
            Assert.Equal(PostgresErrorCodes.LockNotAvailable,
                Assert.IsType<PostgresException>(blocked.GetBaseException()).SqlState);

            await transaction.RollbackAsync();
            await competitor.Database.ExecuteSqlRawAsync(probe);
        }
        finally
        {
            await seed.Database.EnsureDeletedAsync();
        }
    }

    private sealed class EnumTranslator : INpgsqlNameTranslator
    {
        public string TranslateTypeName(string name) => new NpgsqlSnakeCaseNameTranslator().TranslateTypeName(name);
        public string TranslateMemberName(string name) => name;
    }
}
