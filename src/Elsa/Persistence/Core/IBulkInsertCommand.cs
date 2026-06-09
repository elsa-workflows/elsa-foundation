using Elsa.Primitives.Entities;

namespace Elsa.Persistence.Core;

public interface IBulkInsertCommand<TEntity>
    where TEntity : Entity
{
    Task BulkInsertAsync(IList<TEntity> entities, CancellationToken cancellationToken = default);
}