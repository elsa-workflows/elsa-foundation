using Elsa.Persistence.Core.Queries;
using Elsa.Persistence.EFCore.Services;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Persistence.EFCore.DbContext;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Design.Persistence.EFCore.Services;

/// <summary>
/// EF Core implementation of <see cref="IWorkflowDefinitionVersionStore"/>. Translates the named
/// operations into the closed <see cref="Query{TEntity}"/> spec executed by
/// <see cref="EFCoreReadStore{TDbContext,TEntity}"/>. The owning definition is loaded via an explicit
/// EF <c>Include</c>, which a non-relational provider would satisfy with a second aggregate read.
/// </summary>
public sealed class EFCoreWorkflowDefinitionVersionStore(IDbContextFactory<WorkflowsDesignDbContext> dbContextFactory, IServiceProvider serviceProvider)
    : EFCoreReadStore<WorkflowsDesignDbContext, WorkflowDefinitionVersion>(dbContextFactory, serviceProvider), IWorkflowDefinitionVersionStore
{
    public async Task<WorkflowDefinitionVersion> GetAsync(string versionId, CancellationToken cancellationToken = default)
        => await FindByIdAsync(versionId, cancellationToken)
           ?? throw EntityNotFoundException.ForEntity(typeof(WorkflowDefinitionVersion), versionId);

    public Task<WorkflowDefinitionVersion?> FindByIdAsync(string versionId, CancellationToken cancellationToken = default)
        => FirstOrDefaultAsync(ById(versionId), cancellationToken: cancellationToken);

    public async Task<WorkflowDefinitionVersion> GetWithDefinitionAsync(string versionId, CancellationToken cancellationToken = default)
        => await FirstOrDefaultAsync(ById(versionId), include: q => q.Include(x => x.Definition), cancellationToken: cancellationToken)
           ?? throw new ArgumentException($"Workflow definition version with id '{versionId}' does not exist");

    public Task<WorkflowDefinitionVersion?> FindLatestVersionAsync(string definitionId, CancellationToken cancellationToken = default)
        => FirstOrDefaultAsync(
            Query<WorkflowDefinitionVersion>.Where(x => x.DefinitionId, QueryOp.Equal, definitionId)
                .OrderByDescending(x => x.SemVerSortKey),
            cancellationToken: cancellationToken);

    public async Task<IReadOnlyList<WorkflowDefinitionVersion>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default)
        => await QueryAsync(Query<WorkflowDefinitionVersion>.Where(x => x.DefinitionId, QueryOp.Equal, definitionId), cancellationToken: cancellationToken);

    public Task<bool> ExistsAsync(string definitionId, string semVerSortKey, CancellationToken cancellationToken = default)
        => AnyAsync(
            Query<WorkflowDefinitionVersion>.Where(x => x.DefinitionId, QueryOp.Equal, definitionId)
                .And(x => x.SemVerSortKey, QueryOp.Equal, semVerSortKey),
            cancellationToken);

    private static Query<WorkflowDefinitionVersion> ById(string versionId)
        => Query<WorkflowDefinitionVersion>.Where(x => x.Id, QueryOp.Equal, versionId);
}
