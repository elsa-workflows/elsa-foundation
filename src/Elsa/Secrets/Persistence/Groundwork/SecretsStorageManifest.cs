using Elsa.Secrets.Core.Models;
using Groundwork.Core.Indexing;
using Groundwork.Core.Intents;
using Groundwork.Core.Manifests;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Queries;

namespace Elsa.Secrets.Persistence.Groundwork;

public static class SecretsStorageManifest
{
    public const string SchemaVersion = "2.0.0";
    public const string SecretDocumentKind = "secret";
    public const string SecretCollection = "secret";
    public const string CollectionField = "collection";
    public const string ByCollectionIndex = "by-collection";
    public const string SecretsTable = "secrets";
    public const string SecretByNameIndex = "secret-by-name";
    public const string SecretFilteredListIndex = "secret-filtered-list";
    public const string SecretFilteredListV2Index = "secret-filtered-list-v2";
    public const string ListFilteredQuery = "list-filtered";
    public const string ListUnfilteredQuery = "list-unfiltered";
    public const string SearchFilteredQuery = "search-filtered";
    public const string LegacyBackfillQuery = "legacy-backfill";
    public const string NormalizedNameField = "normalizedName";
    public const string TenantIdField = "tenantId";
    public const string NameSearchKeyField = "nameSearchKey";
    public const string DisplayNameSearchKeyField = "displayNameSearchKey";
    public const string TypeNameLookupKeyField = "typeNameLookupKey";
    public const string StoreNameLookupKeyField = "storeNameLookupKey";
    public const string ScopeLookupKeyField = "scopeLookupKey";
    public const string StatusField = "status";
    public const string HasNonExpiringActiveVersionField = "hasNonExpiringActiveVersion";
    public const string MaxActiveVersionExpiresAtField = "maxActiveVersionExpiresAt";

    public static StorageManifest Create()
    {
        var envelope = new DocumentEnvelopeDefinition();
        var nameIndex = new LogicalIndexDeclaration(
            SecretByNameIndex,
            [new IndexField(TenantIdField), new IndexField(NormalizedNameField)],
            IndexValueKind.Keyword,
            isUnique: true,
            MissingValueBehavior.Excluded);
        var collectionIndex = new LogicalIndexDeclaration(
            ByCollectionIndex,
            [new IndexField(CollectionField)],
            IndexValueKind.Keyword,
            isUnique: false,
            MissingValueBehavior.Excluded);
        var filteredListIndex = new LogicalIndexDeclaration(
            SecretFilteredListIndex,
            [
                new IndexField(TenantIdField),
                new IndexField(StatusField),
                new IndexField(NormalizedNameField)
            ],
            IndexValueKind.Keyword,
            isUnique: false,
            MissingValueBehavior.IncludedAsNull);
        var filteredListV2Index = new LogicalIndexDeclaration(
            SecretFilteredListV2Index,
            [
                new IndexField(TenantIdField),
                new IndexField(StatusField),
                new IndexField(NormalizedNameField)
            ],
            IndexValueKind.Keyword,
            isUnique: true,
            MissingValueBehavior.IncludedAsNull);
        var definition = PhysicalTableDefinition.PhysicalEntityTable(
            SecretsTable,
            [
                StringProjection(CollectionField, length: 64, isNullable: false),
                StringProjection(TenantIdField, length: 256, isNullable: false),
                StringProjection(NormalizedNameField, length: SecretNameConstraints.MaximumLength, isNullable: false),
                StringProjection(NameSearchKeyField, isNullable: false),
                StringProjection(DisplayNameSearchKeyField, isNullable: false),
                StringProjection(TypeNameLookupKeyField, length: 64, isNullable: false),
                StringProjection(StoreNameLookupKeyField, length: 64, isNullable: false),
                StringProjection(ScopeLookupKeyField, length: 64),
                StringProjection(StatusField, length: 32, isNullable: false),
                new ProjectedColumnDefinition(
                    HasNonExpiringActiveVersionField,
                    HasNonExpiringActiveVersionField,
                    PortablePhysicalType.Boolean,
                    IsNullable: false,
                    DefaultValue: bool.FalseString.ToLowerInvariant()),
                new ProjectedColumnDefinition(
                    MaxActiveVersionExpiresAtField,
                    MaxActiveVersionExpiresAtField,
                    PortablePhysicalType.DateTime)
            ],
            envelope,
            [
                new PhysicalIndexDefinition(
                    nameIndex.Identity,
                    [
                        new PhysicalIndexColumnDefinition(envelope.StorageScopeColumn, 0),
                        new PhysicalIndexColumnDefinition(TenantIdField, 1),
                        new PhysicalIndexColumnDefinition(NormalizedNameField, 2)
                    ],
                    isUnique: true),
                new PhysicalIndexDefinition(
                    collectionIndex.Identity,
                    [
                        new PhysicalIndexColumnDefinition(envelope.StorageScopeColumn, 0),
                        new PhysicalIndexColumnDefinition(CollectionField, 1)
                    ]),
                new PhysicalIndexDefinition(
                    filteredListIndex.Identity,
                    [
                        new PhysicalIndexColumnDefinition(envelope.StorageScopeColumn, 0),
                        new PhysicalIndexColumnDefinition(TenantIdField, 1),
                        new PhysicalIndexColumnDefinition(StatusField, 2),
                        new PhysicalIndexColumnDefinition(NormalizedNameField, 3)
                    ],
                    isUnique: false,
                    missingValueBehavior: MissingValueBehavior.IncludedAsNull),
                new PhysicalIndexDefinition(
                    filteredListV2Index.Identity,
                    [
                        new PhysicalIndexColumnDefinition(envelope.StorageScopeColumn, 0),
                        new PhysicalIndexColumnDefinition(TenantIdField, 1),
                        new PhysicalIndexColumnDefinition(StatusField, 2),
                        new PhysicalIndexColumnDefinition(NormalizedNameField, 3)
                    ],
                    isUnique: true,
                    missingValueBehavior: MissingValueBehavior.IncludedAsNull)
            ]);
        var listRoute = FilterRoute(
            ListFilteredQuery,
            BoundedQueryExecutionClass.ScaleBearing,
            filteredListV2Index.Identity,
            statusIsIndexed: true);
        var unfilteredListRoute = FilterRoute(
            ListUnfilteredQuery,
            BoundedQueryExecutionClass.Ordinary,
            nameIndex.Identity);
        // Substring predicates and total count are provider-bound and materialization-bounded, but
        // may scan. They are deliberately not advertised as an indexed scale-bearing route.
        var searchRoute = FilterRoute(
            SearchFilteredQuery,
            BoundedQueryExecutionClass.Ordinary,
            nameIndex.Identity,
            supportsContains: true,
            additionalResiduals:
            [
                Residual(NameSearchKeyField, IndexValueKind.String, PortableQueryOperation.Contains),
                Residual(DisplayNameSearchKeyField, IndexValueKind.String, PortableQueryOperation.Contains)
            ]);
        var legacyBackfillRoute = new BoundedQueryDeclaration(
            LegacyBackfillQuery,
            collectionIndex.Identity,
            new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal },
            QuerySortSupport.None,
            QueryPagingSupport.None,
            BoundedQueryExecutionClass.Ordinary,
            predicateFields:
            [
                new BoundedQueryPredicateField(
                    CollectionField,
                    new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal })
            ],
            residualPredicateFields:
            [
                new(
                    TenantIdField,
                    IndexValueKind.Keyword,
                    new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal },
                    isRequired: true)
            ]);
        var unit = new StorageUnit(
            new StorageUnitIdentity(SecretDocumentKind),
            "Secret",
            StorageIntent.PortableDocument(),
            LifecyclePolicy.Mutable,
            IdentityPolicy.StringId(),
            TenancyPolicy.Scoped,
            ConcurrencyPolicy.Optimistic(),
            SerializationPolicy.Json(),
            [],
            [],
            PhysicalizationPolicy.Portable)
        {
            PhysicalStorage = new StorageUnitPhysicalStorage(
                StorageUnitProvisioningMode.Declared,
                PhysicalStoragePolicy.Explicit(definition),
                [nameIndex, collectionIndex, filteredListIndex, filteredListV2Index],
                [listRoute, unfilteredListRoute, searchRoute, legacyBackfillRoute])
        };

        return new StorageManifest(
            new StorageManifestIdentity("elsa-secrets"),
            new StorageManifestOwner("elsa.secrets"),
            new StorageManifestVersion(SchemaVersion),
            [unit],
            new HashSet<string> { "schema-history", "optimistic-concurrency" },
            []);
    }

    private static BoundedQueryDeclaration FilterRoute(
        string identity,
        BoundedQueryExecutionClass executionClass,
        string indexIdentity,
        bool supportsContains = false,
        bool statusIsIndexed = false,
        IReadOnlyList<BoundedQueryResidualPredicateField>? additionalResiduals = null)
    {
        var residuals = new List<BoundedQueryResidualPredicateField>
        {
            Residual(
                TypeNameLookupKeyField,
                IndexValueKind.Keyword,
                PortableQueryOperation.Equal,
                PortableQueryOperation.In),
            Residual(
                StoreNameLookupKeyField,
                IndexValueKind.Keyword,
                PortableQueryOperation.Equal,
                PortableQueryOperation.In),
            Residual(ScopeLookupKeyField, IndexValueKind.Keyword, PortableQueryOperation.Equal),
            Residual(
                HasNonExpiringActiveVersionField,
                IndexValueKind.Boolean,
                PortableQueryOperation.Equal),
            Residual(
                MaxActiveVersionExpiresAtField,
                IndexValueKind.DateTime,
                PortableQueryOperation.GreaterThan)
        };
        if (!statusIsIndexed)
        {
            residuals.Add(new BoundedQueryResidualPredicateField(
                StatusField,
                IndexValueKind.Keyword,
                new HashSet<PortableQueryOperation>
                {
                    PortableQueryOperation.Equal,
                    PortableQueryOperation.NotEqual
                },
                isRequired: true));
        }
        if (additionalResiduals is not null)
            residuals.AddRange(additionalResiduals);

        var operations = new HashSet<PortableQueryOperation>
        {
            PortableQueryOperation.Equal,
            PortableQueryOperation.NotEqual,
            PortableQueryOperation.In,
            PortableQueryOperation.GreaterThan
        };
        if (supportsContains)
            operations.Add(PortableQueryOperation.Contains);

        return new BoundedQueryDeclaration(
            identity,
            indexIdentity,
            operations,
            QuerySortSupport.Ascending,
            QueryPagingSupport.Offset,
            executionClass,
            supportsDisjunction: true,
            supportsTotalCount: true,
            sortFields:
            [
                new BoundedQuerySortField(NormalizedNameField, PhysicalSortDirection.Ascending)
            ],
            predicateFields: statusIsIndexed
                ?
                [
                    new BoundedQueryPredicateField(
                        TenantIdField,
                        new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal }),
                    new BoundedQueryPredicateField(
                        StatusField,
                        new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal })
                ]
                :
                [
                    new BoundedQueryPredicateField(
                        TenantIdField,
                        new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal })
                ],
            residualPredicateFields: residuals);
    }

    private static ProjectedColumnDefinition StringProjection(
        string path,
        int? length = null,
        bool isNullable = true) =>
        new(path, path, PortablePhysicalType.String, Length: length, IsNullable: isNullable);

    private static BoundedQueryResidualPredicateField Residual(
        string path,
        IndexValueKind valueKind,
        params PortableQueryOperation[] operations) =>
        new(path, valueKind, operations.ToHashSet());

}
