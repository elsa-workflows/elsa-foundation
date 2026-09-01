using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Stores;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Services;

public sealed class GroundworkCloneDraftFromVersionCommand(
    IWorkflowDefinitionVersionStore versionStore,
    IWorkflowDefinitionVersionLayoutStore layoutStore,
    IDraftOriginator draftOriginator,
    IPersistenceAccessContextAccessor accessContextAccessor)
    : ICloneDraftFromVersionCommand
{
    private const string OperationKind = "workflow.draft.clone-from-version.v1";

    public async Task<string> Execute(
        DesignOperationKey operationKey,
        string sourceVersionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operationKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceVersionId);

        return await draftOriginator.ExecuteAsync(
            operationKey,
            OperationKind,
            // Versions are immutable, so their identity is the complete canonical clone request.
            // Expanding that identity after a marker miss keeps exact replay independent of whether
            // the source remains physically available.
            new CloneDraftRequestMaterial(sourceVersionId),
            async token =>
            {
                var sourceVersion = await versionStore.FindByIdAsync(sourceVersionId, token)
                    ?? throw new InvalidOperationException(
                        $"Workflow definition version '{sourceVersionId}' not found");
                accessContextAccessor.Current.EnsureTenantScope(sourceVersion.TenantId);
                var sourceLayout = await layoutStore.FindByVersionIdAsync(sourceVersionId, token);
                if (sourceLayout is not null)
                    accessContextAccessor.Current.EnsureTenantScope(sourceLayout.TenantId);
                var sourceState = sourceVersion.State;
                var copiedState = new WorkflowDefinitionState(
                    Variables: [.. sourceState.Variables],
                    RootActivity: sourceState.RootActivity,
                    Inputs: [.. sourceState.Inputs],
                    Outputs: [.. sourceState.Outputs],
                    StrategyOptions: sourceState.StrategyOptions);
                return new DraftOriginationInput(
                    sourceVersion.DefinitionId,
                    copiedState,
                    [.. (sourceLayout?.Records ?? [])],
                    sourceVersionId,
                    sourceVersion.TenantId,
                    [.. (sourceLayout?.ActivityPresentation ?? [])]);
            },
            cancellationToken);
    }

    private sealed record CloneDraftRequestMaterial(string SourceVersionId);
}
