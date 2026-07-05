using Elsa.Events.Core.Contracts;
using Elsa.Events.Strategies;
using Elsa.Locking.Core;
using Elsa.Persistence.EFCore.Events;
using Elsa.Primitives.Contracts;
using Elsa.Primitives.Versioning;
using Elsa.Workflows.Design.Persistence.Core.Constants;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Exceptions;
using Elsa.Workflows.Design.Persistence.Core.Services;
using Elsa.Workflows.Design.Persistence.EFCore.DbContext;
using Elsa.Workflows.Design.Validations.Core;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Design.Persistence.EFCore.Commands;

public sealed class PromoteDraftToVersion(
    IDistributedLockProvider lockProvider,
    IDbContextFactory<WorkflowsDesignDbContext> contextFactory,
    IEventPublisher eventPublisher,
    IIdentityGenerator identityGenerator)
    : IPromoteDraftToVersionCommand
{
    public async Task<string> Execute(string draftId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(draftId);

        var lockKey = WorkflowDesignPersistenceLockKeys.DraftKey(draftId);

        await using var lockHandle = await lockProvider.AcquireLockAsync(lockKey, null, cancellationToken);
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);

        var draft = await dbContext.WorkflowDefinitionDrafts
            .FirstOrDefaultAsync(d => d.Id == draftId, cancellationToken)
            ?? throw new InvalidOperationException($"Workflow definition draft '{draftId}' not found");

        await eventPublisher.Publish(
            new OnEntityLoading(dbContext, draft),
            EventPublishingStrategy.Sequential,
            cancellationToken);

        // FR-024 promotion gate: derive errors against the hydrated Draft (see DraftValidationGate).
        // Runs inside the per-Draft lock, so the validated state is exactly the state promoted.
        var errors = await eventPublisher.DeriveValidationErrorsAsync(draft, cancellationToken);

        if (errors.Count > 0)
            throw new DraftHasValidationErrorsException(draftId, errors.Count);

        var lastVersion = await dbContext.WorkflowDefinitionVersions
            .Where(v => v.DefinitionId == draft.WorkflowDefinitionId)
            .OrderByDescending(v => v.SemVerSortKey)
            .FirstOrDefaultAsync(cancellationToken);

        var versionId = identityGenerator.Generate();
        var version = new WorkflowDefinitionVersion(
            draft.WorkflowDefinitionId,
            WorkflowVersionNumbering.NextMajor(lastVersion?.Version))
        {
            Id = versionId,
            State = draft.State,
        };

        var draftLayout = await dbContext.WorkflowDefinitionDraftLayouts
            .FirstOrDefaultAsync(l => l.WorkflowDefinitionDraftId == draftId, cancellationToken);

        var versionLayout = new WorkflowDefinitionVersionLayout
        {
            Id = identityGenerator.Generate(),
            WorkflowDefinitionVersionId = versionId,
            Records = draftLayout?.Records.ToList() ?? [],
        };

        await dbContext.WorkflowDefinitionVersions.AddAsync(version, cancellationToken);
        await dbContext.WorkflowDefinitionVersionLayouts.AddAsync(versionLayout, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return versionId;
    }
}
