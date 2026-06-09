using Elsa.Persistence.Core;
using Elsa.Persistence.EFCore.Extensions;
using Elsa.Primitives.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.EFCore.Services;

public sealed class EFCoreSaveCommand<TDbContext, TEntity>(IDbContextFactory<TDbContext> dbContextFactory, IServiceProvider serviceProvider) : ISaveCommand<TEntity>
    where TDbContext : DbContext
    where TEntity : Entity
{
    // ReSharper disable once StaticMemberInGenericType
    // Justification: This is a static member that is used to ensure that only one thread can access the database for TEntity at a time.
    private static readonly SemaphoreSlim Semaphore = new(1, 1);

    /// <summary>
    /// Creates a new instance of the database context.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The database context.</returns>
    private async Task<TDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => await dbContextFactory.CreateDbContextAsync(cancellationToken);


    /// <summary>
    /// Saves the entity.
    /// </summary>
    /// <param name="entity">The entity to save.</param>
    /// <param name="keySelector">The key selector to get the primary key property.</param>
    /// <param name="onSaving">The callback to invoke before saving the entity.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task SaveAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        await Semaphore.WaitAsync(cancellationToken); // Asynchronous wait

        try
        {
            await using var dbContext = await CreateDbContextAsync(cancellationToken);

            var set = dbContext.Set<TEntity>();
            var lambda = entity.BuildEqualsExpression(e => e.Id);
            var exists = await set.AnyAsync(lambda, cancellationToken);
            set.Entry(entity).State = exists ? EntityState.Modified : EntityState.Added;
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            var handler = serviceProvider.GetService<IDbExceptionHandler>();

            if (handler != null)
            {
                await handler.HandleAsync(ex, cancellationToken);
            }

            throw;
        }
        finally
        {
            Semaphore.Release();
        }
    }
}