using System.Reflection;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Pegaxus.Repository;

namespace Pegaxus.Service.Test;

public class MigrationDiscoveryTests
{
    [Fact]
    public void EveryMigrationIsDiscoverableByEf()
    {
        var undiscoverable = typeof(AppDbContext).Assembly.GetTypes()
            .Where(type => typeof(Migration).IsAssignableFrom(type) && !type.IsAbstract)
            .Where(type => type.GetCustomAttribute<MigrationAttribute>() is null
                || type.GetCustomAttribute<DbContextAttribute>()?.ContextType != typeof(AppDbContext))
            .Select(type => type.Name)
            .ToArray();

        Assert.Empty(undiscoverable);
    }
}
