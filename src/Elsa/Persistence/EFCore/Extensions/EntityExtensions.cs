using Elsa.Primitives.Extensions;
using System.Collections.Concurrent;
using System.Linq.Expressions;

namespace Elsa.Persistence.EFCore.Extensions;

public static class EntityExtensions
{
    public static Expression<Func<TEntity, bool>> BuildEqualsExpression<TEntity>(this TEntity entity, Expression<Func<TEntity, string>> keySelector)
    {
        var keyName = keySelector.GetProperty()!.Name;

        // Reading the key off the entity uses a compiled accessor cached per (entity type, key name):
        // compiling a throwaway expression tree on every call was a measurable hot-path cost (issue #394).
        var entityKey = KeyAccessorCache<TEntity>.Get(keyName)(entity);

        // Build the predicate that compares the key column against the entity's key value. This lambda
        // is handed to the EF Core query provider for translation and is never compiled here.
        var parameter = Expression.Parameter(typeof(TEntity), "x");
        var comparison = Expression.Equal(Expression.Property(parameter, keyName), Expression.Constant(entityKey));

        return Expression.Lambda<Func<TEntity, bool>>(comparison, parameter);
    }

    private static class KeyAccessorCache<TEntity>
    {
        private static readonly ConcurrentDictionary<string, Func<TEntity, string>> Accessors = new();

        public static Func<TEntity, string> Get(string keyName) => Accessors.GetOrAdd(keyName, static name =>
        {
            var parameter = Expression.Parameter(typeof(TEntity), "x");
            return Expression.Lambda<Func<TEntity, string>>(Expression.Property(parameter, name), parameter).Compile();
        });
    }
}
