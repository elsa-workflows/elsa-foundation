using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.EFCore.DbContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Design.Persistence.EFCore.Commands;

public sealed class DeleteWorkflowDefinitionPermanently(
    IDbContextFactory<WorkflowsDesignDbContext> contextFactory,
    IEnumerable<IWorkflowDefinitionPermanentDeletionGuard>? deletionGuards = null,
    ILogger<DeleteWorkflowDefinitionPermanently>? logger = null)
    : IDeleteWorkflowDefinitionPermanentlyCommand
{
    public async Task Execute(string definitionId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var definition = await dbContext.WorkflowDefinitions
            .SingleOrDefaultAsync(x => x.Id == definitionId, cancellationToken)
            ?? throw new ArgumentException($"Workflow definition '{definitionId}' was not found.");
        if (definition.DeletedAt is null)
            throw new InvalidOperationException("A workflow definition must be soft-deleted before permanent deletion.");
        foreach (var guard in deletionGuards ?? [])
            await guard.EnsureCanDeleteAsync(definitionId, cancellationToken);

        var draftIds = await dbContext.WorkflowDefinitionDrafts
            .Where(x => x.WorkflowDefinitionId == definitionId)
            .Select(x => x.Id)
            .ToArrayAsync(cancellationToken);

        var versionIds = await dbContext.WorkflowDefinitionVersions
            .Where(x => x.DefinitionId == definitionId)
            .Select(x => x.Id)
            .ToArrayAsync(cancellationToken);

        logger?.LogInformation(
            "Permanently deleting workflow definition {DefinitionId} ({VersionCount} version(s), {DraftCount} draft(s)); soft-deleted at {DeletedAt} with reason {DeletedReason}",
            definitionId,
            versionIds.Length,
            draftIds.Length,
            definition.DeletedAt,
            definition.DeletedReason);
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
