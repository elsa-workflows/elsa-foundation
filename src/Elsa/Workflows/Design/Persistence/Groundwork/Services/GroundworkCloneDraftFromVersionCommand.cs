using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Stores;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Services;

public sealed class GroundworkCloneDraftFromVersionCommand(
    IWorkflowDefinitionVersionStore versionStore,
    IWorkflowDefinitionVersionLayoutStore layoutStore,
    ICreateDraftCommand createDraftCommand)
    : ICloneDraftFromVersionCommand
{
    public async Task<string> Execute(string sourceVersionId, CancellationToken cancellationToken = default)
    {
        var sourceVersion = await versionStore.FindByIdAsync(sourceVersionId, cancellationToken)
            ?? throw new InvalidOperationException($"Workflow definition version '{sourceVersionId}' not found");

        var sourceLayout = await layoutStore.FindByVersionIdAsync(sourceVersionId, cancellationToken);
        var sourceState = sourceVersion.State;
        var copiedState = new WorkflowDefinitionState(
            Variables: [.. sourceState.Variables],
            RootActivity: sourceState.RootActivity,
            Inputs: [.. sourceState.Inputs],
            Outputs: [.. sourceState.Outputs],
            WorkflowActivityOptions: sourceState.WorkflowActivityOptions,
            StrategyOptions: sourceState.StrategyOptions);

        return await createDraftCommand.Execute(
            sourceVersion.DefinitionId,
            copiedState,
            [.. (sourceLayout?.Records ?? [])],
            sourceVersionId,
            cancellationToken);
    }
}
