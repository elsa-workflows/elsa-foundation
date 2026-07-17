using Elsa.Persistence.Groundwork.Composition;
using Elsa3.Activities.Design.Import.Contracts;
using Elsa3.Activities.Design.Import.Models;
using Groundwork.Core.Intents;
using Groundwork.Core.Manifests;
using Groundwork.Core.PhysicalStorage;

namespace Elsa3.Activities.Design.Import.Persistence.Groundwork;

#pragma warning disable GW0001 // Legacy physicalization bridge is the admitted provider-neutral document manifest seam.
public static class Elsa3ImportStorageManifest
{
    public const string SchemaVersion = "1.0.0";
    public const string CollectionDocumentKind = "elsa3ReusableImportCollection";
    public const string ReceiptDocumentKind = "elsa3ReusableImportReceipt";

    public static StorageManifest Create() => new(
        new StorageManifestIdentity("elsa3-reusable-activity-import"),
        new StorageManifestOwner("elsa3.activities.design.import"),
        new StorageManifestVersion(SchemaVersion),
        [
            Unit(CollectionDocumentKind, "Elsa 3 reusable import collection"),
            Unit(ReceiptDocumentKind, "Elsa 3 reusable import receipt")
        ],
        new HashSet<string> { "optimistic-concurrency" },
        []);

    public static string ReceiptId(string idempotencyKey, ReusableActivityImportAccessScope accessScope) =>
        ReusableActivityImportIdentity.Create(
            "receipt",
            accessScope.TenantScope,
            accessScope.UserId,
            idempotencyKey);

    private static StorageUnit Unit(string documentKind, string label) => new(
        new StorageUnitIdentity(documentKind),
        label,
        StorageIntent.PortableDocument(),
        LifecyclePolicy.AppendOnly,
        IdentityPolicy.StringId(),
        TenancyPolicy.Scoped,
        ConcurrencyPolicy.Optimistic(),
        SerializationPolicy.Json(),
        [],
        [],
        PhysicalizationPolicy.Portable);
}
#pragma warning restore GW0001

public sealed class Elsa3ImportGroundworkStorageManifestSource : IGroundworkStorageManifestSource
{
    public string FeatureIdentity => "elsa3-reusable-activity-import";

    public ValueTask<GroundworkStorageManifestDeclaration> CreateDeclarationAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var manifest = LegacyGroundworkStorageManifestPhysicalizer.Physicalize(Elsa3ImportStorageManifest.Create());
        return ValueTask.FromResult(new GroundworkStorageManifestDeclaration(
            FeatureIdentity,
            manifest,
            [typeof(IReusableActivityImportOperationStore)],
            [],
            [],
            []));
    }
}
