using Elsa.Persistence.Core;
using Elsa.Primitives.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Persistence.EFCore.Services
{
    public sealed class EFCoreBulkInsert<TDbContext, TEntity>(IDbContextFactory<TDbContext> dbContextFactory)
        : IBulkInsertCommand<TEntity>

        where TDbContext : DbContext
        where TEntity : Entity, new()
    {
        private async Task<TDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => await dbContextFactory.CreateDbContextAsync(cancellationToken);

        /// <inheritdoc />
        public async Task BulkInsertAsync(IList<TEntity> entities, CancellationToken cancellationToken = default)
        {
            await using var dbContext = await CreateDbContextAsync(cancellationToken);
            var set = dbContext.Set<TEntity>();

            if (entities.Any())
                await set.AddRangeAsync(entities, cancellationToken);

            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
