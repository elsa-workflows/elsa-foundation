using Elsa.Events.Core.Contracts;
using Elsa.Persistence.Core.Queries;
using Elsa.Persistence.EFCore.Events;
using Elsa.Persistence.EFCore.Queries;
using Elsa.Primitives.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.EFCore.Services;

/// <summary>
/// Generic EF Core read store that executes the closed, provider-neutral <see cref="Query{TEntity}"/>
/// spec. It is the relational implementation behind the named persistence ports (e.g.
/// <c>IWorkflowDefinitionStore</c>): the ports speak intent, this base speaks EF Core. It reproduces
/// the loading-event plumbing of <c>EFCoreQueries</c> so <c>[NotMapped]</c> projections such as
/// <c>WorkflowDefinitionVersion.State</c> still hydrate.
/// </summary>
/// <typeparam name="TDbContext">The database context type.</typeparam>
/// <typeparam name="TEntity">The entity type.</typeparam>
public class EFCoreReadStore<TDbContext, TEntity>(IDbContextFactory<TDbContext> dbContextFactory, IServiceProvider serviceProvider)
    where TDbContext : DbContext
    where TEntity : Entity
{
    /// <summary>Executes <paramref name="query"/> and returns every matching entity.</summary>
    public async Task<IReadOnlyList<TEntity>> QueryAsync(
        Query<TEntity> query,
        Func<IQueryable<TEntity>, IQueryable<TEntity>>? include = null,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var queryable = BuildQueryable(dbContext, query, include);
        var entities = await queryable.ToListAsync(cancellationToken);

        await PublishEntityLoadingEvents(dbContext, entities, cancellationToken);

        return entities;
    }

    /// <summary>Executes <paramref name="query"/> and returns the first matching entity, or <c>null</c>.</summary>
    public async Task<TEntity?> FirstOrDefaultAsync(
        Query<TEntity> query,
        Func<IQueryable<TEntity>, IQueryable<TEntity>>? include = null,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var queryable = BuildQueryable(dbContext, query, include);
        var entity = await queryable.FirstOrDefaultAsync(cancellationToken);

        if (entity == null)
            return null;

        await PublishEntityLoadingEvents(dbContext, [entity], cancellationToken);

        return entity;
    }

    /// <summary>Determines whether any entity matches <paramref name="query"/>.</summary>
    public async Task<bool> AnyAsync(Query<TEntity> query, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var queryable = BuildQueryable(dbContext, query, include: null);
        return await queryable.AnyAsync(cancellationToken);
    }

    protected static IQueryable<TEntity> BuildQueryable(
        TDbContext dbContext,
        Query<TEntity> query,
        Func<IQueryable<TEntity>, IQueryable<TEntity>>? include)
    {
        IQueryable<TEntity> set = dbContext.Set<TEntity>().AsNoTracking();

        if (query.TenantAgnostic)
            set = set.IgnoreQueryFilters();

        if (include != null)
            set = include(set);

        return EFCoreQueryTranslator.Apply(set, query);
    }

    protected async Task PublishEntityLoadingEvents(TDbContext dbContext, IReadOnlyCollection<TEntity> entities, CancellationToken cancellationToken)
    {
        if (entities.Count == 0)
            return;

        using var scope = serviceProvider.CreateScope();
        var eventPublisher = scope.ServiceProvider.GetService<IInlineEventPublisher>();
        if (eventPublisher is null)
            return;

        foreach (var entity in entities)
            await eventPublisher.Publish(new OnEntityLoading(dbContext, entity), cancellationToken);
    }
}
