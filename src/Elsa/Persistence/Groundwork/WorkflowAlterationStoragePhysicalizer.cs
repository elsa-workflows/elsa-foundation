using Elsa.Persistence.Groundwork.Composition;
using Groundwork.Core.Indexing;
using Groundwork.Core.Manifests;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Queries;

namespace Elsa.Persistence.Groundwork;

/// <summary>Provider-native, scale-bearing indexes for alteration coordination.</summary>
internal static class WorkflowAlterationStoragePhysicalizer
{
    private const int Sha256HexProjectionLength = 64;
    private const int TenantIdempotencyProjectionLength =
        ElsaRuntimeStorageManifest.RuntimeTenantProjectionLength + 1 + Sha256HexProjectionLength;
    private const int ActiveOrderKeyProjectionLength =
        19 + 1 + ElsaRuntimeStorageManifest.RuntimeExecutionIdProjectionLength;
    private static readonly IReadOnlySet<PortableQueryOperation> Equal = new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal };
    private static readonly IReadOnlySet<PortableQueryOperation> After = new HashSet<PortableQueryOperation> { PortableQueryOperation.GreaterThan };
    private static readonly IReadOnlySet<PortableQueryOperation> Due = new HashSet<PortableQueryOperation> { PortableQueryOperation.LessThanOrEqual };

    public static StorageManifest AddRoutes(StorageManifest manifest) => manifest with
    {
        StorageUnits = manifest.StorageUnits.Select(unit => unit.Identity.Value switch
        {
            ElsaRuntimeStorageManifest.WorkflowAlterationPlanDocumentKind => PhysicalizePlans(unit),
            ElsaRuntimeStorageManifest.WorkflowAlterationJobDocumentKind => PhysicalizeJobs(unit),
            _ => unit
        }).ToArray()
    };

    private static StorageUnit PhysicalizePlans(StorageUnit unit)
    {
        var (storage, definition) = Shared(unit);
        const string planId = "alteration_plan_id", tenant = "alteration_tenant", idempotency = "alteration_idempotency",
            tenantIdempotency = "alteration_tenant_idempotency", status = "alteration_status",
            activeOrder = "alteration_active_order";
        var envelope = new DocumentEnvelopeDefinition();
        var list = Index(
            ElsaRuntimeStorageManifest.WorkflowAlterationPlanByCollection,
            IndexValueKind.Keyword,
            Field(ElsaRuntimeStorageManifest.CollectionField),
            Field(ElsaRuntimeStorageManifest.WorkflowAlterationPlanIdField));
        var active = Index(
            ElsaRuntimeStorageManifest.WorkflowAlterationPlanStatusField,
            IndexValueKind.Keyword,
            Field(ElsaRuntimeStorageManifest.WorkflowAlterationPlanStatusField),
            Field(ElsaRuntimeStorageManifest.WorkflowAlterationPlanActiveOrderKeyField));
        var admission = Index(
            ElsaRuntimeStorageManifest.WorkflowAlterationPlanByTenantAndIdempotency,
            IndexValueKind.Keyword,
            Field(ElsaRuntimeStorageManifest.WorkflowAlterationPlanTenantPartitionField),
            Field(ElsaRuntimeStorageManifest.WorkflowAlterationPlanIdempotencyKeyHashField),
            Field(ElsaRuntimeStorageManifest.WorkflowAlterationPlanIdField));
        var activeByTenant = Index(
            ElsaRuntimeStorageManifest.WorkflowAlterationPlanByTenantAndStatus,
            IndexValueKind.Keyword,
            Field(ElsaRuntimeStorageManifest.WorkflowAlterationPlanTenantPartitionField),
            Field(ElsaRuntimeStorageManifest.WorkflowAlterationPlanStatusField),
            Field(ElsaRuntimeStorageManifest.WorkflowAlterationPlanActiveOrderKeyField));
        var uniqueAdmission = new LogicalIndexDeclaration(
            ElsaRuntimeStorageManifest.WorkflowAlterationPlanIdempotencyUniqueness,
            [Field(ElsaRuntimeStorageManifest.WorkflowAlterationPlanTenantIdempotencyKeyField)],
            IndexValueKind.Keyword,
            isUnique: true,
            MissingValueBehavior.Excluded);
        var columns = Columns(definition, [
            Column(planId, ElsaRuntimeStorageManifest.WorkflowAlterationPlanIdField, PortablePhysicalType.String, ElsaRuntimeStorageManifest.RuntimeExecutionIdProjectionLength),
            Column(tenant, ElsaRuntimeStorageManifest.WorkflowAlterationPlanTenantPartitionField, PortablePhysicalType.String, ElsaRuntimeStorageManifest.RuntimeTenantProjectionLength),
            Column(idempotency, ElsaRuntimeStorageManifest.WorkflowAlterationPlanIdempotencyKeyHashField, PortablePhysicalType.String, Sha256HexProjectionLength),
            Column(tenantIdempotency, ElsaRuntimeStorageManifest.WorkflowAlterationPlanTenantIdempotencyKeyField, PortablePhysicalType.String, TenantIdempotencyProjectionLength),
            Column(status, ElsaRuntimeStorageManifest.WorkflowAlterationPlanStatusField, PortablePhysicalType.String, ElsaRuntimeStorageManifest.RuntimeStatusProjectionLength),
            Column(activeOrder, ElsaRuntimeStorageManifest.WorkflowAlterationPlanActiveOrderKeyField, PortablePhysicalType.String, ActiveOrderKeyProjectionLength)]);
        var listCollectionColumn = ColumnName(columns, ElsaRuntimeStorageManifest.CollectionField);
        var planIdColumn = ColumnName(columns, ElsaRuntimeStorageManifest.WorkflowAlterationPlanIdField);
        var tenantColumn = ColumnName(columns, ElsaRuntimeStorageManifest.WorkflowAlterationPlanTenantPartitionField);
        var idempotencyColumn = ColumnName(columns, ElsaRuntimeStorageManifest.WorkflowAlterationPlanIdempotencyKeyHashField);
        var tenantIdempotencyColumn = ColumnName(columns, ElsaRuntimeStorageManifest.WorkflowAlterationPlanTenantIdempotencyKeyField);
        var statusColumn = ColumnName(columns, ElsaRuntimeStorageManifest.WorkflowAlterationPlanStatusField);
        var activeOrderColumn = ColumnName(columns, ElsaRuntimeStorageManifest.WorkflowAlterationPlanActiveOrderKeyField);
        var indexes = new[] { list, active, admission, activeByTenant, uniqueAdmission };
        var physical = PhysicalTableDefinition.SharedDocuments(
            definition.SharedStorage!,
            columns,
            definition.Indexes
                .Where(index => indexes.All(replacement => replacement.Identity != index.LogicalName))
                .Concat([
                    Physical(envelope, list, listCollectionColumn, planIdColumn),
                    Physical(envelope, active, statusColumn, activeOrderColumn),
                    Physical(envelope, admission, tenantColumn, idempotencyColumn, planIdColumn),
                    Physical(envelope, activeByTenant, tenantColumn, statusColumn, activeOrderColumn),
                    PhysicalExact(envelope, uniqueAdmission, tenantIdempotencyColumn)])
                .ToArray(),
            definition.SchemaVersion,
            definition.Evolution,
            definition.LinkedProjectionLogicalName,
            definition.LinkedKey);
        return Replace(unit, storage, physical, indexes, [
            Query(ElsaRuntimeStorageManifest.ListWorkflowAlterationPlansQuery, list, [new BoundedQueryPredicateField(ElsaRuntimeStorageManifest.CollectionField, Equal)], [new BoundedQuerySortField(ElsaRuntimeStorageManifest.WorkflowAlterationPlanIdField, PhysicalSortDirection.Ascending)]),
            Query(ElsaRuntimeStorageManifest.PageActiveWorkflowAlterationPlansQuery, active, [new BoundedQueryPredicateField(ElsaRuntimeStorageManifest.WorkflowAlterationPlanStatusField, Equal), new BoundedQueryPredicateField(ElsaRuntimeStorageManifest.WorkflowAlterationPlanActiveOrderKeyField, After)], [new BoundedQuerySortField(ElsaRuntimeStorageManifest.WorkflowAlterationPlanActiveOrderKeyField, PhysicalSortDirection.Ascending)]),
            Query(ElsaRuntimeStorageManifest.PageActiveWorkflowAlterationPlansByTenantQuery, activeByTenant, [new BoundedQueryPredicateField(ElsaRuntimeStorageManifest.WorkflowAlterationPlanTenantPartitionField, Equal), new BoundedQueryPredicateField(ElsaRuntimeStorageManifest.WorkflowAlterationPlanStatusField, Equal), new BoundedQueryPredicateField(ElsaRuntimeStorageManifest.WorkflowAlterationPlanActiveOrderKeyField, After)], [new BoundedQuerySortField(ElsaRuntimeStorageManifest.WorkflowAlterationPlanActiveOrderKeyField, PhysicalSortDirection.Ascending)]),
            Query(ElsaRuntimeStorageManifest.FindWorkflowAlterationPlanByTenantAndIdempotencyQuery, admission, [new BoundedQueryPredicateField(ElsaRuntimeStorageManifest.WorkflowAlterationPlanTenantPartitionField, Equal), new BoundedQueryPredicateField(ElsaRuntimeStorageManifest.WorkflowAlterationPlanIdempotencyKeyHashField, Equal)], [new BoundedQuerySortField(ElsaRuntimeStorageManifest.WorkflowAlterationPlanIdField, PhysicalSortDirection.Ascending)])]);
    }

    private static StorageUnit PhysicalizeJobs(StorageUnit unit)
    {
        var (storage, definition) = Shared(unit);
        const string jobId = "alteration_job_id", planId = "alteration_job_plan_id", ordinal = "alteration_capture_ordinal", claimable = "alteration_claimable", status = "alteration_job_status", checkpoint = "alteration_checkpoint";
        var envelope = new DocumentEnvelopeDefinition();
        var page = Index(
            ElsaRuntimeStorageManifest.WorkflowAlterationJobByPlan,
            IndexValueKind.Keyword,
            Field(ElsaRuntimeStorageManifest.WorkflowAlterationJobPlanIdField),
            Field(ElsaRuntimeStorageManifest.WorkflowAlterationJobCaptureOrdinalField, IndexValueKind.Number),
            Field(ElsaRuntimeStorageManifest.WorkflowAlterationJobIdField));
        var claim = Index(
            ElsaRuntimeStorageManifest.WorkflowAlterationJobByClaimability,
            IndexValueKind.DateTime,
            Field(ElsaRuntimeStorageManifest.WorkflowAlterationJobClaimableAtField, IndexValueKind.DateTime),
            Field(ElsaRuntimeStorageManifest.WorkflowAlterationJobIdField));
        var claimByPlan = Index(
            ElsaRuntimeStorageManifest.WorkflowAlterationJobByPlanAndClaimability,
            IndexValueKind.Keyword,
            Field(ElsaRuntimeStorageManifest.WorkflowAlterationJobPlanIdField),
            Field(ElsaRuntimeStorageManifest.WorkflowAlterationJobClaimableAtField, IndexValueKind.DateTime),
            Field(ElsaRuntimeStorageManifest.WorkflowAlterationJobIdField));
        var claimByPlanAndStatus = Index(
            ElsaRuntimeStorageManifest.WorkflowAlterationJobByPlanStatusAndClaimability,
            IndexValueKind.Keyword,
            Field(ElsaRuntimeStorageManifest.WorkflowAlterationJobPlanIdField),
            Field(ElsaRuntimeStorageManifest.WorkflowAlterationJobStatusField),
            Field(ElsaRuntimeStorageManifest.WorkflowAlterationJobClaimableAtField, IndexValueKind.DateTime),
            Field(ElsaRuntimeStorageManifest.WorkflowAlterationJobIdField));
        var counts = Index(
            "alteration_jobs_counts",
            IndexValueKind.Keyword,
            Field(ElsaRuntimeStorageManifest.WorkflowAlterationJobPlanIdField),
            Field(ElsaRuntimeStorageManifest.WorkflowAlterationJobStatusField),
            Field(ElsaRuntimeStorageManifest.WorkflowAlterationJobIdField));
        var commit = Index(
            "alteration_job_checkpoint",
            IndexValueKind.Keyword,
            Field(ElsaRuntimeStorageManifest.WorkflowAlterationJobCheckpointCommitIdField),
            Field(ElsaRuntimeStorageManifest.WorkflowAlterationJobIdField));
        var columns = Columns(definition, [
            Column(jobId, ElsaRuntimeStorageManifest.WorkflowAlterationJobIdField, PortablePhysicalType.String, ElsaRuntimeStorageManifest.RuntimeExecutionIdProjectionLength),
            Column(planId, ElsaRuntimeStorageManifest.WorkflowAlterationJobPlanIdField, PortablePhysicalType.String, ElsaRuntimeStorageManifest.RuntimeExecutionIdProjectionLength),
            Column(ordinal, ElsaRuntimeStorageManifest.WorkflowAlterationJobCaptureOrdinalField, PortablePhysicalType.Int64),
            Column(claimable, ElsaRuntimeStorageManifest.WorkflowAlterationJobClaimableAtField, PortablePhysicalType.DateTime),
            Column(status, ElsaRuntimeStorageManifest.WorkflowAlterationJobStatusField, PortablePhysicalType.String, ElsaRuntimeStorageManifest.RuntimeStatusProjectionLength),
            Column(checkpoint, ElsaRuntimeStorageManifest.WorkflowAlterationJobCheckpointCommitIdField, PortablePhysicalType.String, ElsaRuntimeStorageManifest.RuntimeExecutionIdProjectionLength)]);
        var jobIdColumn = ColumnName(columns, ElsaRuntimeStorageManifest.WorkflowAlterationJobIdField);
        var planIdColumn = ColumnName(columns, ElsaRuntimeStorageManifest.WorkflowAlterationJobPlanIdField);
        var ordinalColumn = ColumnName(columns, ElsaRuntimeStorageManifest.WorkflowAlterationJobCaptureOrdinalField);
        var claimableColumn = ColumnName(columns, ElsaRuntimeStorageManifest.WorkflowAlterationJobClaimableAtField);
        var statusColumn = ColumnName(columns, ElsaRuntimeStorageManifest.WorkflowAlterationJobStatusField);
        var checkpointColumn = ColumnName(columns, ElsaRuntimeStorageManifest.WorkflowAlterationJobCheckpointCommitIdField);
        var indexes = new[] { page, claim, claimByPlan, claimByPlanAndStatus, counts, commit };
        var physical = PhysicalTableDefinition.SharedDocuments(
            definition.SharedStorage!,
            columns,
            definition.Indexes
                .Where(index => indexes.All(replacement => replacement.Identity != index.LogicalName))
                .Concat([
                    Physical(envelope, page, planIdColumn, ordinalColumn, jobIdColumn),
                    Physical(envelope, claim, claimableColumn, jobIdColumn),
                    Physical(envelope, claimByPlan, planIdColumn, claimableColumn, jobIdColumn),
                    Physical(envelope, claimByPlanAndStatus, planIdColumn, statusColumn, claimableColumn, jobIdColumn),
                    PhysicalExact(envelope, counts, planIdColumn, statusColumn, jobIdColumn),
                    PhysicalExact(envelope, commit, checkpointColumn, jobIdColumn)])
                .ToArray(),
            definition.SchemaVersion,
            definition.Evolution,
            definition.LinkedProjectionLogicalName,
            definition.LinkedKey);
        return Replace(unit, storage, physical, indexes, [
            Query(ElsaRuntimeStorageManifest.PageWorkflowAlterationJobsByPlanQuery, page, [new BoundedQueryPredicateField(ElsaRuntimeStorageManifest.WorkflowAlterationJobPlanIdField, Equal)], [new BoundedQuerySortField(ElsaRuntimeStorageManifest.WorkflowAlterationJobCaptureOrdinalField, PhysicalSortDirection.Ascending), new BoundedQuerySortField(ElsaRuntimeStorageManifest.WorkflowAlterationJobIdField, PhysicalSortDirection.Ascending)]),
            Query(ElsaRuntimeStorageManifest.ListClaimableWorkflowAlterationJobsQuery, claim, [new BoundedQueryPredicateField(ElsaRuntimeStorageManifest.WorkflowAlterationJobClaimableAtField, Due)], [new BoundedQuerySortField(ElsaRuntimeStorageManifest.WorkflowAlterationJobClaimableAtField, PhysicalSortDirection.Ascending), new BoundedQuerySortField(ElsaRuntimeStorageManifest.WorkflowAlterationJobIdField, PhysicalSortDirection.Ascending)]),
            Query(ElsaRuntimeStorageManifest.ListClaimableWorkflowAlterationJobsByPlanQuery, claimByPlan, [new BoundedQueryPredicateField(ElsaRuntimeStorageManifest.WorkflowAlterationJobPlanIdField, Equal), new BoundedQueryPredicateField(ElsaRuntimeStorageManifest.WorkflowAlterationJobClaimableAtField, Due)], [new BoundedQuerySortField(ElsaRuntimeStorageManifest.WorkflowAlterationJobClaimableAtField, PhysicalSortDirection.Ascending), new BoundedQuerySortField(ElsaRuntimeStorageManifest.WorkflowAlterationJobIdField, PhysicalSortDirection.Ascending)]),
            Query(ElsaRuntimeStorageManifest.ListRunningClaimableWorkflowAlterationJobsByPlanQuery, claimByPlanAndStatus, [new BoundedQueryPredicateField(ElsaRuntimeStorageManifest.WorkflowAlterationJobPlanIdField, Equal), new BoundedQueryPredicateField(ElsaRuntimeStorageManifest.WorkflowAlterationJobStatusField, Equal), new BoundedQueryPredicateField(ElsaRuntimeStorageManifest.WorkflowAlterationJobClaimableAtField, Due)], [new BoundedQuerySortField(ElsaRuntimeStorageManifest.WorkflowAlterationJobClaimableAtField, PhysicalSortDirection.Ascending), new BoundedQuerySortField(ElsaRuntimeStorageManifest.WorkflowAlterationJobIdField, PhysicalSortDirection.Ascending)]),
            Query(ElsaRuntimeStorageManifest.ListPendingClaimableWorkflowAlterationJobsByPlanQuery, claimByPlanAndStatus, [new BoundedQueryPredicateField(ElsaRuntimeStorageManifest.WorkflowAlterationJobPlanIdField, Equal), new BoundedQueryPredicateField(ElsaRuntimeStorageManifest.WorkflowAlterationJobStatusField, Equal), new BoundedQueryPredicateField(ElsaRuntimeStorageManifest.WorkflowAlterationJobClaimableAtField, Due)], [new BoundedQuerySortField(ElsaRuntimeStorageManifest.WorkflowAlterationJobClaimableAtField, PhysicalSortDirection.Ascending), new BoundedQuerySortField(ElsaRuntimeStorageManifest.WorkflowAlterationJobIdField, PhysicalSortDirection.Ascending)]),
            Query(ElsaRuntimeStorageManifest.ListPendingWorkflowAlterationJobsByPlanQuery, counts, [new BoundedQueryPredicateField(ElsaRuntimeStorageManifest.WorkflowAlterationJobPlanIdField, Equal), new BoundedQueryPredicateField(ElsaRuntimeStorageManifest.WorkflowAlterationJobStatusField, Equal)], [new BoundedQuerySortField(ElsaRuntimeStorageManifest.WorkflowAlterationJobIdField, PhysicalSortDirection.Ascending)]),
            Query(ElsaRuntimeStorageManifest.CountWorkflowAlterationJobsByPlanAndStatusQuery, counts, [new BoundedQueryPredicateField(ElsaRuntimeStorageManifest.WorkflowAlterationJobPlanIdField, Equal), new BoundedQueryPredicateField(ElsaRuntimeStorageManifest.WorkflowAlterationJobStatusField, Equal)], [new BoundedQuerySortField(ElsaRuntimeStorageManifest.WorkflowAlterationJobIdField, PhysicalSortDirection.Ascending)]),
            Query(ElsaRuntimeStorageManifest.FindWorkflowAlterationJobByCheckpointCommitIdQuery, commit, [new BoundedQueryPredicateField(ElsaRuntimeStorageManifest.WorkflowAlterationJobCheckpointCommitIdField, Equal)], [new BoundedQuerySortField(ElsaRuntimeStorageManifest.WorkflowAlterationJobIdField, PhysicalSortDirection.Ascending)])]);
    }

    private static (StorageUnitPhysicalStorage, PhysicalTableDefinition) Shared(StorageUnit unit)
    {
        var storage = unit.PhysicalStorage ?? throw new InvalidOperationException("Alteration storage must be physicalized.");
        if (storage.Policy is not PhysicalStoragePolicy.ExplicitPolicy { Definition: var definition } || definition.Form != PhysicalStorageForm.SharedDocuments) throw new InvalidOperationException("Alteration storage requires shared documents.");
        return (storage, definition);
    }
    private static ProjectedColumnDefinition Column(string name, string path, PortablePhysicalType type, int? length = null) =>
        new(name, path, type, type == PortablePhysicalType.String ? length ?? SharedDocumentsStorage.StringProjectionLength : null);
    private static ProjectedColumnDefinition[] Columns(PhysicalTableDefinition definition, ProjectedColumnDefinition[] additions)
    {
        var additionsByPath = additions.ToDictionary(addition => addition.Path, StringComparer.Ordinal);
        return definition.ProjectedColumns
            .Select(existing => additionsByPath.TryGetValue(existing.Path, out var replacement)
                ? existing with { Length = replacement.Length }
                : existing)
            .Concat(additions.Where(addition => definition.ProjectedColumns.All(existing => existing.Path != addition.Path)))
            .ToArray();
    }
    private static string ColumnName(IReadOnlyCollection<ProjectedColumnDefinition> columns, string path) =>
        columns.Single(column => StringComparer.Ordinal.Equals(column.Path, path)).LogicalName;
    private static IndexField Field(string path, IndexValueKind kind = IndexValueKind.Keyword) => new(path, kind);
    private static LogicalIndexDeclaration Index(string id, IndexValueKind valueKind, params IndexField[] fields) =>
        // Alteration job/plan sweeps must see every row, so these traversal indexes cannot omit the
        // ones whose keyed columns have no value.
        new(id, fields, valueKind, false, MissingValueBehavior.IncludedAsNull);
    private static PhysicalIndexDefinition Physical(
        DocumentEnvelopeDefinition envelope,
        LogicalIndexDeclaration index,
        params string[] columns) =>
        new(
            index.Identity,
            [
                new PhysicalIndexColumnDefinition(envelope.StorageScopeColumn, 0),
                .. columns.Select((column, position) => new PhysicalIndexColumnDefinition(column, position + 1)),
                new PhysicalIndexColumnDefinition(envelope.IdLookupKeyColumn, columns.Length + 1)
            ]);

    private static PhysicalIndexDefinition PhysicalExact(
        DocumentEnvelopeDefinition envelope,
        LogicalIndexDeclaration index,
        params string[] columns) =>
        new(
            index.Identity,
            [
                new PhysicalIndexColumnDefinition(envelope.StorageScopeColumn, 0),
                .. columns.Select((column, position) => new PhysicalIndexColumnDefinition(column, position + 1))
            ]);
    private static BoundedQueryDeclaration Query(string id, LogicalIndexDeclaration index, IReadOnlyList<BoundedQueryPredicateField> predicates, IReadOnlyList<BoundedQuerySortField> sort) => new(
        id,
        index.Identity,
        predicates.SelectMany(p => p.Operations).ToHashSet(),
        QuerySortSupport.Ascending,
        QueryPagingSupport.Cursor,
        BoundedQueryExecutionClass.ScaleBearing,
        supportsTotalCount: true,
        sortFields: sort,
        predicateFields: predicates,
        resultOperations: new HashSet<BoundedQueryResultOperation> { BoundedQueryResultOperation.Documents, BoundedQueryResultOperation.Count });
    private static StorageUnit Replace(StorageUnit unit, StorageUnitPhysicalStorage storage, PhysicalTableDefinition definition, LogicalIndexDeclaration[] indexes, BoundedQueryDeclaration[] queries) => unit with
    {
        PhysicalStorage = new StorageUnitPhysicalStorage(
            storage.ProvisioningMode,
            PhysicalStoragePolicy.Explicit(definition),
            storage.LogicalIndexes
                .Where(existing => indexes.All(replacement => replacement.Identity != existing.Identity))
                .Concat(indexes)
                .ToArray(),
            storage.BoundedQueries
                .Where(existing => queries.All(replacement => replacement.Identity != existing.Identity))
                .Concat(queries)
                .ToArray(),
            storage.NameOverrides,
            storage.BoundedMutations)
    };
}
