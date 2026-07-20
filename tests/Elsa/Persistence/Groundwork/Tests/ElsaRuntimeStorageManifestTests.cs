using Elsa.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Testing;
using Elsa.Persistence.Groundwork.Unified.Composition;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Core.Capabilities;
using Groundwork.Core.Indexing;
using Groundwork.Core.Manifests;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Queries;
using Groundwork.Core.SchemaEvolution;
using Groundwork.Sqlite;
using Groundwork.Sqlite.PhysicalStorage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests;

/// <summary>
/// Manifest-shape guards for the runtime storage manifest. These pin the declared index surface the store
/// bridges depend on so a rename or accidental removal fails loudly rather than silently degrading a query.
/// </summary>
public sealed class ElsaRuntimeStorageManifestTests
{
    [Fact]
    public async Task Attention_routes_upgrade_existing_sqlite_schema_additively()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"elsa-groundwork-attention-upgrade-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={databasePath}";

        try
        {
            var legacyManifest = CreatePreAttentionRuntimeManifest();
            var legacySource = await CreateSqliteSchemaSourceAsync(legacyManifest);
            await using (var connection = new SqliteConnection(connectionString))
            {
                var applied = await PhysicalSchemaApplication.ApplyAsync(
                    legacySource.PhysicalTarget,
                    new SqlitePhysicalSchemaExecutor(connection));
                Assert.NotEqual(PhysicalSchemaApplicationOutcome.Rejected, applied.Outcome);
                Assert.NotEqual(PhysicalSchemaApplicationOutcome.AuthorizationRequired, applied.Outcome);
            }

            var currentSource = await CreateSqliteSchemaSourceAsync(ElsaRuntimeStorageManifest.CreatePhysicalized());
            await using var inspectionConnection = new SqliteConnection(connectionString);
            var admission = await currentSource.InspectRuntimeAdmissionAsync(
                new SqlitePhysicalSchemaExecutor(inspectionConnection),
                new GroundworkRuntimeSchemaAdmissionOptions { AutoApplyOnStartup = true });

            Assert.True(admission.IsReady, string.Join(Environment.NewLine, admission.Diagnostics.Select(x => x.Message)));
            Assert.True(admission.AppliedOperationCount > 0);
            Assert.DoesNotContain(admission.Diagnostics, diagnostic => diagnostic.Code == "ELSA-GW-SCHEMA-DRIFT");
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public void ActivityExecutionState_Declares_ByParent_Index_And_Query()
    {
        var manifest = ElsaRuntimeStorageManifest.Create();
        var unit = manifest.StorageUnits.Single(u => u.Identity.Value == ElsaRuntimeStorageManifest.ActivityExecutionStateDocumentKind);

        // The additive parent-scoped index (#514/#413 item 3) must be declared over the persisted nested field, alongside
        // the pre-existing by-workflow-execution index (this is additive, not a replacement).
        Assert.Contains(unit.Indexes, i => i.Identity == ElsaRuntimeStorageManifest.ByWorkflowExecutionIndex);

        var byParent = Assert.Single(unit.Indexes, i => i.Identity == ElsaRuntimeStorageManifest.ByParentActivityExecutionIndex);
        Assert.Equal(ElsaRuntimeStorageManifest.ParentActivityExecutionIdField, Assert.Single(byParent.Fields).Path);
        Assert.Equal("state.parentActivityExecutionId", byParent.Fields[0].Path);

        Assert.Contains(unit.Queries, q => q.IndexIdentity == ElsaRuntimeStorageManifest.ByParentActivityExecutionIndex);
    }

    [Fact]
    public async Task Activity_execution_inspection_declares_ordered_cursor_summary_route()
    {
        var declaration = await new RuntimeGroundworkStorageManifestSource().CreateDeclarationAsync();
        var unit = declaration.Manifest.StorageUnits.Single(candidate =>
            candidate.Identity.Value == ElsaRuntimeStorageManifest.ActivityExecutionInspectionDocumentKind);
        var logical = Assert.Single(
            unit.PhysicalStorage!.LogicalIndexes,
            index => index.Identity == ElsaRuntimeStorageManifest.ActivityExecutionInspectionOrderIndex);
        var route = Assert.Single(
            unit.PhysicalStorage.BoundedQueries,
            query => query.Identity == ElsaRuntimeStorageManifest.PageActivityExecutionInspectionSummariesQuery);
        var physical = Assert.IsType<PhysicalStoragePolicy.ExplicitPolicy>(
            unit.PhysicalStorage.Policy).Definition;

        Assert.Collection(
            logical.Fields,
            workflow => Assert.Equal(ElsaRuntimeStorageManifest.WorkflowExecutionIdField, workflow.Path),
            sequence =>
            {
                Assert.Equal(
                    ElsaRuntimeStorageManifest.ActivityExecutionInspectionSummaryExecutionSequenceField,
                    sequence.Path);
                Assert.Equal(IndexValueKind.Number, sequence.ValueKind);
            },
            scheduled =>
            {
                Assert.Equal(
                    ElsaRuntimeStorageManifest.ActivityExecutionInspectionSummaryScheduledAtField,
                    scheduled.Path);
                Assert.Equal(IndexValueKind.DateTime, scheduled.ValueKind);
            },
            activity => Assert.Equal(
                ElsaRuntimeStorageManifest.ActivityExecutionInspectionSummaryActivityExecutionIdField,
                activity.Path));
        Assert.Equal(QueryPagingSupport.Cursor, route.PagingSupport);
        Assert.True(route.SupportsTotalCount);
        Assert.Collection(
            route.SortFields,
            sequence => Assert.Equal(
                ElsaRuntimeStorageManifest.ActivityExecutionInspectionSummaryExecutionSequenceField,
                sequence.Path),
            scheduled => Assert.Equal(
                ElsaRuntimeStorageManifest.ActivityExecutionInspectionSummaryScheduledAtField,
                scheduled.Path),
            activity => Assert.Equal(
                ElsaRuntimeStorageManifest.ActivityExecutionInspectionSummaryActivityExecutionIdField,
                activity.Path));
        Assert.Contains(
            physical.ProjectedColumns,
            column =>
                column.Path == ElsaRuntimeStorageManifest.ActivityExecutionInspectionSummaryExecutionSequenceField &&
                column.Type == PortablePhysicalType.Int64);
        Assert.Contains(
            physical.ProjectedColumns,
            column =>
                column.Path == ElsaRuntimeStorageManifest.ActivityExecutionInspectionSummaryScheduledAtField &&
                column.Type == PortablePhysicalType.DateTime);
    }

    [Fact]
    public void ExecutableActivityTemplate_Declares_FlatEnvelope_TemplateHash_Index()
    {
        var manifest = ElsaRuntimeStorageManifest.Create();
        var unit = manifest.StorageUnits.Single(u => u.Identity.Value == ElsaRuntimeStorageManifest.ExecutableActivityTemplateDocumentKind);

        Assert.Contains(unit.Indexes, i => i.Identity == ElsaRuntimeStorageManifest.ByCollectionIndex);
        var byHash = Assert.Single(unit.Indexes, i => i.Identity == ElsaRuntimeStorageManifest.ByTemplateHashIndex);
        Assert.Equal("templateHash", Assert.Single(byHash.Fields).Path);
        Assert.True(byHash.IsUnique);
        Assert.Contains(unit.Queries, q => q.IndexIdentity == ElsaRuntimeStorageManifest.ByTemplateHashIndex);
    }

    [Fact]
    public void ExecutableActivityTemplateHashClaim_Declares_Atomic_Claim_Unit()
    {
        var manifest = ElsaRuntimeStorageManifest.Create();
        var unit = manifest.StorageUnits.Single(u => u.Identity.Value == ElsaRuntimeStorageManifest.ExecutableActivityTemplateHashClaimDocumentKind);

        Assert.Empty(unit.Indexes);
        Assert.Empty(unit.Queries);
    }

    [Fact]
    public async Task Scheduler_work_declares_bounded_routes_with_provider_distinct_pending_executions()
    {
        var declaration = await new RuntimeGroundworkStorageManifestSource().CreateDeclarationAsync();
        var unit = declaration.Manifest.StorageUnits.Single(candidate =>
            candidate.Identity.Value == ElsaRuntimeStorageManifest.SchedulerWorkItemDocumentKind);
        var storage = unit.PhysicalStorage!;
        var physical = Assert.IsType<PhysicalStoragePolicy.ExplicitPolicy>(storage.Policy).Definition;
        var pendingIndex = Assert.Single(
            storage.LogicalIndexes,
            index => index.Identity == ElsaRuntimeStorageManifest.SchedulerWorkPendingByWorkflowOrderIndex);
        var workIndex = Assert.Single(
            storage.LogicalIndexes,
            index => index.Identity == ElsaRuntimeStorageManifest.SchedulerWorkByWorkflowOrderIndex);
        var workPage = Assert.Single(
            storage.BoundedQueries,
            query => query.Identity == ElsaRuntimeStorageManifest.ListByWorkflowExecutionQuery);
        var pendingPage = Assert.Single(
            storage.BoundedQueries,
            query => query.Identity == ElsaRuntimeStorageManifest.ListPendingSchedulerWorkflowExecutionsQuery);

        Assert.DoesNotContain(
            storage.LogicalIndexes,
            index => index.Identity == ElsaRuntimeStorageManifest.ByCollectionIndex);
        Assert.DoesNotContain(
            storage.BoundedQueries,
            query => query.Identity == ElsaRuntimeStorageManifest.ListAllQuery);
        Assert.DoesNotContain(
            physical.Indexes,
            index => index.LogicalName == ElsaRuntimeStorageManifest.ByCollectionIndex);
        Assert.Collection(
            workIndex.Fields,
            workflow => Assert.Equal(ElsaRuntimeStorageManifest.WorkflowExecutionIdField, workflow.Path),
            order => Assert.Equal(ElsaRuntimeStorageManifest.SchedulerWorkOrderKeyField, order.Path));
        Assert.Collection(
            pendingIndex.Fields,
            collection => Assert.Equal(ElsaRuntimeStorageManifest.CollectionField, collection.Path),
            workflow => Assert.Equal(ElsaRuntimeStorageManifest.WorkflowExecutionIdField, workflow.Path),
            order => Assert.Equal(ElsaRuntimeStorageManifest.SchedulerWorkOrderKeyField, order.Path));
        var workPhysicalIndex = Assert.Single(
            physical.Indexes,
            index => index.LogicalName == ElsaRuntimeStorageManifest.SchedulerWorkByWorkflowOrderIndex);
        Assert.Collection(
            workPhysicalIndex.Columns,
            scope => Assert.Equal("storage_scope", scope.ColumnLogicalName),
            workflow => Assert.Equal("scheduler_workflow_execution_id", workflow.ColumnLogicalName),
            order => Assert.Equal("scheduler_work_order_key", order.ColumnLogicalName),
            id => Assert.Equal("id_lookup_key", id.ColumnLogicalName));
        var pendingPhysicalIndex = Assert.Single(
            physical.Indexes,
            index => index.LogicalName == ElsaRuntimeStorageManifest.SchedulerWorkPendingByWorkflowOrderIndex);
        Assert.Collection(
            pendingPhysicalIndex.Columns,
            scope => Assert.Equal("storage_scope", scope.ColumnLogicalName),
            collection => Assert.Equal("by-collection", collection.ColumnLogicalName),
            workflow => Assert.Equal("scheduler_workflow_execution_id", workflow.ColumnLogicalName),
            order => Assert.Equal("scheduler_work_order_key", order.ColumnLogicalName));
        Assert.Equal(
            ElsaRuntimeStorageManifest.RuntimeCollectionProjectionLength,
            physical.ProjectedColumns.Single(column =>
                column.Path == ElsaRuntimeStorageManifest.CollectionField).Length);
        Assert.Equal(
            ElsaRuntimeStorageManifest.RuntimeExecutionIdProjectionLength,
            physical.ProjectedColumns.Single(column =>
                column.Path == ElsaRuntimeStorageManifest.WorkflowExecutionIdField).Length);
        Assert.Equal(
            ElsaRuntimeStorageManifest.SchedulerWorkOrderKeyProjectionLength,
            physical.ProjectedColumns.Single(column =>
                column.Path == ElsaRuntimeStorageManifest.SchedulerWorkOrderKeyField).Length);
        Assert.Equal(QueryPagingSupport.Cursor, workPage.PagingSupport);
        Assert.Collection(
            workPage.SortFields,
            order => Assert.Equal(ElsaRuntimeStorageManifest.SchedulerWorkOrderKeyField, order.Path));
        Assert.Collection(
            workPage.PredicateFields,
            workflow => Assert.Equal(ElsaRuntimeStorageManifest.WorkflowExecutionIdField, workflow.Path));
        Assert.Equal(QueryPagingSupport.None, pendingPage.PagingSupport);
        Assert.Collection(
            pendingPage.SortFields,
            workflow => Assert.Equal(ElsaRuntimeStorageManifest.WorkflowExecutionIdField, workflow.Path),
            order => Assert.Equal(ElsaRuntimeStorageManifest.SchedulerWorkOrderKeyField, order.Path));
        Assert.Collection(
            pendingPage.PredicateFields,
            collection => Assert.Equal(ElsaRuntimeStorageManifest.CollectionField, collection.Path));
        Assert.Equal(ElsaRuntimeStorageManifest.WorkflowExecutionIdField, pendingPage.LatestPerKeyPath);
    }

    [Fact]
    public void WorkflowExecutableSourceReference_Declares_Bounded_Scope_Gc_And_Artifact_Routes()
    {
        var manifest = ElsaRuntimeStorageManifest.Create();
        var unit = manifest.StorageUnits.Single(u => u.Identity.Value == ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceDocumentKind);

        Assert.Contains(unit.Indexes, i => i.Identity == ElsaRuntimeStorageManifest.ByCollectionIndex);
        Assert.Contains(unit.Indexes, i => i.Identity == ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceByArtifact);

        var byScope = Assert.Single(unit.Indexes, i => i.Identity == ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceByScope);
        Assert.Equal(ElsaRuntimeStorageManifest.ScopeField, Assert.Single(byScope.Fields).Path);

        var byExpiresAt = Assert.Single(unit.Indexes, i => i.Identity == ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceByExpiresAt);
        Assert.Equal(IndexValueKind.DateTime, byExpiresAt.ValueKind);
        Assert.Equal(ElsaRuntimeStorageManifest.ExpiresAtField, Assert.Single(byExpiresAt.Fields).Path);
        Assert.Contains(PortableQueryOperation.LessThanOrEqual, byExpiresAt.SupportedOperations);

        var byRetired = Assert.Single(unit.Indexes, i => i.Identity == ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceByRetired);
        Assert.Equal(ElsaRuntimeStorageManifest.IsRetiredField, Assert.Single(byRetired.Fields).Path);

        Assert.Contains(
            unit.Queries,
            q => q.Identity == ElsaRuntimeStorageManifest.ListWorkflowExecutableSourceReferencesByArtifactQuery &&
                 q.IndexIdentity == ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceByArtifact);
        Assert.Contains(
            unit.Queries,
            q => q.Identity == ElsaRuntimeStorageManifest.ListWorkflowExecutableSourceReferencesByScopeQuery &&
                 q.IndexIdentity == ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceByScope);

        var expired = Assert.Single(unit.Queries, q => q.Identity == ElsaRuntimeStorageManifest.ListExpiredWorkflowExecutableSourceReferencesQuery);
        Assert.Equal(ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceByExpiresAt, expired.IndexIdentity);
        Assert.Contains(PortableQueryOperation.LessThanOrEqual, expired.Operations);
        Assert.Equal(QuerySortSupport.Ascending, expired.SortSupport);

        Assert.Contains(
            unit.Queries,
            q => q.Identity == ElsaRuntimeStorageManifest.ListRetiredWorkflowExecutableSourceReferencesQuery &&
                 q.IndexIdentity == ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceByRetired);
    }

    [Fact]
    public void ActivityExecutionHierarchy_Declares_FlatEnvelope_Scope_And_Workflow_Indexes()
    {
        var manifest = ElsaRuntimeStorageManifest.Create();
        var unit = manifest.StorageUnits.Single(u => u.Identity.Value == ElsaRuntimeStorageManifest.ActivityExecutionHierarchyDocumentKind);

        var byWorkflow = Assert.Single(unit.Indexes, i => i.Identity == ElsaRuntimeStorageManifest.ByWorkflowExecutionIndex);
        Assert.Equal("workflowExecutionId", Assert.Single(byWorkflow.Fields).Path);

        var byScope = Assert.Single(unit.Indexes, i => i.Identity == ElsaRuntimeStorageManifest.ByExecutionScopeIndex);
        Assert.Equal("executionScopeId", Assert.Single(byScope.Fields).Path);

        Assert.Contains(unit.Queries, q => q.IndexIdentity == ElsaRuntimeStorageManifest.ByWorkflowExecutionIndex);
        Assert.Contains(unit.Queries, q => q.IndexIdentity == ElsaRuntimeStorageManifest.ByExecutionScopeIndex);
    }

    [Fact]
    public async Task Activity_execution_hierarchy_declares_watermark_and_scoped_cursor_routes()
    {
        var declaration = await new RuntimeGroundworkStorageManifestSource().CreateDeclarationAsync();
        var unit = declaration.Manifest.StorageUnits.Single(candidate =>
            candidate.Identity.Value == ElsaRuntimeStorageManifest.ActivityExecutionHierarchyDocumentKind);
        var physical = Assert.IsType<PhysicalStoragePolicy.ExplicitPolicy>(
            unit.PhysicalStorage!.Policy).Definition;
        var latest = Assert.Single(
            unit.PhysicalStorage.BoundedQueries,
            query => query.Identity ==
                     ElsaRuntimeStorageManifest.FindLatestActivityExecutionHierarchyByWorkflowQuery);
        var workflowPage = Assert.Single(
            unit.PhysicalStorage.BoundedQueries,
            query => query.Identity ==
                     ElsaRuntimeStorageManifest.PageActivityExecutionHierarchyByWorkflowQuery);
        var page = Assert.Single(
            unit.PhysicalStorage.BoundedQueries,
            query => query.Identity ==
                     ElsaRuntimeStorageManifest.PageActivityExecutionHierarchyByScopeQuery);

        Assert.Equal(
            ElsaRuntimeStorageManifest.ActivityExecutionHierarchyLatestByWorkflowIndex,
            latest.IndexIdentity);
        Assert.Equal(QuerySortSupport.Descending, latest.SortSupport);
        Assert.Equal(QueryPagingSupport.None, latest.PagingSupport);
        Assert.Collection(
            latest.SortFields,
            sequence =>
            {
                Assert.Equal(
                    ElsaRuntimeStorageManifest.ActivityExecutionHierarchyExecutionSequenceField,
                    sequence.Path);
                Assert.Equal(PhysicalSortDirection.Descending, sequence.Direction);
            },
            id => Assert.Equal(PhysicalSortDirection.Descending, id.Direction));
        Assert.Equal(
            ElsaRuntimeStorageManifest.ActivityExecutionHierarchyPageByWorkflowIndex,
            workflowPage.IndexIdentity);
        Assert.Equal(QueryPagingSupport.Cursor, workflowPage.PagingSupport);

        Assert.Equal(
            ElsaRuntimeStorageManifest.ActivityExecutionHierarchyByScopeAndOrderIndex,
            page.IndexIdentity);
        Assert.Equal(QueryPagingSupport.Cursor, page.PagingSupport);
        Assert.True(page.SupportsTotalCount);
        Assert.Collection(
            page.PredicateFields,
            workflow => Assert.Equal(ElsaRuntimeStorageManifest.WorkflowExecutionIdField, workflow.Path),
            scope => Assert.Equal(ElsaRuntimeStorageManifest.ExecutionScopeIdField, scope.Path),
            root =>
            {
                Assert.Equal(
                    ElsaRuntimeStorageManifest.ActivityExecutionHierarchyIsScopeRootField,
                    root.Path);
                Assert.Equal(
                    new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal },
                    root.Operations);
            },
            sequence =>
            {
                Assert.Equal(
                    ElsaRuntimeStorageManifest.ActivityExecutionHierarchyExecutionSequenceField,
                    sequence.Path);
                Assert.Equal(
                    new HashSet<PortableQueryOperation> { PortableQueryOperation.LessThanOrEqual },
                    sequence.Operations);
            });
        Assert.Contains(
            physical.ProjectedColumns,
            column =>
                column.Path == ElsaRuntimeStorageManifest.ActivityExecutionHierarchyIsScopeRootField &&
                column.Type == PortablePhysicalType.Boolean);
        Assert.Contains(
            physical.ProjectedColumns,
            column =>
                column.Path == ElsaRuntimeStorageManifest.ActivityExecutionHierarchyExecutionSequenceField &&
                column.Type == PortablePhysicalType.Int64);
        Assert.All(
            physical.ProjectedColumns.Where(column => column.Path is
                ElsaRuntimeStorageManifest.WorkflowExecutionIdField or
                ElsaRuntimeStorageManifest.ExecutionScopeIdField or
                ElsaRuntimeStorageManifest.ActivityExecutionHierarchyActivityExecutionIdField),
            column => Assert.Equal(
                ElsaRuntimeStorageManifest.RuntimeExecutionIdProjectionLength,
                column.Length));
        Assert.Contains(
            physical.Indexes,
            index =>
                index.LogicalName ==
                ElsaRuntimeStorageManifest.ActivityExecutionHierarchyByScopeAndOrderIndex &&
                index.Columns.Count == 7);
        var latestIndex = Assert.Single(
            physical.Indexes,
            index =>
                index.LogicalName ==
                ElsaRuntimeStorageManifest.ActivityExecutionHierarchyLatestByWorkflowIndex);
        Assert.Equal(4, latestIndex.Columns.Count);
        var workflowPageIndex = Assert.Single(
            physical.Indexes,
            index =>
                index.LogicalName ==
                ElsaRuntimeStorageManifest.ActivityExecutionHierarchyPageByWorkflowIndex);
        Assert.Collection(
            workflowPageIndex.Columns,
            scope => Assert.Equal(new DocumentEnvelopeDefinition().StorageScopeColumn, scope.ColumnLogicalName),
            workflow => Assert.Equal(
                ElsaRuntimeStorageManifest.ByWorkflowExecutionIndex,
                workflow.ColumnLogicalName),
            sequence => Assert.Equal(PhysicalSortDirection.Descending, sequence.Direction),
            activity => Assert.Equal(PhysicalSortDirection.Descending, activity.Direction),
            identity => Assert.Equal(new DocumentEnvelopeDefinition().IdLookupKeyColumn, identity.ColumnLogicalName));
    }

    [Fact]
    public void RecurringTriggerSchedule_Declares_Due_Date_Route()
    {
        var manifest = ElsaRuntimeStorageManifest.Create();
        var unit = manifest.StorageUnits.Single(u => u.Identity.Value == ElsaRuntimeStorageManifest.RecurringTriggerScheduleDocumentKind);

        var byNextOccurrence = Assert.Single(
            unit.Indexes,
            i => i.Identity == ElsaRuntimeStorageManifest.RecurringTriggerScheduleByNextOccurrence);
        Assert.Equal(IndexValueKind.DateTime, byNextOccurrence.ValueKind);
        Assert.Equal(ElsaRuntimeStorageManifest.RecurringTriggerScheduleNextOccurrenceField, Assert.Single(byNextOccurrence.Fields).Path);
        Assert.Contains(PortableQueryOperation.LessThanOrEqual, byNextOccurrence.SupportedOperations);

        var query = Assert.Single(unit.Queries, q => q.Identity == ElsaRuntimeStorageManifest.ListDueRecurringTriggerSchedulesQuery);
        Assert.Equal(ElsaRuntimeStorageManifest.RecurringTriggerScheduleByNextOccurrence, query.IndexIdentity);
        Assert.Contains(PortableQueryOperation.LessThanOrEqual, query.Operations);
        Assert.Equal(QuerySortSupport.Ascending, query.SortSupport);
    }

    [Fact]
    public void DurableTimer_Declares_Due_Date_Route()
    {
        var manifest = ElsaRuntimeStorageManifest.Create();
        var unit = manifest.StorageUnits.Single(u => u.Identity.Value == ElsaRuntimeStorageManifest.DurableTimerDocumentKind);

        var byWorkflowExecution = Assert.Single(
            unit.Indexes,
            i => i.Identity == ElsaRuntimeStorageManifest.DurableTimerByWorkflowExecution);
        Assert.Equal(ElsaRuntimeStorageManifest.WorkflowExecutionIdField, Assert.Single(byWorkflowExecution.Fields).Path);

        var byDueTime = Assert.Single(
            unit.Indexes,
            i => i.Identity == ElsaRuntimeStorageManifest.DurableTimerByDueTime);
        Assert.Equal(IndexValueKind.DateTime, byDueTime.ValueKind);
        Assert.Equal(ElsaRuntimeStorageManifest.DurableTimerDueTimeField, Assert.Single(byDueTime.Fields).Path);
        Assert.Contains(PortableQueryOperation.LessThanOrEqual, byDueTime.SupportedOperations);

        var byWorkflowQuery = Assert.Single(unit.Queries, q => q.Identity == ElsaRuntimeStorageManifest.ListDurableTimersByWorkflowExecutionQuery);
        Assert.Equal(ElsaRuntimeStorageManifest.DurableTimerByWorkflowExecution, byWorkflowQuery.IndexIdentity);

        var query = Assert.Single(unit.Queries, q => q.Identity == ElsaRuntimeStorageManifest.ListDueDurableTimersQuery);
        Assert.Equal(ElsaRuntimeStorageManifest.DurableTimerByDueTime, query.IndexIdentity);
        Assert.Contains(PortableQueryOperation.LessThanOrEqual, query.Operations);
        Assert.Equal(QuerySortSupport.Ascending, query.SortSupport);
    }

    [Fact]
    public void SchemaVersion_Stays_The_Frozen_Storage_Manifest_Version_Despite_The_Additive_Index()
    {
        // Adding an index must NOT change this storage-manifest version. Per-kind document versions are independent;
        // added-index backfill (Condition 7) triggers on the physicalized index-set change, not on this string — the
        // pre-existing bookmarkState by-stimulus index added an index without bumping it too.
        Assert.Equal("1.0.0", ElsaRuntimeStorageManifest.SchemaVersion);
    }

    [Fact]
    public async Task Workflow_execution_history_declares_history_and_grouped_pinned_artifact_routes()
    {
        var declaration = await new RuntimeGroundworkStorageManifestSource().CreateDeclarationAsync();
        var unit = declaration.Manifest.StorageUnits.Single(candidate =>
            candidate.Identity.Value == ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind);
        var physical = Assert.IsType<PhysicalStoragePolicy.ExplicitPolicy>(
            unit.PhysicalStorage!.Policy).Definition;
        var logical = Assert.Single(
            unit.PhysicalStorage.LogicalIndexes,
            index => index.Identity == ElsaRuntimeStorageManifest.WorkflowExecutionHistoryOrderIndex);
        var route = Assert.Single(
            unit.PhysicalStorage.BoundedQueries,
            query => query.Identity == ElsaRuntimeStorageManifest.PageWorkflowExecutionsQuery);
        var faultAttentionLogical = Assert.Single(
            unit.PhysicalStorage.LogicalIndexes,
            index => index.Identity == ElsaRuntimeStorageManifest.WorkflowExecutionFaultedAttentionOrderIndex);
        var faultAttentionRoute = Assert.Single(
            unit.PhysicalStorage.BoundedQueries,
            query => query.Identity == ElsaRuntimeStorageManifest.PageFaultedWorkflowExecutionsForAttentionQuery);
        var pinnedLogical = Assert.Single(
            unit.PhysicalStorage.LogicalIndexes,
            index => index.Identity == ElsaRuntimeStorageManifest.WorkflowExecutionPinnedArtifactOrderIndex);
        var pinnedRoute = Assert.Single(
            unit.PhysicalStorage.BoundedQueries,
            query => query.Identity == ElsaRuntimeStorageManifest.PagePinnedExecutableArtifactIdsQuery);

        Assert.DoesNotContain(
            unit.PhysicalStorage.LogicalIndexes,
            index => index.Identity == ElsaRuntimeStorageManifest.ByCollectionIndex);
        Assert.DoesNotContain(
            unit.PhysicalStorage.BoundedQueries,
            query => query.Identity == ElsaRuntimeStorageManifest.ListAllQuery);
        Assert.DoesNotContain(
            physical.Indexes,
            index => index.LogicalName == ElsaRuntimeStorageManifest.ByCollectionIndex);
        Assert.Equal(PhysicalStorageForm.PhysicalEntityTable, physical.Form);
        Assert.Collection(
            logical.Fields,
            timestamp => Assert.Equal(
                ElsaRuntimeStorageManifest.WorkflowExecutionHistorySortTicksField,
                timestamp.Path),
            executionId => Assert.Equal(
                ElsaRuntimeStorageManifest.WorkflowExecutionHistoryWorkflowExecutionIdField,
                executionId.Path));
        Assert.Equal(BoundedQueryExecutionClass.ScaleBearing, route.ExecutionClass);
        Assert.Equal(QueryPagingSupport.Cursor, route.PagingSupport);
        Assert.True(route.SupportsTotalCount);
        Assert.Equal(
            new[]
            {
                ElsaRuntimeStorageManifest.WorkflowExecutionHistorySortTicksField,
                ElsaRuntimeStorageManifest.WorkflowExecutionHistoryWorkflowExecutionIdField
            },
            route.SortFields.Select(field => field.Path));
        Assert.Contains(
            route.ResidualPredicateFields,
            field => field.Path == ElsaRuntimeStorageManifest.WorkflowExecutionHistoryTenantIdField);
        Assert.Contains(
            route.ResidualPredicateFields,
            field => field.Path == PhysicalDocumentFieldPaths.Id);
        Assert.Equal(
            ElsaRuntimeStorageManifest.WorkflowExecutionFaultedAttentionOrderIndex,
            faultAttentionRoute.IndexIdentity);
        Assert.Contains(
            faultAttentionRoute.PredicateFields,
            field => field.Path == ElsaRuntimeStorageManifest.WorkflowExecutionHistoryStatusField);
        Assert.DoesNotContain(
            faultAttentionRoute.ResidualPredicateFields,
            field => field.Path == ElsaRuntimeStorageManifest.WorkflowExecutionHistoryStatusField);
        Assert.Collection(
            faultAttentionLogical.Fields,
            status => Assert.Equal(ElsaRuntimeStorageManifest.WorkflowExecutionHistoryStatusField, status.Path),
            timestamp => Assert.Equal(ElsaRuntimeStorageManifest.WorkflowExecutionHistorySortTicksField, timestamp.Path),
            executionId => Assert.Equal(ElsaRuntimeStorageManifest.WorkflowExecutionHistoryWorkflowExecutionIdField, executionId.Path));
        Assert.Contains(
            physical.ProjectedColumns,
            column =>
                column.Path == ElsaRuntimeStorageManifest.WorkflowExecutionHistoryWorkflowExecutionIdField &&
                column.Type == PortablePhysicalType.String &&
                column.Length == ElsaRuntimeStorageManifest.RuntimeExecutionIdProjectionLength);
        Assert.Equal(
            ElsaRuntimeStorageManifest.RuntimeCollectionProjectionLength,
            physical.ProjectedColumns.Single(column =>
                column.Path == ElsaRuntimeStorageManifest.CollectionField).Length);
        Assert.Equal(
            ElsaRuntimeStorageManifest.RuntimeExecutionIdProjectionLength,
            physical.ProjectedColumns.Single(column =>
                column.Path == ElsaRuntimeStorageManifest.WorkflowExecutionHistoryArtifactIdField).Length);
        Assert.Contains(
            physical.Indexes,
            index =>
                index.LogicalName == ElsaRuntimeStorageManifest.WorkflowExecutionHistoryOrderIndex &&
                index.Columns.Count == 4);
        Assert.Contains(
            physical.Indexes,
            index =>
                index.LogicalName == ElsaRuntimeStorageManifest.WorkflowExecutionFaultedAttentionOrderIndex &&
                index.Columns.Count == 5);
        Assert.Equal(QueryPagingSupport.Offset, pinnedRoute.PagingSupport);
        Assert.Equal(
            ElsaRuntimeStorageManifest.WorkflowExecutionHistoryArtifactIdField,
            pinnedRoute.LatestPerKeyPath);
        Assert.Collection(
            pinnedLogical.Fields,
            collection => Assert.Equal(ElsaRuntimeStorageManifest.CollectionField, collection.Path),
            artifact => Assert.Equal(
                ElsaRuntimeStorageManifest.WorkflowExecutionHistoryArtifactIdField,
                artifact.Path),
            executionId => Assert.Equal(
                ElsaRuntimeStorageManifest.WorkflowExecutionHistoryWorkflowExecutionIdField,
                executionId.Path));
        Assert.Contains(
            physical.Indexes,
            index =>
                index.LogicalName == ElsaRuntimeStorageManifest.WorkflowExecutionPinnedArtifactOrderIndex &&
                index.Columns.Count == 4);
    }

    [Fact]
    public void Every_Query_Backed_Index_Is_An_Optimized_Physical_Projection()
    {
        var manifest = ElsaRuntimeStorageManifest.Create();

        foreach (var unit in manifest.StorageUnits)
        {
            foreach (var query in unit.Queries)
            {
                var index = Assert.Single(unit.Indexes, candidate => candidate.Identity == query.IndexIdentity);
                Assert.Equal(IndexPhysicalizationPolicy.Optimized, index.Physicalization);
            }
        }
    }

    [Fact]
    public async Task WorkflowDispatch_declares_single_and_composite_bounded_query_routes()
    {
        var declaration = await new RuntimeGroundworkStorageManifestSource().CreateDeclarationAsync();
        var unit = declaration.Manifest.StorageUnits.Single(candidate =>
            candidate.Identity.Value == ElsaRuntimeStorageManifest.WorkflowDispatchDocumentKind);
        var physical = Assert.IsType<PhysicalStoragePolicy.ExplicitPolicy>(unit.PhysicalStorage!.Policy).Definition;

        Assert.Equal(
            [
                ElsaRuntimeStorageManifest.ByChildWorkflowExecutionIndex,
                ElsaRuntimeStorageManifest.ByCollectionIndex,
                ElsaRuntimeStorageManifest.ByCreatedAtIndex,
                ElsaRuntimeStorageManifest.ByDispatchIdIndex,
                ElsaRuntimeStorageManifest.ByParentWorkflowExecutionIndex,
                ElsaRuntimeStorageManifest.ByStatusIndex,
                ElsaRuntimeStorageManifest.ByTestScopeIndex
            ],
            unit.Indexes.Select(index => index.Identity).Order(StringComparer.Ordinal));
        Assert.Contains(
            unit.PhysicalStorage.LogicalIndexes,
            index => index.Identity == ElsaRuntimeStorageManifest.ByParentWorkflowExecutionAndStatusIndex && index.Fields.Count == 2);
        Assert.Contains(
            unit.PhysicalStorage.LogicalIndexes,
            index => index.Identity == ElsaRuntimeStorageManifest.ByChildWorkflowExecutionAndStatusIndex && index.Fields.Count == 2);
        Assert.Contains(
            unit.PhysicalStorage.BoundedQueries,
            query => query.Identity == ElsaRuntimeStorageManifest.ListWorkflowDispatchesByParentAndStatusQuery && query.PredicateFields.Count == 2);
        Assert.Contains(
            unit.PhysicalStorage.BoundedQueries,
            query => query.Identity == ElsaRuntimeStorageManifest.ListWorkflowDispatchesByChildAndStatusQuery && query.PredicateFields.Count == 2);
        Assert.Contains(
            unit.PhysicalStorage.BoundedQueries,
            query => query.Identity == ElsaRuntimeStorageManifest.ListWorkflowDispatchesByTestScopeQuery &&
                     query.IndexIdentity == ElsaRuntimeStorageManifest.ByTestScopeIndex);
        Assert.Contains(
            physical.Indexes,
            index => index.LogicalName == ElsaRuntimeStorageManifest.ByParentWorkflowExecutionAndStatusIndex && index.Columns.Count == 3);
        Assert.Contains(
            physical.Indexes,
            index => index.LogicalName == ElsaRuntimeStorageManifest.ByChildWorkflowExecutionAndStatusIndex && index.Columns.Count == 3);
        Assert.Equal(
            ElsaRuntimeStorageManifest.RuntimeStatusProjectionLength,
            physical.ProjectedColumns.Single(column =>
                column.LogicalName == ElsaRuntimeStorageManifest.ByStatusIndex).Length);
        Assert.Equal(
            WorkflowTestScope.MaximumScopeIdLength,
            physical.ProjectedColumns.Single(column =>
                column.LogicalName == ElsaRuntimeStorageManifest.ByTestScopeIndex).Length);
        Assert.Equal(
            ElsaRuntimeStorageManifest.WorkflowDispatchIdProjectionLength,
            physical.ProjectedColumns.Single(column =>
                column.LogicalName == ElsaRuntimeStorageManifest.ByDispatchIdIndex).Length);
        Assert.Equal(
            ElsaRuntimeStorageManifest.WorkflowDispatchIdProjectionLength,
            new WorkflowDispatchIdentity("parent", "activity").DispatchId.Length);
    }

    [Fact]
    public async Task Post_commit_outbox_declares_provider_side_deliverable_and_claimable_routes()
    {
        var declaration = await new RuntimeGroundworkStorageManifestSource().CreateDeclarationAsync();
        var unit = declaration.Manifest.StorageUnits.Single(candidate =>
            candidate.Identity.Value == ElsaRuntimeStorageManifest.PostCommitOutboxDocumentKind);

        var deliverable = Assert.Single(
            unit.PhysicalStorage!.BoundedQueries,
            query => query.Identity == ElsaRuntimeStorageManifest.ListDeliverablePostCommitOutboxQuery);
        var deliverableIndex = Assert.Single(
            unit.PhysicalStorage.LogicalIndexes,
            index => index.Identity == deliverable.IndexIdentity);
        Assert.Equal(MissingValueBehavior.Excluded, deliverableIndex.MissingValueBehavior);
        Assert.Equal(
            IndexValueKind.Number,
            unit.Indexes.Single(index =>
                index.Identity == ElsaRuntimeStorageManifest.ByOutboxStatusIndex).ValueKind);
        var physical = Assert.IsType<PhysicalStoragePolicy.ExplicitPolicy>(
            unit.PhysicalStorage.Policy).Definition;
        var statusProjection = physical.ProjectedColumns.Single(column =>
            column.LogicalName == ElsaRuntimeStorageManifest.ByOutboxStatusIndex);
        Assert.Equal(PortablePhysicalType.Int32, statusProjection.Type);
        Assert.Null(statusProjection.Length);
        Assert.Equal(
            RuntimePostCommitIntent.MaximumKindLength,
            physical.ProjectedColumns.Single(column =>
                column.LogicalName == ElsaRuntimeStorageManifest.ByOutboxIntentKindIndex).Length);
        Assert.Equal(
            ElsaRuntimeStorageManifest.PostCommitOutboxItemIdProjectionLength,
            physical.ProjectedColumns.Single(column =>
                column.LogicalName == ElsaRuntimeStorageManifest.ByOutboxItemIdIndex).Length);
        Assert.Equal(
            ElsaRuntimeStorageManifest.RuntimeExecutionIdProjectionLength,
            physical.ProjectedColumns.Single(column =>
                column.LogicalName == ElsaRuntimeStorageManifest.ByWorkflowExecutionIndex).Length);
        var deliverablePhysicalIndex = Assert.Single(
            physical.Indexes,
            index => index.LogicalName == deliverable.IndexIdentity);
        Assert.Equal(
            MissingValueBehavior.Excluded,
            deliverablePhysicalIndex.MissingValueBehavior);
        var deliverableAt = Assert.Single(
            deliverable.PredicateFields,
            field => field.Path == ElsaRuntimeStorageManifest.PostCommitOutboxDeliverableAtField);
        Assert.Equal(
            new[] { PortableQueryOperation.LessThanOrEqual },
            deliverableAt.Operations);
        Assert.Contains(
            deliverable.SortFields,
            field => field.Path == ElsaRuntimeStorageManifest.PostCommitOutboxDeliverableAtField);
        Assert.Contains(
            deliverable.SortFields,
            field => field.Path == ElsaRuntimeStorageManifest.PostCommitOutboxItemIdField);

        var claimable = Assert.Single(
            unit.PhysicalStorage.BoundedQueries,
            query => query.Identity == ElsaRuntimeStorageManifest.ListClaimablePostCommitOutboxQuery);
        var claimableIndex = Assert.Single(
            unit.PhysicalStorage.LogicalIndexes,
            index => index.Identity == claimable.IndexIdentity);
        Assert.Equal(MissingValueBehavior.Excluded, claimableIndex.MissingValueBehavior);
        Assert.Contains(
            claimable.PredicateFields,
            field => field.Path == ElsaRuntimeStorageManifest.PostCommitOutboxClaimableAtField);
        Assert.Contains(
            claimable.SortFields,
            field => field.Path == ElsaRuntimeStorageManifest.PostCommitOutboxItemIdField);
    }

    [Fact]
    public async Task Workflow_trigger_binding_declares_exact_and_type_page_routes()
    {
        var declaration = await new RuntimeGroundworkStorageManifestSource().CreateDeclarationAsync();
        var unit = declaration.Manifest.StorageUnits.Single(candidate =>
            candidate.Identity.Value == ElsaRuntimeStorageManifest.WorkflowTriggerBindingDocumentKind);
        var physical = Assert.IsType<PhysicalStoragePolicy.ExplicitPolicy>(unit.PhysicalStorage!.Policy).Definition;

        var logical = Assert.Single(
            unit.PhysicalStorage.LogicalIndexes,
            index => index.Identity == ElsaRuntimeStorageManifest.ByStimulusAndTypeIndex);
        Assert.Collection(
            logical.Fields,
            stimulus => Assert.Equal(
                ElsaRuntimeStorageManifest.WorkflowTriggerBindingStimulusLookupKeyField,
                stimulus.Path),
            active =>
            {
                Assert.Equal(ElsaRuntimeStorageManifest.WorkflowTriggerBindingIsActiveField, active.Path);
                Assert.Equal(IndexValueKind.Boolean, active.ValueKind);
            },
            binding => Assert.Equal(ElsaRuntimeStorageManifest.TriggerBindingIdField, binding.Path));

        var route = Assert.Single(
            unit.PhysicalStorage.BoundedQueries,
            query => query.Identity == ElsaRuntimeStorageManifest.ListTriggerBindingsByStimulusAndTypeQuery);
        Assert.Equal(ElsaRuntimeStorageManifest.ByStimulusAndTypeIndex, route.IndexIdentity);
        Assert.Collection(
            route.PredicateFields,
            stimulus => Assert.Equal(
                ElsaRuntimeStorageManifest.WorkflowTriggerBindingStimulusLookupKeyField,
                stimulus.Path),
            active => Assert.Equal(ElsaRuntimeStorageManifest.WorkflowTriggerBindingIsActiveField, active.Path));
        Assert.Equal(BoundedQueryExecutionClass.ScaleBearing, route.ExecutionClass);
        Assert.Equal(QueryPagingSupport.Cursor, route.PagingSupport);
        Assert.Collection(
            route.SortFields,
            binding => Assert.Equal(ElsaRuntimeStorageManifest.TriggerBindingIdField, binding.Path));
        Assert.True(route.SupportsTotalCount);
        Assert.Contains(
            physical.Indexes,
            index =>
                index.LogicalName == ElsaRuntimeStorageManifest.ByStimulusAndTypeIndex &&
                index.Columns.Count == 5 &&
                index.Columns[^1].ColumnLogicalName == new DocumentEnvelopeDefinition().IdLookupKeyColumn);
        Assert.Contains(
            physical.ProjectedColumns,
            column =>
                column.LogicalName == ElsaRuntimeStorageManifest.WorkflowTriggerBindingByActive &&
                column.Type == PortablePhysicalType.Boolean);

        var typeLogical = Assert.Single(
            unit.PhysicalStorage.LogicalIndexes,
            index => index.Identity == ElsaRuntimeStorageManifest.WorkflowTriggerBindingByStimulusTypeAndActive);
        Assert.Collection(
            typeLogical.Fields,
            type => Assert.Equal(
                ElsaRuntimeStorageManifest.WorkflowTriggerBindingStimulusTypeLookupKeyField,
                type.Path),
            active =>
            {
                Assert.Equal(ElsaRuntimeStorageManifest.WorkflowTriggerBindingIsActiveField, active.Path);
                Assert.Equal(IndexValueKind.Boolean, active.ValueKind);
            },
            id => Assert.Equal(ElsaRuntimeStorageManifest.TriggerBindingIdField, id.Path));
        var typeRoute = Assert.Single(
            unit.PhysicalStorage.BoundedQueries,
            query => query.Identity == ElsaRuntimeStorageManifest.ListTriggerBindingsByStimulusTypeQuery);
        Assert.Equal(typeLogical.Identity, typeRoute.IndexIdentity);
        Assert.Equal(BoundedQueryExecutionClass.ScaleBearing, typeRoute.ExecutionClass);
        Assert.Equal(QueryPagingSupport.Cursor, typeRoute.PagingSupport);
        Assert.True(typeRoute.SupportsTotalCount);
        Assert.Collection(
            typeRoute.PredicateFields,
            type => Assert.Equal(
                ElsaRuntimeStorageManifest.WorkflowTriggerBindingStimulusTypeLookupKeyField,
                type.Path),
            active => Assert.Equal(ElsaRuntimeStorageManifest.WorkflowTriggerBindingIsActiveField, active.Path));
        Assert.Collection(
            typeRoute.SortFields,
            id => Assert.Equal(ElsaRuntimeStorageManifest.TriggerBindingIdField, id.Path));
        Assert.Contains(
            physical.Indexes,
            index =>
                index.LogicalName == typeLogical.Identity &&
                index.Columns.Count == 5 &&
                index.Columns[^1].ColumnLogicalName == new DocumentEnvelopeDefinition().IdLookupKeyColumn);
        Assert.Equal(
            WorkflowTriggerBinding.MaximumIdLength,
            physical.ProjectedColumns.Single(column =>
                column.LogicalName == ElsaRuntimeStorageManifest.WorkflowTriggerBindingById).Length);
        Assert.Equal(
            ElsaRuntimeStorageManifest.BookmarkStimulusLookupKeyProjectionLength,
            physical.ProjectedColumns.Single(column =>
                column.Path == ElsaRuntimeStorageManifest.WorkflowTriggerBindingStimulusLookupKeyField).Length);
        Assert.Equal(
            ElsaRuntimeStorageManifest.BookmarkStimulusLookupKeyProjectionLength,
            physical.ProjectedColumns.Single(column =>
                column.Path == ElsaRuntimeStorageManifest.WorkflowTriggerBindingStimulusTypeLookupKeyField).Length);
        AssertStimulusProjectionLengths(
            physical,
            ElsaRuntimeStorageManifest.WorkflowTriggerBindingStimulusTypeProjectionLength);

        AssertOrderedTriggerLookup(
            unit,
            physical,
            ElsaRuntimeStorageManifest.ListTriggerBindingsByPublicationQuery,
            ElsaRuntimeStorageManifest.WorkflowTriggerBindingByPublicationAndId,
            ElsaRuntimeStorageManifest.PublicationIdField);
        AssertOrderedTriggerLookup(
            unit,
            physical,
            ElsaRuntimeStorageManifest.ListTriggerBindingsByArtifactQuery,
            ElsaRuntimeStorageManifest.WorkflowTriggerBindingByArtifactAndId,
            ElsaRuntimeStorageManifest.ArtifactIdField);
    }

    private static void AssertOrderedTriggerLookup(
        StorageUnit unit,
        PhysicalTableDefinition physical,
        string queryIdentity,
        string indexIdentity,
        string predicatePath)
    {
        var logical = Assert.Single(
            unit.PhysicalStorage!.LogicalIndexes,
            index => index.Identity == indexIdentity);
        Assert.Collection(
            logical.Fields,
            predicate => Assert.Equal(predicatePath, predicate.Path),
            id => Assert.Equal(ElsaRuntimeStorageManifest.TriggerBindingIdField, id.Path));

        var route = Assert.Single(
            unit.PhysicalStorage.BoundedQueries,
            query => query.Identity == queryIdentity);
        Assert.Equal(indexIdentity, route.IndexIdentity);
        Assert.Equal(QueryPagingSupport.Cursor, route.PagingSupport);
        Assert.True(route.SupportsTotalCount);
        Assert.Collection(
            route.SortFields,
            id => Assert.Equal(ElsaRuntimeStorageManifest.TriggerBindingIdField, id.Path));
        Assert.Contains(
            physical.Indexes,
            index =>
                index.LogicalName == indexIdentity &&
                index.Columns.Count == 4 &&
                index.Columns[^1].ColumnLogicalName ==
                new DocumentEnvelopeDefinition().IdLookupKeyColumn);
    }

    [Fact]
    public async Task Bookmark_state_declares_cursor_ordered_stimulus_routes()
    {
        var declaration = await new RuntimeGroundworkStorageManifestSource().CreateDeclarationAsync();
        var unit = declaration.Manifest.StorageUnits.Single(candidate =>
            candidate.Identity.Value == ElsaRuntimeStorageManifest.BookmarkStateDocumentKind);
        var physical = Assert.IsType<PhysicalStoragePolicy.ExplicitPolicy>(unit.PhysicalStorage!.Policy).Definition;

        var logical = Assert.Single(
            unit.PhysicalStorage.LogicalIndexes,
            index => index.Identity == ElsaRuntimeStorageManifest.BookmarkStateByStimulusAndTypeAndIdentity);
        Assert.Collection(
            logical.Fields,
            stimulus => Assert.Equal(
                ElsaRuntimeStorageManifest.BookmarkStimulusLookupKeyField,
                stimulus.Path),
            workflow => Assert.Equal(ElsaRuntimeStorageManifest.WorkflowExecutionIdField, workflow.Path),
            bookmark => Assert.Equal(ElsaRuntimeStorageManifest.BookmarkIdField, bookmark.Path));

        var route = Assert.Single(
            unit.PhysicalStorage.BoundedQueries,
            query => query.Identity == ElsaRuntimeStorageManifest.ListBookmarksByStimulusAndTypeQuery);
        Assert.Equal(ElsaRuntimeStorageManifest.BookmarkStateByStimulusAndTypeAndIdentity, route.IndexIdentity);
        Assert.Equal(QueryPagingSupport.Cursor, route.PagingSupport);
        Assert.Collection(
            route.PredicateFields,
            stimulus => Assert.Equal(
                ElsaRuntimeStorageManifest.BookmarkStimulusLookupKeyField,
                stimulus.Path));
        Assert.Collection(
            route.SortFields,
            workflow => Assert.Equal(ElsaRuntimeStorageManifest.WorkflowExecutionIdField, workflow.Path),
            bookmark => Assert.Equal(ElsaRuntimeStorageManifest.BookmarkIdField, bookmark.Path));

        var typeRoute = Assert.Single(
            unit.PhysicalStorage.BoundedQueries,
            query => query.Identity == ElsaRuntimeStorageManifest.ListBookmarksByStimulusTypeQuery);
        Assert.Equal(ElsaRuntimeStorageManifest.BookmarkStateByStimulusTypeAndIdentity, typeRoute.IndexIdentity);
        Assert.Equal(QueryPagingSupport.Cursor, typeRoute.PagingSupport);
        Assert.Contains(
            physical.Indexes,
            index =>
                index.LogicalName == ElsaRuntimeStorageManifest.BookmarkStateByStimulusAndTypeAndIdentity &&
                index.Columns.Count == 5);
        Assert.Contains(
            physical.Indexes,
            index =>
                index.LogicalName == ElsaRuntimeStorageManifest.BookmarkStateByStimulusTypeAndIdentity &&
                index.Columns.Count == 5);
        Assert.Equal(
            ElsaRuntimeStorageManifest.BookmarkStimulusLookupKeyProjectionLength,
            physical.ProjectedColumns.Single(column =>
                column.Path == ElsaRuntimeStorageManifest.BookmarkStimulusLookupKeyField).Length);
        Assert.Equal(
            ElsaRuntimeStorageManifest.BookmarkStimulusLookupKeyProjectionLength,
            physical.ProjectedColumns.Single(column =>
                column.Path == ElsaRuntimeStorageManifest.BookmarkStimulusTypeLookupKeyField).Length);
        AssertStimulusProjectionLengths(physical);
    }

    [Fact]
    public async Task Workflow_test_scope_declares_bounded_open_expiry_route()
    {
        var declaration = await new RuntimeGroundworkStorageManifestSource().CreateDeclarationAsync();
        var unit = declaration.Manifest.StorageUnits.Single(candidate =>
            candidate.Identity.Value == ElsaRuntimeStorageManifest.WorkflowTestScopeDocumentKind);

        var route = Assert.Single(
            unit.PhysicalStorage!.BoundedQueries,
            query => query.Identity == ElsaRuntimeStorageManifest.ListExpiredOpenWorkflowTestScopesQuery);
        Assert.Equal(ElsaRuntimeStorageManifest.ByStateAndExpiresAtIndex, route.IndexIdentity);
        Assert.Collection(
            route.PredicateFields,
            state => Assert.Equal(ElsaRuntimeStorageManifest.StateField, state.Path),
            scope => Assert.Equal(ElsaRuntimeStorageManifest.ScopeIdField, scope.Path),
            expiry => Assert.Equal(ElsaRuntimeStorageManifest.ExpiresAtField, expiry.Path));
        Assert.Contains(
            unit.PhysicalStorage.BoundedQueries,
            query => query.Identity == ElsaRuntimeStorageManifest.ListWorkflowTestScopesByStatePageQuery &&
                     query.IndexIdentity == ElsaRuntimeStorageManifest.ByStateAndScopeIdIndex);
        var physical = Assert.IsType<PhysicalStoragePolicy.ExplicitPolicy>(unit.PhysicalStorage.Policy).Definition;
        Assert.Contains(
            physical.ProjectedColumns,
            column =>
                column.LogicalName == ElsaRuntimeStorageManifest.ByStatusIndex &&
                column.Length == ElsaRuntimeStorageManifest.RuntimeStatusProjectionLength);
        Assert.Contains(
            physical.ProjectedColumns,
            column =>
                column.LogicalName == ElsaRuntimeStorageManifest.ByScopeIdIndex &&
                column.Length == WorkflowTestScope.MaximumScopeIdLength);
    }

    [Fact]
    public async Task Recurring_trigger_schedule_declares_physical_due_route()
    {
        var declaration = await new RuntimeGroundworkStorageManifestSource().CreateDeclarationAsync();
        var unit = declaration.Manifest.StorageUnits.Single(candidate =>
            candidate.Identity.Value == ElsaRuntimeStorageManifest.RecurringTriggerScheduleDocumentKind);

        var route = Assert.Single(
            unit.PhysicalStorage!.BoundedQueries,
            query => query.Identity == ElsaRuntimeStorageManifest.ListDueRecurringTriggerSchedulesQuery);
        Assert.Equal(
            ElsaRuntimeStorageManifest.RecurringTriggerScheduleByActiveNextOccurrenceAndScheduleId,
            route.IndexIdentity);
        Assert.Equal(BoundedQueryExecutionClass.ScaleBearing, route.ExecutionClass);
        Assert.Collection(
            route.PredicateFields,
            active =>
            {
                Assert.Equal(ElsaRuntimeStorageManifest.RecurringTriggerScheduleIsActiveField, active.Path);
                Assert.Contains(PortableQueryOperation.Equal, active.Operations);
            },
            due =>
            {
                Assert.Equal(ElsaRuntimeStorageManifest.RecurringTriggerScheduleNextOccurrenceField, due.Path);
                Assert.Contains(PortableQueryOperation.LessThanOrEqual, due.Operations);
            });
        Assert.Collection(
            route.SortFields,
            active => Assert.Equal(ElsaRuntimeStorageManifest.RecurringTriggerScheduleIsActiveField, active.Path),
            due => Assert.Equal(ElsaRuntimeStorageManifest.RecurringTriggerScheduleNextOccurrenceField, due.Path),
            schedule => Assert.Equal(ElsaRuntimeStorageManifest.RecurringTriggerScheduleIdField, schedule.Path));

        var physical = Assert.IsType<PhysicalStoragePolicy.ExplicitPolicy>(unit.PhysicalStorage.Policy).Definition;
        Assert.Equal(
            PortablePhysicalType.Boolean,
            physical.ProjectedColumns.Single(column =>
                column.LogicalName == ElsaRuntimeStorageManifest.ByRecurringScheduleActiveIndex).Type);
        Assert.Contains(
            physical.Indexes,
            index =>
                index.LogicalName ==
                ElsaRuntimeStorageManifest.RecurringTriggerScheduleByActiveNextOccurrenceAndScheduleId);
        Assert.DoesNotContain(
            physical.Indexes,
            index => index.LogicalName is
                ElsaRuntimeStorageManifest.ByRecurringScheduleActiveIndex or
                ElsaRuntimeStorageManifest.RecurringTriggerScheduleByNextOccurrence);
    }

    [Fact]
    public async Task Durable_timer_declares_physical_due_route()
    {
        var declaration = await new RuntimeGroundworkStorageManifestSource().CreateDeclarationAsync();
        var unit = declaration.Manifest.StorageUnits.Single(candidate =>
            candidate.Identity.Value == ElsaRuntimeStorageManifest.DurableTimerDocumentKind);

        var route = Assert.Single(
            unit.PhysicalStorage!.BoundedQueries,
            query => query.Identity == ElsaRuntimeStorageManifest.ListDueDurableTimersQuery);
        Assert.Equal(ElsaRuntimeStorageManifest.DurableTimerByDueTimeAndTimerId, route.IndexIdentity);
        Assert.Equal(BoundedQueryExecutionClass.ScaleBearing, route.ExecutionClass);
        Assert.Collection(
            route.PredicateFields,
            due =>
            {
                Assert.Equal(ElsaRuntimeStorageManifest.DurableTimerDueTimeField, due.Path);
                Assert.Contains(PortableQueryOperation.LessThanOrEqual, due.Operations);
            });
        Assert.Collection(
            route.SortFields,
            due => Assert.Equal(ElsaRuntimeStorageManifest.DurableTimerDueTimeField, due.Path),
            timer => Assert.Equal(ElsaRuntimeStorageManifest.DurableTimerIdField, timer.Path));

        var physical = Assert.IsType<PhysicalStoragePolicy.ExplicitPolicy>(unit.PhysicalStorage.Policy).Definition;
        Assert.Contains(
            physical.Indexes,
            index => index.LogicalName == ElsaRuntimeStorageManifest.DurableTimerByDueTimeAndTimerId);
        Assert.DoesNotContain(
            physical.Indexes,
            index => index.LogicalName == ElsaRuntimeStorageManifest.DurableTimerByDueTime);
    }

    [Fact]
    public async Task Durable_timer_declares_the_claim_due_physical_route_and_its_sort()
    {
        var declaration = await new RuntimeGroundworkStorageManifestSource().CreateDeclarationAsync();
        var unit = declaration.Manifest.StorageUnits.Single(candidate =>
            candidate.Identity.Value == ElsaRuntimeStorageManifest.DurableTimerDocumentKind);

        var route = Assert.Single(
            unit.PhysicalStorage!.BoundedQueries,
            query => query.Identity == ElsaRuntimeStorageManifest.ClaimDueDurableTimersQuery);
        Assert.Equal(ElsaRuntimeStorageManifest.DurableTimerByClaimOrder, route.IndexIdentity);
        Assert.Equal(BoundedQueryExecutionClass.ScaleBearing, route.ExecutionClass);
        Assert.Collection(
            route.PredicateFields,
            claimOrder =>
            {
                Assert.Equal(ElsaRuntimeStorageManifest.DurableTimerClaimOrderKeyField, claimOrder.Path);
                Assert.Contains(PortableQueryOperation.LessThanOrEqual, claimOrder.Operations);
            });
        Assert.Collection(
            route.SortFields,
            claimOrder =>
            {
                Assert.Equal(ElsaRuntimeStorageManifest.DurableTimerClaimOrderKeyField, claimOrder.Path);
                Assert.Equal(PhysicalSortDirection.Ascending, claimOrder.Direction);
            });

        var physical = Assert.IsType<PhysicalStoragePolicy.ExplicitPolicy>(unit.PhysicalStorage.Policy).Definition;
        var claimOrderColumn = Assert.Single(
            physical.ProjectedColumns,
            column => column.Path == ElsaRuntimeStorageManifest.DurableTimerClaimOrderKeyField);
        Assert.Equal(ElsaRuntimeStorageManifest.DurableTimerClaimOrderKeyProjectionLength, claimOrderColumn.Length);
        var claimOrderIndex = Assert.Single(
            physical.Indexes,
            index => index.LogicalName == ElsaRuntimeStorageManifest.DurableTimerByClaimOrder);
        Assert.Collection(
            claimOrderIndex.Columns,
            scope => Assert.Equal(new DocumentEnvelopeDefinition().StorageScopeColumn, scope.ColumnLogicalName),
            claimOrder =>
            {
                Assert.Equal(claimOrderColumn.LogicalName, claimOrder.ColumnLogicalName);
                Assert.Equal(PhysicalSortDirection.Ascending, claimOrder.Direction);
            });
    }

    [Fact]
    public async Task Execution_liveness_declares_finite_owner_aware_recovery_routes()
    {
        var declaration = await new RuntimeGroundworkStorageManifestSource().CreateDeclarationAsync();
        var unit = declaration.Manifest.StorageUnits.Single(candidate =>
            candidate.Identity.Value == ElsaRuntimeStorageManifest.ExecutionLivenessStateDocumentKind);
        var recoveryRoutes = unit.PhysicalStorage!.BoundedQueries
            .Where(query => query.Identity.StartsWith("list-recovery-", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(10, recoveryRoutes.Length);
        Assert.All(recoveryRoutes, route =>
        {
            Assert.Equal(BoundedQueryExecutionClass.ScaleBearing, route.ExecutionClass);
            Assert.Equal(QueryPagingSupport.None, route.PagingSupport);
            Assert.NotEmpty(route.PredicateFields);
            Assert.NotEmpty(route.SortFields);
        });

        var ownerless = Assert.Single(
            recoveryRoutes,
            route => route.Identity == ElsaRuntimeStorageManifest.ListRecoveryDetectedOwnerlessQuery);
        Assert.Collection(
            ownerless.PredicateFields,
            status => Assert.Equal(ElsaRuntimeStorageManifest.RecoveryInterruptionStatusField, status.Path),
            owner => Assert.Equal(ElsaRuntimeStorageManifest.RecoveryHasOperationalOwnerField, owner.Path));

        var leaseOwner = Assert.Single(
            recoveryRoutes,
            route => route.Identity == ElsaRuntimeStorageManifest.ListRecoveryByLeaseExpiryAndOwnerQuery);
        Assert.Collection(
            leaseOwner.PredicateFields,
            owner => Assert.Equal(ElsaRuntimeStorageManifest.RecoveryLeaseOwnerIdField, owner.Path),
            expiry => Assert.Equal(ElsaRuntimeStorageManifest.RecoveryLeaseExpiresAtField, expiry.Path));
        Assert.Contains(PortableQueryOperation.LessThanOrEqual, leaseOwner.PredicateFields[1].Operations);

        var physical = Assert.IsType<PhysicalStoragePolicy.ExplicitPolicy>(unit.PhysicalStorage.Policy).Definition;
        Assert.Contains(
            physical.ProjectedColumns,
            column => column.Path == ElsaRuntimeStorageManifest.RecoveryHasOperationalOwnerField &&
                      column.Type == PortablePhysicalType.Boolean);
        Assert.All(
            recoveryRoutes,
            route => Assert.Contains(physical.Indexes, index => index.LogicalName == route.IndexIdentity));
    }

    [Fact]
    public async Task Workflow_executable_source_reference_declares_physical_bounded_routes()
    {
        var declaration = await new RuntimeGroundworkStorageManifestSource().CreateDeclarationAsync();
        var unit = declaration.Manifest.StorageUnits.Single(candidate =>
            candidate.Identity.Value == ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceDocumentKind);

        Assert.Contains(
            unit.PhysicalStorage!.BoundedQueries,
            query => query.Identity == ElsaRuntimeStorageManifest.ListWorkflowExecutableSourceReferencesByArtifactQuery &&
                     query.IndexIdentity == ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceByArtifact);
        Assert.Contains(
            unit.PhysicalStorage.BoundedQueries,
            query => query.Identity == ElsaRuntimeStorageManifest.ListWorkflowExecutableSourceReferencesByScopeQuery &&
                     query.IndexIdentity == ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceByScope);
        Assert.Contains(
            unit.PhysicalStorage.BoundedQueries,
            query => query.Identity == ElsaRuntimeStorageManifest.ListExpiredWorkflowExecutableSourceReferencesQuery &&
                     query.IndexIdentity == ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceByExpiresAt);
        Assert.Contains(
            unit.PhysicalStorage.BoundedQueries,
            query => query.Identity == ElsaRuntimeStorageManifest.ListRetiredWorkflowExecutableSourceReferencesQuery &&
                     query.IndexIdentity == ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceByRetired);

        var physicalExpiryRoutes = unit.PhysicalStorage.BoundedQueries
            .Where(query => query.Identity is
                ElsaRuntimeStorageManifest.PageLiveWorkflowExecutableSourceReferencesQuery or
                ElsaRuntimeStorageManifest.PageLiveWorkflowExecutableSourceReferencesByScopeQuery or
                ElsaRuntimeStorageManifest.BatchExpiredWorkflowExecutableSourceReferencesQuery or
                ElsaRuntimeStorageManifest.BatchRetiredWorkflowExecutableSourceReferencesQuery or
                ElsaRuntimeStorageManifest.FindLiveWorkflowExecutableSourceReferenceByArtifactQuery)
            .ToArray();
        Assert.Equal(5, physicalExpiryRoutes.Length);
        Assert.All(
            physicalExpiryRoutes,
            route => Assert.Collection(
                route.SortFields,
                expiresAt => Assert.Equal(ElsaRuntimeStorageManifest.ExpiresAtField, expiresAt.Path),
                id => Assert.Equal(ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceIdField, id.Path)));
    }

    [Fact]
    public async Task B7_query_catalog_is_closed_and_every_route_is_physically_admitted()
    {
        var routes = ElsaGroundworkQueryRoutes.All;

        Assert.Equal(30, routes.Count);
        Assert.Equal(routes.Count, routes.Select(route => route.Key).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            7,
            routes.Count(route => route.Kind == ElsaGroundworkQueryRouteKind.PrimaryIdentityRead));
        Assert.Equal(
            23,
            routes.Count(route => route.Kind == ElsaGroundworkQueryRouteKind.BoundedRoute));
        Assert.All(routes, route => Assert.InRange(route.MaximumResultCount, 1, ElsaGroundworkQueryRoutes.MaximumResultCount));
        Assert.All(
            routes.Where(route => route.Kind == ElsaGroundworkQueryRouteKind.PrimaryIdentityRead),
            route =>
            {
                Assert.Equal(1, route.MaximumResultCount);
                Assert.NotNull(route.PrimaryReadIdentity);
                Assert.Empty(route.PhysicalRoutes);
            });
        Assert.All(
            routes.Where(route => route.Kind == ElsaGroundworkQueryRouteKind.BoundedRoute),
            route =>
            {
                Assert.Null(route.PrimaryReadIdentity);
                Assert.NotEmpty(route.PhysicalRoutes);
                Assert.NotEmpty(route.RequiredPhysicalFields);
            });

        var deleteExpiredOrRetired = Assert.Single(routes, route =>
            route.CoverageRow == "runtime-executable-source-reference" &&
            route.QueryShape == "delete-expired-or-retired-bounded");
        Assert.Equal(
            [
                ElsaRuntimeStorageManifest.BatchExpiredWorkflowExecutableSourceReferencesQuery,
                ElsaRuntimeStorageManifest.BatchRetiredWorkflowExecutableSourceReferencesQuery
            ],
            deleteExpiredOrRetired.PhysicalRoutes.Select(route => route.Identity));

        var activityStatePage = Assert.Single(routes, route =>
            route.CoverageRow == "runtime-activity-execution-state" &&
            route.QueryShape == "list-by-workflow-bounded");
        Assert.Equal(ElsaGroundworkQueryContinuation.Cursor, activityStatePage.Continuation);
        var activityStateRoute = Assert.Single(activityStatePage.PhysicalRoutes);
        Assert.Equal(
            ElsaRuntimeStorageManifest.PageActivityExecutionStatesByWorkflowExecutionQuery,
            activityStateRoute.Identity);
        Assert.Equal(
            [ElsaRuntimeStorageManifest.ActivityExecutionIdField],
            activityStateRoute.OrderingFields);

        var templateByHash = Assert.Single(routes, route =>
            route.CoverageRow == "runtime-workflow-executable" &&
            route.QueryShape == "find-template-by-hash");
        Assert.Equal(ElsaGroundworkQueryResultOperation.Documents, templateByHash.ResultOperation);

        var unreferencedArtifacts = Assert.Single(routes, route =>
            route.CoverageRow == "runtime-executable-source-reference" &&
            route.QueryShape == "list-unreferenced-artifacts-bounded");
        Assert.Equal(ElsaGroundworkQueryResultOperation.Documents, unreferencedArtifacts.ResultOperation);

        var pinnedArtifactPage = Assert.Single(routes, route =>
            route.CoverageRow == "runtime-workflow-execution-state" &&
            route.QueryShape == "list-pinned-artifact-ids-bounded");
        Assert.Equal(ElsaGroundworkQueryContinuation.Offset, pinnedArtifactPage.Continuation);
        var pinnedArtifactRoute = Assert.Single(pinnedArtifactPage.PhysicalRoutes);
        Assert.Equal(
            ElsaRuntimeStorageManifest.WorkflowExecutionHistoryArtifactIdField,
            pinnedArtifactRoute.LatestPerKeyPath);
        Assert.Equal(
            [
                ElsaRuntimeStorageManifest.WorkflowExecutionHistoryArtifactIdField,
                ElsaRuntimeStorageManifest.WorkflowExecutionHistoryWorkflowExecutionIdField
            ],
            pinnedArtifactRoute.OrderingFields);

        var activityStateParentPage = Assert.Single(routes, route =>
            route.CoverageRow == "runtime-activity-execution-state" &&
            route.QueryShape == "list-by-parent-bounded");
        Assert.Collection(
            Assert.Single(activityStateParentPage.PhysicalRoutes).Predicates,
            workflow => Assert.Equal(ElsaRuntimeStorageManifest.WorkflowExecutionIdField, workflow.Path),
            parent => Assert.Equal(ElsaRuntimeStorageManifest.ParentActivityExecutionIdField, parent.Path));

        var declaration = await new RuntimeGroundworkStorageManifestSource().CreateDeclarationAsync();
        foreach (var catalogRoute in routes.Where(route => route.Kind == ElsaGroundworkQueryRouteKind.BoundedRoute))
        {
            var unit = declaration.Manifest.StorageUnits.Single(candidate =>
                candidate.Identity.Value == catalogRoute.DocumentKind);
            var storage = unit.PhysicalStorage;
            Assert.NotNull(storage);
            var physical = Assert.IsType<PhysicalStoragePolicy.ExplicitPolicy>(storage.Policy).Definition;

            Assert.All(
                catalogRoute.RequiredPhysicalFields,
                field => Assert.True(
                    field == PhysicalDocumentFieldPaths.Id ||
                    physical.ProjectedColumns.Any(column => column.Path == field),
                    $"Required field '{field}' is neither an envelope field nor a projected column."));

            foreach (var route in catalogRoute.PhysicalRoutes)
            {
                var admitted = Assert.Single(storage.BoundedQueries, candidate => candidate.Identity == route.Identity);
                Assert.Equal(route.IndexIdentity, admitted.IndexIdentity);
                Assert.Equal(BoundedQueryExecutionClass.ScaleBearing, admitted.ExecutionClass);
                Assert.Equal(route.OrderingFields, admitted.SortFields.Select(field => field.Path));
                var expectedResultOperation = catalogRoute.ResultOperation switch
                {
                    ElsaGroundworkQueryResultOperation.Any => BoundedQueryResultOperation.Any,
                    ElsaGroundworkQueryResultOperation.First => BoundedQueryResultOperation.First,
                    ElsaGroundworkQueryResultOperation.Documents or
                    ElsaGroundworkQueryResultOperation.Projection => BoundedQueryResultOperation.Documents,
                    _ => throw new ArgumentOutOfRangeException()
                };
                Assert.Contains(expectedResultOperation, admitted.ResultOperations);
                Assert.DoesNotContain(
                    physical.Indexes,
                    index => index.LogicalName == route.IndexIdentity && index.Columns.Count == 0);
            }
        }
    }

    private static void AssertStimulusProjectionLengths(
        PhysicalTableDefinition physical,
        int stimulusTypeLength = ElsaRuntimeStorageManifest.StimulusTypeProjectionLength)
    {
        var stimulusHash = Assert.Single(
            physical.ProjectedColumns,
            column => column.LogicalName == ElsaRuntimeStorageManifest.ByStimulusIndex);
        var stimulusType = Assert.Single(
            physical.ProjectedColumns,
            column => column.LogicalName == ElsaRuntimeStorageManifest.ByStimulusTypeIndex);

        Assert.Equal(ElsaRuntimeStorageManifest.StimulusHashProjectionLength, stimulusHash.Length);
        Assert.Equal(stimulusTypeLength, stimulusType.Length);
    }

    private static StorageManifest CreatePreAttentionRuntimeManifest()
    {
        var current = ElsaRuntimeStorageManifest.CreatePhysicalized();
        var legacyIncident = LegacyGroundworkStorageManifestPhysicalizer
            .Physicalize(ElsaRuntimeStorageManifest.Create())
            .StorageUnits
            .Single(unit => unit.Identity.Value == ElsaRuntimeStorageManifest.IncidentStateDocumentKind);

        return current with
        {
            StorageUnits = current.StorageUnits
                .Select(unit => unit.Identity.Value == ElsaRuntimeStorageManifest.IncidentStateDocumentKind
                    ? legacyIncident
                    : unit)
                .ToArray()
        };
    }

    private static ValueTask<GroundworkPhysicalSchemaManifestSource> CreateSqliteSchemaSourceAsync(StorageManifest manifest)
    {
        var topology = new GroundworkProviderTopologySnapshot(
            SqliteGroundworkCapabilities.Provider.Name,
            "sqlite-file",
            new HashSet<string>(StringComparer.Ordinal)
            {
                RuntimeGroundworkStorageManifestSource.MultiDocumentTransactionsTopologyIdentity
            });
        return GroundworkStoreInitialization.CreatePhysicalSchemaSourceAsync(
            SqliteGroundworkCapabilities.Runtime(),
            topology,
            SqliteGroundworkCapabilities.PhysicalNames,
            [new StaticRuntimeManifestSource(manifest)]);
    }

    private sealed class StaticRuntimeManifestSource(StorageManifest manifest) : IGroundworkStorageManifestSource
    {
        public string FeatureIdentity => RuntimeGroundworkStorageManifestSource.FeatureName;

        public ValueTask<GroundworkStorageManifestDeclaration> CreateDeclarationAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new GroundworkStorageManifestDeclaration(
                FeatureIdentity,
                manifest,
                [],
                [],
                [],
                []));
    }
}
