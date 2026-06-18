using Elsa.Persistence.Core;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Stores;

namespace Elsa.Workflows.Design.Persistence.EFCore.Commands;

/// <summary>
/// Unit C FR-028 implementation. Reads the source <see cref="WorkflowDefinitionVersion"/>'s State
/// + layout records, deep-copies them, and delegates to <see cref="ICreateDraftCommand"/> — the
/// single Draft-origination path — passing the source version id. Clone is therefore just a
/// create with seeded State/layout and a non-null
/// <see cref="WorkflowDefinitionDraft.SourceVersionId"/>; the lock, validation gate, atomic flush,
/// and lifecycle eventing (<c>OnDraftCreated</c> carrying the source version id, then
/// <c>OnDraftValidated</c>) all live in the create command.
/// </summary>
/// <remarks>
/// The reads go through <see cref="IQueries{TEntity}"/> rather than a hand-rolled
/// <c>DbContextFactory</c> + loading-handler loop: the clone only reads (no change tracking is
/// needed on the source Version or its layout), so the query service — which already runs the
/// loading-handler / <c>OnEntityLoading</c> hydration pipeline and disposes its own short-lived
/// context — is the preferred path. The mutation (the new Draft) is owned entirely by the create
/// command, which opens its own tracked context under the per-Draft lock.
/// </remarks>
public sealed class CloneDraftFromVersion(
    IWorkflowDefinitionVersionStore versionStore,
    IQueries<WorkflowDefinitionVersionLayout> layoutQueries,
    ICreateDraftCommand createDraftCommand
) : ICloneDraftFromVersionCommand
{
    public async Task<string> Execute(string sourceVersionId, CancellationToken cancellationToken = default)
    {
        var sourceVersion = await versionStore.FindByIdAsync(sourceVersionId, cancellationToken)
            ?? throw new InvalidOperationException($"Workflow definition version '{sourceVersionId}' not found");

        var sourceLayout = await layoutQueries.Find(l => l.WorkflowDefinitionVersionId == sourceVersionId, cancellationToken);
        var sourceLayoutRecords = sourceLayout?.Records ?? [];

        var sourceState = sourceVersion.State;

        // Deep-copy: materialise each State collection so the new Draft has independent lists.
        // The inner records (VariableDefinition, ActivityNode, etc.) are immutable, so by-reference
        // sharing of the elements themselves is safe.
        var copiedState = new WorkflowDefinitionState(
            Variables: [.. sourceState.Variables],
            RootActivity: sourceState.RootActivity,
            Inputs: [.. sourceState.Inputs],
            Outputs: [.. sourceState.Outputs],
            WorkflowActivityOptions: sourceState.WorkflowActivityOptions,
            StrategyOptions: sourceState.StrategyOptions
        );

        return await createDraftCommand.Execute(
            sourceVersion.DefinitionId,
            copiedState,
            [.. sourceLayoutRecords],
            sourceVersionId,
            cancellationToken);
    }
}
