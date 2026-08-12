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
    private const string AdditiveIndexVersionSuffix = "-v2";

    public const string ByResourceStatusIndex = "by-resource-status";
    public const string ByResourceServiceIndex = "by-resource-service";
    public const string ByInstrumentResourceIndex = "by-resource";
    public const string ByInstrumentKindIndex = "by-kind";
    public const string ByLastSeenIndex = "by-last-seen";
    public const string ByRetentionTieBreakerIndex = "by-retention-tie-breaker";
    public const string ByRetentionIndex = "by-retention";
    public const string ByCaptureCreatedAtIndex = "by-created-at";
    public const string ResourcesByLastSeenQuery = "resources-by-last-seen";
    public const string ResourcesFilteredQuery = "resources-filtered";
    public const string ResourcesByStatusQuery = "resources-by-status";
    public const string ResourcesByServiceQuery = "resources-by-service";
    public const string InstrumentsByLastSeenQuery = "instruments-by-last-seen";
    public const string CaptureOperationsByCreatedAtQuery = "capture-operations-by-created-at";

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
                    Column(CatalogDocuments.ResourceIdSearchKeyPath, PortablePhysicalType.String, length: 512),
                    Column(CatalogDocuments.ServiceNameComparisonKeyPath, PortablePhysicalType.String),
                    Column(CatalogDocuments.ServiceNameHashPath, PortablePhysicalType.String, length: 64),
                    Column(CatalogDocuments.ServiceNameSearchKeyPath, PortablePhysicalType.String),
                    Column(CatalogDocuments.ResourceStatusPath, PortablePhysicalType.Int32),
                    Column(CatalogDocuments.LastSeenPath, PortablePhysicalType.Int64),
                    Column(CatalogDocuments.RetentionTieBreakerPath, PortablePhysicalType.String, length: 512)
                ],
                [
                    Logical(ByResourceStatusIndex, CatalogDocuments.ResourceStatusPath, IndexValueKind.Number),
                    ResourceServiceLogical(),
                    Logical(ByLastSeenIndex, CatalogDocuments.LastSeenPath, IndexValueKind.Number),
                    Logical(ByRetentionTieBreakerIndex, CatalogDocuments.RetentionTieBreakerPath, IndexValueKind.Keyword),
                    RetentionLogical()
                ],
                [
                    ResourceFilterQuery(),
                    ResourceStatusQuery(),
                    ResourceServiceQuery(),
                    RetentionQuery(ResourcesByLastSeenQuery),
                    Query("resources-by-retention-tie-breaker", ByRetentionTieBreakerIndex, CatalogDocuments.RetentionTieBreakerPath, sortable: true)
                ]),
            CatalogUnit(
                CatalogDocuments.InstrumentKind,
                "OpenTelemetry metric instrument catalog",
                "elsa_open_telemetry_instruments",
                "elsa_open_telemetry_instrument_indexes",
                [
                    Column(CatalogDocuments.InstrumentResourceIdPath, PortablePhysicalType.String, length: 512),
                    Column(CatalogDocuments.InstrumentKindPath, PortablePhysicalType.Int32),
                    Column(CatalogDocuments.LastSeenPath, PortablePhysicalType.Int64),
                    Column(CatalogDocuments.RetentionTieBreakerPath, PortablePhysicalType.String, length: 512)
                ],
                [
                    Logical(ByInstrumentResourceIndex, CatalogDocuments.InstrumentResourceIdPath, IndexValueKind.Keyword),
                    Logical(ByInstrumentKindIndex, CatalogDocuments.InstrumentKindPath, IndexValueKind.Number),
                    Logical(ByLastSeenIndex, CatalogDocuments.LastSeenPath, IndexValueKind.Number),
                    Logical(ByRetentionTieBreakerIndex, CatalogDocuments.RetentionTieBreakerPath, IndexValueKind.Keyword),
                    RetentionLogical()
                ],
                [
                    Query("instruments-by-resource", ByInstrumentResourceIndex),
                    Query("instruments-by-kind", ByInstrumentKindIndex),
                    RetentionQuery(InstrumentsByLastSeenQuery),
                    Query("instruments-by-retention-tie-breaker", ByRetentionTieBreakerIndex, CatalogDocuments.RetentionTieBreakerPath, sortable: true)
                ]),
            LedgerUnit()
        ],
        RequiredCapabilities,
        [
            "Catalog identities use Groundwork's released Unicode ordinal-ignore-case policy; cross-store joins remain outside the bounded catalog contract.",
            "The capture-operation document binds a drain batch identity and tracks a bounded attempt for every non-empty record stream."
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
        var changedIndexes = logicalIndexes
            .Where(index => queries.Any(query =>
                query.ExecutionClass == BoundedQueryExecutionClass.ScaleBearing &&
                StringComparer.Ordinal.Equals(query.IndexIdentity, index.Identity)))
            .Select(index => index.Identity)
            .ToHashSet(StringComparer.Ordinal);
        var versionedLogicalIndexes = logicalIndexes
            .SelectMany(index => changedIndexes.Contains(index.Identity)
                ? new[] { index, CloneLogicalIndex(index, V2(index.Identity)) }
                : [index])
            .ToArray();
        var versionedQueries = queries
            .Select(query => changedIndexes.Contains(query.IndexIdentity)
                ? CloneQuery(query, V2(query.IndexIdentity))
                : query)
            .ToArray();
        var indexes = logicalIndexes
            .SelectMany(index => changedIndexes.Contains(index.Identity)
                ? new[]
                {
                    LegacyPhysicalIndex(index),
                    PhysicalIndex(
                        CloneLogicalIndex(index, V2(index.Identity)),
                        versionedQueries)
                }
                : [LegacyPhysicalIndex(index)])
            .ToArray();
        var definition = PhysicalTableDefinition.PhysicalEntityTable(
            tableName,
            columns,
            indexes: indexes,
            schemaVersion: 1);
        return Unit(
            documentKind,
            label,
            IdentityPolicy.StringId(stringCasePolicy: StringIdentityCasePolicy.UnicodeOrdinalIgnoreCase)) with
        {
            PhysicalStorage = new(
                StorageUnitProvisioningMode.Declared,
                PhysicalStoragePolicy.Explicit(definition),
                versionedLogicalIndexes,
                versionedQueries)
        };
    }

    private static StorageUnit LedgerUnit()
    {
        var createdAt = new LogicalIndexDeclaration(
            ByCaptureCreatedAtIndex,
            [new IndexField("createdAt")],
            IndexValueKind.DateTime,
            isUnique: false);
        var route = new BoundedQueryDeclaration(
            CaptureOperationsByCreatedAtQuery,
            createdAt.Identity,
            new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal },
            QuerySortSupport.Descending,
            QueryPagingSupport.Offset,
            BoundedQueryExecutionClass.Ordinary,
            supportsTotalCount: true,
            sortFields: [new BoundedQuerySortField("createdAt", PhysicalSortDirection.Descending)]);
        var envelope = new DocumentEnvelopeDefinition();
        var definition = PhysicalTableDefinition.DedicatedDocumentTable(
            "elsa_open_telemetry_capture_operations",
            envelope,
            [new PhysicalIndexDefinition(
                createdAt.Identity,
                [
                    new PhysicalIndexColumnDefinition(envelope.StorageScopeColumn, 0),
                    new PhysicalIndexColumnDefinition("createdAt", 1)
                ])],
            linkedProjectedColumns:
            [new ProjectedColumnDefinition("createdAt", "createdAt", PortablePhysicalType.DateTime, IsNullable: false)],
            linkedProjectionLogicalName: "elsa_open_telemetry_capture_operation_indexes");
        return Unit(OperationLedgerKind, "OpenTelemetry capture operation ledger") with
        {
            PhysicalStorage = new(
                StorageUnitProvisioningMode.Declared,
                PhysicalStoragePolicy.Explicit(definition),
                [createdAt],
                [route])
        };
    }

    private static StorageUnit Unit(
        string documentKind,
        string label,
        IdentityPolicy? identityPolicy = null)
    {
#pragma warning disable GW0001 // PhysicalStorage on the returned unit is the authoritative declaration.
        return new(
            new StorageUnitIdentity(documentKind),
            label,
            StorageIntent.PortableDocument(),
            LifecyclePolicy.Mutable,
            identityPolicy ?? IdentityPolicy.StringId(),
            TenancyPolicy.Scoped,
            ConcurrencyPolicy.Optimistic(),
            SerializationPolicy.Json(),
            [],
            [],
            PhysicalizationPolicy.Portable);
#pragma warning restore GW0001
    }

    private static ProjectedColumnDefinition Column(
        string path,
        PortablePhysicalType type,
        int? length = null) => new(Path(path), Path(path), type, Length: length, IsNullable: false);

    private static LogicalIndexDeclaration Logical(string identity, string path, IndexValueKind kind) => new(
        identity,
        [new IndexField(Path(path))],
        kind,
        false);

    private static LogicalIndexDeclaration RetentionLogical() => new(
        ByRetentionIndex,
        [
            new IndexField(Path(CatalogDocuments.LastSeenPath), IndexValueKind.Number),
            new IndexField(Path(CatalogDocuments.RetentionTieBreakerPath), IndexValueKind.Keyword)
        ],
        IndexValueKind.Keyword,
        false);

    private static LogicalIndexDeclaration ResourceServiceLogical() => new(
        ByResourceServiceIndex,
        [
            new IndexField(Path(CatalogDocuments.ServiceNameHashPath), IndexValueKind.Keyword),
            new IndexField(Path(CatalogDocuments.LastSeenPath), IndexValueKind.Number),
            new IndexField(Path(CatalogDocuments.RetentionTieBreakerPath), IndexValueKind.Keyword)
        ],
        IndexValueKind.Keyword,
        false);

    private static BoundedQueryDeclaration Query(
        string name,
        string indexName,
        string? sortPath = null,
        bool sortable = false) => new(
        name,
        indexName,
        new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal },
        sortable ? QuerySortSupport.Both : QuerySortSupport.None,
        QueryPagingSupport.Cursor,
        BoundedQueryExecutionClass.ScaleBearing,
        supportsTotalCount: true,
        sortFields: sortable
            ? [new BoundedQuerySortField(Path(sortPath!), PhysicalSortDirection.Descending)]
            : []);

    private static BoundedQueryDeclaration RetentionQuery(string identity) => new(
        identity,
        ByRetentionIndex,
        new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal },
        QuerySortSupport.Descending,
        QueryPagingSupport.Cursor,
        BoundedQueryExecutionClass.ScaleBearing,
        supportsTotalCount: true,
        sortFields:
        [
            new(Path(CatalogDocuments.LastSeenPath), PhysicalSortDirection.Descending),
            new(Path(CatalogDocuments.RetentionTieBreakerPath), PhysicalSortDirection.Descending)
        ]);

    private static BoundedQueryDeclaration ResourceFilterQuery() => new(
        ResourcesFilteredQuery,
        ByRetentionIndex,
        new HashSet<PortableQueryOperation>
        {
            PortableQueryOperation.Equal,
            PortableQueryOperation.Contains
        },
        QuerySortSupport.Descending,
        QueryPagingSupport.Offset,
        BoundedQueryExecutionClass.Ordinary,
        supportsDisjunction: true,
        supportsTotalCount: true,
        sortFields:
        [
            new(Path(CatalogDocuments.LastSeenPath), PhysicalSortDirection.Descending),
            new(Path(CatalogDocuments.RetentionTieBreakerPath), PhysicalSortDirection.Descending)
        ],
        predicateFields:
        [
            new(
                Path(CatalogDocuments.LastSeenPath),
                new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal }),
            new(
                Path(CatalogDocuments.RetentionTieBreakerPath),
                new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal })
        ],
        residualPredicateFields:
        [
            new(
                Path(CatalogDocuments.ResourceStatusPath),
                IndexValueKind.Number,
                new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal }),
            new(
                Path(CatalogDocuments.ServiceNameHashPath),
                IndexValueKind.Keyword,
                new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal }),
            new(
                Path(CatalogDocuments.ResourceIdSearchKeyPath),
                IndexValueKind.String,
                new HashSet<PortableQueryOperation> { PortableQueryOperation.Contains }),
            new(
                Path(CatalogDocuments.ServiceNameComparisonKeyPath),
                IndexValueKind.String,
                new HashSet<PortableQueryOperation>
                {
                    PortableQueryOperation.Equal
                }),
            new(
                Path(CatalogDocuments.ServiceNameSearchKeyPath),
                IndexValueKind.String,
                new HashSet<PortableQueryOperation>
                {
                    PortableQueryOperation.Contains
                })
        ]);

    private static BoundedQueryDeclaration ResourceStatusQuery() => new(
        ResourcesByStatusQuery,
        ByRetentionIndex,
        new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal },
        QuerySortSupport.Descending,
        QueryPagingSupport.Cursor,
        BoundedQueryExecutionClass.ScaleBearing,
        supportsTotalCount: true,
        sortFields:
        [
            new(Path(CatalogDocuments.LastSeenPath), PhysicalSortDirection.Descending),
            new(Path(CatalogDocuments.RetentionTieBreakerPath), PhysicalSortDirection.Descending)
        ],
        predicateFields:
        [
            new(
                Path(CatalogDocuments.LastSeenPath),
                new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal }),
            new(
                Path(CatalogDocuments.RetentionTieBreakerPath),
                new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal })
        ],
        residualPredicateFields:
        [
            new(
                Path(CatalogDocuments.ResourceStatusPath),
                IndexValueKind.Number,
                new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal },
                isRequired: true)
        ]);

    private static BoundedQueryDeclaration ResourceServiceQuery() => new(
        ResourcesByServiceQuery,
        ByResourceServiceIndex,
        new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal },
        QuerySortSupport.Descending,
        QueryPagingSupport.Cursor,
        BoundedQueryExecutionClass.ScaleBearing,
        supportsTotalCount: true,
        sortFields:
        [
            new(Path(CatalogDocuments.LastSeenPath), PhysicalSortDirection.Descending),
            new(Path(CatalogDocuments.RetentionTieBreakerPath), PhysicalSortDirection.Descending)
        ],
        predicateFields:
        [
            new(
                Path(CatalogDocuments.ServiceNameHashPath),
                new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal })
        ],
        residualPredicateFields:
        [
            new(
                Path(CatalogDocuments.ServiceNameComparisonKeyPath),
                IndexValueKind.String,
                new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal },
                isRequired: true),
            new(
                Path(CatalogDocuments.ResourceStatusPath),
                IndexValueKind.Number,
                new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal })
        ]);

    private static PhysicalIndexDefinition PhysicalIndex(
        LogicalIndexDeclaration index,
        IReadOnlyCollection<BoundedQueryDeclaration> queries)
    {
        var paths = index.Fields.Select(field => Path(field.Path)).ToArray();
        var scaleBearingQueries = queries
            .Where(query =>
                query.ExecutionClass == BoundedQueryExecutionClass.ScaleBearing &&
                StringComparer.Ordinal.Equals(query.IndexIdentity, index.Identity))
            .ToArray();
        var fieldDirections = scaleBearingQueries
            .Select(query => index.Fields.Select(field =>
                    query.SortFields
                        .SingleOrDefault(sort => StringComparer.Ordinal.Equals(sort.Path, field.Path))
                        ?.Direction ?? PhysicalSortDirection.Ascending)
                .ToArray())
            .DistinctBy(directions => string.Join(",", directions.Select(direction => (int)direction)))
            .ToArray();
        if (fieldDirections.Length > 1)
        {
            throw new InvalidOperationException(
                $"OpenTelemetry catalog index '{index.Identity}' cannot serve conflicting scale-bearing sort shapes.");
        }

        var providerTieBreakPaging = scaleBearingQueries
            .Select(query => query.PagingSupport)
            .Distinct()
            .ToArray();
        if (providerTieBreakPaging.Length > 1)
        {
            throw new InvalidOperationException(
                $"OpenTelemetry catalog index '{index.Identity}' cannot mix cursor and offset provider identity tie-breaks.");
        }

        var columns = new List<PhysicalIndexColumnDefinition>
        {
            new("storage_scope", 0)
        };
        columns.AddRange(paths.Select((path, position) =>
            new PhysicalIndexColumnDefinition(
                path,
                position + 1,
                fieldDirections.SingleOrDefault()?[position] ?? PhysicalSortDirection.Ascending)));
        if (!index.IsUnique && providerTieBreakPaging.Length == 1)
        {
            var envelope = new DocumentEnvelopeDefinition();
            columns.Add(new PhysicalIndexColumnDefinition(
                providerTieBreakPaging[0] == QueryPagingSupport.Cursor
                    ? envelope.IdLookupKeyColumn
                    : envelope.IdComparisonKeyColumn,
                columns.Count,
                PhysicalSortDirection.Ascending));
        }

        return new(
            index.Identity,
            columns);
    }

    private static PhysicalIndexDefinition LegacyPhysicalIndex(LogicalIndexDeclaration index)
    {
        var paths = index.Fields.Select(field => Path(field.Path)).ToArray();
        return new(
            index.Identity,
            [
                new PhysicalIndexColumnDefinition("storage_scope", 0),
                .. paths.Select((path, position) =>
                    new PhysicalIndexColumnDefinition(
                        path,
                        position + 1,
                        index.Identity == ByResourceServiceIndex && position > 0
                            ? PhysicalSortDirection.Descending
                            : PhysicalSortDirection.Ascending))
            ]);
    }

    private static LogicalIndexDeclaration CloneLogicalIndex(
        LogicalIndexDeclaration index,
        string identity) =>
        new(
            identity,
            index.Fields,
            index.ValueKind,
            index.IsUnique,
            index.MissingValueBehavior);

    private static BoundedQueryDeclaration CloneQuery(
        BoundedQueryDeclaration query,
        string indexIdentity) =>
        new(
            query.Identity,
            indexIdentity,
            query.Operations,
            query.SortSupport,
            query.PagingSupport,
            query.ExecutionClass,
            query.SupportsDisjunction,
            query.SupportsTotalCount,
            query.SortFields,
            query.PredicateBindingMode == BoundedQueryPredicateBindingMode.ImplicitFirstLogicalIndexField
                ? null
                : query.PredicateFields,
            query.ResultOperations,
            query.LatestPerKeyPath,
            query.ResidualPredicateFields);

    private static string V2(string identity) => $"{identity}{AdditiveIndexVersionSuffix}";

    private static string Path(string path) => path.TrimStart('/');
}
