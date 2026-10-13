using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Pegaxus.Repository.Extensions;

public static class TransactionExtensions
{
    /// <summary>Begins a Serializable transaction on relational providers and a plain one otherwise.</summary>
    public static Task<IDbContextTransaction> BeginSerializableAsync(
        this DatabaseFacade database,
        CancellationToken cancellationToken = default) =>
        database.IsRelational()
            ? database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : database.BeginTransactionAsync(cancellationToken);

    /// <summary>Begins a Serializable transaction on relational providers and returns null otherwise.</summary>
    public static async Task<IDbContextTransaction?> BeginSerializableOrNoneAsync(
        this DatabaseFacade database,
        CancellationToken cancellationToken = default) =>
        database.IsRelational()
            ? await database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;
}
