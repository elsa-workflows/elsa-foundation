using Elsa.Persistence.Core;
using Elsa.Primitives.Entities;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace Elsa.Persistence.EFCore.Services
{
    public sealed class EFCoreDeleteCommand<TDbContext, TEntity>(IDbContextFactory<TDbContext> dbContextFactory, IQueries<TEntity> queries)
        : IDeleteCommand<TEntity>
        where TDbContext : DbContext
        where TEntity : Entity
    {
        private async Task<TDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => await dbContextFactory.CreateDbContextAsync(cancellationToken);

        /// <summary>
        /// Deletes entities using a predicate.
        /// </summary>
        /// <returns>The number of entities deleted.</returns>
        public async Task<long> DeleteWhere(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
        {
            await using var dbContext = await CreateDbContextAsync(cancellationToken);
            var set = dbContext.Set<TEntity>().AsNoTracking();
            return await set.Where(predicate).ExecuteDeleteAsync(cancellationToken);
        }

        /// <summary>
        /// Deletes entities using a query.
        /// </summary>
        /// <returns>The number of entities deleted.</returns>
        public async Task<long> DeleteWhere(IFilter<TEntity> filter, CancellationToken cancellationToken = default)
        {
            var query = GetDeleteQuery(filter);
            var ids = await queries.Query(
                query,
                selector: e => e.Id,
                cancellationToken: cancellationToken
            );

            return await DeleteWhere(
                x => ids.Contains(x.Id),
                cancellationToken
            );
        }

        private static Func<IQueryable<TEntity>, IQueryable<TEntity>> GetDeleteQuery(IFilter<TEntity> filter)
        {
            return queryable =>
            {
                var result = filter.Apply(queryable);
                if (filter.TenantAgnostic == true)
                {
                    result = result.IgnoreQueryFilters();
                }

                return result.DistinctBy(x => x.Id);
            };
        }
    }
}
