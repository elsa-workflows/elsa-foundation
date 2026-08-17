using Elsa.Activities.Design.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Persistence.Groundwork.Unified.Composition;
using Elsa.Workflows.Design.Persistence.Groundwork;
using Elsa.Workflows.Publishing.Persistence.Groundwork;

namespace Elsa.Persistence.Groundwork.ReferenceComposition;

/// <summary>
/// The public, parameterless deployment schema for the shipped unified provider leaves. It selects
/// Runtime, Workflows Design, Activities Design, their shared atomic-operation
/// ledger, and Publishing with the identity host naming policy. Identity is intentionally explicit:
/// Distributed Runtime owns independent public-v2 units and registers them directly; it is therefore not part of
/// this transitional v1 deployment manifest.
/// ASP.NET Core Identity hosts opt in through the Groundwork Identity feature rather than through substrate
/// provider selection.
/// </summary>
public sealed class GroundworkAllFeaturesDeploymentSchema : GroundworkDeploymentSchemaManifestSource
{
    protected override IReadOnlyCollection<Type> ManifestSourceTypes =>
        GroundworkReferenceDeploymentSchemaSources.WithoutIdentity;
}

internal static class GroundworkReferenceDeploymentSchemaSources
{
    public static readonly IReadOnlyCollection<Type> WithoutIdentity = Array.AsReadOnly<Type>(
    [
        typeof(RuntimeGroundworkStorageManifestSource),
        typeof(WorkflowsDesignGroundworkStorageManifestSource),
        typeof(ActivitiesDesignGroundworkStorageManifestSource),
        typeof(GroundworkDesignAtomicWriteStorageManifestSource),
        typeof(PublishingGroundworkStorageManifestSource)
    ]);

}
