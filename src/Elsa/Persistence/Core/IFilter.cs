using Elsa.Primitives.Entities;

namespace Elsa.Persistence.Core;

public interface IFilter<TEntity>
    where TEntity : Entity
{
    IQueryable<TEntity> Apply(IQueryable<TEntity> queryable);

    bool? TenantAgnostic { get; }
}
