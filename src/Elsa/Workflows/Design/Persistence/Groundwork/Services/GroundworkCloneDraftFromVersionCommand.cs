using Elsa.Persistence.Core;
using Elsa.Persistence.Core.Design;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Stores;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Services;

public sealed class GroundworkCloneDraftFromVersionCommand(
    IWorkflowDefinitionVersionStore versionStore,
    IWorkflowDefinitionVersionLayoutStore layoutStore,
    ICreateDraftCommand createDraftCommand,
    IPersistenceAccessContextAccessor accessContextAccessor)
    : ICloneDraftFromVersionCommand
{
    public async Task<string> Execute(
        DesignOperationKey operationKey,
        string sourceVersionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operationKey);
        var sourceVersion = await versionStore.FindByIdAsync(sourceVersionId, cancellationToken)
            ?? throw new InvalidOperationException($"Workflow definition version '{sourceVersionId}' not found");
        accessContextAccessor.Current.EnsureTenantScope(sourceVersion.TenantId);

        var sourceLayout = await layoutStore.FindByVersionIdAsync(sourceVersionId, cancellationToken);
        if (sourceLayout is not null)
            accessContextAccessor.Current.EnsureTenantScope(sourceLayout.TenantId);
        var sourceState = sourceVersion.State;
        var copiedState = new WorkflowDefinitionState(
            Variables: [.. sourceState.Variables],
            RootActivity: sourceState.RootActivity,
            Inputs: [.. sourceState.Inputs],
            Outputs: [.. sourceState.Outputs],
            StrategyOptions: sourceState.StrategyOptions);

        return await createDraftCommand.Execute(
            operationKey,
            sourceVersion.DefinitionId,
            copiedState,
            [.. (sourceLayout?.Records ?? [])],
            sourceVersionId,
            cancellationToken);
    }
}
