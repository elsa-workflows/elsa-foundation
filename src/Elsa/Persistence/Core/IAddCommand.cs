using Elsa.Primitives.Entities;

namespace Elsa.Persistence.Core;

public interface IAddCommand<TEntity>
    where TEntity : Entity
{
    /// <summary>
    /// Adds the specified entity.
    /// </summary>
    /// <param name="entity">The entity to add.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task Add(TEntity entity, CancellationToken cancellationToken = default);
}