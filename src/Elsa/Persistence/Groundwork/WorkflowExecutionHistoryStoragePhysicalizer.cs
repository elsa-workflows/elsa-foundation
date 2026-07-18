using Elsa.Persistence.Groundwork.Composition;
using Groundwork.Core.Indexing;
using Groundwork.Core.Manifests;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Queries;

namespace Elsa.Persistence.Groundwork;

/// <summary>
/// Gives workflow-execution history one physical entity table with a certified keyset order and
/// projected residual filters. Every provider compiles this same declaration into its native query path.
/// </summary>
internal static class WorkflowExecutionHistoryStoragePhysicalizer
{
    private const string TableName = "workflow_execution_states";
    private const string CollectionColumn = "collection";
    private const string SortTicksColumn = "history_sort_ticks";
    private const string WorkflowExecutionIdColumn = "history_workflow_execution_id";
    private const string TenantIdColumn = "history_tenant_id";
    private const string DefinitionIdColumn = "history_definition_id";
    private const string StatusColumn = "history_status";
    private const string RunKindColumn = "history_run_kind";
    private const string CorrelationIdColumn = "history_correlation_id";
    private const string ArtifactIdColumn = "history_artifact_id";

    private static readonly IReadOnlySet<PortableQueryOperation> Equal =
        new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal };
    private static readonly IReadOnlySet<PortableQueryOperation> Range =
        new HashSet<PortableQueryOperation>
        {
            PortableQueryOperation.GreaterThanOrEqual,
            PortableQueryOperation.LessThanOrEqual
        };

    public static StorageManifest AddRoute(StorageManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return manifest with
        {
            StorageUnits = manifest.StorageUnits.Select(unit =>
                StringComparer.Ordinal.Equals(
                    unit.Identity.Value,
                    ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind)
                    ? Physicalize(unit)
                    : unit).ToArray()
        };
    }

    private static StorageUnit Physicalize(StorageUnit unit)
    {
        var storage = unit.PhysicalStorage
            ?? throw new InvalidOperationException(
                "Workflow execution history requires an existing physical storage declaration.");
        var collectionIndex = storage.LogicalIndexes.Single(index =>
            StringComparer.Ordinal.Equals(index.Identity, ElsaRuntimeStorageManifest.ByCollectionIndex));
        var historyIndex = new LogicalIndexDeclaration(
            ElsaRuntimeStorageManifest.WorkflowExecutionHistoryOrderIndex,
            [
                new IndexField(
                    ElsaRuntimeStorageManifest.WorkflowExecutionHistorySortTicksField,
                    IndexValueKind.Number),
                new IndexField(
                    ElsaRuntimeStorageManifest.WorkflowExecutionHistoryWorkflowExecutionIdField,
                    IndexValueKind.Keyword)
            ],
            IndexValueKind.Number,
            isUnique: false,
            MissingValueBehavior.Excluded);
        var envelope = new DocumentEnvelopeDefinition();
        var definition = PhysicalTableDefinition.PhysicalEntityTable(
            TableName,
            [
                Projected(CollectionColumn, ElsaRuntimeStorageManifest.CollectionField, PortablePhysicalType.String),
                Projected(
                    SortTicksColumn,
                    ElsaRuntimeStorageManifest.WorkflowExecutionHistorySortTicksField,
                    PortablePhysicalType.Int64),
                Projected(
                    WorkflowExecutionIdColumn,
                    ElsaRuntimeStorageManifest.WorkflowExecutionHistoryWorkflowExecutionIdField,
                    PortablePhysicalType.String),
                Projected(
                    TenantIdColumn,
                    ElsaRuntimeStorageManifest.WorkflowExecutionHistoryTenantIdField,
                    PortablePhysicalType.String),
                Projected(
                    DefinitionIdColumn,
                    ElsaRuntimeStorageManifest.WorkflowExecutionHistoryDefinitionIdField,
                    PortablePhysicalType.String),
                Projected(
                    StatusColumn,
                    ElsaRuntimeStorageManifest.WorkflowExecutionHistoryStatusField,
                    PortablePhysicalType.Int32),
                Projected(
                    RunKindColumn,
                    ElsaRuntimeStorageManifest.WorkflowExecutionHistoryRunKindField,
                    PortablePhysicalType.Int32),
                Projected(
                    CorrelationIdColumn,
                    ElsaRuntimeStorageManifest.WorkflowExecutionHistoryCorrelationIdField,
                    PortablePhysicalType.String),
                Projected(
                    ArtifactIdColumn,
                    ElsaRuntimeStorageManifest.WorkflowExecutionHistoryArtifactIdField,
                    PortablePhysicalType.String)
            ],
            envelope,
            [
                new PhysicalIndexDefinition(
                    collectionIndex.Identity,
                    [
                        new PhysicalIndexColumnDefinition(envelope.StorageScopeColumn, 0),
                        new PhysicalIndexColumnDefinition(CollectionColumn, 1)
                    ]),
                new PhysicalIndexDefinition(
                    historyIndex.Identity,
                    [
                        new PhysicalIndexColumnDefinition(envelope.StorageScopeColumn, 0),
                        new PhysicalIndexColumnDefinition(
                            SortTicksColumn,
                            1,
                            PhysicalSortDirection.Descending),
                        new PhysicalIndexColumnDefinition(WorkflowExecutionIdColumn, 2),
                        new PhysicalIndexColumnDefinition(envelope.IdLookupKeyColumn, 3)
                    ])
            ]);
        var historyQuery = new BoundedQueryDeclaration(
            ElsaRuntimeStorageManifest.PageWorkflowExecutionsQuery,
            historyIndex.Identity,
            new HashSet<PortableQueryOperation>
            {
                PortableQueryOperation.Equal,
                PortableQueryOperation.GreaterThanOrEqual,
                PortableQueryOperation.LessThanOrEqual
            },
            QuerySortSupport.Descending,
            QueryPagingSupport.Cursor,
            BoundedQueryExecutionClass.ScaleBearing,
            supportsTotalCount: true,
            sortFields:
            [
                new BoundedQuerySortField(
                    ElsaRuntimeStorageManifest.WorkflowExecutionHistorySortTicksField,
                    PhysicalSortDirection.Descending),
                new BoundedQuerySortField(
                    ElsaRuntimeStorageManifest.WorkflowExecutionHistoryWorkflowExecutionIdField,
                    PhysicalSortDirection.Ascending)
            ],
            predicateFields:
            [
                new BoundedQueryPredicateField(
                    ElsaRuntimeStorageManifest.WorkflowExecutionHistorySortTicksField,
                    Range)
            ],
            resultOperations: new HashSet<BoundedQueryResultOperation>
            {
                BoundedQueryResultOperation.Documents,
                BoundedQueryResultOperation.Count
            },
            residualPredicateFields:
            [
                Residual(
                    ElsaRuntimeStorageManifest.WorkflowExecutionHistoryTenantIdField,
                    IndexValueKind.Keyword,
                    Equal),
                Residual(
                    ElsaRuntimeStorageManifest.WorkflowExecutionHistoryDefinitionIdField,
                    IndexValueKind.Keyword,
                    Equal),
                Residual(
                    ElsaRuntimeStorageManifest.WorkflowExecutionHistoryStatusField,
                    IndexValueKind.Number,
                    Equal),
                Residual(
                    ElsaRuntimeStorageManifest.WorkflowExecutionHistoryRunKindField,
                    IndexValueKind.Number,
                    Equal),
                Residual(
                    ElsaRuntimeStorageManifest.WorkflowExecutionHistoryCorrelationIdField,
                    IndexValueKind.Keyword,
                    Equal),
                Residual(PhysicalDocumentFieldPaths.Id, IndexValueKind.Keyword, Equal),
                Residual(
                    ElsaRuntimeStorageManifest.WorkflowExecutionHistoryArtifactIdField,
                    IndexValueKind.Keyword,
                    Equal)
            ]);

        return unit with
        {
            PhysicalStorage = new StorageUnitPhysicalStorage(
                StorageUnitProvisioningMode.Declared,
                PhysicalStoragePolicy.Explicit(definition),
                storage.LogicalIndexes
                    .Where(index => !StringComparer.Ordinal.Equals(
                        index.Identity,
                        ElsaRuntimeStorageManifest.WorkflowExecutionHistoryOrderIndex))
                    .Append(historyIndex)
                    .ToArray(),
                storage.BoundedQueries
                    .Where(query => !StringComparer.Ordinal.Equals(
                        query.Identity,
                        ElsaRuntimeStorageManifest.PageWorkflowExecutionsQuery))
                    .Append(historyQuery)
                    .ToArray(),
                storage.NameOverrides,
                storage.BoundedMutations)
        };
    }

    private static ProjectedColumnDefinition Projected(
        string logicalName,
        string path,
        PortablePhysicalType type) =>
        new(
            logicalName,
            path,
            type,
            Length: type == PortablePhysicalType.String
                ? LegacyGroundworkStorageManifestPhysicalizer.LegacyStringProjectionLength
                : null);

    private static BoundedQueryResidualPredicateField Residual(
        string path,
        IndexValueKind kind,
        IReadOnlySet<PortableQueryOperation> operations) =>
        new(path, kind, operations);
}
