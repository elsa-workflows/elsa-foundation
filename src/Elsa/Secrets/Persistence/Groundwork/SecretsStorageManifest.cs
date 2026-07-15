using Groundwork.Core.Indexing;
using Groundwork.Core.Intents;
using Groundwork.Core.Manifests;
using Groundwork.Core.Queries;

namespace Elsa.Secrets.Persistence.Groundwork;

public static class SecretsStorageManifest
{
    public const string SchemaVersion = "1.0.0";
    public const string SecretDocumentKind = "secret";
    public const string ByCollectionIndex = "by-collection";
    public const string CollectionField = "collection";
    public const string SecretCollection = "secret";
    public const string ListAllQuery = "list-all";

    public static StorageManifest Create() => new(
        new StorageManifestIdentity("elsa-secrets"),
        new StorageManifestOwner("elsa.secrets"),
        new StorageManifestVersion(SchemaVersion),
        [
            Unit(
                SecretDocumentKind,
                "Secret",
                [Keyword(ByCollectionIndex, CollectionField)],
                [Query(ListAllQuery, ByCollectionIndex)])
        ],
        new HashSet<string> { "schema-history", "optimistic-concurrency" },
        []);

    private static StorageUnit Unit(
        string documentKind,
        string label,
        IndexDeclaration[] indexes,
        PortableQueryDeclaration[] queries) => new(
        new StorageUnitIdentity(documentKind),
        label,
        StorageIntent.PortableDocument(),
        LifecyclePolicy.Mutable,
        IdentityPolicy.StringId(),
        TenancyPolicy.Global,
        ConcurrencyPolicy.Optimistic(),
        SerializationPolicy.Json(),
        indexes,
        queries,
        PhysicalizationPolicy.Portable);

    private static PortableQueryDeclaration Query(string name, string indexName) => new(
        name,
        indexName,
        new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal },
        QuerySortSupport.None,
        QueryPagingSupport.Offset);

    private static IndexDeclaration Keyword(string identity, string field) => new(
        identity,
        [new IndexField(field)],
        IndexValueKind.Keyword,
        false,
        true,
        MissingValueBehavior.Excluded,
        new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal },
        IndexPhysicalizationPolicy.Optimized);
}
