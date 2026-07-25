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
    public const int MaxIndexedUriLength = 256;
    public const int MaxIndexedIdentifierLength = 128;
    public const int MaxIndexedSubjectLength = 256;
    public const int MaxIndexedStatusLength = 64;
    public const int MaxIndexedTypeLength = 128;

    public const string ListApplicationsQuery = "list-applications";
    public const string FindApplicationByClientIdQuery = "find-application-by-client-id";
    public const string FindApplicationByRedirectUriQuery = "find-application-by-redirect-uri";
    public const string FindApplicationByPostLogoutRedirectUriQuery = "find-application-by-post-logout-redirect-uri";
    public const string ListAuthorizationsQuery = "list-authorizations";
    public const string FindAuthorizationBySubjectQuery = "find-authorization-by-subject";
    public const string FindAuthorizationByScopeQuery = "find-authorization-by-scope";
    public const string ListScopesQuery = "list-scopes";
    public const string FindScopeByNameQuery = "find-scope-by-name";
    public const string FindScopesByNamesQuery = "find-scopes-by-names";
    public const string FindScopeByResourceQuery = "find-scope-by-resource";
    public const string ListTokensQuery = "list-tokens";
    public const string FindTokenQuery = "find-token";
    public const string FindTokenByApplicationIdQuery = "find-token-by-application-id";
    public const string FindTokenByAuthorizationIdQuery = "find-token-by-authorization-id";
    public const string FindTokenByReferenceIdQuery = "find-token-by-reference-id";
    public const string FindTokenBySubjectQuery = "find-token-by-subject";
    public const string FindPrunableTokensQuery = "find-prunable-tokens";
    public const string SelectTokensForRevokeQuery = "select-tokens-for-revoke";
    public const string PruneAuthorizationsMutation = "prune-openiddict-authorizations";
    public const string PruneTokensMutation = "prune-openiddict-tokens";
    public const string RevokeTokensMutation = "revoke-openiddict-tokens";
    public const string RevokeTokensByApplicationIdMutation = "revoke-openiddict-tokens-by-application-id";
    public const string RevokeTokensByAuthorizationIdMutation = "revoke-openiddict-tokens-by-authorization-id";
    public const string RevokeTokensBySubjectMutation = "revoke-openiddict-tokens-by-subject";

    public static IReadOnlyList<GroundworkStorageRouteRequirement> BoundedRoutes { get; } =
    [
        Route(OpenIddictGroundworkJson.ApplicationDocumentKind, ListApplicationsQuery),
        Route(OpenIddictGroundworkJson.ApplicationDocumentKind, FindApplicationByClientIdQuery),
        Route(OpenIddictGroundworkJson.ApplicationDocumentKind, FindApplicationByRedirectUriQuery),
        Route(OpenIddictGroundworkJson.ApplicationDocumentKind, FindApplicationByPostLogoutRedirectUriQuery),
        Route(OpenIddictGroundworkJson.AuthorizationDocumentKind, ListAuthorizationsQuery),
        Route(OpenIddictGroundworkJson.AuthorizationDocumentKind, FindAuthorizationBySubjectQuery),
        Route(OpenIddictGroundworkJson.AuthorizationDocumentKind, FindAuthorizationByScopeQuery),
        Route(OpenIddictGroundworkJson.ScopeDocumentKind, ListScopesQuery),
        Route(OpenIddictGroundworkJson.ScopeDocumentKind, FindScopeByNameQuery),
        Route(OpenIddictGroundworkJson.ScopeDocumentKind, FindScopesByNamesQuery),
        Route(OpenIddictGroundworkJson.ScopeDocumentKind, FindScopeByResourceQuery),
        Route(OpenIddictGroundworkJson.TokenDocumentKind, ListTokensQuery),
        Route(OpenIddictGroundworkJson.TokenDocumentKind, FindTokenQuery),
        Route(OpenIddictGroundworkJson.TokenDocumentKind, FindTokenByApplicationIdQuery),
        Route(OpenIddictGroundworkJson.TokenDocumentKind, FindTokenByAuthorizationIdQuery),
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
                [ApplicationIdIndex, ClientIdIndex, ApplicationRedirectUriIndex, ApplicationPostLogoutRedirectUriIndex],
                [ApplicationListQuery, ClientIdQuery, ApplicationRedirectUriQuery, ApplicationPostLogoutRedirectUriQuery]),
            Unit(OpenIddictGroundworkJson.AuthorizationDocumentKind, "OpenIddict authorization", CreateAuthorizationDefinition(),
                [AuthorizationIdIndex, AuthorizationSubjectIndex, AuthorizationScopeIndex],
                [AuthorizationListQuery, AuthorizationSubjectQuery, AuthorizationScopeQuery]),
            Unit(OpenIddictGroundworkJson.ScopeDocumentKind, "OpenIddict scope", CreateScopeDefinition(),
                [ScopeIdIndex, ScopeNameIndex, ScopeResourceIndex],
                [ScopeListQuery, ScopeNameQuery, ScopeNamesQuery, ScopeResourceQuery]),
            Unit(OpenIddictGroundworkJson.TokenDocumentKind, "OpenIddict token", CreateTokenDefinition(),
                [
                    TokenIdIndex,
                    TokenApplicationIndex,
                    TokenAuthorizationIndex,
                    TokenReferenceIndex,
                    TokenSubjectIndex
                ],
                [
                    TokenListQuery,
                    TokenCompoundQuery,
                    TokenApplicationQuery,
                    TokenAuthorizationQuery,
                    TokenReferenceQuery,
                    TokenSubjectQuery
                ])
        ],
        new HashSet<string> { "optimistic-concurrency", "global-openiddict-stores" },
        []);

    public static PhysicalTableDefinition CreateApplicationDefinition() => PhysicalTableDefinition.PhysicalEntityTable(
        "openiddict_applications",
        [
            Column("clientId", "clientId", isNullable: true),
            CollectionColumn("redirectUri", "redirectUris", length: MaxIndexedUriLength),
            CollectionColumn("postLogoutRedirectUri", "postLogoutRedirectUris", length: MaxIndexedUriLength)
        ],
        Envelope,
        [
            OrderedIndex(
                ApplicationIdIndex.Identity,
                Envelope.IdComparisonKeyColumn),
            UniqueIndex(ClientIdIndex.Identity, "clientId")
        ]);

    public static PhysicalTableDefinition CreateAuthorizationDefinition() => PhysicalTableDefinition.PhysicalEntityTable(
        "openiddict_authorizations",
        [
            Column("subject", "subject", isNullable: true),
            Column("status", "status", isNullable: true),
            Column("expiration", "expiration", PortablePhysicalType.DateTime, isNullable: true),
            CollectionColumn("scope", "scopes")
        ],
        Envelope,
        [
            OrderedIndex(
                AuthorizationIdIndex.Identity,
                Envelope.IdComparisonKeyColumn),
            Index(AuthorizationSubjectIndex.Identity, "subject", "status", "expiration")
        ]);

    public static PhysicalTableDefinition CreateScopeDefinition() => PhysicalTableDefinition.PhysicalEntityTable(
        "openiddict_scopes",
        [
            Column("name", "name", isNullable: true),
            CollectionColumn("resource", "resources")
        ],
        Envelope,
        [
            OrderedIndex(
                ScopeIdIndex.Identity,
                Envelope.IdComparisonKeyColumn),
            UniqueIndex(ScopeNameIndex.Identity, "name")
        ]);

    public static PhysicalTableDefinition CreateTokenDefinition() => PhysicalTableDefinition.PhysicalEntityTable(
        "openiddict_tokens",
        [
            Column("applicationId", "applicationId", length: MaxIndexedIdentifierLength, isNullable: true),
            Column("authorizationId", "authorizationId", length: MaxIndexedIdentifierLength, isNullable: true),
            Column("creationDate", "creationDate", PortablePhysicalType.DateTime, isNullable: true),
            Column("expiration", "expiration", PortablePhysicalType.DateTime, isNullable: true),
            Column("referenceId", "referenceId", length: MaxIndexedIdentifierLength, isNullable: true),
            Column("status", "status", length: MaxIndexedStatusLength, isNullable: true),
            Column("subject", "subject", length: MaxIndexedSubjectLength, isNullable: true),
            Column("type", "type", length: MaxIndexedTypeLength, isNullable: true)
        ],
        Envelope,
        [
            OrderedIndex(
                TokenIdIndex.Identity,
                Envelope.IdComparisonKeyColumn),
            Index(TokenApplicationIndex.Identity, "applicationId", "status"),
            Index(TokenAuthorizationIndex.Identity, "authorizationId", "status"),
            UniqueIndex(TokenReferenceIndex.Identity, "referenceId"),
            Index(TokenSubjectIndex.Identity, "subject", "status")
        ]);

    private static readonly DocumentEnvelopeDefinition Envelope = new();

    private static readonly LogicalIndexDeclaration ApplicationIdIndex =
        KeywordIndex("openiddict-application-by-id", "id");
    private static readonly LogicalIndexDeclaration ClientIdIndex =
        KeywordIndex("openiddict-application-by-client-id", "clientId", unique: true);
    private static readonly LogicalIndexDeclaration ApplicationRedirectUriIndex =
        CollectionIndex("openiddict-application-by-redirect-uri", "redirectUris");
    private static readonly LogicalIndexDeclaration ApplicationPostLogoutRedirectUriIndex =
        CollectionIndex("openiddict-application-by-post-logout-redirect-uri", "postLogoutRedirectUris");
    private static readonly LogicalIndexDeclaration AuthorizationIdIndex =
        KeywordIndex("openiddict-authorization-by-id", "id");
    private static readonly LogicalIndexDeclaration AuthorizationSubjectIndex = CompoundIndex(
        "openiddict-authorization-by-subject-status-expiration", "subject", "status", "expiration");
    private static readonly LogicalIndexDeclaration AuthorizationScopeIndex =
        CollectionIndex("openiddict-authorization-by-scope", "scopes");
    private static readonly LogicalIndexDeclaration ScopeIdIndex =
        KeywordIndex("openiddict-scope-by-id", "id");
    private static readonly LogicalIndexDeclaration ScopeNameIndex =
        KeywordIndex("openiddict-scope-by-name", "name", unique: true);
    private static readonly LogicalIndexDeclaration ScopeResourceIndex =
        CollectionIndex("openiddict-scope-by-resource", "resources");
    private static readonly LogicalIndexDeclaration TokenIdIndex =
        KeywordIndex("openiddict-token-by-id", "id");
    private static readonly LogicalIndexDeclaration TokenApplicationIndex = CompoundIndex(
        "openiddict-token-by-application-id", "applicationId", "status");
    private static readonly LogicalIndexDeclaration TokenAuthorizationIndex = CompoundIndex(
        "openiddict-token-by-authorization-id", "authorizationId", "status");
    private static readonly LogicalIndexDeclaration TokenReferenceIndex = KeywordIndex("openiddict-token-by-reference-id", "referenceId", unique: true);
    private static readonly LogicalIndexDeclaration TokenSubjectIndex = CompoundIndex(
        "openiddict-token-by-subject", "subject", "status");
    private static readonly BoundedQueryDeclaration ApplicationListQuery =
        UnfilteredListQuery(ListApplicationsQuery, ApplicationIdIndex);
    private static readonly BoundedQueryDeclaration ClientIdQuery =
        PointQuery(FindApplicationByClientIdQuery, ClientIdIndex);
    private static readonly BoundedQueryDeclaration ApplicationRedirectUriQuery =
        CollectionQuery(FindApplicationByRedirectUriQuery, ApplicationRedirectUriIndex);
    private static readonly BoundedQueryDeclaration ApplicationPostLogoutRedirectUriQuery =
        CollectionQuery(FindApplicationByPostLogoutRedirectUriQuery, ApplicationPostLogoutRedirectUriIndex);
    private static readonly BoundedQueryDeclaration AuthorizationListQuery =
        UnfilteredListQuery(ListAuthorizationsQuery, AuthorizationIdIndex);
    private static readonly BoundedQueryDeclaration AuthorizationSubjectQuery =
        RangeQuery(FindAuthorizationBySubjectQuery, AuthorizationSubjectIndex);
    private static readonly BoundedQueryDeclaration AuthorizationScopeQuery =
        CollectionQuery(FindAuthorizationByScopeQuery, AuthorizationScopeIndex);
    private static readonly BoundedQueryDeclaration ScopeListQuery =
        UnfilteredListQuery(ListScopesQuery, ScopeIdIndex);
    private static readonly BoundedQueryDeclaration ScopeNameQuery =
        PointQuery(FindScopeByNameQuery, ScopeNameIndex);
    private static readonly BoundedQueryDeclaration ScopeNamesQuery = new(
        FindScopesByNamesQuery,
        ScopeNameIndex.Identity,
        new HashSet<PortableQueryOperation> { PortableQueryOperation.In },
        QuerySortSupport.None,
        QueryPagingSupport.Offset,
        BoundedQueryExecutionClass.ScaleBearing,
        supportsTotalCount: true,
        predicateFields:
        [
            new BoundedQueryPredicateField(
                "name",
                new HashSet<PortableQueryOperation> { PortableQueryOperation.In })
        ],
        resultOperations: new HashSet<BoundedQueryResultOperation>
        {
            BoundedQueryResultOperation.Documents,
            BoundedQueryResultOperation.Count
        });
    private static readonly BoundedQueryDeclaration ScopeResourceQuery = new(
        FindScopeByResourceQuery,
        ScopeResourceIndex.Identity,
        new HashSet<PortableQueryOperation> { PortableQueryOperation.CollectionContains },
        QuerySortSupport.None,
        QueryPagingSupport.Offset,
        BoundedQueryExecutionClass.ScaleBearing,
        supportsTotalCount: true,
        predicateFields:
        [
            new BoundedQueryPredicateField(
                "resources",
                new HashSet<PortableQueryOperation> { PortableQueryOperation.CollectionContains })
        ],
        resultOperations: new HashSet<BoundedQueryResultOperation>
        {
            BoundedQueryResultOperation.Documents,
            BoundedQueryResultOperation.Count
        });
    private static readonly BoundedQueryDeclaration TokenListQuery =
        UnfilteredListQuery(ListTokensQuery, TokenIdIndex);
    private static readonly BoundedQueryDeclaration TokenCompoundQuery = new(
        FindTokenQuery,
        TokenIdIndex.Identity,
        new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal },
        QuerySortSupport.Ascending,
        QueryPagingSupport.Offset,
        BoundedQueryExecutionClass.ScaleBearing,
        supportsDisjunction: true,
        sortFields: [new BoundedQuerySortField("id", PhysicalSortDirection.Ascending)],
        predicateFields: [],
        residualPredicateFields:
        [
            Residual("subject", IndexValueKind.Keyword, PortableQueryOperation.Equal),
            Residual("applicationId", IndexValueKind.Keyword, PortableQueryOperation.Equal),
            Residual("status", IndexValueKind.Keyword, PortableQueryOperation.Equal),
            Residual("type", IndexValueKind.Keyword, PortableQueryOperation.Equal)
        ]);
    private static readonly BoundedQueryDeclaration TokenApplicationQuery =
        EqualityQuery(FindTokenByApplicationIdQuery, TokenApplicationIndex);
    private static readonly BoundedQueryDeclaration TokenAuthorizationQuery =
        EqualityQuery(FindTokenByAuthorizationIdQuery, TokenAuthorizationIndex);
    private static readonly BoundedQueryDeclaration TokenReferenceQuery = PointQuery(FindTokenByReferenceIdQuery, TokenReferenceIndex);
    private static readonly BoundedQueryDeclaration TokenSubjectQuery =
        EqualityQuery(FindTokenBySubjectQuery, TokenSubjectIndex);
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
        int? length = null,
        bool isNullable = false) =>
        new(
            physicalName,
            jsonPath,
            type,
            Length: type == PortablePhysicalType.String ? length ?? 256 : null,
            IsNullable: isNullable);

    private static ProjectedColumnDefinition CollectionColumn(
        string physicalName,
        string jsonPath,
        int length = 256) =>
        new(
            physicalName,
            jsonPath,
            PortablePhysicalType.String,
            Length: length,
            IsNullable: true,
            Cardinality: ProjectionCardinality.CollectionElements,
            MaxCollectionElements: 64);

    private static LogicalIndexDeclaration KeywordIndex(string identity, string path, bool unique = false) =>
        new(identity, [new IndexField(path, IndexValueKind.Keyword)], IndexValueKind.Keyword, unique, MissingValueBehavior.Excluded);

    private static LogicalIndexDeclaration CollectionIndex(string identity, string path) =>
        new(identity, [new IndexField(path, IndexValueKind.String)], IndexValueKind.String, false, MissingValueBehavior.Excluded);

    private static LogicalIndexDeclaration CompoundIndex(string identity, params string[] paths) =>
        new(identity,
            paths.Select(path => new IndexField(path, path == "expiration" ? IndexValueKind.DateTime : IndexValueKind.Keyword)).ToArray(),
            IndexValueKind.Keyword,
            isUnique: false,
            MissingValueBehavior.Excluded);

    private static PhysicalIndexDefinition UniqueIndex(string identity, params string[] columns) =>
        Index(identity, columns, isUnique: true);

    private static PhysicalIndexDefinition OrderedIndex(string identity, params string[] columns) =>
        new(
            identity,
            columns.Select((column, index) =>
                new PhysicalIndexColumnDefinition(column, index, PhysicalSortDirection.Ascending)).ToArray(),
            isUnique: false,
            missingValueBehavior: MissingValueBehavior.Excluded);

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

    private static BoundedQueryDeclaration UnfilteredListQuery(
        string identity,
        LogicalIndexDeclaration index) =>
        new(
            identity,
            index.Identity,
            new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal },
            QuerySortSupport.Ascending,
            QueryPagingSupport.Offset,
            BoundedQueryExecutionClass.ScaleBearing,
            supportsTotalCount: true,
            sortFields: [new BoundedQuerySortField("id", PhysicalSortDirection.Ascending)],
            predicateFields: [],
            resultOperations: new HashSet<BoundedQueryResultOperation>
            {
                BoundedQueryResultOperation.Documents,
                BoundedQueryResultOperation.Count
            });

    private static BoundedQueryDeclaration EqualityQuery(
        string identity,
        LogicalIndexDeclaration index) =>
        new(
            identity,
            index.Identity,
            new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal },
            QuerySortSupport.None,
            QueryPagingSupport.Offset,
            BoundedQueryExecutionClass.ScaleBearing,
            predicateFields: index.Fields.Select(field => new BoundedQueryPredicateField(
                field.Path,
                new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal })).ToArray());

    private static BoundedQueryDeclaration CollectionQuery(string identity, LogicalIndexDeclaration index) => new(
        identity,
        index.Identity,
        new HashSet<PortableQueryOperation>
        {
            PortableQueryOperation.CollectionContains,
            PortableQueryOperation.CollectionContainsAll
        },
        QuerySortSupport.None,
        QueryPagingSupport.Offset,
        BoundedQueryExecutionClass.ScaleBearing,
        supportsTotalCount: true);

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

    private static BoundedQueryResidualPredicateField Residual(
        string path,
        IndexValueKind valueKind,
        PortableQueryOperation operation) =>
        new(path, valueKind, new HashSet<PortableQueryOperation> { operation });

}
