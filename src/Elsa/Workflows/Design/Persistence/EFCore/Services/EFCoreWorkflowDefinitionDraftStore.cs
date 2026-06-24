using Elsa.Persistence.Core.Queries;
using Elsa.Persistence.EFCore.Services;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Persistence.EFCore.DbContext;
using Elsa.Workflows.Design.Validations.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Design.Persistence.EFCore.Services;

/// <summary>
/// EF Core implementation of <see cref="IWorkflowDefinitionDraftStore"/>. Routes through
/// <see cref="EFCoreReadStore{TDbContext,TEntity}"/> so the <c>OnEntityLoading</c> pipeline hydrates
/// the <c>[NotMapped]</c> <see cref="WorkflowDefinitionDraft.State"/> from its serialized source.
/// </summary>
public sealed class EFCoreWorkflowDefinitionDraftStore(IDbContextFactory<WorkflowsDesignDbContext> dbContextFactory, IServiceProvider serviceProvider)
    : EFCoreReadStore<WorkflowsDesignDbContext, WorkflowDefinitionDraft>(dbContextFactory, serviceProvider), IWorkflowDefinitionDraftStore
{
    public Task<WorkflowDefinitionDraft?> FindByIdAsync(string draftId, CancellationToken cancellationToken = default)
        => FirstOrDefaultAsync(
            Query<WorkflowDefinitionDraft>.Where(x => x.Id, QueryOp.Equal, draftId),
            cancellationToken: cancellationToken);

    public Task<WorkflowDefinitionDraft?> FindByWorkflowDefinitionIdAsync(string workflowDefinitionId, CancellationToken cancellationToken = default)
        => FirstOrDefaultAsync(
            Query<WorkflowDefinitionDraft>.Where(x => x.WorkflowDefinitionId, QueryOp.Equal, workflowDefinitionId),
            cancellationToken: cancellationToken);

    public async Task<IReadOnlyCollection<DesignMetadataRecord>> FindLayoutByDraftIdAsync(string draftId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var layout = await dbContext.WorkflowDefinitionDraftLayouts
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.WorkflowDefinitionDraftId == draftId, cancellationToken);

        return layout?.Records.ToArray() ?? [];
    }

    public async Task<IReadOnlyCollection<ValidationError>> FindValidationErrorsByDraftIdAsync(string draftId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var validation = await dbContext.WorkflowDefinitionDraftValidations
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.WorkflowDefinitionDraftId == draftId, cancellationToken);

        return validation?.Errors.ToArray() ?? [];
    }
}
