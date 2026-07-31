#pragma warning disable GW0001, GW0002, GW0003 // Identity still bridges legacy portable declarations into physical storage during the Groundwork migration.
using Groundwork.Core.Indexing;
using Groundwork.Core.Intents;
using Groundwork.Core.Manifests;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Physicalization;
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
    private static readonly DocumentEnvelopeDefinition Envelope = new();
    private const string AdditiveIndexVersionSuffix = "-v2";

    public const int MaxAggregateRelationshipEntries = 512;
    public const string SchemaVersion = "1.0.6";
    public const int ProjectedLookupColumnLength = 400;
    public const int SqlServerStorageScopeKeyBytes = 256;
    public const int SqlServerDocumentIdentityLookupKeyBytes = 32;
    public const int SqlServerDateTime2KeyBytes = 10;
    public const int SqlServerUnicodeBytesPerCodeUnit = 2;
    public const int SqlServerMaxNonclusteredIndexKeyBytes = 1_700;
    public const int MaxBoundedQueryParameters = 1;

    public const string IdentityUserDocumentKind = "identityUser";
    public const string IdentityRoleDocumentKind = "identityRole";
    public const string IdentityApplicationDocumentKind = "identityApplication";
    public const string IdentityCredentialDocumentKind = "identityCredential";
    public const string IdentityClaimMappingDocumentKind = "identityClaimMapping";
    public const string IdentityProviderConfigurationDocumentKind = "identityProviderConfiguration";
    public const string IdentityGlobalProviderConfigurationDocumentKind = "identityGlobalProviderConfiguration";
    public const string UserClaimDocumentKind = "identityUserClaim";
    public const string RoleClaimDocumentKind = "identityRoleClaim";
    public const string ExternalLoginDocumentKind = "identityExternalLogin";
    public const string UserRoleDocumentKind = "identityUserRole";
    public const string UserTokenDocumentKind = "identityUserToken";
    public const string IdentityTenantMembershipDocumentKind = "identityTenantMembership";
    public const string UserNameReservationDocumentKind = "identityUserNameReservation";
    public const string EmailReservationDocumentKind = "identityEmailReservation";
    public const string RoleNameReservationDocumentKind = "identityRoleNameReservation";
    public const string IdentityMutationReceiptDocumentKind = "identityMutationReceipt";

    public const string NormalizedUserNameField = "normalizedUserName";
    public const string NormalizedEmailField = "normalizedEmail";
    public const string NormalizedRoleNameField = "normalizedRoleName";
    public const string NormalizedUserNameKeyField = "normalizedUserNameKey";
    public const string NormalizedEmailKeyField = "normalizedEmailKey";
    public const string NormalizedRoleNameKeyField = "normalizedRoleNameKey";
    public const string UserLookupKeyField = "userLookupKey";
    public const string RoleLookupKeyField = "roleLookupKey";
    public const string ProviderLookupKeyField = "providerLookupKey";
    public const string ClaimKeyField = "claimKey";
    public const string TenantIdField = "tenantId";
    public const string MutationReceiptExpiresAtField = "expiresAt";

    public const string FindUserByNormalizedNameQuery = "find-user-by-normalized-name";
    public const string FindUserByNormalizedEmailQuery = "find-user-by-normalized-email";
    public const string FindRoleByNormalizedNameQuery = "find-role-by-normalized-name";
    public const string ListRolesByTenantQuery = "list-roles-by-tenant";
    public const string ListUserClaimsQuery = "list-user-claims";
    public const string FindUsersByClaimQuery = "find-users-by-claim";
    public const string ListRoleClaimsQuery = "list-role-claims";
    public const string ListUserRolesQuery = "list-user-roles";
    public const string ListRoleUsersQuery = "list-role-users";
    public const string ListUserLoginsQuery = "list-user-logins";
    public const string ListClaimMappingsByProviderQuery = "list-claim-mappings-by-provider";
    public const string ListExpiredMutationReceiptsQuery = "list-expired-mutation-receipts";

    public static StorageManifest Create() => new(
        new StorageManifestIdentity("elsa-identity"),
        new StorageManifestOwner("elsa.identity"),
        new StorageManifestVersion(SchemaVersion),
        [
            Unit(
                IdentityUserDocumentKind,
                "Identity User",
                "identity_users",
                [Keyword("identity-user-by-normalized-name", NormalizedUserNameKeyField), Keyword("identity-user-by-normalized-email", NormalizedEmailKeyField)],
                [Query(FindUserByNormalizedNameQuery, "identity-user-by-normalized-name"), Query(FindUserByNormalizedEmailQuery, "identity-user-by-normalized-email")]),
            Unit(
                IdentityRoleDocumentKind,
                "Identity Role",
                "identity_roles",
                [Keyword("identity-role-by-normalized-name", NormalizedRoleNameKeyField), Keyword("identity-role-by-tenant", TenantIdField)],
                [Query(FindRoleByNormalizedNameQuery, "identity-role-by-normalized-name"), Query(ListRolesByTenantQuery, "identity-role-by-tenant")]),
            Unit(
                IdentityApplicationDocumentKind,
                "Identity Application",
                "identity_applications",
                [],
                []),
            Unit(
                IdentityCredentialDocumentKind,
                "Identity Credential",
                "identity_credentials",
                [],
                []),
            Unit(
                IdentityClaimMappingDocumentKind,
                "Identity Claim Mapping",
                "identity_claim_mappings",
                [Keyword("identity-claim-mapping-by-provider", ProviderLookupKeyField)],
                [Query(ListClaimMappingsByProviderQuery, "identity-claim-mapping-by-provider")]),
            Unit(
                IdentityProviderConfigurationDocumentKind,
                "Identity Tenant Provider Configuration",
                "identity_provider_configurations",
                [],
                []),
            Unit(
                IdentityGlobalProviderConfigurationDocumentKind,
                "Identity Global Provider Configuration",
                "identity_global_provider_configurations",
                [],
                [],
                TenancyPolicy.Global),
            Unit(
                UserClaimDocumentKind,
                "Identity User Claim",
                "identity_user_claims",
                [Keyword("identity-user-claim-by-user", UserLookupKeyField), Keyword("identity-user-claim-by-claim", ClaimKeyField)],
                [Query(ListUserClaimsQuery, "identity-user-claim-by-user"), Query(FindUsersByClaimQuery, "identity-user-claim-by-claim")]),
            Unit(
                RoleClaimDocumentKind,
                "Identity Role Claim",
                "identity_role_claims",
                [Keyword("identity-role-claim-by-role", RoleLookupKeyField)],
                [Query(ListRoleClaimsQuery, "identity-role-claim-by-role")]),
            Unit(
                ExternalLoginDocumentKind,
                "Identity External Login",
                "identity_external_logins",
                [Keyword("identity-login-by-user", UserLookupKeyField)],
                [Query(ListUserLoginsQuery, "identity-login-by-user")]),
            Unit(
                UserRoleDocumentKind,
                "Identity User Role",
                "identity_user_roles",
                [Keyword("identity-user-role-by-user", UserLookupKeyField), Keyword("identity-user-role-by-role", RoleLookupKeyField)],
                [Query(ListUserRolesQuery, "identity-user-role-by-user"), Query(ListRoleUsersQuery, "identity-user-role-by-role")]),
            Unit(
                UserTokenDocumentKind,
                "Identity User Token",
                "identity_user_tokens",
                [],
                []),
            Unit(
                IdentityTenantMembershipDocumentKind,
                "Identity Tenant Membership",
                "identity_tenant_memberships",
                [],
                []),
            Unit(
                UserNameReservationDocumentKind,
                "Identity User Name Reservation",
                "identity_user_name_reservations",
                [],
                []),
            Unit(
                EmailReservationDocumentKind,
                "Identity Email Reservation",
                "identity_email_reservations",
                [],
                []),
            Unit(
                RoleNameReservationDocumentKind,
                "Identity Role Name Reservation",
                "identity_role_name_reservations",
                [],
                []),
            Unit(
                IdentityMutationReceiptDocumentKind,
                "Identity Mutation Receipt",
                "identity_mutation_receipts",
                [DateTimeIndex("identity-mutation-receipt-by-expiry", MutationReceiptExpiresAtField)],
                [ExpiredReceiptQuery("identity-mutation-receipt-by-expiry")])
        ],
        new HashSet<string> { "schema-history", "optimistic-concurrency" },
        []);

    private static StorageUnit Unit(
        string documentKind,
        string label,
        string tableName,
        IndexDeclaration[] indexes,
        PortableQueryDeclaration[] queries,
        TenancyPolicy? tenancy = null)
    {
        return new StorageUnit(
            new StorageUnitIdentity(documentKind),
            label,
            StorageIntent.PortableDocument(),
            LifecyclePolicy.Mutable,
            IdentityPolicy.StringId(),
            tenancy ?? TenancyPolicy.Scoped,
            ConcurrencyPolicy.Optimistic(),
            SerializationPolicy.Json(),
            indexes,
            queries,
            PhysicalizationPolicy.Portable)
        {
            PhysicalStorage = new StorageUnitPhysicalStorage(
                StorageUnitProvisioningMode.Declared,
                PhysicalStoragePolicy.Explicit(PhysicalTable(documentKind, tableName, indexes, queries)),
                logicalIndexes: indexes
                    .SelectMany(index =>
                    {
                        var legacy = LogicalIndex(index, index.Identity);
                        return RequiresAdditiveV2(index, queries)
                            ? new[] { legacy, LogicalIndex(index, V2(index.Identity)) }
                            : [legacy];
                    })
                    .ToArray(),
                boundedQueries: queries
                    .Select(query =>
                    {
                        var index = indexes.Single(index => index.Identity == query.IndexIdentity);
                        return BoundedQuery(
                            query,
                            index,
                            RequiresAdditiveV2(index, queries) ? V2(index.Identity) : index.Identity);
                    })
                    .ToArray())
        };
    }

    private static PhysicalTableDefinition PhysicalTable(
        string documentKind,
        string tableName,
        IReadOnlyCollection<IndexDeclaration> indexes,
        IReadOnlyCollection<PortableQueryDeclaration> queries)
    {
        var projectedColumns = indexes
            .SelectMany(index => index.Fields.Select(field => new
            {
                field.Path,
                ValueKind = field.ValueKind ?? index.ValueKind
            }))
            .GroupBy(field => field.Path, StringComparer.Ordinal)
            .Select(group => new ProjectedColumnDefinition(
                group.Key,
                group.Key,
                PhysicalType(group.Select(field => field.ValueKind).Distinct().Single()),
                Length: group.First().ValueKind is IndexValueKind.String or IndexValueKind.Keyword
                    ? ProjectedLookupColumnLength
                    : null,
                IsNullable: IsOptionalLookupProjection(documentKind, group.Key)))
            .ToArray();
        if (projectedColumns.Length == 0)
            return PhysicalTableDefinition.DedicatedDocumentTable(tableName);

        var physicalIndexes = indexes
            .SelectMany(index =>
            {
                var legacyColumns = new List<PhysicalIndexColumnDefinition>
                {
                    new(Envelope.StorageScopeColumn, 0)
                };
                legacyColumns.AddRange(index.Fields.Select((field, order) =>
                    new PhysicalIndexColumnDefinition(field.Path, order + 1)));

                var providerTieBreakPaging = queries
                    .Where(query => query.IndexIdentity == index.Identity)
                    .Select(query => query.PagingSupport)
                    .Where(paging => paging is QueryPagingSupport.Cursor or QueryPagingSupport.Offset)
                    .Distinct()
                    .ToArray();
                if (!index.IsUnique && providerTieBreakPaging.Length > 1)
                {
                    throw new InvalidOperationException(
                        $"Identity index '{index.Identity}' cannot mix cursor and offset provider identity tie-breaks.");
                }

                var legacy = new PhysicalIndexDefinition(
                    index.Identity,
                    legacyColumns,
                    missingValueBehavior: index.MissingValueBehavior);
                if (!RequiresAdditiveV2(index, queries))
                    return new[] { legacy };

                var versionedColumns = new List<PhysicalIndexColumnDefinition>(legacyColumns)
                {
                    new(
                        providerTieBreakPaging[0] == QueryPagingSupport.Cursor
                            ? Envelope.IdLookupKeyColumn
                            : Envelope.IdComparisonKeyColumn,
                        legacyColumns.Count)
                };
                return new[]
                {
                    legacy,
                    new PhysicalIndexDefinition(
                        V2(index.Identity),
                        versionedColumns,
                        missingValueBehavior: index.MissingValueBehavior)
                };
            })
            .ToArray();
        return PhysicalTableDefinition.PhysicalEntityTable(
            tableName,
            projectedColumns,
            Envelope,
            indexes: physicalIndexes);
    }

    private static BoundedQueryDeclaration BoundedQuery(
        PortableQueryDeclaration query,
        IndexDeclaration index,
        string indexIdentity)
    {
        var path = index.Fields.Single().Path;
        return new BoundedQueryDeclaration(
            query.Identity,
            indexIdentity,
            query.Operations,
            query.SortSupport,
            query.PagingSupport,
            BoundedQueryExecutionClass.ScaleBearing,
            sortFields: query.SortSupport == QuerySortSupport.None
                ? null
                : [new BoundedQuerySortField(path, PhysicalSortDirection.Ascending)],
            predicateFields: [new BoundedQueryPredicateField(path, query.Operations)]);
    }

    private static LogicalIndexDeclaration LogicalIndex(IndexDeclaration index, string identity) =>
        new(identity, index.Fields, index.ValueKind, index.IsUnique, index.MissingValueBehavior);

    private static bool RequiresAdditiveV2(
        IndexDeclaration index,
        IReadOnlyCollection<PortableQueryDeclaration> queries) =>
        !index.IsUnique && queries.Any(query =>
            query.IndexIdentity == index.Identity &&
            query.PagingSupport is QueryPagingSupport.Cursor or QueryPagingSupport.Offset);

    private static string V2(string identity) => $"{identity}{AdditiveIndexVersionSuffix}";

    private static PortableQueryDeclaration Query(string name, string indexName) => new(
        name,
        indexName,
        new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal },
        QuerySortSupport.None,
        QueryPagingSupport.Cursor);

    private static PortableQueryDeclaration ExpiredReceiptQuery(string indexName) => new(
        ListExpiredMutationReceiptsQuery,
        indexName,
        new HashSet<PortableQueryOperation> { PortableQueryOperation.LessThanOrEqual },
        QuerySortSupport.Ascending,
        QueryPagingSupport.Cursor);

    private static IndexDeclaration Keyword(string identity, params string[] fields) => new(
        identity,
        fields.Select(field => new IndexField(field)).ToArray(),
        IndexValueKind.Keyword,
        false,
        true,
        MissingValueBehavior.Excluded,
        new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal },
        IndexPhysicalizationPolicy.Optimized);

    private static IndexDeclaration DateTimeIndex(string identity, string field) => new(
        identity,
        [new IndexField(field)],
        IndexValueKind.DateTime,
        false,
        true,
        MissingValueBehavior.Excluded,
        new HashSet<PortableQueryOperation> { PortableQueryOperation.LessThanOrEqual },
        IndexPhysicalizationPolicy.Optimized);

    private static PortablePhysicalType PhysicalType(IndexValueKind valueKind) => valueKind switch
    {
        IndexValueKind.String or IndexValueKind.Keyword => PortablePhysicalType.String,
        IndexValueKind.Number => PortablePhysicalType.Decimal,
        IndexValueKind.Boolean => PortablePhysicalType.Boolean,
        IndexValueKind.DateTime => PortablePhysicalType.DateTime,
        _ => throw new ArgumentOutOfRangeException(nameof(valueKind), valueKind, null)
    };

    private static bool IsOptionalLookupProjection(string documentKind, string field) =>
        documentKind switch
        {
            IdentityUserDocumentKind => field is NormalizedUserNameKeyField or NormalizedEmailKeyField,
            IdentityRoleDocumentKind => field is NormalizedRoleNameKeyField,
            _ => false
        };
}
#pragma warning restore GW0001, GW0002, GW0003
