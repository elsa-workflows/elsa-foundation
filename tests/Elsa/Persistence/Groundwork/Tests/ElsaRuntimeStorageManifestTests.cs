using Elsa.Persistence.Groundwork;
using Groundwork.Core.Indexing;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Queries;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests;

/// <summary>
/// Manifest-shape guards for the runtime storage manifest. These pin the declared index surface the store
/// bridges depend on so a rename or accidental removal fails loudly rather than silently degrading a query.
/// </summary>
public sealed class ElsaRuntimeStorageManifestTests
{
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
    }

    [Fact]
    public async Task Workflow_trigger_binding_declares_composite_stimulus_type_route()
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
            stimulus => Assert.Equal(ElsaRuntimeStorageManifest.StimulusHashField, stimulus.Path),
            type => Assert.Equal(ElsaRuntimeStorageManifest.StimulusTypeField, type.Path));

        var route = Assert.Single(
            unit.PhysicalStorage.BoundedQueries,
            query => query.Identity == ElsaRuntimeStorageManifest.ListTriggerBindingsByStimulusAndTypeQuery);
        Assert.Equal(ElsaRuntimeStorageManifest.ByStimulusAndTypeIndex, route.IndexIdentity);
        Assert.Collection(
            route.PredicateFields,
            stimulus => Assert.Equal(ElsaRuntimeStorageManifest.StimulusHashField, stimulus.Path),
            type => Assert.Equal(ElsaRuntimeStorageManifest.StimulusTypeField, type.Path));
        Assert.Contains(
            physical.Indexes,
            index => index.LogicalName == ElsaRuntimeStorageManifest.ByStimulusAndTypeIndex && index.Columns.Count == 3);
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
        Assert.Equal(ElsaRuntimeStorageManifest.RecurringTriggerScheduleByNextOccurrence, route.IndexIdentity);
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
        Assert.Equal(ElsaRuntimeStorageManifest.DurableTimerByDueTime, route.IndexIdentity);
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
    }
}
