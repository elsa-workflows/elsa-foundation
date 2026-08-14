using Elsa.Persistence.Groundwork.Composition;
using Elsa3.Activities.Design.Import.Contracts;
using Elsa3.Activities.Design.Import.Models;
using Groundwork.Core.Intents;
using Groundwork.Core.Manifests;

namespace Elsa3.Activities.Design.Import.Persistence.Groundwork;

public static class Elsa3ImportStorageManifest
{
    public const string SchemaVersion = "1.0.0";
    public const string CollectionDocumentKind = "elsa3ReusableImportCollection";
    public const string ReceiptDocumentKind = "elsa3ReusableImportReceipt";
    public const string DefinitionBindingDocumentKind = "elsa3ReusableImportDefinitionBinding";

    public static StorageManifest Create() => new StorageManifest(
        new StorageManifestIdentity("elsa3-reusable-activity-import"),
        new StorageManifestOwner("elsa3.activities.design.import"),
        new StorageManifestVersion(SchemaVersion),
        [
            Unit(CollectionDocumentKind, "Elsa 3 reusable import collection"),
            Unit(ReceiptDocumentKind, "Elsa 3 reusable import receipt"),
            Unit(DefinitionBindingDocumentKind, "Elsa 3 reusable import definition binding")
        ],
        new HashSet<string> { "optimistic-concurrency" },
        [])
    {
        // Every unit stores its documents in the shared Groundwork document table, so the manifest declares
        // that table itself instead of having a physicalization wrapper inject it.
        SharedDocumentStorages = [SharedDocumentsStorage.Definition]
    };

    public static string ReceiptId(string idempotencyKey, ReusableActivityImportAccessScope accessScope) =>
        ReusableActivityImportIdentity.Create(
            "receipt",
            accessScope.TenantScope,
            accessScope.UserId,
            idempotencyKey);

    private static StorageUnit Unit(string documentKind, string label)
    {
        // The unit and its shared-documents recipe must agree on tenancy: the recipe prefixes every
        // physical index with the storage-scope column exactly when the unit is tenant-scoped.
        var tenancy = TenancyPolicy.Scoped;
        return StorageUnit.Create(
            new StorageUnitIdentity(documentKind),
            label,
            StorageIntent.PortableDocument(),
            LifecyclePolicy.AppendOnly,
            IdentityPolicy.StringId(),
            tenancy,
            ConcurrencyPolicy.Optimistic(),
            SerializationPolicy.Json(),
            SharedDocumentsStorage.Create(documentKind, tenancy, [], []));
    }
}

public sealed class Elsa3ImportGroundworkStorageManifestSource : IGroundworkStorageManifestSource
{
    public string FeatureIdentity => "elsa3-reusable-activity-import";

    public ValueTask<GroundworkStorageManifestDeclaration> CreateDeclarationAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var manifest = Elsa3ImportStorageManifest.Create();
        return ValueTask.FromResult(new GroundworkStorageManifestDeclaration(
            FeatureIdentity,
            manifest,
            [typeof(IReusableActivityImportOperationStore)],
            [],
            [],
            []));
    }
}
