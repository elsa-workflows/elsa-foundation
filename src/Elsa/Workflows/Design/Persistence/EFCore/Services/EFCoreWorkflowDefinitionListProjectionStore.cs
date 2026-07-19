using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Persistence.EFCore.DbContext;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Design.Persistence.EFCore.Services;

/// <summary>
/// Projects draft and version summary facts for an entire definition list in one database query.
/// </summary>
public sealed class EFCoreWorkflowDefinitionListProjectionStore(
    IDbContextFactory<WorkflowsDesignDbContext> dbContextFactory) : IWorkflowDefinitionListProjectionStore
{
    public async Task<IReadOnlyList<WorkflowDefinitionListProjection>> ListByDefinitionIdsAsync(
        IReadOnlyCollection<string> workflowDefinitionIds,
        CancellationToken cancellationToken = default)
    {
        var definitionIds = workflowDefinitionIds.Distinct(StringComparer.Ordinal).ToArray();
        if (definitionIds.Length == 0)
            return [];

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.WorkflowDefinitions
            .AsNoTracking()
            .Where(definition => definitionIds.Contains(definition.Id))
            .Select(definition => new WorkflowDefinitionListProjection(
                definition.Id,
                dbContext.WorkflowDefinitionDrafts
                    .Where(draft => draft.WorkflowDefinitionId == definition.Id)
                    .OrderByDescending(draft => draft.LastModifiedAt)
                    .ThenByDescending(draft => draft.CreatedAt)
                    .ThenByDescending(draft => draft.Id)
                    .Select(draft => draft.Id)
                    .FirstOrDefault(),
                dbContext.WorkflowDefinitionVersions
                    .Where(version => version.DefinitionId == definition.Id)
                    .OrderByDescending(version => version.SemVerSortKey)
                    .Select(version => version.Id)
                    .FirstOrDefault(),
                dbContext.WorkflowDefinitionVersions
                    .Where(version => version.DefinitionId == definition.Id)
                    .OrderByDescending(version => version.SemVerSortKey)
                    .Select(version => version.Version)
                    .FirstOrDefault(),
                dbContext.WorkflowDefinitionVersions.Count(version => version.DefinitionId == definition.Id)))
            .ToListAsync(cancellationToken);
    }
}
