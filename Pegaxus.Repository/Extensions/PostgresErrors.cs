using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Pegaxus.Repository.Extensions;

public static class PostgresErrors
{
    /// <summary>
    /// True for optimistic-concurrency failures, unique violations (23505), serialization
    /// failures (40001) and deadlocks (40P01), directly or wrapped in <see cref="DbUpdateException"/>.
    /// </summary>
    public static bool IsWriteConflict(Exception error) =>
        error is DbUpdateConcurrencyException ||
        error is DbUpdateException { InnerException: PostgresException { SqlState: "23505" or "40001" or "40P01" } } ||
        error is PostgresException { SqlState: "23505" or "40001" or "40P01" };
}
