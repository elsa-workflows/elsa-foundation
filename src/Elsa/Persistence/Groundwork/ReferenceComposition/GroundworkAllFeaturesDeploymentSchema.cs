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
/// Runtime, Secrets, Distributed Runtime, Workflows Design, Activities Design and Publishing with
/// the identity host naming policy. Identity is intentionally explicit: ASP.NET Core Identity hosts
/// opt in through the Groundwork Identity feature rather than through substrate provider selection.
/// </summary>
public sealed class GroundworkAllFeaturesDeploymentSchema : GroundworkDeploymentSchemaManifestSource
{
    protected override IReadOnlyCollection<Type> ManifestSourceTypes =>
        GroundworkReferenceDeploymentSchemaSources.WithoutIdentity;
}

/// <summary>
/// Public, parameterless deployment schema for hosts that explicitly select the Groundwork-backed
/// ASP.NET Core Identity feature in addition to the shipped unified provider leaves.
/// </summary>
public sealed class GroundworkAllFeaturesWithIdentityDeploymentSchema : GroundworkDeploymentSchemaManifestSource
{
    protected override IReadOnlyCollection<Type> ManifestSourceTypes =>
        GroundworkReferenceDeploymentSchemaSources.WithIdentity;
}

internal static class GroundworkReferenceDeploymentSchemaSources
{
    public static readonly IReadOnlyCollection<Type> WithoutIdentity = Array.AsReadOnly<Type>(
    [
        typeof(RuntimeGroundworkStorageManifestSource),
        typeof(SecretsGroundworkStorageManifestSource),
        typeof(DistributedGroundworkStorageManifestSource),
        typeof(WorkflowsDesignGroundworkStorageManifestSource),
        typeof(ActivitiesDesignGroundworkStorageManifestSource),
        typeof(PublishingGroundworkStorageManifestSource)
    ]);

    public static readonly IReadOnlyCollection<Type> WithIdentity = Array.AsReadOnly<Type>(
    [
        .. WithoutIdentity,
        typeof(IdentityGroundworkStorageManifestSource)
    ]);
}
