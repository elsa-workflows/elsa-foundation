using Elsa.Locking.Core;
using Elsa.Persistence.Core.Design;
using Elsa.Primitives.Contracts;
using Elsa.Primitives.Versioning;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Constants;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Services;
using Elsa.Workflows.Design.Persistence.EFCore.DbContext;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Design.Persistence.EFCore.Commands;

/// <summary>
/// EF Core oracle for API-authored version creation. The operation key is deliberately accepted
/// but not persisted here: only Groundwork owns the production idempotency ledger.
/// </summary>
public sealed class AddWorkflowDefinitionVersion(
    IIdentityGenerator identityGenerator,
    IDistributedLockProvider lockProvider,
    IDbContextFactory<WorkflowsDesignDbContext> contextFactory)
    : IAddWorkflowDefinitionVersionCommand
{
    public async Task<WorkflowDefinitionVersionAdded> Execute(
        DesignOperationKey operationKey,
        string definitionId,
        WorkflowDefinitionState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operationKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionId);
        ArgumentNullException.ThrowIfNull(state);

        await using var lockHandle = await lockProvider.AcquireLockAsync(
            WorkflowDesignPersistenceLockKeys.DefinitionKey(definitionId),
            null,
            cancellationToken);
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);

        var definitionExists = await dbContext.WorkflowDefinitions
            .AnyAsync(x => x.Id == definitionId, cancellationToken);
        if (!definitionExists)
            throw new ArgumentException($"Workflow definition '{definitionId}' was not found.");

        var lastVersion = await dbContext.WorkflowDefinitionVersions
            .Where(x => x.DefinitionId == definitionId)
            .OrderByDescending(x => x.SemVerSortKey)
            .FirstOrDefaultAsync(cancellationToken);
        var nextVersion = WorkflowVersionNumbering.NextMajor(lastVersion?.Version);
        var version = new WorkflowDefinitionVersion(definitionId, nextVersion)
        {
            Id = identityGenerator.Generate(),
            State = state,
        };

        await dbContext.WorkflowDefinitionVersions.AddAsync(version, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new WorkflowDefinitionVersionAdded(definitionId, version.Id, version.Version);
    }
}
