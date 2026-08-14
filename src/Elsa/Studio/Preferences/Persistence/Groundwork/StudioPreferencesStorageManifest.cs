using Elsa.Persistence.Groundwork.Composition;
using Groundwork.Core.Intents;
using Groundwork.Core.Manifests;

namespace Elsa.Studio.Preferences.Persistence.Groundwork;

public static class StudioPreferencesStorageManifest
{
    public const string SchemaVersion = "1.0.0";
    public const string DocumentKind = "studioPreference";

    public static StorageManifest Create() =>
        new StorageManifest(
            new StorageManifestIdentity("elsa-studio-preferences"),
            new StorageManifestOwner("elsa.studio.preferences"),
            new StorageManifestVersion(SchemaVersion),
            [
                StorageUnit.Create(
                    new StorageUnitIdentity(DocumentKind),
                    "Studio Preference",
                    StorageIntent.PortableDocument(),
                    LifecyclePolicy.Mutable,
                    IdentityPolicy.StringId(),
                    // Studio preferences are isolated by the validated subject, tenant, host, and namespace
                    // carried in the preference key and persisted document. They are therefore stored in the
                    // explicit global document session rather than inheriting an ambient tenant partition.
                    TenancyPolicy.Global,
                    ConcurrencyPolicy.Optimistic(),
                    SerializationPolicy.Json(),
                    SharedDocumentsStorage.Create(DocumentKind, TenancyPolicy.Global, [], []))
            ],
            new HashSet<string> { "schema-history", "optimistic-concurrency" },
            [])
        {
            SharedDocumentStorages = [SharedDocumentsStorage.Definition]
        };
}
