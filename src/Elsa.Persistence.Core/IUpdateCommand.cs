using Elsa.Primitives.Entities;
using System.Linq.Expressions;

namespace Elsa.Persistence.Core
{
    public interface IUpdateCommand<TEntity>
        where TEntity : Entity, new()
    {
        /// <summary>
        /// Updates the entity.
        /// </summary>
        /// <param name="entity">The entity to update.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        Task UpdateAsync(TEntity entity, CancellationToken cancellationToken = default);

        /// <summary>
        /// Updates specific properties of an entity in the database.
        /// </summary>
        /// <param name="entity">The entity to update.</param>
        /// <param name="properties">An array of expressions indicating the properties to update.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        Task UpdatePartialAsync(TEntity entity, Expression<Func<TEntity, object>>[] properties, CancellationToken cancellationToken = default);
    }
}
