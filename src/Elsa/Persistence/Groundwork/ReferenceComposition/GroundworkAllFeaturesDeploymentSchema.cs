using Elsa.Activities.Design.Persistence.Groundwork;
using Elsa.Diagnostics.Persistence.Groundwork;
using Elsa.Foundation.Identity.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.Unified.Composition;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Secrets.Persistence.Groundwork;
using Elsa.Workflows.Design.Persistence.Groundwork;
using Elsa.Workflows.Publishing.Persistence.Groundwork;
using Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork;
using Groundwork.DiagnosticRecords;

namespace Elsa.Persistence.Groundwork.ReferenceComposition;

/// <summary>
/// The public, parameterless deployment schema for the shipped unified provider leaves. It selects
/// Runtime, Secrets, Distributed Runtime, Workflows Design, Activities Design, their shared atomic-operation
/// ledger, and Publishing with the identity host naming policy. Identity is intentionally explicit:
/// ASP.NET Core Identity hosts opt in through the Groundwork Identity feature rather than through substrate
/// provider selection.
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

/// <summary>
/// Public, parameterless deployment schema for hosts that select the combined diagnostics Groundwork
/// feature. It is the single authority for the document catalog definitions and diagnostic-record streams.
/// </summary>
public sealed class GroundworkAllFeaturesWithDiagnosticsDeploymentSchema :
    GroundworkDeploymentSchemaManifestSource,
    IDiagnosticRecordDeploymentManifestSource
{
    protected override IReadOnlyCollection<Type> ManifestSourceTypes =>
        GroundworkReferenceDeploymentSchemaSources.WithDiagnostics;

    public DiagnosticRecordDeploymentManifest CreateDeploymentManifest() =>
        GroundworkReferenceDeploymentSchemaSources.CreateDiagnosticsDeploymentManifest(this);
}

/// <summary>
/// Public, parameterless deployment schema for hosts that select both Groundwork-backed ASP.NET Core
/// Identity and the combined diagnostics Groundwork feature.
/// </summary>
public sealed class GroundworkAllFeaturesWithIdentityAndDiagnosticsDeploymentSchema :
    GroundworkDeploymentSchemaManifestSource,
    IDiagnosticRecordDeploymentManifestSource
{
    protected override IReadOnlyCollection<Type> ManifestSourceTypes =>
        GroundworkReferenceDeploymentSchemaSources.WithIdentityAndDiagnostics;

    public DiagnosticRecordDeploymentManifest CreateDeploymentManifest() =>
        GroundworkReferenceDeploymentSchemaSources.CreateDiagnosticsDeploymentManifest(this);
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
        typeof(GroundworkDesignAtomicWriteStorageManifestSource),
        typeof(PublishingGroundworkStorageManifestSource)
    ]);

    public static readonly IReadOnlyCollection<Type> WithIdentity = Array.AsReadOnly<Type>(
    [
        .. WithoutIdentity,
        typeof(IdentityGroundworkStorageManifestSource)
    ]);

    public static readonly IReadOnlyCollection<Type> WithDiagnostics = Array.AsReadOnly<Type>(
    [
        .. WithoutIdentity,
        typeof(DiagnosticsGroundworkStorageManifestSource)
    ]);

    public static readonly IReadOnlyCollection<Type> WithIdentityAndDiagnostics = Array.AsReadOnly<Type>(
    [
        .. WithDiagnostics,
        typeof(IdentityGroundworkStorageManifestSource)
    ]);

    public static DiagnosticRecordDeploymentManifest CreateDiagnosticsDeploymentManifest(
        GroundworkDeploymentSchemaManifestSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new(source.CreateManifest(), DiagnosticsGroundworkStorageManifest.CreateDiagnosticRecordStreams());
    }
}
