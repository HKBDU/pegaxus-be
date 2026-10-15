using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Pegaxus.Repository;

namespace Pegaxus.Service.Test;

internal static class NpgsqlTestOptions
{
    // Every test builds its own data source/interceptors, so EF creates one internal service provider per test.
    // The suite shares one process, which trips ManyServiceProvidersCreatedWarning (thrown by default) after 20.
    internal static DbContextOptionsBuilder<AppDbContext> UseTestNpgsql(
        this DbContextOptionsBuilder<AppDbContext> builder, NpgsqlDataSource source) =>
        builder.UseNpgsql(source)
            .ConfigureWarnings(warnings => warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
}
