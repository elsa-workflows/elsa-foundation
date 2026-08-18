using Elsa.Persistence.Groundwork.Querying;
using Elsa.Persistence.Groundwork.Unified.Composition;

namespace Elsa.Persistence.Groundwork.ReferenceComposition;

/// <summary>
/// The public, parameterless deployment schema for the shipped unified provider leaves. It selects
/// Runtime and the shared design atomic-operation ledger with the identity host naming policy. Several
/// lanes are intentionally absent: Distributed Runtime, Workflows Design, Activities Design and Publishing
/// each own independent public-v2 units and declare them directly against the catalog, so they contribute
/// no composed manifest to this transitional v1 deployment schema and provision their own schema. They
/// remain resolvable as lanes for cross-lane target questions.
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
        typeof(GroundworkDesignAtomicWriteStorageManifestSource)
    ]);

}
