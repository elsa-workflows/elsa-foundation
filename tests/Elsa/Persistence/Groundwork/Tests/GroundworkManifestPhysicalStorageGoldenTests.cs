using Elsa.Activities.Design.Persistence.Groundwork;
using Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork;
using Elsa.Foundation.Identity.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Secrets.Persistence.Groundwork;
using Elsa.Studio.Preferences.Persistence.Groundwork;
using Elsa.Workflows.Design.Persistence.Groundwork;
using Elsa.Workflows.Publishing.Persistence.Groundwork;
using Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork;
using Groundwork.Core.Manifests;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests;

/// <summary>
/// Pins every feature manifest's physical surface — resolved schema fingerprints plus the declared
/// logical indexes and bounded queries — to committed golden files. The manifests are rendered
/// exactly as their production manifest sources compose them, so the goldens must stay byte-identical
/// through the portable-surface migration (and the eventual Groundwork package bump): any drift means
/// providers would materialize a different schema.
/// </summary>
public class GroundworkManifestPhysicalStorageGoldenTests
{
    // Each factory mirrors the corresponding production manifest source's composition path,
    // including the legacy physicalization wrapper where that source still applies it.
    private static readonly IReadOnlyDictionary<string, Func<StorageManifest>> Manifests =
        new Dictionary<string, Func<StorageManifest>>(StringComparer.Ordinal)
        {
            ["runtime"] = ElsaRuntimeStorageManifest.CreatePhysicalized,
            ["design-atomic-write"] = GroundworkDesignAtomicWriteStorageManifest.Create,
            ["publishing"] = () => LegacyGroundworkStorageManifestPhysicalizer.Physicalize(PublishingGroundworkStorageManifest.Create()),
            ["studio-preferences"] = StudioPreferencesStorageManifest.Create,
            ["activities-design"] = () => LegacyGroundworkStorageManifestPhysicalizer.Physicalize(ActivitiesDesignStorageManifest.Create()),
            ["workflows-design"] = () => LegacyGroundworkStorageManifestPhysicalizer.Physicalize(WorkflowsDesignStorageManifest.Create()),
            ["distributed"] = () => LegacyGroundworkStorageManifestPhysicalizer.Physicalize(DistributedGroundworkStorageManifest.Create()),
            ["identity"] = () => LegacyGroundworkStorageManifestPhysicalizer.Physicalize(IdentityStorageManifest.Create()),
            ["secrets"] = SecretsStorageManifest.Create,
            ["opentelemetry"] = OpenTelemetryGroundworkStorageSchema.CreateDocumentManifest
        };

    public static TheoryData<string> ManifestNames => [.. Manifests.Keys];

    [Theory]
    [MemberData(nameof(ManifestNames))]
    public void Manifest_physical_surface_matches_committed_golden(string name) =>
        GroundworkManifestGoldenVerifier.Verify(
            Manifests[name](),
            "tests", "Elsa", "Persistence", "Groundwork", "Tests", "Goldens", $"{name}.json");
}
