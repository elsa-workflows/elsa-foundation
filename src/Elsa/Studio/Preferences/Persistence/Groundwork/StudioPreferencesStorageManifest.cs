using Groundwork.Core.Intents;
using Groundwork.Core.Manifests;

namespace Elsa.Studio.Preferences.Persistence.Groundwork;

public static class StudioPreferencesStorageManifest
{
    public const string SchemaVersion = "1.0.0";
    public const string DocumentKind = "studioPreference";

    public static StorageManifest Create() => new(
        new StorageManifestIdentity("elsa-studio-preferences"),
        new StorageManifestOwner("elsa.studio.preferences"),
        new StorageManifestVersion(SchemaVersion),
        [
            new StorageUnit(
                new StorageUnitIdentity(DocumentKind),
                "Studio Preference",
                StorageIntent.PortableDocument(),
                LifecyclePolicy.Mutable,
                IdentityPolicy.StringId(),
                TenancyPolicy.Global,
                ConcurrencyPolicy.Optimistic(),
                SerializationPolicy.Json(),
                [],
                [],
                PhysicalizationPolicy.Portable)
        ],
        new HashSet<string> { "schema-history", "optimistic-concurrency" },
        []);
}
