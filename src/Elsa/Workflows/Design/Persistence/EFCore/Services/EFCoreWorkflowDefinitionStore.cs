using Elsa.Persistence.Core.Queries;
using Elsa.Persistence.EFCore.Services;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Filters;
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
}
