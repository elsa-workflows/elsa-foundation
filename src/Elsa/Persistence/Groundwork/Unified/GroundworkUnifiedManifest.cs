using Elsa.Activities.Design.Persistence.Groundwork;
using Elsa.Persistence.Groundwork;
using Elsa.Secrets.Persistence.Groundwork;
using Elsa.Studio.Preferences.Persistence.Groundwork;
using Elsa.Workflows.Design.Persistence.Groundwork;
using Groundwork.Core.Manifests;

namespace Elsa.Persistence.Groundwork.Unified;

/// <summary>
/// Composes the Elsa runtime, workflows-design and activities-design Groundwork manifests into one
/// <see cref="StorageManifest"/> so a single host-selected document store can back every Elsa module.
/// <para>
/// This manifest is <b>provider-neutral</b>: it declares document kinds only, with no dependency on a
/// concrete Groundwork provider (SQLite, PostgreSQL, …). Each provider's Unified feature materializes it
/// into its own store, so the union is defined once and reused across providers.
/// </para>
/// <para>
/// The three lanes declare disjoint document kinds (<c>workflowExecutionState</c>, <c>bookmarkState</c>, …
/// for runtime; <c>workflowDefinition</c>, <c>workflowDefinitionVersion</c>, <c>workflowDefinitionDraft</c>,
/// <c>workflowDefinitionVersionLayout</c> for workflows-design; <c>activityDefinition</c>,
/// <c>activityDefinitionVersion</c> for activities-design), so
/// <see cref="StorageManifestComposition.Union(StorageManifestIdentity, StorageManifestOwner, StorageManifestVersion, StorageManifest[])"/>
/// merges them without collision. The composite carries a <b>stable</b> identity (<see cref="Identity"/>) so
/// the materializer's schema-history is consistent across runs.
/// </para>
/// </summary>
public static class GroundworkUnifiedManifest
{
    /// <summary>The stable composite manifest identity. Materialization/planning keys schema-history off this value.</summary>
    public static readonly StorageManifestIdentity Identity = new("elsa-documents");

    private static readonly StorageManifestOwner Owner = new("elsa.documents");
    private static readonly StorageManifestVersion Version = new("1.0.0");

    /// <summary>
    /// Builds the unioned manifest spanning runtime + workflows-design + activities-design. Document kinds are
    /// disjoint across the lanes, so the union passes the Groundwork manifest validator unchanged.
    /// </summary>
    public static StorageManifest Create() => StorageManifestComposition.Union(
        Identity,
        Owner,
        Version,
        ElsaRuntimeStorageManifest.Create(),
        WorkflowsDesignStorageManifest.Create(),
        ActivitiesDesignStorageManifest.Create(),
        SecretsStorageManifest.Create(),
        StudioPreferencesStorageManifest.Create());
}
