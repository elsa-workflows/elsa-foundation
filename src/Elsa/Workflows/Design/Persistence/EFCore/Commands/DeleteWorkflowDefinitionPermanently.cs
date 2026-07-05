using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.EFCore.DbContext;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Design.Persistence.EFCore.Commands;

public sealed class DeleteWorkflowDefinitionPermanently(IDbContextFactory<WorkflowsDesignDbContext> contextFactory)
    : IDeleteWorkflowDefinitionPermanentlyCommand
{
    public async Task Execute(string definitionId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var draftIds = await dbContext.WorkflowDefinitionDrafts
            .Where(x => x.WorkflowDefinitionId == definitionId)
            .Select(x => x.Id)
            .ToArrayAsync(cancellationToken);

        var versionIds = await dbContext.WorkflowDefinitionVersions
            .Where(x => x.DefinitionId == definitionId)
            .Select(x => x.Id)
            .ToArrayAsync(cancellationToken);

        await dbContext.WorkflowDefinitionDraftLayouts
            .Where(x => draftIds.Contains(x.WorkflowDefinitionDraftId))
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.WorkflowDefinitionVersionLayouts
            .Where(x => versionIds.Contains(x.WorkflowDefinitionVersionId))
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.WorkflowDefinitionDrafts
            .Where(x => x.WorkflowDefinitionId == definitionId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.WorkflowDefinitionVersions
            .Where(x => x.DefinitionId == definitionId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.WorkflowDefinitions
            .Where(x => x.Id == definitionId)
            .ExecuteDeleteAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }
}
