namespace Groundwork.Core.Manifests;

public sealed record StorageManifest(
    StorageManifestIdentity Identity,
    StorageManifestOwner Owner,
    StorageManifestVersion Version,
    IReadOnlyList<StorageUnit> StorageUnits,
    IReadOnlySet<string> RequiredCapabilities,
    IReadOnlyList<string> CompatibilityNotes);
