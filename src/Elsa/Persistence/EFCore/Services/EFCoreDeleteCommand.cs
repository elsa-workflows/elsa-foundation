using Elsa.Persistence.Core;
using Elsa.Primitives.Entities;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace Elsa.Persistence.EFCore.Services;

public sealed class EFCoreDeleteCommand<TDbContext, TEntity>(IDbContextFactory<TDbContext> dbContextFactory)
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
}