using Groundwork.Core.Indexing;
using Groundwork.Core.Intents;
using Groundwork.Core.Manifests;
using Groundwork.Core.Queries;

namespace Elsa.Foundation.Identity.Persistence.Groundwork;

/// <summary>
/// Declares the Groundwork storage manifest for the durable Foundation identity stores. Identity is a
/// Foundation domain, so it owns its own manifest (mirroring the Secrets Groundwork precedent) rather than
/// folding its document kinds into the runtime document manifest. The document-kind names are wire-safe,
/// stable persistence identifiers per docs/serialization.md and must never be renamed without a schema
/// migration and golden-fixture bump.
/// </summary>
public static class IdentityStorageManifest
{
    public const string SchemaVersion = "1.0.0";

    public const string UserDocumentKind = "identityUser";
    public const string RoleDocumentKind = "identityRole";
    public const string ExternalIdentityDocumentKind = "identityExternalIdentity";
    public const string TenantMembershipDocumentKind = "identityTenantMembership";

    public const string ByEmailIndex = "by-email";
    public const string EmailKeyField = "emailKey";
    public const string FindUserByEmailQuery = "find-by-email";

    public const string ByTenantIndex = "by-tenant";
    public const string TenantKeyField = "tenantKey";
    public const string ListRolesByTenantQuery = "list-by-tenant";

    public const string ByUserIndex = "by-user";
    public const string UserKeyField = "userKey";
    public const string ListExternalIdentitiesByUserQuery = "list-by-user";

    public static StorageManifest Create() => new(
        new StorageManifestIdentity("elsa-identity"),
        new StorageManifestOwner("elsa.identity"),
        new StorageManifestVersion(SchemaVersion),
        [
            Unit(
                UserDocumentKind,
                "Identity User",
                [Keyword(ByEmailIndex, EmailKeyField)],
                [Query(FindUserByEmailQuery, ByEmailIndex)]),
            Unit(
                RoleDocumentKind,
                "Identity Role",
                [Keyword(ByTenantIndex, TenantKeyField)],
                [Query(ListRolesByTenantQuery, ByTenantIndex)]),
            Unit(
                ExternalIdentityDocumentKind,
                "Identity External Identity",
                [Keyword(ByUserIndex, UserKeyField)],
                [Query(ListExternalIdentitiesByUserQuery, ByUserIndex)]),
            Unit(
                TenantMembershipDocumentKind,
                "Identity Tenant Membership",
                [Keyword(ByTenantIndex, TenantKeyField)],
                [Query("list-by-tenant", ByTenantIndex)])
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
