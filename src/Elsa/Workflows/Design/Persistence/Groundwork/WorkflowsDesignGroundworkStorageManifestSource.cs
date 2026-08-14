using Elsa.Persistence.Groundwork.Composition;
using Elsa.Workflows.Design.Persistence.Core.Stores;

namespace Elsa.Workflows.Design.Persistence.Groundwork;

/// <summary>Contributes the workflows-design family's durable Groundwork declaration.</summary>
public sealed class WorkflowsDesignGroundworkStorageManifestSource : IGroundworkStorageManifestSource
{
    public string FeatureIdentity => "elsa-workflows-design";

    public ValueTask<GroundworkStorageManifestDeclaration> CreateDeclarationAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var manifest = WorkflowsDesignStorageManifest.Create();

        return ValueTask.FromResult(new GroundworkStorageManifestDeclaration(
            FeatureIdentity,
            manifest,
            [
                typeof(IWorkflowDefinitionStore),
                typeof(IWorkflowDefinitionVersionStore),
                typeof(IWorkflowDefinitionDraftStore),
                typeof(IWorkflowDefinitionListProjectionStore),
                typeof(IWorkflowDefinitionVersionLayoutStore)
            ],
            [],
            [],
            []));
    }
}
