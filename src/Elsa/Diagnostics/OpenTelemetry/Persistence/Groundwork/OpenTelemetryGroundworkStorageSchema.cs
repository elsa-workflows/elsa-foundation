using Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork.Catalogs;
using Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork.Records;
using Groundwork.Core.Indexing;
using Groundwork.Core.Intents;
using Groundwork.Core.Manifests;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Queries;
using Groundwork.DiagnosticRecords;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork;

/// <summary>Complete Groundwork stream, physical catalog, index, and operation-ledger declarations for OpenTelemetry.</summary>
public static class OpenTelemetryGroundworkStorageSchema
{
    public const string SchemaVersion = "1.0.0";
    public const string OperationLedgerKind = "openTelemetryCaptureOperation";

    public const string ByServiceNameIndex = "by-service-name";
    public const string ByResourceStatusIndex = "by-resource-status";
    public const string ByInstrumentResourceIndex = "by-resource";
    public const string ByInstrumentNameIndex = "by-name";
    public const string ByInstrumentKindIndex = "by-kind";
    public const string ByLastSeenIndex = "by-last-seen";
    public const string ByRetentionTieBreakerIndex = "by-retention-tie-breaker";
    public const string ByRetentionIndex = "by-retention";

    public static IReadOnlySet<string> RequiredCapabilities { get; } =
        new HashSet<string>(StringComparer.Ordinal) { "schema-history", "optimistic-concurrency" };

    public static IReadOnlySet<string> RequiredOperationLedgers { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "diagnostic-append-idempotency",
            "open-telemetry-capture-operation"
        };

    public static IReadOnlyList<DiagnosticRecordStreamDefinition> CreateStreams(GroundworkOpenTelemetryBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        binding.ValidateAll();
        return
        [
            OpenTelemetryRecordStreamDefinitions.CreateTraces(binding.TraceStreamId),
            OpenTelemetryRecordStreamDefinitions.CreateSpans(binding.SpanStreamId),
            OpenTelemetryRecordStreamDefinitions.CreateMetricPoints(binding.MetricPointStreamId),
            OpenTelemetryRecordStreamDefinitions.CreateLogs(binding.LogStreamId)
        ];
    }

    /// <summary>
    /// Creates scoped, dedicated document tables for the two mutable catalogs and capture-operation ledger.
    /// Canonical JSON remains authoritative while declared projected columns service exact bounded lookups.
    /// </summary>
    public static StorageManifest CreateDocumentManifest() => new(
        new StorageManifestIdentity("elsa-open-telemetry-diagnostics"),
        new StorageManifestOwner("elsa.diagnostics.open-telemetry"),
        new StorageManifestVersion(SchemaVersion),
        [
            CatalogUnit(
                CatalogDocuments.ResourceKind,
                "OpenTelemetry resource catalog",
                "elsa_open_telemetry_resources",
                "elsa_open_telemetry_resource_indexes",
                [
                    Column(CatalogDocuments.ServiceNamePath, PortablePhysicalType.String, length: 4_096),
                    Column(CatalogDocuments.ResourceStatusPath, PortablePhysicalType.Int32),
                    Column(CatalogDocuments.LastSeenPath, PortablePhysicalType.Int64),
                    Column(CatalogDocuments.RetentionTieBreakerPath, PortablePhysicalType.String, length: 512)
                ],
                [
                    Logical(ByServiceNameIndex, CatalogDocuments.ServiceNamePath, IndexValueKind.Keyword),
                    Logical(ByResourceStatusIndex, CatalogDocuments.ResourceStatusPath, IndexValueKind.Number),
                    Logical(ByLastSeenIndex, CatalogDocuments.LastSeenPath, IndexValueKind.Number),
                    Logical(ByRetentionTieBreakerIndex, CatalogDocuments.RetentionTieBreakerPath, IndexValueKind.Keyword)
                ],
                [
                    Query("resources-by-service-name", ByServiceNameIndex),
                    Query("resources-by-status", ByResourceStatusIndex),
                    Query("resources-by-last-seen", ByLastSeenIndex, sortable: true),
                    Query("resources-by-retention-tie-breaker", ByRetentionTieBreakerIndex, sortable: true)
                ]),
            CatalogUnit(
                CatalogDocuments.InstrumentKind,
                "OpenTelemetry metric instrument catalog",
                "elsa_open_telemetry_instruments",
                "elsa_open_telemetry_instrument_indexes",
                [
                    Column(CatalogDocuments.InstrumentResourceIdPath, PortablePhysicalType.String, length: 512),
                    Column(CatalogDocuments.InstrumentNamePath, PortablePhysicalType.String, length: 4_096),
                    Column(CatalogDocuments.InstrumentKindPath, PortablePhysicalType.Int32),
                    Column(CatalogDocuments.LastSeenPath, PortablePhysicalType.Int64),
                    Column(CatalogDocuments.RetentionTieBreakerPath, PortablePhysicalType.String, length: 512)
                ],
                [
                    Logical(ByInstrumentResourceIndex, CatalogDocuments.InstrumentResourceIdPath, IndexValueKind.Keyword),
                    Logical(ByInstrumentNameIndex, CatalogDocuments.InstrumentNamePath, IndexValueKind.Keyword),
                    Logical(ByInstrumentKindIndex, CatalogDocuments.InstrumentKindPath, IndexValueKind.Number),
                    Logical(ByLastSeenIndex, CatalogDocuments.LastSeenPath, IndexValueKind.Number),
                    Logical(ByRetentionTieBreakerIndex, CatalogDocuments.RetentionTieBreakerPath, IndexValueKind.Keyword)
                ],
                [
                    Query("instruments-by-resource", ByInstrumentResourceIndex),
                    Query("instruments-by-name", ByInstrumentNameIndex),
                    Query("instruments-by-kind", ByInstrumentKindIndex),
                    Query("instruments-by-last-seen", ByLastSeenIndex, sortable: true),
                    Query("instruments-by-retention-tie-breaker", ByRetentionTieBreakerIndex, sortable: true)
                ]),
            LedgerUnit()
        ],
        RequiredCapabilities,
        [
            "Catalog identities and comparisons remain ordinal until Groundwork exposes the required portable comparison-key policy.",
            "The capture-operation document fixes the issued-at value reused by every per-stream append retry."
        ]);

    private static StorageUnit CatalogUnit(
        string documentKind,
        string label,
        string tableName,
        string projectionName,
        ProjectedColumnDefinition[] columns,
        LogicalIndexDeclaration[] logicalIndexes,
        BoundedQueryDeclaration[] queries)
    {
        var indexes = logicalIndexes
            .Select(index => new PhysicalIndexDefinition(
                index.Identity,
                new[] { new PhysicalIndexColumnDefinition("storage_scope", 0) }
                    .Concat(index.Fields.Select((field, position) =>
                        new PhysicalIndexColumnDefinition(Path(field.Path), position + 1)))
                    .ToArray()))
            .Append(new PhysicalIndexDefinition(
                ByRetentionIndex,
                [
                    new PhysicalIndexColumnDefinition("storage_scope", 0),
                    new PhysicalIndexColumnDefinition(Path(CatalogDocuments.LastSeenPath), 1),
                    new PhysicalIndexColumnDefinition(Path(CatalogDocuments.RetentionTieBreakerPath), 2)
                ]))
            .ToArray();
        var definition = PhysicalTableDefinition.DedicatedDocumentTable(
            tableName,
            indexes: indexes,
            linkedProjectedColumns: columns,
            linkedProjectionLogicalName: projectionName);
        return Unit(documentKind, label, logicalIndexes, queries) with
        {
            PhysicalStorage = new(
                StorageUnitProvisioningMode.Declared,
                PhysicalStoragePolicy.Explicit(definition),
                logicalIndexes,
                queries)
        };
    }

    private static StorageUnit LedgerUnit() => Unit(
        OperationLedgerKind,
        "OpenTelemetry capture operation ledger",
        [],
        []) with
    {
        PhysicalStorage = new(
            StorageUnitProvisioningMode.Declared,
            PhysicalStoragePolicy.Explicit(PhysicalTableDefinition.DedicatedDocumentTable(
                "elsa_open_telemetry_capture_operations")))
    };

#pragma warning disable GW0001, GW0002, GW0003
    private static StorageUnit Unit(
        string documentKind,
        string label,
        IReadOnlyList<LogicalIndexDeclaration> logicalIndexes,
        IReadOnlyList<BoundedQueryDeclaration> boundedQueries) => new(
        new StorageUnitIdentity(documentKind),
        label,
        StorageIntent.PortableDocument(),
        LifecyclePolicy.Mutable,
        IdentityPolicy.StringId(),
        TenancyPolicy.Scoped,
        ConcurrencyPolicy.Optimistic(),
        SerializationPolicy.Json(),
        logicalIndexes.Select(index => new IndexDeclaration(
            index.Identity,
            index.Fields,
            index.ValueKind,
            index.IsUnique,
            boundedQueries.Any(query => query.IndexIdentity == index.Identity && query.SortSupport != QuerySortSupport.None),
            index.MissingValueBehavior,
            boundedQueries
                .Where(query => query.IndexIdentity == index.Identity)
                .SelectMany(query => query.Operations)
                .ToHashSet())).ToArray(),
        boundedQueries.Select(query => new PortableQueryDeclaration(
            query.Identity,
            query.IndexIdentity,
            query.Operations,
            query.SortSupport,
            query.PagingSupport)).ToArray(),
        PhysicalizationPolicy.Portable);
#pragma warning restore GW0001, GW0002, GW0003

    private static ProjectedColumnDefinition Column(
        string path,
        PortablePhysicalType type,
        int? length = null) => new(Path(path), Path(path), type, Length: length, IsNullable: false);

    private static LogicalIndexDeclaration Logical(string identity, string path, IndexValueKind kind) => new(
        identity,
        [new IndexField(Path(path))],
        kind,
        false,
        MissingValueBehavior.Excluded);

    private static BoundedQueryDeclaration Query(string name, string indexName, bool sortable = false) => new(
        name,
        indexName,
        new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal },
        sortable ? QuerySortSupport.Both : QuerySortSupport.None,
        QueryPagingSupport.Offset,
        BoundedQueryExecutionClass.ScaleBearing,
        supportsTotalCount: true);

    private static string Path(string path) => path.TrimStart('/');
}
