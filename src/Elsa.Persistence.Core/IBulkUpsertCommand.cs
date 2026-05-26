using Elsa.Primitives.Entities;

namespace Elsa.Persistence.Core
{
    public interface IBulkUpsertCommand<TEntity>
        where TEntity : Entity
    {
        /// <summary>
        /// Performs a bulk upsert operation on a list of entities in the specified database context using a key selector and optional batch size.
        /// </summary>
        /// <typeparam name="TDbContext">The type of the database context.</typeparam>
        /// <typeparam name="TEntity">The type of the entity being upserted.</typeparam>
        /// <param name="dbContext">The database context where the bulk upsert operation will be executed.</param>
        /// <param name="entities">The list of entities to be upserted.</param>
        /// <param name="keySelector">An expression used to determine the key for upsert operations.</param>
        /// <param name="batchSize">The size of each batch for processing the upsert operation. Defaults to 50.</param>
        /// <param name="cancellationToken">A token to observe while waiting for the operation to complete.</param>
        /// <exception cref="NotSupportedException">Thrown if the database provider for the context is not supported.</exception>
        Task BulkUpsertAsync(
            IList<TEntity> entities,
            int batchSize,
            CancellationToken cancellationToken = default
        );

        /// <summary>
        /// Performs a bulk upsert operation on a list of entities in the specified database context using a key selector.
        /// </summary>
        /// <typeparam name="TDbContext">The type of the database context.</typeparam>
        /// <typeparam name="TEntity">The type of the entity being upserted.</typeparam>
        /// <param name="dbContext">The database context where the bulk upsert operation will be executed.</param>
        /// <param name="entities">The list of entities to be upserted.</param>
        /// <param name="keySelector">An expression used to determine the key for upsert operations.</param>
        /// <param name="cancellationToken">A token to observe while waiting for the operation to complete.</param>
        Task BulkUpsertAsync(
            IList<TEntity> entities,
            CancellationToken cancellationToken = default
        )
        {
            return BulkUpsertAsync(entities, 50, cancellationToken);
        }
    }
}
