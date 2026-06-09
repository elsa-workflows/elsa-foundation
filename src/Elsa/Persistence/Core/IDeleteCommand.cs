using Elsa.Primitives.Entities;
using System.Linq.Expressions;

namespace Elsa.Persistence.Core
{
    public interface IDeleteCommand<TEntity>
        where TEntity : Entity
    {
        /// <summary>
        /// Deletes entities using a predicate.
        /// </summary>
        /// <returns>The number of entities deleted.</returns>
        Task<long> DeleteWhere(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes entities using a query.
        /// </summary>
        /// <returns>The number of entities deleted.</returns>
        Task<long> DeleteWhere(IFilter<TEntity> filter, CancellationToken cancellationToken = default);
    }
}
