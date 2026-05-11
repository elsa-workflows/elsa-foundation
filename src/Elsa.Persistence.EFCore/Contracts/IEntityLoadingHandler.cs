using Elsa.Primitives.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Persistence.EFCore.Contracts
{
    public interface IEntityLoadingHandler<TDbContext>
        where TDbContext : DbContext
    {
    }

    public interface IEntityLoadingHandler<TDbContext, TEntity> : IEntityLoadingHandler<TDbContext>
        where TDbContext : DbContext
        where TEntity : Entity, new()
    {
        ValueTask Handle(TDbContext dbContext, TEntity? entity, CancellationToken cancellationToken);
    }
}
