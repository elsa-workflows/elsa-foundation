using Elsa.Persistence.Core.Queries;
using Elsa.Persistence.EFCore.Services;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Persistence.EFCore.DbContext;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Design.Persistence.EFCore.Services;

/// <summary>
/// EF Core implementation of <see cref="IWorkflowDefinitionVersionLayoutStore"/>. Translates the named
/// operation into the closed <see cref="Query{TEntity}"/> spec executed by
/// <see cref="EFCoreReadStore{TDbContext,TEntity}"/>. The layout's <c>Records</c> are materialized by
/// the entity's value converter, so no loading handler participates.
/// </summary>
public sealed class EFCoreWorkflowDefinitionVersionLayoutStore(IDbContextFactory<WorkflowsDesignDbContext> dbContextFactory, IServiceProvider serviceProvider)
    : EFCoreReadStore<WorkflowsDesignDbContext, WorkflowDefinitionVersionLayout>(dbContextFactory, serviceProvider), IWorkflowDefinitionVersionLayoutStore
{
    public Task<WorkflowDefinitionVersionLayout?> FindByVersionIdAsync(string workflowDefinitionVersionId, CancellationToken cancellationToken = default)
        => FirstOrDefaultAsync(
            Query<WorkflowDefinitionVersionLayout>.Where(x => x.WorkflowDefinitionVersionId, QueryOp.Equal, workflowDefinitionVersionId),
            cancellationToken: cancellationToken);
}
