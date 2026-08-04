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

    public const string FindApplicationByClientIdQuery = "find-application-by-client-id";
    public const string FindApplicationByRedirectUriQuery = "find-application-by-redirect-uri";
    public const string FindApplicationByPostLogoutRedirectUriQuery = "find-application-by-post-logout-redirect-uri";
    public const string FindAuthorizationBySubjectQuery = "find-authorization-by-subject";
    public const string FindAuthorizationByScopeQuery = "find-authorization-by-scope";
    public const string FindScopeByNameQuery = "find-scope-by-name";
    public const string FindScopeByResourceQuery = "find-scope-by-resource";
    public const string FindTokenByReferenceIdQuery = "find-token-by-reference-id";
    public const string FindTokenBySubjectQuery = "find-token-by-subject";

    /// <summary>
    /// The expiry field and cleanup query for <see cref="OpenIddictGroundworkJson.MutationReceiptDocumentKind"/>
    /// (spec 106 T030). Mirrors <c>IdentityStorageManifest.MutationReceiptExpiresAtField</c> /
    /// <c>ListExpiredMutationReceiptsQuery</c> so <c>OpenIddictGroundworkAtomicWrite</c> can bound its
    /// opportunistic cleanup query the same way the identity atomic-mutation executor does.
    /// </summary>
    public const string MutationReceiptExpiresAtField = "expiresAt";

    public const string ListExpiredMutationReceiptsQuery = "list-expired-mutation-receipts";

    public static IReadOnlyList<GroundworkStorageRouteRequirement> BoundedRoutes { get; } =
    [
        Route(OpenIddictGroundworkJson.ApplicationDocumentKind, FindApplicationByClientIdQuery),
        Route(OpenIddictGroundworkJson.ApplicationDocumentKind, FindApplicationByRedirectUriQuery),
        Route(OpenIddictGroundworkJson.ApplicationDocumentKind, FindApplicationByPostLogoutRedirectUriQuery),
        Route(OpenIddictGroundworkJson.AuthorizationDocumentKind, FindAuthorizationBySubjectQuery),
        Route(OpenIddictGroundworkJson.AuthorizationDocumentKind, FindAuthorizationByScopeQuery),
        Route(OpenIddictGroundworkJson.ScopeDocumentKind, FindScopeByNameQuery),
        Route(OpenIddictGroundworkJson.ScopeDocumentKind, FindScopeByResourceQuery),
        Route(OpenIddictGroundworkJson.TokenDocumentKind, FindTokenByReferenceIdQuery),
        Route(OpenIddictGroundworkJson.TokenDocumentKind, FindTokenBySubjectQuery),
        Route(OpenIddictGroundworkJson.MutationReceiptDocumentKind, ListExpiredMutationReceiptsQuery)
    ];

    public static string Fingerprint { get; } = Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(string.Join('|',
            ManifestIdentity,
            ManifestVersion,
            OpenIddictGroundworkJson.ApplicationDocumentKind,
            OpenIddictGroundworkJson.AuthorizationDocumentKind,
            OpenIddictGroundworkJson.ScopeDocumentKind,
            OpenIddictGroundworkJson.TokenDocumentKind,
            OpenIddictGroundworkJson.MutationReceiptDocumentKind,
            string.Join('|', BoundedRoutes.Select(route => route.RouteIdentity))))));

    public static StorageManifest Create() => new(
        new StorageManifestIdentity(ManifestIdentity),
        new StorageManifestOwner("elsa.foundation.identity"),
        new StorageManifestVersion(ManifestVersion),
        [
            Unit(OpenIddictGroundworkJson.ApplicationDocumentKind, "OpenIddict application", CreateApplicationDefinition(),
                [ClientIdIndex, ApplicationRedirectUriIndex, ApplicationPostLogoutRedirectUriIndex],
                [ClientIdQuery, ApplicationRedirectUriQuery, ApplicationPostLogoutRedirectUriQuery]),
            Unit(OpenIddictGroundworkJson.AuthorizationDocumentKind, "OpenIddict authorization", CreateAuthorizationDefinition(),
                [AuthorizationSubjectV2Index, AuthorizationScopeIndex],
                [AuthorizationSubjectQuery, AuthorizationScopeQuery]),
            Unit(OpenIddictGroundworkJson.ScopeDocumentKind, "OpenIddict scope", CreateScopeDefinition(),
                [ScopeNameIndex, ScopeResourceIndex],
                [ScopeNameQuery, ScopeResourceQuery]),
            Unit(OpenIddictGroundworkJson.TokenDocumentKind, "OpenIddict token", CreateTokenDefinition(),
                [TokenReferenceIndex, TokenSubjectIndex, TokenSubjectV2Index], [TokenReferenceQuery, TokenSubjectQuery]),
            Unit(OpenIddictGroundworkJson.MutationReceiptDocumentKind, "OpenIddict mutation receipt", CreateMutationReceiptDefinition(),
                [MutationReceiptExpiryIndex], [MutationReceiptExpiryQuery])
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
        [UniqueIndex(ClientIdIndex.Identity, "clientId")]);

    public static PhysicalTableDefinition CreateAuthorizationDefinition() => PhysicalTableDefinition.PhysicalEntityTable(
        "openiddict_authorizations",
        [
            Column("applicationId", "applicationId", isNullable: true),
            Column("creationDate", "creationDate", PortablePhysicalType.DateTime, isNullable: true),
            Column("subject", "subject", isNullable: true),
            Column("status", "status", isNullable: true),
            Column("type", "type", isNullable: true),
            CollectionColumn("scope", "scopes")
        ],
        Envelope,
        [
            Index(AuthorizationSubjectV2Index.Identity, "subject", Envelope.IdLookupKeyColumn)
        ]);

    public static PhysicalTableDefinition CreateScopeDefinition() => PhysicalTableDefinition.PhysicalEntityTable(
        "openiddict_scopes",
        [
            Column("name", "name", isNullable: true),
            CollectionColumn("resource", "resources")
        ],
        Envelope,
        [UniqueIndex(ScopeNameIndex.Identity, "name")]);

    public static PhysicalTableDefinition CreateTokenDefinition() => PhysicalTableDefinition.PhysicalEntityTable(
        "openiddict_tokens",
        [
            Column("applicationId", "applicationId", isNullable: true),
            Column("authorizationId", "authorizationId", isNullable: true),
            Column("creationDate", "creationDate", PortablePhysicalType.DateTime, isNullable: true),
            Column("expirationDate", "expirationDate", PortablePhysicalType.DateTime, isNullable: true),
            Column("referenceId", "referenceId", isNullable: true),
            Column("subject", "subject", isNullable: true),
            Column("status", "status", isNullable: true),
            Column("type", "type", isNullable: true)
        ],
        Envelope,
        [
            UniqueIndex(TokenReferenceIndex.Identity, "referenceId"),
            Index(TokenSubjectIndex.Identity, "subject", "status", "expirationDate"),
            Index(TokenSubjectV2Index.Identity, "subject", Envelope.IdLookupKeyColumn)
        ]);

    /// <summary>
    /// The replay-safe mutation-receipt table (spec 106 T030). It carries only the expiry field the
    /// cleanup query needs; the receipt payload itself lives in the envelope content like every other
    /// OpenIddict Groundwork document.
    /// </summary>
    public static PhysicalTableDefinition CreateMutationReceiptDefinition() => PhysicalTableDefinition.PhysicalEntityTable(
        "openiddict_mutation_receipts",
        [
            Column("expiresAt", "expiresAt", PortablePhysicalType.DateTime, isNullable: false)
        ],
        Envelope,
        // The cursor-paged, sorted cleanup query needs a deterministic tie-break beyond "expiresAt" alone
        // (two receipts can expire at the same instant), mirroring the subject/token cursor indexes above.
        [Index(MutationReceiptExpiryIndex.Identity, "expiresAt", Envelope.IdLookupKeyColumn)]);

    private static readonly DocumentEnvelopeDefinition Envelope = new();

    /// <summary>Backs the opportunistic expired-receipt cleanup query with a bounded ascending scan.</summary>
    private static readonly LogicalIndexDeclaration MutationReceiptExpiryIndex =
        DateTimeIndex("openiddict-mutation-receipt-by-expiry", MutationReceiptExpiresAtField);

    private static readonly BoundedQueryDeclaration MutationReceiptExpiryQuery =
        ExpiredReceiptQuery(ListExpiredMutationReceiptsQuery, MutationReceiptExpiryIndex);

    private static readonly LogicalIndexDeclaration ClientIdIndex = KeywordIndex("openiddict-application-by-client-id", "clientId", unique: true);
    private static readonly LogicalIndexDeclaration ApplicationRedirectUriIndex =
        CollectionIndex("openiddict-application-by-redirect-uri", "redirectUris");
    private static readonly LogicalIndexDeclaration ApplicationPostLogoutRedirectUriIndex =
        CollectionIndex("openiddict-application-by-post-logout-redirect-uri", "postLogoutRedirectUris");
    // Spec 106 is greenfield: the earlier four-field authorization index was never part of the
    // retained preview.95 schema and exceeds SQL Server's 1,700-byte key limit. It is intentionally
    // replaced, not upgraded; manually materialized pre-release schemas are outside this contract.
    private static readonly LogicalIndexDeclaration AuthorizationSubjectV2Index = KeywordIndex(
        "openiddict-authorization-by-subject-application-status-type-v2", "subject");
    private static readonly LogicalIndexDeclaration AuthorizationScopeIndex =
        CollectionIndex("openiddict-authorization-by-scope", "scopes");
    private static readonly LogicalIndexDeclaration ScopeNameIndex = KeywordIndex("openiddict-scope-by-name", "name", unique: true);
    private static readonly LogicalIndexDeclaration ScopeResourceIndex =
        CollectionIndex("openiddict-scope-by-resource", "resources");
    private static readonly LogicalIndexDeclaration TokenReferenceIndex = KeywordIndex("openiddict-token-by-reference-id", "referenceId", unique: true);
    private static readonly LogicalIndexDeclaration TokenSubjectIndex = CompoundIndex(
        "openiddict-token-by-subject-status-expiration", "subject", "status", "expirationDate");
    private static readonly LogicalIndexDeclaration TokenSubjectV2Index = KeywordIndex(
        "openiddict-token-by-subject-status-expiration-v2", "subject");

    private static readonly BoundedQueryDeclaration ClientIdQuery = PointQuery(FindApplicationByClientIdQuery, ClientIdIndex);
    private static readonly BoundedQueryDeclaration ApplicationRedirectUriQuery =
        CollectionQuery(FindApplicationByRedirectUriQuery, ApplicationRedirectUriIndex);
    private static readonly BoundedQueryDeclaration ApplicationPostLogoutRedirectUriQuery =
        CollectionQuery(FindApplicationByPostLogoutRedirectUriQuery, ApplicationPostLogoutRedirectUriIndex);
    private static readonly BoundedQueryDeclaration AuthorizationSubjectQuery = SubjectQuery(FindAuthorizationBySubjectQuery, AuthorizationSubjectV2Index);
    private static readonly BoundedQueryDeclaration AuthorizationScopeQuery =
        CollectionQuery(FindAuthorizationByScopeQuery, AuthorizationScopeIndex);
    // Admits In alongside Equal so a finite name set resolves as one membership query rather than one
    // point lookup per name. OpenIddict's IOpenIddictScopeStore.FindByNamesAsync takes a name array.
    private static readonly BoundedQueryDeclaration ScopeNameQuery = new(
        FindScopeByNameQuery,
        ScopeNameIndex.Identity,
        new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal, PortableQueryOperation.In },
        QuerySortSupport.None,
        QueryPagingSupport.Offset,
        BoundedQueryExecutionClass.ScaleBearing,
        predicateFields:
        [
            new BoundedQueryPredicateField(
                ScopeNameIndex.Fields.Single().Path,
                new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal, PortableQueryOperation.In })
        ]);

    private static readonly BoundedQueryDeclaration ScopeResourceQuery =
        CollectionQuery(FindScopeByResourceQuery, ScopeResourceIndex);
    private static readonly BoundedQueryDeclaration TokenReferenceQuery = PointQuery(FindTokenByReferenceIdQuery, TokenReferenceIndex);
    private static readonly BoundedQueryDeclaration TokenSubjectQuery = SubjectQuery(FindTokenBySubjectQuery, TokenSubjectV2Index);
    private static StorageUnit Unit(
        string identity,
        string displayName,
        PhysicalTableDefinition definition,
        IReadOnlyCollection<LogicalIndexDeclaration> indexes,
        IReadOnlyCollection<BoundedQueryDeclaration> queries) =>
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
                queries.ToArray()));

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

    private static LogicalIndexDeclaration DateTimeIndex(string identity, string path) =>
        new(identity, [new IndexField(path, IndexValueKind.DateTime)], IndexValueKind.DateTime, false, MissingValueBehavior.Excluded);

    private static LogicalIndexDeclaration CompoundIndex(string identity, params string[] paths) =>
        new(identity,
            paths.Select(path => new IndexField(path, path == "expirationDate" ? IndexValueKind.DateTime : IndexValueKind.Keyword)).ToArray(),
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

    /// <summary>
    /// Offset paging with no declared sort is the only shape Groundwork admits for a collection-membership
    /// query. Cursor paging was tried and rejected at plan compilation with <c>GW-QUERY-008</c>: no
    /// provider certifies cursor paging or latest-per-key selection over element-to-owner shapes. That
    /// makes exhaustive iteration unavailable here, so callers read one bounded page and fail closed above
    /// it rather than truncating silently. Lifting this needs upstream provider certification, not a
    /// change to this declaration.
    /// </summary>
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

    /// <summary>
    /// Ascending, cursor-paged scan of the oldest-expiring receipts, mirroring
    /// <c>IdentityStorageManifest.ExpiredReceiptQuery</c> so <c>OpenIddictGroundworkAtomicWrite</c> can bound
    /// its opportunistic cleanup to a finite oldest-first page.
    /// </summary>
    private static BoundedQueryDeclaration ExpiredReceiptQuery(string identity, LogicalIndexDeclaration index)
    {
        var path = index.Fields.Single().Path;
        return new BoundedQueryDeclaration(
            identity,
            index.Identity,
            new HashSet<PortableQueryOperation> { PortableQueryOperation.LessThanOrEqual },
            QuerySortSupport.Ascending,
            QueryPagingSupport.Cursor,
            BoundedQueryExecutionClass.ScaleBearing,
            sortFields: [new BoundedQuerySortField(path, PhysicalSortDirection.Ascending)],
            predicateFields: [new BoundedQueryPredicateField(path, new HashSet<PortableQueryOperation> { PortableQueryOperation.LessThanOrEqual })]);
    }

    private static BoundedQueryDeclaration SubjectQuery(string identity, LogicalIndexDeclaration index) => new(
        identity,
        index.Identity,
        new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal },
        QuerySortSupport.None,
        QueryPagingSupport.Cursor,
        BoundedQueryExecutionClass.ScaleBearing,
        predicateFields: [new BoundedQueryPredicateField(
            index.Fields[0].Path,
            new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal })]);
}
