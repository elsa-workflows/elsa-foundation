using Elsa.Primitives.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Persistence.EFCore.Contracts
{
    public interface IEntitySavingHandler<TDbContext>
        where TDbContext : DbContext
    {
    }
    
    public interface IEntitySavingHandler<TDbContext, TEntity> : IEntitySavingHandler<TDbContext>
        where TDbContext : DbContext
        where TEntity : Entity, new()
    {
        ValueTask Handle(TDbContext dbContext, TEntity entity, CancellationToken cancellationToken);
    }
}
