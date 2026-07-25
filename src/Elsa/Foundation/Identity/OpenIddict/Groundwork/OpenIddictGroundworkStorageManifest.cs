using System.Security.Cryptography;
using System.Text;
using Elsa.Foundation.Identity.OpenIddict.Groundwork.Serialization;
using Elsa.Persistence.Groundwork.Composition;
using Groundwork.Core.Capabilities;
using Groundwork.Core.Indexing;
using Groundwork.Core.Intents;
using Groundwork.Core.Manifests;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Queries;

namespace Elsa.Foundation.Identity.OpenIddict.Groundwork;

/// <summary>
/// Storage declaration for the OpenIddict Groundwork adapter. Its four
/// explicitly-global physical entity tables provide the schema contract for
/// provider-driver admission; runtime wiring remains a later adapter task.
/// </summary>
public static class OpenIddictGroundworkStorageManifest
{
    public const string ManifestIdentity = "elsa-openiddict";
    public const string ManifestVersion = "1";

    public const string FindApplicationByClientIdQuery = "find-application-by-client-id";
    public const string FindAuthorizationBySubjectQuery = "find-authorization-by-subject";
    public const string FindScopeByNameQuery = "find-scope-by-name";
    public const string FindTokenByReferenceIdQuery = "find-token-by-reference-id";
    public const string FindTokenBySubjectQuery = "find-token-by-subject";
    public const string PruneAuthorizationsMutation = "prune-openiddict-authorizations";
    public const string PruneTokensMutation = "prune-openiddict-tokens";

    public static IReadOnlyList<GroundworkStorageRouteRequirement> BoundedRoutes { get; } =
    [
        Route(OpenIddictGroundworkJson.ApplicationDocumentKind, FindApplicationByClientIdQuery),
        Route(OpenIddictGroundworkJson.AuthorizationDocumentKind, FindAuthorizationBySubjectQuery),
        Route(OpenIddictGroundworkJson.ScopeDocumentKind, FindScopeByNameQuery),
        Route(OpenIddictGroundworkJson.TokenDocumentKind, FindTokenByReferenceIdQuery),
        Route(OpenIddictGroundworkJson.TokenDocumentKind, FindTokenBySubjectQuery)
    ];

    public static string Fingerprint { get; } = Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(string.Join('|',
            ManifestIdentity,
            ManifestVersion,
            OpenIddictGroundworkJson.ApplicationDocumentKind,
            OpenIddictGroundworkJson.AuthorizationDocumentKind,
            OpenIddictGroundworkJson.ScopeDocumentKind,
            OpenIddictGroundworkJson.TokenDocumentKind,
            string.Join('|', BoundedRoutes.Select(route => route.RouteIdentity))))));

    public static StorageManifest Create() => new(
        new StorageManifestIdentity(ManifestIdentity),
        new StorageManifestOwner("elsa.foundation.identity"),
        new StorageManifestVersion(ManifestVersion),
        [
            Unit(OpenIddictGroundworkJson.ApplicationDocumentKind, "OpenIddict application", CreateApplicationDefinition(),
                [ClientIdIndex], [ClientIdQuery]),
            Unit(OpenIddictGroundworkJson.AuthorizationDocumentKind, "OpenIddict authorization", CreateAuthorizationDefinition(),
                [AuthorizationSubjectIndex], [AuthorizationSubjectQuery], [PruneAuthorizations]),
            Unit(OpenIddictGroundworkJson.ScopeDocumentKind, "OpenIddict scope", CreateScopeDefinition(),
                [ScopeNameIndex], [ScopeNameQuery]),
            Unit(OpenIddictGroundworkJson.TokenDocumentKind, "OpenIddict token", CreateTokenDefinition(),
                [TokenReferenceIndex, TokenSubjectIndex], [TokenReferenceQuery, TokenSubjectQuery], [PruneTokens])
        ],
        new HashSet<string> { "optimistic-concurrency", "global-openiddict-stores" },
        []);

    public static PhysicalTableDefinition CreateApplicationDefinition() => PhysicalTableDefinition.PhysicalEntityTable(
        "openiddict_applications",
        [Column("clientId", "clientId", isNullable: true)],
        Envelope,
        [UniqueIndex(ClientIdIndex.Identity, "clientId")]);

    public static PhysicalTableDefinition CreateAuthorizationDefinition() => PhysicalTableDefinition.PhysicalEntityTable(
        "openiddict_authorizations",
        [Column("subject", "subject", isNullable: true), Column("status", "status", isNullable: true), Column("expiration", "expiration", PortablePhysicalType.DateTime)],
        Envelope,
        [Index(AuthorizationSubjectIndex.Identity, "subject", "status", "expiration")]);

    public static PhysicalTableDefinition CreateScopeDefinition() => PhysicalTableDefinition.PhysicalEntityTable(
        "openiddict_scopes",
        [Column("name", "name", isNullable: true)],
        Envelope,
        [UniqueIndex(ScopeNameIndex.Identity, "name")]);

    public static PhysicalTableDefinition CreateTokenDefinition() => PhysicalTableDefinition.PhysicalEntityTable(
        "openiddict_tokens",
        [
            Column("referenceId", "referenceId", isNullable: true),
            Column("subject", "subject", isNullable: true),
            Column("status", "status", isNullable: true),
            Column("expiration", "expiration", PortablePhysicalType.DateTime)
        ],
        Envelope,
        [
            UniqueIndex(TokenReferenceIndex.Identity, "referenceId"),
            Index(TokenSubjectIndex.Identity, "subject", "status", "expiration")
        ]);

    private static readonly DocumentEnvelopeDefinition Envelope = new();

    private static readonly LogicalIndexDeclaration ClientIdIndex = KeywordIndex("openiddict-application-by-client-id", "clientId", unique: true);
    private static readonly LogicalIndexDeclaration AuthorizationSubjectIndex = CompoundIndex(
        "openiddict-authorization-by-subject-status-expiration", "subject", "status", "expiration");
    private static readonly LogicalIndexDeclaration ScopeNameIndex = KeywordIndex("openiddict-scope-by-name", "name", unique: true);
    private static readonly LogicalIndexDeclaration TokenReferenceIndex = KeywordIndex("openiddict-token-by-reference-id", "referenceId", unique: true);
    private static readonly LogicalIndexDeclaration TokenSubjectIndex = CompoundIndex(
        "openiddict-token-by-subject-status-expiration", "subject", "status", "expiration");

    private static readonly BoundedQueryDeclaration ClientIdQuery = PointQuery(FindApplicationByClientIdQuery, ClientIdIndex);
    private static readonly BoundedQueryDeclaration AuthorizationSubjectQuery = RangeQuery(FindAuthorizationBySubjectQuery, AuthorizationSubjectIndex);
    private static readonly BoundedQueryDeclaration ScopeNameQuery = PointQuery(FindScopeByNameQuery, ScopeNameIndex);
    private static readonly BoundedQueryDeclaration TokenReferenceQuery = PointQuery(FindTokenByReferenceIdQuery, TokenReferenceIndex);
    private static readonly BoundedQueryDeclaration TokenSubjectQuery = RangeQuery(FindTokenBySubjectQuery, TokenSubjectIndex);
    private static readonly BoundedMutationDeclaration PruneAuthorizations = new(
        PruneAuthorizationsMutation, AuthorizationSubjectQuery.Identity, BoundedMutationAction.Delete());
    private static readonly BoundedMutationDeclaration PruneTokens = new(
        PruneTokensMutation, TokenSubjectQuery.Identity, BoundedMutationAction.Delete());

    private static StorageUnit Unit(
        string identity,
        string displayName,
        PhysicalTableDefinition definition,
        IReadOnlyCollection<LogicalIndexDeclaration> indexes,
        IReadOnlyCollection<BoundedQueryDeclaration> queries,
        IReadOnlyCollection<BoundedMutationDeclaration>? mutations = null) =>
        StorageUnit.Create(
            new StorageUnitIdentity(identity),
            displayName,
            StorageIntent.PortableDocument(),
            LifecyclePolicy.Mutable,
            IdentityPolicy.StringId(),
            TenancyPolicy.Global,
            ConcurrencyPolicy.Optimistic(),
            SerializationPolicy.Json(),
            new StorageUnitPhysicalStorage(
                StorageUnitProvisioningMode.Declared,
                PhysicalStoragePolicy.Explicit(definition),
                indexes.ToArray(),
                queries.ToArray(),
                boundedMutations: mutations?.ToArray() ?? []));

    private static GroundworkStorageRouteRequirement Route(string storageUnit, string routeIdentity) => new(
        new StorageUnitIdentity(storageUnit),
        routeIdentity,
        new HashSet<CapabilityId> { WellKnownCapabilities.AtomicCommit });

    private static ProjectedColumnDefinition Column(
        string physicalName,
        string jsonPath,
        PortablePhysicalType type = PortablePhysicalType.String,
        bool isNullable = false) =>
        new(physicalName, jsonPath, type, Length: type == PortablePhysicalType.String ? 256 : null, IsNullable: isNullable);

    private static LogicalIndexDeclaration KeywordIndex(string identity, string path, bool unique = false) =>
        new(identity, [new IndexField(path, IndexValueKind.Keyword)], IndexValueKind.Keyword, unique, MissingValueBehavior.Excluded);

    private static LogicalIndexDeclaration CompoundIndex(string identity, params string[] paths) =>
        new(identity,
            paths.Select(path => new IndexField(path, path == "expiration" ? IndexValueKind.DateTime : IndexValueKind.Keyword)).ToArray(),
            IndexValueKind.Keyword,
            isUnique: false,
            MissingValueBehavior.Excluded);

    private static PhysicalIndexDefinition UniqueIndex(string identity, params string[] columns) =>
        Index(identity, columns, isUnique: true);

    private static PhysicalIndexDefinition Index(string identity, params string[] columns) =>
        Index(identity, columns, isUnique: false);

    private static PhysicalIndexDefinition Index(string identity, IReadOnlyCollection<string> columns, bool isUnique) => new(
        identity,
        columns.Select((column, index) => new PhysicalIndexColumnDefinition(column, index)).ToArray(),
        isUnique: isUnique,
        missingValueBehavior: MissingValueBehavior.Excluded);

    private static BoundedQueryDeclaration PointQuery(string identity, LogicalIndexDeclaration index) => new(
        identity,
        index.Identity,
        new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal },
        QuerySortSupport.None,
        QueryPagingSupport.Offset,
        BoundedQueryExecutionClass.ScaleBearing,
        predicateFields: [new BoundedQueryPredicateField(index.Fields.Single().Path, new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal })]);

    private static BoundedQueryDeclaration RangeQuery(string identity, LogicalIndexDeclaration index) => new(
        identity,
        index.Identity,
        new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal, PortableQueryOperation.LessThanOrEqual },
        QuerySortSupport.None,
        QueryPagingSupport.Offset,
        BoundedQueryExecutionClass.ScaleBearing,
        predicateFields: index.Fields.Select(field => new BoundedQueryPredicateField(
            field.Path,
            new HashSet<PortableQueryOperation>
            {
                field.Path == "expiration" ? PortableQueryOperation.LessThanOrEqual : PortableQueryOperation.Equal
            })).ToArray());
}
