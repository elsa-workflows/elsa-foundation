using Elsa.Primitives.Entities;

namespace Elsa.Persistence.Core
{
    public interface ISaveCommand<TEntity>
        where TEntity : Entity
    {
        /// <summary>
        /// Saves the entity.
        /// </summary>
        /// <param name="entity">The entity to save.</param>
        /// <param name="onSaving">The callback to invoke before saving the entity.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        Task SaveAsync(TEntity entity, CancellationToken cancellationToken = default);
    }
}
