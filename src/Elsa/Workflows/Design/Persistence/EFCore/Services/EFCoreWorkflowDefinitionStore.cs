using Elsa.Persistence.Core.Queries;
using Elsa.Persistence.EFCore.Services;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Filters;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Persistence.EFCore.DbContext;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Design.Persistence.EFCore.Services;

/// <summary>
/// EF Core implementation of <see cref="IWorkflowDefinitionStore"/>. Translates the named operations
/// into the closed <see cref="Query{TEntity}"/> spec executed by <see cref="EFCoreReadStore{TDbContext,TEntity}"/>.
/// </summary>
public sealed class EFCoreWorkflowDefinitionStore(IDbContextFactory<WorkflowsDesignDbContext> dbContextFactory, IServiceProvider serviceProvider)
    : EFCoreReadStore<WorkflowsDesignDbContext, WorkflowDefinition>(dbContextFactory, serviceProvider), IWorkflowDefinitionStore
{
    public async Task<WorkflowDefinition> GetAsync(string id, CancellationToken cancellationToken = default)
        => await FindByIdAsync(id, cancellationToken)
           ?? throw EntityNotFoundException.ForEntity(typeof(WorkflowDefinition), id);

    public Task<WorkflowDefinition?> FindByIdAsync(string id, CancellationToken cancellationToken = default)
        => FirstOrDefaultAsync(Query<WorkflowDefinition>.Where(x => x.Id, QueryOp.Equal, id), cancellationToken: cancellationToken);

    public async Task<IReadOnlyList<WorkflowDefinition>> ListAsync(WorkflowDefinitionFilter filter, CancellationToken cancellationToken = default)
        => await QueryAsync(filter.ToQuery(), cancellationToken: cancellationToken);

    public async Task<WorkflowDefinitionPage> ListPageAsync(
        WorkflowDefinitionListQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        query.Validate();

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var definitions = BuildQueryable(dbContext, query.Filter.ToQuery(), include: null);
        definitions = query.Scope switch
        {
            WorkflowDefinitionLifecycleScope.Deleted => definitions.Where(definition => definition.DeletedAt != null),
            WorkflowDefinitionLifecycleScope.All => definitions,
            _ => definitions.Where(definition => definition.DeletedAt == null)
        };

        var totalCount = await definitions.CountAsync(cancellationToken);
        definitions = (query.SortBy, query.SortDirection) switch
        {
            (WorkflowDefinitionSortBy.LastModifiedAt, WorkflowDefinitionSortDirection.Desc) => definitions.OrderByDescending(definition => definition.LastModifiedAt).ThenBy(definition => definition.Id),
            (WorkflowDefinitionSortBy.LastModifiedAt, _) => definitions.OrderBy(definition => definition.LastModifiedAt).ThenBy(definition => definition.Id),
            (WorkflowDefinitionSortBy.CreatedAt, WorkflowDefinitionSortDirection.Desc) => definitions.OrderByDescending(definition => definition.CreatedAt).ThenBy(definition => definition.Id),
            (WorkflowDefinitionSortBy.CreatedAt, _) => definitions.OrderBy(definition => definition.CreatedAt).ThenBy(definition => definition.Id),
            (WorkflowDefinitionSortBy.Name, WorkflowDefinitionSortDirection.Desc) => definitions.OrderByDescending(definition => definition.Name).ThenBy(definition => definition.Id),
            _ => definitions.OrderBy(definition => definition.Name).ThenBy(definition => definition.Id)
        };

        var items = await definitions.Skip(query.Skip).Take(query.PageSize).ToListAsync(cancellationToken);
        await PublishEntityLoadingEvents(dbContext, items, cancellationToken);
        return new WorkflowDefinitionPage(items, totalCount);
    }
}
