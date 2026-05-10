using Elsa.Common.Entities;
using System.Linq.Expressions;

namespace Elsa.Persistence.Core
{
    public interface IDeleteCommand<TEntity>
        where TEntity : Entity, new()
    {
        /// <summary>
        /// Deletes entities using a predicate.
        /// </summary>
        /// <returns>The number of entities deleted.</returns>
        Task<long> DeleteWhereAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes entities using a query.
        /// </summary>
        /// <returns>The number of entities deleted.</returns>
        Task<long> DeleteWhereAsync(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, CancellationToken cancellationToken = default);        
    }
}
