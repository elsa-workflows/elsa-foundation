using Elsa.Activities.Design.Persistence.Groundwork;
using Elsa.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Persistence.Groundwork.Unified.Composition;
using Elsa.Secrets.Persistence.Groundwork;
using Elsa.Studio.Preferences.Persistence.Groundwork;
using Elsa.Workflows.Design.Persistence.Groundwork;
using Elsa.Workflows.Publishing.Persistence.Groundwork;
using Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork;
using Groundwork.Core.Indexing;
using Groundwork.Core.Intents;
using Groundwork.Core.Manifests;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Queries;
using Groundwork.Documents.Store;

namespace Elsa.Persistence.Groundwork.DesignConformance.PostgreSql.Tests;

/// <summary>
/// Leaf-owned deployment-schema extensions for the T064 additive schema-evolution contract. These
/// public, parameterless sources are activated exactly like the shipped
/// <c>GroundworkAllFeaturesDeploymentSchema</c> — both by the composed runtime host (through the
/// generic <c>AddGroundworkSqliteUnifiedPersistence&lt;TDeploymentSource&gt;</c> registration) and by
/// the in-process admission gate — so the CLI/tool contract lives elsewhere (T061) while this leaf
/// owns the evolved shape.
/// </summary>
internal static class PostgreSqlDesignEvolutionSources
{
    /// <summary>The shipped design/runtime deployment source set (the without-identity reference set).</summary>
    public static readonly IReadOnlyList<Type> Baseline =
    [
        typeof(RuntimeGroundworkStorageManifestSource),
        typeof(SecretsGroundworkStorageManifestSource),
        typeof(StudioPreferencesGroundworkStorageManifestSource),
        typeof(DistributedGroundworkStorageManifestSource),
        typeof(WorkflowsDesignGroundworkStorageManifestSource),
        typeof(ActivitiesDesignGroundworkStorageManifestSource),
        typeof(GroundworkDesignAtomicWriteStorageManifestSource),
        typeof(PublishingGroundworkStorageManifestSource)
    ];
}

/// <summary>The evolved deployment schema: the baseline reference set plus one additive probe unit.</summary>
public sealed class PostgreSqlDesignEvolutionDeploymentSchema : GroundworkDeploymentSchemaManifestSource
{
    protected override IReadOnlyCollection<Type> ManifestSourceTypes =>
        [.. PostgreSqlDesignEvolutionSources.Baseline, typeof(DesignEvolutionProbeGroundworkStorageManifestSource)];
}

/// <summary>
/// A deployment schema whose extension resolves a physical object name that collides with the shipped
/// workflow-definition table. Composition must reject it with the exact collision diagnostic.
/// </summary>
public sealed class PostgreSqlDesignEvolutionCollisionDeploymentSchema : GroundworkDeploymentSchemaManifestSource
{
    protected override IReadOnlyCollection<Type> ManifestSourceTypes =>
        [.. PostgreSqlDesignEvolutionSources.Baseline, typeof(DesignEvolutionCollisionGroundworkStorageManifestSource)];
}

/// <summary>The additive test-only design family: one new document kind, its own entity table and route.</summary>
public sealed class DesignEvolutionProbeGroundworkStorageManifestSource : IGroundworkStorageManifestSource
{
    public string FeatureIdentity => "elsa-design-conformance-evolution-probe";

    public ValueTask<GroundworkStorageManifestDeclaration> CreateDeclarationAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var manifest = DesignEvolutionProbeManifest.Create(FeatureIdentity, DesignEvolutionProbeManifest.ProbeKind, DesignEvolutionProbeManifest.ProbeKind);
        return ValueTask.FromResult(new GroundworkStorageManifestDeclaration(
            FeatureIdentity, manifest, [], [], [], []));
    }
}

/// <summary>
/// A colliding test-only family whose entity-table logical name is the shipped workflow-definition
/// table name, forcing a resolved physical-name collision with an existing object.
/// </summary>
public sealed class DesignEvolutionCollisionGroundworkStorageManifestSource : IGroundworkStorageManifestSource
{
    public string FeatureIdentity => "elsa-design-conformance-evolution-collision";

    public ValueTask<GroundworkStorageManifestDeclaration> CreateDeclarationAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var manifest = DesignEvolutionProbeManifest.Create(FeatureIdentity, "designEvolutionCollisionProbe", "workflowDefinition");
        return ValueTask.FromResult(new GroundworkStorageManifestDeclaration(
            FeatureIdentity, manifest, [], [], [], []));
    }
}

/// <summary>
/// Builds a single-unit legacy manifest for the additive/collision probe. The unit is a scoped,
/// optimistic, JSON portable-document entity table with a projected key column, a unique point-lookup
/// index and a find-by-id route — the minimum servable shape. The physical table's logical name is a
/// parameter so the collision family can deliberately re-use the shipped workflow-definition name.
/// </summary>
internal static class DesignEvolutionProbeManifest
{
    public const string ProbeKind = "designEvolutionProbe";
    public const string ProbeCollection = ProbeKind;
    public const string SchemaVersion = "1.0.0";

    public const string ProbeIdField = "entity.id";
    public const string ProbeValueField = "entity.value";
    public const string DocumentIdField = PhysicalDocumentFieldPaths.Id;

    public const string FindProbeByIdQuery = "find-probe-by-id";

    private const int IdentityColumnLength = 128;

    public static StorageManifest Create(string manifestIdentity, string documentKind, string tableLogicalName)
    {
        var columns = new[]
        {
            Column("probe_id", ProbeIdField, false, IdentityColumnLength),
            Column("probe_value", ProbeValueField)
        };
        var physicalIndexes = new[]
        {
            PointLookupIndex("probe-by-id-point"),
            ValueIndex("probe-by-value", "probe_value")
        };
        var logicalIndexes = new[]
        {
            LogicalIndex("probe-by-id-point", [DocumentIdField], unique: true),
            LogicalIndex("probe-by-value", [ProbeValueField])
        };
        var queries = new[]
        {
            Query(
                FindProbeByIdQuery,
                "probe-by-id-point",
                [Predicate(DocumentIdField, PortableQueryOperation.Equal)],
                QueryPagingSupport.None,
                QuerySortSupport.None,
                [BoundedQueryResultOperation.First, BoundedQueryResultOperation.Any])
        };

        return new StorageManifest(
            new StorageManifestIdentity(manifestIdentity),
            new StorageManifestOwner(manifestIdentity),
            new StorageManifestVersion(SchemaVersion),
            [Unit(documentKind, tableLogicalName, columns, logicalIndexes, physicalIndexes, queries)],
            new HashSet<string> { "optimistic-concurrency" },
            [])
        {
            // Matches what the production families declare, so the probe never introduces a shared
            // document storage difference into the evolution comparison.
            SharedDocumentStorages = [SharedDocumentsStorage.Definition]
        };
    }

    private static StorageUnit Unit(
        string documentKind,
        string tableLogicalName,
        ProjectedColumnDefinition[] columns,
        LogicalIndexDeclaration[] logicalIndexes,
        PhysicalIndexDefinition[] physicalIndexes,
        BoundedQueryDeclaration[] boundedQueries)
    {
        return StorageUnit.Create(
            new StorageUnitIdentity(documentKind),
            "Design evolution probe",
            StorageIntent.PortableDocument(),
            LifecyclePolicy.Mutable,
            IdentityPolicy.StringId(),
            TenancyPolicy.Scoped,
            ConcurrencyPolicy.Optimistic(),
            SerializationPolicy.Json(),
            new StorageUnitPhysicalStorage(
                StorageUnitProvisioningMode.Declared,
                PhysicalStoragePolicy.Explicit(PhysicalTableDefinition.PhysicalEntityTable(tableLogicalName, columns, indexes: physicalIndexes)),
                logicalIndexes,
                boundedQueries));
    }

    private static LogicalIndexDeclaration LogicalIndex(string identity, string[] fields, bool unique = false) =>
        new(identity, fields.Select(field => new IndexField(field, IndexValueKind.Keyword)).ToArray(), IndexValueKind.Keyword, unique, MissingValueBehavior.Excluded);

    private static PhysicalIndexDefinition PointLookupIndex(string identity) =>
        new(identity,
        [
            new PhysicalIndexColumnDefinition("storage_scope", 0),
            new PhysicalIndexColumnDefinition("id_lookup_key", 1),
            new PhysicalIndexColumnDefinition("id_comparison_key", 2)
        ],
        isUnique: true,
        missingValueBehavior: MissingValueBehavior.Excluded);

    private static PhysicalIndexDefinition ValueIndex(string identity, string column) =>
        new(identity,
        [
            new PhysicalIndexColumnDefinition("storage_scope", 0),
            new PhysicalIndexColumnDefinition(column, 1)
        ],
        isUnique: false,
        missingValueBehavior: MissingValueBehavior.Excluded);

    private static BoundedQueryDeclaration Query(
        string identity,
        string index,
        BoundedQueryPredicateField[] predicates,
        QueryPagingSupport paging,
        QuerySortSupport sort,
        BoundedQueryResultOperation[] results) =>
        new(
            identity,
            index,
            predicates.SelectMany(predicate => predicate.Operations).ToHashSet(),
            sort,
            paging,
            BoundedQueryExecutionClass.ScaleBearing,
            supportsDisjunction: false,
            supportsTotalCount: results.Contains(BoundedQueryResultOperation.Count),
            sortFields: null,
            predicateFields: predicates,
            resultOperations: results.ToHashSet(),
            residualPredicateFields: null);

    private static BoundedQueryPredicateField Predicate(string path, params PortableQueryOperation[] operations) =>
        new(path, operations.ToHashSet());

    private static ProjectedColumnDefinition Column(string name, string path, bool nullable = true, int length = 256) =>
        new(name, path, PortablePhysicalType.String, Length: length, IsNullable: nullable);
}
