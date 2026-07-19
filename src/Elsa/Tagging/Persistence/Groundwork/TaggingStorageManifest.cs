using Elsa.Tagging.Core.Models;
using Groundwork.Core.Indexing;
using Groundwork.Core.Intents;
using Groundwork.Core.Manifests;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Queries;

namespace Elsa.Tagging.Persistence.Groundwork;

public static class TaggingStorageManifest
{
    public const string SchemaVersion = "1.0.0";
    public const string TagDefinitionDocumentKind = "tag-definition";
    public const string TagDefinitionAuditDocumentKind = "tag-definition-audit";
    public const string TagDefinitionsTable = "tag_definitions";
    public const string TagDefinitionAuditsTable = "tag_definition_audits";
    public const string CanonicalKeyField = "canonicalKey";
    public const string TagDefinitionIdField = "tagDefinitionId";
    public const string StatusField = "status";
    public const string ListQuery = "list";
    public const string FindByIdQuery = "find-by-id";

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
            new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal },
            QuerySortSupport.None,
            QueryPagingSupport.Offset,
            BoundedQueryExecutionClass.ScaleBearing,
            supportsDisjunction: false,
            supportsTotalCount: true,
            sortFields: [],
            predicateFields: null,
            residualPredicateFields: [new BoundedQueryResidualPredicateField(TagDefinitionIdField, IndexValueKind.Keyword, new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal }, isRequired: true)]);
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
        var auditTable = PhysicalTableDefinition.PhysicalEntityTable(TagDefinitionAuditsTable, [], auditEnvelope, []);
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
            [definitions, audits],
            new HashSet<string> { "schema-history", "optimistic-concurrency", "append-only-audit" },
            []);
    }
}
