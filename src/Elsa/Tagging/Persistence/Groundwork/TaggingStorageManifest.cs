using Elsa.Tagging.Core.Models;
using Groundwork.Core.Indexing;
using Groundwork.Core.Intents;
using Groundwork.Core.Manifests;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Queries;

namespace Elsa.Tagging.Persistence.Groundwork;

public static class TaggingStorageManifest
{
    public const string SchemaVersion = "1.2.0";
    public const string TagDefinitionDocumentKind = "tag-definition";
    public const string TagDefinitionAuditDocumentKind = "tag-definition-audit";
    public const string ControlledTagValueDocumentKind = "controlled-tag-value";
    public const string ControlledTagValueAuditDocumentKind = "controlled-tag-value-audit";
    public const string ControlledTagValueCatalogGuardDocumentKind = "controlled-tag-value-catalog-guard";
    public const string TagDefinitionsTable = "tag_definitions";
    public const string TagDefinitionAuditsTable = "tag_definition_audits";
    public const string ControlledTagValuesTable = "controlled_tag_values";
    public const string ControlledTagValueAuditsTable = "controlled_tag_value_audits";
    public const string ControlledTagValueCatalogGuardsTable = "controlled_tag_value_catalog_guards";
    public const string CanonicalKeyField = "canonicalKey";
    public const string TagDefinitionIdField = "tagDefinitionId";
    public const string StatusField = "status";
    public const string ControlledTagValueIdField = "controlledTagValueId";
    public const string ControlledTagValueDefinitionIdField = "tagDefinitionId";
    public const string ControlledTagValueSortOrderField = "sortOrder";
    public const string ControlledTagValueDisplayNameSortKeyField = "displayNameSortKey";
    public const string ListQuery = "list";
    public const string FindByIdQuery = "find-by-id";
    public const string ListValuesByDefinitionQuery = "list-values-by-definition";
    public const string FindValueByIdQuery = "find-value-by-id";

    public static StorageManifest Create()
    {
        var definitionEnvelope = new DocumentEnvelopeDefinition();
        var keyIndex = new LogicalIndexDeclaration(
            "tag-definition-by-canonical-key",
            [new IndexField(CanonicalKeyField)],
            IndexValueKind.Keyword,
            isUnique: true,
            MissingValueBehavior.Excluded);
        var idIndex = new LogicalIndexDeclaration(
            "tag-definition-by-id",
            [new IndexField(TagDefinitionIdField)],
            IndexValueKind.Keyword,
            isUnique: true,
            MissingValueBehavior.Excluded);
        var definitionTable = PhysicalTableDefinition.PhysicalEntityTable(
            TagDefinitionsTable,
            [
                new ProjectedColumnDefinition(TagDefinitionIdField, TagDefinitionIdField, PortablePhysicalType.String, Length: 64, IsNullable: false),
                new ProjectedColumnDefinition(CanonicalKeyField, CanonicalKeyField, PortablePhysicalType.String, Length: TagDefinitionConstraints.MaximumCanonicalKeyLength, IsNullable: false),
                new ProjectedColumnDefinition(StatusField, StatusField, PortablePhysicalType.String, Length: 16, IsNullable: false)
            ],
            definitionEnvelope,
            [new PhysicalIndexDefinition(keyIndex.Identity,
                [new PhysicalIndexColumnDefinition(definitionEnvelope.StorageScopeColumn, 0), new PhysicalIndexColumnDefinition(CanonicalKeyField, 1)],
                isUnique: true),
             new PhysicalIndexDefinition(idIndex.Identity,
                [new PhysicalIndexColumnDefinition(definitionEnvelope.StorageScopeColumn, 0), new PhysicalIndexColumnDefinition(TagDefinitionIdField, 1)],
                isUnique: true)]);
        var listRoute = new BoundedQueryDeclaration(
            ListQuery,
            keyIndex.Identity,
            new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal },
            QuerySortSupport.Ascending,
            QueryPagingSupport.Offset,
            BoundedQueryExecutionClass.ScaleBearing,
            supportsDisjunction: false,
            supportsTotalCount: true,
            sortFields: [new BoundedQuerySortField(CanonicalKeyField, PhysicalSortDirection.Ascending)],
            predicateFields: null,
            residualPredicateFields: [new BoundedQueryResidualPredicateField(StatusField, IndexValueKind.Keyword, new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal })]);
        var findByIdRoute = new BoundedQueryDeclaration(
            FindByIdQuery,
            idIndex.Identity,
            new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal, PortableQueryOperation.In },
            QuerySortSupport.None,
            QueryPagingSupport.Offset,
            BoundedQueryExecutionClass.ScaleBearing,
            supportsDisjunction: false,
            supportsTotalCount: true,
            sortFields: [],
            predicateFields: [new BoundedQueryPredicateField(
                TagDefinitionIdField,
                new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal, PortableQueryOperation.In })],
            residualPredicateFields: null);
        var definitions = new StorageUnit(
            new StorageUnitIdentity(TagDefinitionDocumentKind),
            "Tag definition",
            StorageIntent.PortableDocument(),
            LifecyclePolicy.Mutable,
            IdentityPolicy.StringId(),
            TenancyPolicy.Scoped,
            ConcurrencyPolicy.Optimistic(),
            SerializationPolicy.Json(),
            [], [], PhysicalizationPolicy.Portable)
        {
            PhysicalStorage = new StorageUnitPhysicalStorage(
                StorageUnitProvisioningMode.Declared,
                PhysicalStoragePolicy.Explicit(definitionTable),
                [keyIndex, idIndex],
                [listRoute, findByIdRoute])
        };

        var auditEnvelope = new DocumentEnvelopeDefinition();
        var auditTable = PhysicalTableDefinition.PhysicalEntityTable(
            TagDefinitionAuditsTable,
            [
                new ProjectedColumnDefinition(
                    TagDefinitionIdField,
                    TagDefinitionIdField,
                    PortablePhysicalType.String,
                    Length: 64,
                    IsNullable: false)
            ],
            auditEnvelope,
            []);
        var audits = new StorageUnit(
            new StorageUnitIdentity(TagDefinitionAuditDocumentKind),
            "Tag definition audit record",
            StorageIntent.PortableDocument(),
            LifecyclePolicy.Mutable,
            IdentityPolicy.StringId(),
            TenancyPolicy.Scoped,
            ConcurrencyPolicy.Optimistic(),
            SerializationPolicy.Json(),
            [], [], PhysicalizationPolicy.Portable)
        {
            PhysicalStorage = new StorageUnitPhysicalStorage(
                StorageUnitProvisioningMode.Declared,
                PhysicalStoragePolicy.Explicit(auditTable),
                [], [])
        };

        return new StorageManifest(
            new StorageManifestIdentity("elsa-tagging"),
            new StorageManifestOwner("elsa.tagging"),
            new StorageManifestVersion(SchemaVersion),
            [definitions, audits, ControlledValues(), ControlledValueAudits(), ControlledValueCatalogGuards()],
            new HashSet<string> { "schema-history", "optimistic-concurrency", "append-only-audit" },
            []);
    }

    private static StorageUnit ControlledValues()
    {
        var envelope = new DocumentEnvelopeDefinition();
        var definitionIndex = new LogicalIndexDeclaration(
            "controlled-tag-value-by-definition",
            [
                new IndexField(ControlledTagValueDefinitionIdField),
                new IndexField(ControlledTagValueSortOrderField, IndexValueKind.Number),
                new IndexField(ControlledTagValueDisplayNameSortKeyField),
                new IndexField(ControlledTagValueIdField)
            ],
            IndexValueKind.Keyword,
            false,
            MissingValueBehavior.Excluded);
        var idIndex = new LogicalIndexDeclaration("controlled-tag-value-by-id", [new IndexField(ControlledTagValueIdField)], IndexValueKind.Keyword, true, MissingValueBehavior.Excluded);
        var table = PhysicalTableDefinition.PhysicalEntityTable(ControlledTagValuesTable,
        [
            new ProjectedColumnDefinition(ControlledTagValueIdField, ControlledTagValueIdField, PortablePhysicalType.String, Length: 64, IsNullable: false),
            new ProjectedColumnDefinition(ControlledTagValueDefinitionIdField, ControlledTagValueDefinitionIdField, PortablePhysicalType.String, Length: 64, IsNullable: false),
            new ProjectedColumnDefinition(CanonicalKeyField, CanonicalKeyField, PortablePhysicalType.String, Length: ControlledTagValueConstraints.MaximumCanonicalKeyLength, IsNullable: false),
            new ProjectedColumnDefinition(StatusField, StatusField, PortablePhysicalType.String, Length: 16, IsNullable: false),
            new ProjectedColumnDefinition(ControlledTagValueSortOrderField, ControlledTagValueSortOrderField, PortablePhysicalType.Int32, IsNullable: false),
            new ProjectedColumnDefinition(ControlledTagValueDisplayNameSortKeyField, ControlledTagValueDisplayNameSortKeyField, PortablePhysicalType.String, Length: TagDefinitionConstraints.MaximumDisplayNameLength, IsNullable: false)
        ], envelope,
        [
            new PhysicalIndexDefinition(
                definitionIndex.Identity,
                [
                    new PhysicalIndexColumnDefinition(envelope.StorageScopeColumn, 0),
                    new PhysicalIndexColumnDefinition(ControlledTagValueDefinitionIdField, 1),
                    new PhysicalIndexColumnDefinition(ControlledTagValueSortOrderField, 2),
                    new PhysicalIndexColumnDefinition(ControlledTagValueDisplayNameSortKeyField, 3),
                    new PhysicalIndexColumnDefinition(ControlledTagValueIdField, 4)
                ]),
            new PhysicalIndexDefinition(idIndex.Identity, [new PhysicalIndexColumnDefinition(envelope.StorageScopeColumn, 0), new PhysicalIndexColumnDefinition(ControlledTagValueIdField, 1)], isUnique: true)
        ]);
        return new StorageUnit(new StorageUnitIdentity(ControlledTagValueDocumentKind), "Controlled tag value", StorageIntent.PortableDocument(), LifecyclePolicy.Mutable, IdentityPolicy.StringId(), TenancyPolicy.Scoped, ConcurrencyPolicy.Optimistic(), SerializationPolicy.Json(), [], [], PhysicalizationPolicy.Portable)
        {
            PhysicalStorage = new StorageUnitPhysicalStorage(StorageUnitProvisioningMode.Declared, PhysicalStoragePolicy.Explicit(table), [definitionIndex, idIndex],
            [
                new BoundedQueryDeclaration(ListValuesByDefinitionQuery, definitionIndex.Identity, new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal }, QuerySortSupport.Ascending, QueryPagingSupport.Offset, BoundedQueryExecutionClass.ScaleBearing, supportsDisjunction: false, supportsTotalCount: true, sortFields: [new BoundedQuerySortField(ControlledTagValueSortOrderField, PhysicalSortDirection.Ascending), new BoundedQuerySortField(ControlledTagValueDisplayNameSortKeyField, PhysicalSortDirection.Ascending), new BoundedQuerySortField(ControlledTagValueIdField, PhysicalSortDirection.Ascending)], predicateFields: [new BoundedQueryPredicateField(ControlledTagValueDefinitionIdField, new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal })], residualPredicateFields: [new BoundedQueryResidualPredicateField(StatusField, IndexValueKind.Keyword, new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal })]),
                new BoundedQueryDeclaration(FindValueByIdQuery, idIndex.Identity, new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal, PortableQueryOperation.In }, QuerySortSupport.None, QueryPagingSupport.Offset, BoundedQueryExecutionClass.ScaleBearing, supportsDisjunction: false, supportsTotalCount: true, sortFields: [], predicateFields: [new BoundedQueryPredicateField(ControlledTagValueIdField, new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal, PortableQueryOperation.In })], residualPredicateFields: null)
            ])
        };
    }

    private static StorageUnit ControlledValueAudits()
    {
        var envelope = new DocumentEnvelopeDefinition();
        var table = PhysicalTableDefinition.PhysicalEntityTable(ControlledTagValueAuditsTable, [new ProjectedColumnDefinition(ControlledTagValueIdField, ControlledTagValueIdField, PortablePhysicalType.String, Length: 64, IsNullable: false)], envelope, []);
        return new StorageUnit(new StorageUnitIdentity(ControlledTagValueAuditDocumentKind), "Controlled tag value audit record", StorageIntent.PortableDocument(), LifecyclePolicy.Mutable, IdentityPolicy.StringId(), TenancyPolicy.Scoped, ConcurrencyPolicy.Optimistic(), SerializationPolicy.Json(), [], [], PhysicalizationPolicy.Portable)
        {
            PhysicalStorage = new StorageUnitPhysicalStorage(StorageUnitProvisioningMode.Declared, PhysicalStoragePolicy.Explicit(table), [], [])
        };
    }

    private static StorageUnit ControlledValueCatalogGuards()
    {
        var envelope = new DocumentEnvelopeDefinition();
        var table = PhysicalTableDefinition.PhysicalEntityTable(
            ControlledTagValueCatalogGuardsTable,
            [
                new ProjectedColumnDefinition(
                    TagDefinitionIdField,
                    TagDefinitionIdField,
                    PortablePhysicalType.String,
                    Length: 64,
                    IsNullable: false)
            ],
            envelope,
            []);
        return new StorageUnit(
            new StorageUnitIdentity(ControlledTagValueCatalogGuardDocumentKind),
            "Controlled tag value catalog guard",
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
                PhysicalStoragePolicy.Explicit(table),
                [],
                [])
        };
    }
}
