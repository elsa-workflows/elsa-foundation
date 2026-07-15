using Elsa.Activities.Design.Persistence.Groundwork;
using Elsa.Foundation.Identity.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.Unified.Composition;
using Elsa.Secrets.Persistence.Groundwork;
using Elsa.Workflows.Design.Persistence.Groundwork;
using Elsa.Workflows.Publishing.Persistence.Groundwork;
using Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork;

namespace Elsa.Persistence.Groundwork.ReferenceComposition;

/// <summary>
/// The public, parameterless deployment schema for the shipped unified provider leaves. It selects
/// Runtime, Identity, Secrets, Distributed Runtime, Workflows Design, Activities Design and
/// Publishing with the identity host naming policy.
/// </summary>
public sealed class GroundworkAllFeaturesDeploymentSchema : GroundworkDeploymentSchemaManifestSource
{
    private static readonly IReadOnlyCollection<Type> Sources = Array.AsReadOnly<Type>(
    [
        typeof(RuntimeGroundworkStorageManifestSource),
        typeof(IdentityGroundworkStorageManifestSource),
        typeof(SecretsGroundworkStorageManifestSource),
        typeof(DistributedGroundworkStorageManifestSource),
        typeof(WorkflowsDesignGroundworkStorageManifestSource),
        typeof(ActivitiesDesignGroundworkStorageManifestSource),
        typeof(PublishingGroundworkStorageManifestSource)
    ]);

    protected override IReadOnlyCollection<Type> ManifestSourceTypes => Sources;
}
