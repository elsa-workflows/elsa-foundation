using Elsa.Common.Extensions;
using System.Linq.Expressions;

namespace Elsa.Persistence.EFCore.Extensions
{
    public static class EntityExtensions
    {
        public static Expression<Func<TEntity, bool>> BuildEqualsExpression<TEntity>(this TEntity entity, Expression<Func<TEntity, string>> keySelector)
        {
            var keyName = keySelector.GetProperty()!.Name;

            // Define parameters for the lambda expression
            var parameter = Expression.Parameter(typeof(TEntity), "x");
            var keySelectorLambda = Expression.Lambda<Func<TEntity, string>>(Expression.Property(parameter, keyName), parameter);

            // Build the expression that compares the keys
            var entityKey = keySelectorLambda.Compile()(entity);
            var comparison = Expression.Equal(keySelectorLambda.Body, Expression.Constant(entityKey));

            // Create the final lambda expression that can be used in AnyAsync
            var lambda = Expression.Lambda<Func<TEntity, bool>>(comparison, parameter);

            return lambda;
        }
    }
}
