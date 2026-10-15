using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

namespace Pegaxus.Repository.Extensions;

public static class RowLockExtensions
{
    /// <summary>
    /// Updates the key of every matched row to itself so PostgreSQL holds a row lock until the
    /// transaction ends. Does nothing on providers that cannot run set-based updates (InMemory).
    /// </summary>
    public static Task LockRowsAsync<TEntity, TKey>(
        this DbContext db,
        IQueryable<TEntity> rows,
        Expression<Func<TEntity, TKey>> key,
        CancellationToken cancellationToken = default)
        where TEntity : class
    {
        if (!db.Database.IsRelational())
            return Task.CompletedTask;

        return rows.ExecuteUpdateAsync(SelfAssignment(key), cancellationToken);
    }

    private static Expression<Func<SetPropertyCalls<TEntity>, SetPropertyCalls<TEntity>>> SelfAssignment<TEntity, TKey>(
        Expression<Func<TEntity, TKey>> key)
    {
        var setProperty = SetPropertyMethod<TEntity>().MakeGenericMethod(typeof(TKey));
        var setters = Expression.Parameter(typeof(SetPropertyCalls<TEntity>), "setters");
        return Expression.Lambda<Func<SetPropertyCalls<TEntity>, SetPropertyCalls<TEntity>>>(
            Expression.Call(setters, setProperty, key, key), setters);
    }

    private static MethodInfo SetPropertyMethod<TEntity>() =>
        typeof(SetPropertyCalls<TEntity>).GetMethods().Single(method =>
            method.Name == nameof(SetPropertyCalls<TEntity>.SetProperty) &&
            method.IsGenericMethodDefinition &&
            method.GetParameters()[1].ParameterType.IsGenericType);
}
