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
    private const string AuthorityPartitionColumn = "history_authority_partition";
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
            // page-history sweeps the whole history, so the index cannot omit rows whose sort ticks or
            // execution id have no value.
            MissingValueBehavior.IncludedAsNull);
        var alterationCaptureIndex = new LogicalIndexDeclaration(
            ElsaRuntimeStorageManifest.WorkflowExecutionAlterationCaptureOrderIndex,
            [
                new IndexField(ElsaRuntimeStorageManifest.WorkflowExecutionHistoryTenantIdField),
                new IndexField(ElsaRuntimeStorageManifest.WorkflowExecutionHistoryAuthorityPartitionField),
                new IndexField(ElsaRuntimeStorageManifest.WorkflowExecutionHistoryWorkflowExecutionIdField)
            ],
            IndexValueKind.Keyword,
            isUnique: false,
            MissingValueBehavior.IncludedAsNull);
        var pinnedArtifactIndex = new LogicalIndexDeclaration(
            ElsaRuntimeStorageManifest.WorkflowExecutionPinnedArtifactOrderIndex,
            [
                new IndexField(ElsaRuntimeStorageManifest.CollectionField),
                new IndexField(ElsaRuntimeStorageManifest.WorkflowExecutionHistoryArtifactIdField),
                new IndexField(
                    ElsaRuntimeStorageManifest.WorkflowExecutionHistoryWorkflowExecutionIdField)
            ],
            IndexValueKind.Keyword,
            isUnique: false,
            MissingValueBehavior.Excluded);
        var pinnedArtifactV2Index = new LogicalIndexDeclaration(
            $"{ElsaRuntimeStorageManifest.WorkflowExecutionPinnedArtifactOrderIndex}-v2",
            [
                new IndexField(ElsaRuntimeStorageManifest.CollectionField),
                new IndexField(ElsaRuntimeStorageManifest.WorkflowExecutionHistoryArtifactIdField),
                new IndexField(
                    ElsaRuntimeStorageManifest.WorkflowExecutionHistoryWorkflowExecutionIdField)
            ],
            IndexValueKind.Keyword,
            isUnique: true,
            // Stays Excluded: a unique index keying nullable columns cannot keep missing values,
            // because whether two valueless rows collide differs by provider (GW-ROUTE-007). Stated
            // on the physical definition below too, so the two cannot drift.
            MissingValueBehavior.Excluded);
        var faultedAttentionIndex = new LogicalIndexDeclaration(
            ElsaRuntimeStorageManifest.WorkflowExecutionFaultedAttentionOrderIndex,
            [
                new IndexField(
                    ElsaRuntimeStorageManifest.WorkflowExecutionHistoryStatusField,
                    IndexValueKind.Number),
                new IndexField(
                    ElsaRuntimeStorageManifest.WorkflowExecutionHistorySortTicksField,
                    IndexValueKind.Number),
                new IndexField(
                    ElsaRuntimeStorageManifest.WorkflowExecutionHistoryWorkflowExecutionIdField,
                    IndexValueKind.Keyword)
            ],
            IndexValueKind.Number,
            isUnique: false,
            MissingValueBehavior.IncludedAsNull);
        var envelope = new DocumentEnvelopeDefinition();
        var definition = PhysicalTableDefinition.PhysicalEntityTable(
            TableName,
            [
                Projected(
                    CollectionColumn,
                    ElsaRuntimeStorageManifest.CollectionField,
                    PortablePhysicalType.String,
                    ElsaRuntimeStorageManifest.RuntimeCollectionProjectionLength),
                Projected(
                    SortTicksColumn,
                    ElsaRuntimeStorageManifest.WorkflowExecutionHistorySortTicksField,
                    PortablePhysicalType.Int64),
                Projected(
                    WorkflowExecutionIdColumn,
                    ElsaRuntimeStorageManifest.WorkflowExecutionHistoryWorkflowExecutionIdField,
                    PortablePhysicalType.String,
                    ElsaRuntimeStorageManifest.RuntimeExecutionIdProjectionLength),
                Projected(
                    TenantIdColumn,
                    ElsaRuntimeStorageManifest.WorkflowExecutionHistoryTenantIdField,
                    PortablePhysicalType.String,
                    ElsaRuntimeStorageManifest.RuntimeTenantProjectionLength),
                Projected(
                    AuthorityPartitionColumn,
                    ElsaRuntimeStorageManifest.WorkflowExecutionHistoryAuthorityPartitionField,
                    PortablePhysicalType.String,
                    64),
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
                    PortablePhysicalType.String,
                    ElsaRuntimeStorageManifest.RuntimeExecutionIdProjectionLength)
            ],
            envelope,
            [
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
                    ],
                    // Must match the logical declaration above.
                    missingValueBehavior: MissingValueBehavior.IncludedAsNull),
                new PhysicalIndexDefinition(
                    alterationCaptureIndex.Identity,
                    [
                        new PhysicalIndexColumnDefinition(envelope.StorageScopeColumn, 0),
                        new PhysicalIndexColumnDefinition(TenantIdColumn, 1),
                        new PhysicalIndexColumnDefinition(AuthorityPartitionColumn, 2),
                        new PhysicalIndexColumnDefinition(WorkflowExecutionIdColumn, 3),
                        new PhysicalIndexColumnDefinition(envelope.IdLookupKeyColumn, 4)
                    ]),
                new PhysicalIndexDefinition(
                    faultedAttentionIndex.Identity,
                    [
                        new PhysicalIndexColumnDefinition(envelope.StorageScopeColumn, 0),
                        new PhysicalIndexColumnDefinition(StatusColumn, 1),
                        new PhysicalIndexColumnDefinition(
                            SortTicksColumn,
                            2,
                            PhysicalSortDirection.Descending),
                        new PhysicalIndexColumnDefinition(WorkflowExecutionIdColumn, 3),
                        new PhysicalIndexColumnDefinition(envelope.IdLookupKeyColumn, 4)
                    ]),
                new PhysicalIndexDefinition(
                    pinnedArtifactIndex.Identity,
                    [
                        new PhysicalIndexColumnDefinition(envelope.StorageScopeColumn, 0),
                        new PhysicalIndexColumnDefinition(CollectionColumn, 1),
                        new PhysicalIndexColumnDefinition(ArtifactIdColumn, 2),
                        new PhysicalIndexColumnDefinition(WorkflowExecutionIdColumn, 3)
                    ],
                    // Superseded by the -v2 index and not scale-bearing, so it keeps the narrow
                    // behavior its logical declaration states. Stated explicitly because the
                    // package default is IncludedAsNull and the two must not drift.
                    missingValueBehavior: MissingValueBehavior.Excluded),
                new PhysicalIndexDefinition(
                    pinnedArtifactV2Index.Identity,
                    [
                        new PhysicalIndexColumnDefinition(envelope.StorageScopeColumn, 0),
                        new PhysicalIndexColumnDefinition(CollectionColumn, 1),
                        new PhysicalIndexColumnDefinition(ArtifactIdColumn, 2),
                        new PhysicalIndexColumnDefinition(WorkflowExecutionIdColumn, 3)
                    ],
                    isUnique: true,
                    missingValueBehavior: MissingValueBehavior.Excluded)
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
        var faultedAttentionQuery = new BoundedQueryDeclaration(
            ElsaRuntimeStorageManifest.PageFaultedWorkflowExecutionsForAttentionQuery,
            faultedAttentionIndex.Identity,
            new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal },
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
                    ElsaRuntimeStorageManifest.WorkflowExecutionHistoryStatusField,
                    Equal)
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
                    Equal)
            ]);
        var alterationCaptureQuery = new BoundedQueryDeclaration(
            ElsaRuntimeStorageManifest.PageWorkflowExecutionsForAlterationCaptureQuery,
            alterationCaptureIndex.Identity,
            new HashSet<PortableQueryOperation>
            {
                PortableQueryOperation.Equal,
                PortableQueryOperation.GreaterThanOrEqual,
                PortableQueryOperation.LessThanOrEqual
            },
            QuerySortSupport.Ascending,
            QueryPagingSupport.Cursor,
            BoundedQueryExecutionClass.ScaleBearing,
            supportsTotalCount: true,
            sortFields:
            [
                new BoundedQuerySortField(
                    ElsaRuntimeStorageManifest.WorkflowExecutionHistoryWorkflowExecutionIdField,
                    PhysicalSortDirection.Ascending)
            ],
            predicateFields:
            [
                new BoundedQueryPredicateField(
                    ElsaRuntimeStorageManifest.WorkflowExecutionHistoryTenantIdField,
                    Equal),
                new BoundedQueryPredicateField(
                    ElsaRuntimeStorageManifest.WorkflowExecutionHistoryAuthorityPartitionField,
                    Equal)
            ],
            resultOperations: new HashSet<BoundedQueryResultOperation>
            {
                BoundedQueryResultOperation.Documents,
                BoundedQueryResultOperation.Count
            },
            residualPredicateFields:
            [
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
                    Equal),
                Residual(
                    ElsaRuntimeStorageManifest.WorkflowExecutionHistorySortTicksField,
                    IndexValueKind.Number,
                    Range)
            ]);
        var pinnedArtifactQuery = new BoundedQueryDeclaration(
            ElsaRuntimeStorageManifest.PagePinnedExecutableArtifactIdsQuery,
            pinnedArtifactV2Index.Identity,
            new HashSet<PortableQueryOperation>
            {
                PortableQueryOperation.Equal,
                PortableQueryOperation.NotEqual
            },
            QuerySortSupport.Ascending,
            QueryPagingSupport.Offset,
            BoundedQueryExecutionClass.ScaleBearing,
            supportsTotalCount: true,
            sortFields:
            [
                new BoundedQuerySortField(
                    ElsaRuntimeStorageManifest.WorkflowExecutionHistoryArtifactIdField,
                    PhysicalSortDirection.Ascending),
                new BoundedQuerySortField(
                    ElsaRuntimeStorageManifest.WorkflowExecutionHistoryWorkflowExecutionIdField,
                    PhysicalSortDirection.Ascending)
            ],
            predicateFields:
            [
                new BoundedQueryPredicateField(
                    ElsaRuntimeStorageManifest.CollectionField,
                    Equal)
            ],
            residualPredicateFields:
            [
                new BoundedQueryResidualPredicateField(
                    ElsaRuntimeStorageManifest.WorkflowExecutionHistoryArtifactIdField,
                    IndexValueKind.Keyword,
                    new HashSet<PortableQueryOperation> { PortableQueryOperation.NotEqual }),
                new BoundedQueryResidualPredicateField(
                    ElsaRuntimeStorageManifest.WorkflowExecutionHistoryWorkflowExecutionIdField,
                    IndexValueKind.Keyword,
                    new HashSet<PortableQueryOperation> { PortableQueryOperation.NotEqual })
            ],
            resultOperations: new HashSet<BoundedQueryResultOperation>
            {
                BoundedQueryResultOperation.Documents,
                BoundedQueryResultOperation.Count
            },
            latestPerKeyPath: ElsaRuntimeStorageManifest.WorkflowExecutionHistoryArtifactIdField);

        return unit with
        {
            PhysicalStorage = new StorageUnitPhysicalStorage(
                StorageUnitProvisioningMode.Declared,
                PhysicalStoragePolicy.Explicit(definition),
                storage.LogicalIndexes
                    .Where(index => !StringComparer.Ordinal.Equals(
                        index.Identity,
                        ElsaRuntimeStorageManifest.ByCollectionIndex))
                    .Where(index => !StringComparer.Ordinal.Equals(
                        index.Identity,
                        ElsaRuntimeStorageManifest.WorkflowExecutionHistoryOrderIndex))
                    .Where(index => !StringComparer.Ordinal.Equals(
                        index.Identity,
                        ElsaRuntimeStorageManifest.WorkflowExecutionAlterationCaptureOrderIndex))
                    .Where(index => !StringComparer.Ordinal.Equals(
                        index.Identity,
                        ElsaRuntimeStorageManifest.WorkflowExecutionFaultedAttentionOrderIndex))
                    .Where(index => !StringComparer.Ordinal.Equals(
                        index.Identity,
                        ElsaRuntimeStorageManifest.WorkflowExecutionPinnedArtifactOrderIndex))
                    .Concat([historyIndex, alterationCaptureIndex, faultedAttentionIndex, pinnedArtifactIndex, pinnedArtifactV2Index])
                    .ToArray(),
                storage.BoundedQueries
                    .Where(query => !StringComparer.Ordinal.Equals(
                        query.Identity,
                        ElsaRuntimeStorageManifest.ListAllQuery))
                    .Where(query => !StringComparer.Ordinal.Equals(
                        query.Identity,
                        ElsaRuntimeStorageManifest.PageWorkflowExecutionsQuery))
                    .Where(query => !StringComparer.Ordinal.Equals(
                        query.Identity,
                        ElsaRuntimeStorageManifest.PageWorkflowExecutionsForAlterationCaptureQuery))
                    .Where(query => !StringComparer.Ordinal.Equals(
                        query.Identity,
                        ElsaRuntimeStorageManifest.PageFaultedWorkflowExecutionsForAttentionQuery))
                    .Where(query => !StringComparer.Ordinal.Equals(
                        query.Identity,
                        ElsaRuntimeStorageManifest.PagePinnedExecutableArtifactIdsQuery))
                    .Concat([historyQuery, alterationCaptureQuery, faultedAttentionQuery, pinnedArtifactQuery])
                    .ToArray(),
                storage.NameOverrides,
                storage.BoundedMutations)
        };
    }

    private static ProjectedColumnDefinition Projected(
        string logicalName,
        string path,
        PortablePhysicalType type,
        int? length = null) =>
        new(
            logicalName,
            path,
            type,
            Length: type == PortablePhysicalType.String
                ? length ?? SharedDocumentsStorage.StringProjectionLength
                : null);

    private static BoundedQueryResidualPredicateField Residual(
        string path,
        IndexValueKind kind,
        IReadOnlySet<PortableQueryOperation> operations) =>
        new(path, kind, operations);
}
