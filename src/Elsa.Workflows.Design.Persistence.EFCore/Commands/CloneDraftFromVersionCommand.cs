using Elsa.Persistence.EFCore.Contracts;
using Elsa.Primitives.Contracts;
using Elsa.Workflows.Design.Core.Events;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.EFCore.DbContext;
using Elsa.Workflows.Design.Persistence.EFCore.Services;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Design.Persistence.EFCore.Commands;

/// <summary>
/// Unit C FR-028 implementation. Deep-copies <see cref="WorkflowDefinitionVersion.State"/> +
/// the Version's <see cref="WorkflowDefinitionVersionLayout"/> records into a brand-new Draft
/// + DraftLayout, then routes through <see cref="DraftMutationPipeline.ExecuteCreation"/>
/// so the new Draft is created under its per-Draft lock, gets its validation sibling
/// populated, and emits the standard lifecycle event sequence (cause: cloning event;
/// consequence: validated event).
/// </summary>
public sealed class CloneDraftFromVersionCommand(
    IIdentityGenerator identityGenerator,
    IDbContextFactory<WorkflowsDesignDbContext> contextFactory,
    IEnumerable<IEntityLoadingHandler<WorkflowsDesignDbContext, WorkflowDefinitionVersion>> versionLoadingHandlers,
    DraftMutationPipeline pipeline
) : ICloneDraftFromVersionCommand
{
    public async Task<string> Execute(string sourceVersionId, CancellationToken cancellationToken = default)
    {
        WorkflowDefinitionState sourceState;
        string targetDefinitionId;
        IEnumerable<DesignMetadataRecord> sourceLayoutRecords;

        await using (var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken))
        {
            var sourceVersion = await dbContext.WorkflowDefinitionVersions
                .FirstOrDefaultAsync(v => v.Id == sourceVersionId, cancellationToken)
                ?? throw new InvalidOperationException($"Workflow definition version '{sourceVersionId}' not found");

            foreach (var handler in versionLoadingHandlers)
                await handler.Handle(dbContext, sourceVersion, cancellationToken);

            sourceState = sourceVersion.State;
            targetDefinitionId = sourceVersion.DefinitionId;

            var sourceLayout = await dbContext.WorkflowDefinitionVersionLayouts
                .FirstOrDefaultAsync(l => l.WorkflowDefinitionVersionId == sourceVersionId, cancellationToken);

            sourceLayoutRecords = sourceLayout?.Records ?? [];
        }

        var newDraftId = identityGenerator.Generate();

        // Deep-copy: materialise each State collection so the new Draft has independent lists.
        // The inner records (VariableDefinition, ActivityNode, etc.) are immutable, so by-reference
        // sharing of the elements themselves is safe.
        var copiedState = new WorkflowDefinitionState(
            Variables: [.. sourceState.Variables],
            ActivityConnections: [.. sourceState.ActivityConnections],
            Activities: [.. sourceState.Activities],
            Inputs: [.. sourceState.Inputs],
            Outputs: [.. sourceState.Outputs],
            WorkflowActivityOptions: sourceState.WorkflowActivityOptions,
            StrategyOptions: sourceState.StrategyOptions
        );

        var newDraft = new WorkflowDefinitionDraft
        {
            Id = newDraftId,
            WorkflowDefinitionId = targetDefinitionId,
            State = copiedState,
        };

        var newLayout = new WorkflowDefinitionDraftLayout
        {
            Id = identityGenerator.Generate(),
            WorkflowDefinitionDraftId = newDraftId,
            Records = [.. sourceLayoutRecords],
        };

        var originationEvent = new OnDraftClonedFromVersion(newDraftId, sourceVersionId, targetDefinitionId);

        await pipeline.ExecuteCreation(newDraft, newLayout, originationEvent, cancellationToken);

        return newDraftId;
    }
}
