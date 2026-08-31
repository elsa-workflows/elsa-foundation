using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Runtime;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Persistence.Groundwork.Testing;
using Groundwork.Kernel;
using Groundwork.MongoDb;
using Groundwork.PostgreSql;
using Groundwork.Query.Model;
using Groundwork.Sqlite;
using Groundwork.SqlServer;
using Groundwork.Store;
using System.Text.Json;
using Xunit;
using Xunit.Sdk;

namespace Elsa.Persistence.Groundwork.V2.Runtime.Tests;

public sealed class GroundworkV2ActivityExecutionInspectionHierarchyStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Inspection_save_and_summary_pages_are_scoped_and_composite()
    {
        await using var runtime = new SqliteRuntime();
        var tenantA = runtime.Inspection("tenant-a");
        var tenantB = runtime.Inspection("tenant-b");

        await tenantA.SaveAsync(Projection("wf-a", "same", 2));
        await tenantA.SaveAsync(Projection("wf-a", "first", 1));
        await tenantA.SaveAsync(Projection("wf-b", "same", 1));

        Assert.Equal("wf-a", (await tenantA.FindAsync("wf-a", "same"))!.WorkflowExecutionId);
        Assert.Equal("wf-b", (await tenantA.FindAsync("wf-b", "same"))!.WorkflowExecutionId);
        Assert.Null(await tenantB.FindAsync("wf-a", "same"));

        var first = await tenantA.ListSummariesPageAsync(
            new ActivityExecutionInspectionSummaryPageQuery("wf-a", 1));
        var second = await tenantA.ListSummariesPageAsync(
            new ActivityExecutionInspectionSummaryPageQuery("wf-a", 1, first.NextContinuationToken));

        Assert.Equal(2, first.TotalCount);
        Assert.Equal("first", Assert.Single(first.Items).ActivityExecutionId);
        Assert.Equal("same", Assert.Single(second.Items).ActivityExecutionId);
        Assert.Null(second.NextContinuationToken);
    }

    [Fact]
    public async Task Hierarchy_pages_use_a_stable_watermark_and_project_boundaries()
    {
        await using var runtime = new SqliteRuntime();
        var store = runtime.Hierarchy("tenant-a");
        await store.SaveAsync(Record("wf", "root", "root", null, 1, boundary: true));
        await store.SaveAsync(Record("wf", "child-a", "root", "root", 2));
        await store.SaveAsync(Record("wf", "child-b", "root", "child-a", 3));

        var first = await store.ReadPageAsync(Query("wf", "root", limit: 1));
        Assert.Equal("child-a", Assert.Single(first!.Items).ActivityExecutionId);
        Assert.Equal(1, first.Items[0].RelativeDepth);
        Assert.Equal(3, first.CommittedThroughSequence);

        await store.SaveAsync(Record("wf", "late", "root", "root", 99));
        var second = await store.ReadPageAsync(Query("wf", "root", 1, first.NextCursor));

        Assert.Equal("child-b", Assert.Single(second!.Items).ActivityExecutionId);
        Assert.Equal(3, second.CommittedThroughSequence);
        Assert.Null(second.NextCursor);

        var boundary = await store.FindBoundaryAsync("wf", "root");
        Assert.NotNull(boundary);
        Assert.Equal(3, boundary!.CommittedDescendantCount);
    }

    [Fact]
    public async Task Hierarchy_continuation_rejects_a_non_adjacent_provider_cycle()
    {
        var root = Record("wf-cycle", "root", "root", null, 1, boundary: true);
        var firstChild = Record("wf-cycle", "child-a", "root", "root", 2);
        var secondChild = Record("wf-cycle", "child-b", "root", "root", 3);
        var unit = ElsaRuntimeV2StorageManifest.Require(
            ElsaRuntimeV2StorageManifest.ActivityExecutionHierarchyDocumentKind);
        var session = new ScriptedHierarchySession(
            unit,
            new StoredEntry(GroundworkV2ActivityExecutionHierarchyStorageConventions.Values(root), 1),
            [
                new QueryMaterializedResult([GroundworkV2ActivityExecutionHierarchyStorageConventions.Values(secondChild).Values], null, null),
                new QueryMaterializedResult([GroundworkV2ActivityExecutionHierarchyStorageConventions.Values(firstChild).Values], null, "a"),
                new QueryMaterializedResult([GroundworkV2ActivityExecutionHierarchyStorageConventions.Values(secondChild).Values], null, null),
                new QueryMaterializedResult([GroundworkV2ActivityExecutionHierarchyStorageConventions.Values(secondChild).Values], null, "b"),
                new QueryMaterializedResult([GroundworkV2ActivityExecutionHierarchyStorageConventions.Values(secondChild).Values], null, null),
                new QueryMaterializedResult([GroundworkV2ActivityExecutionHierarchyStorageConventions.Values(firstChild).Values], null, "a")
            ]);
        var store = new GroundworkV2ActivityExecutionHierarchyStore(
            new ScriptedHierarchySource(session, unit),
            new FixedAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))),
            new TestCursorCodec());

        var first = await store.ReadPageAsync(Query("wf-cycle", "root", 1));
        var second = await store.ReadPageAsync(Query("wf-cycle", "root", 1, first!.NextCursor));
        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            store.ReadPageAsync(Query("wf-cycle", "root", 1, second!.NextCursor)).AsTask());

        Assert.Contains("advance", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Hierarchy_read_page_rejects_provider_rows_out_of_order()
    {
        var root = Record("wf-order", "root", "root", null, 1, boundary: true);
        var firstChild = Record("wf-order", "child-a", "root", "root", 2);
        var secondChild = Record("wf-order", "child-b", "root", "root", 3);
        var unit = ElsaRuntimeV2StorageManifest.Require(
            ElsaRuntimeV2StorageManifest.ActivityExecutionHierarchyDocumentKind);
        var session = new ScriptedHierarchySession(
            unit,
            new StoredEntry(GroundworkV2ActivityExecutionHierarchyStorageConventions.Values(root), 1),
            [
                new QueryMaterializedResult([GroundworkV2ActivityExecutionHierarchyStorageConventions.Values(secondChild).Values], null, null),
                new QueryMaterializedResult([
                    GroundworkV2ActivityExecutionHierarchyStorageConventions.Values(secondChild).Values,
                    GroundworkV2ActivityExecutionHierarchyStorageConventions.Values(firstChild).Values
                ], null, null)
            ]);
        var store = new GroundworkV2ActivityExecutionHierarchyStore(
            new ScriptedHierarchySource(session, unit),
            new FixedAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))),
            new TestCursorCodec());

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            store.ReadPageAsync(Query("wf-order", "root", 2)).AsTask());

        Assert.Contains("advance", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Hierarchy_read_page_rejects_provider_rows_over_the_requested_limit()
    {
        var root = Record("wf-limit", "root", "root", null, 1, boundary: true);
        var firstChild = Record("wf-limit", "child-a", "root", "root", 2);
        var secondChild = Record("wf-limit", "child-b", "root", "root", 3);
        var unit = ElsaRuntimeV2StorageManifest.Require(
            ElsaRuntimeV2StorageManifest.ActivityExecutionHierarchyDocumentKind);
        var session = new ScriptedHierarchySession(
            unit,
            new StoredEntry(GroundworkV2ActivityExecutionHierarchyStorageConventions.Values(root), 1),
            [
                new QueryMaterializedResult([GroundworkV2ActivityExecutionHierarchyStorageConventions.Values(secondChild).Values], null, null),
                new QueryMaterializedResult([
                    GroundworkV2ActivityExecutionHierarchyStorageConventions.Values(firstChild).Values,
                    GroundworkV2ActivityExecutionHierarchyStorageConventions.Values(secondChild).Values
                ], null, null)
            ]);
        var store = new GroundworkV2ActivityExecutionHierarchyStore(
            new ScriptedHierarchySource(session, unit),
            new FixedAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))),
            new TestCursorCodec());

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            store.ReadPageAsync(Query("wf-limit", "root", 1)).AsTask());

        Assert.Contains("limit", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Hierarchy_continuation_fails_closed_when_the_root_snapshot_changes()
    {
        await using var runtime = new SqliteRuntime();
        var store = runtime.Hierarchy("tenant-a");
        await store.SaveAsync(Record("wf-root-snapshot", "root", "root", null, 1, boundary: true));
        await store.SaveAsync(Record("wf-root-snapshot", "child-a", "root", "root", 2));
        await store.SaveAsync(Record("wf-root-snapshot", "child-b", "root", "root", 3));

        var first = await store.ReadPageAsync(Query("wf-root-snapshot", "root", 1));
        await store.SaveAsync(Record("wf-root-snapshot", "root", "root", null, 99, boundary: true));

        var exception = await Assert.ThrowsAsync<ActivityExecutionHierarchyCursorException>(() =>
            store.ReadPageAsync(Query("wf-root-snapshot", "root", 1, first!.NextCursor)).AsTask());

        Assert.Equal(ActivityExecutionHierarchyCursorFailure.Expired, exception.Failure);
        AssertCursorRestartMetadata(exception.Metadata);
    }

    [Fact]
    public async Task Hierarchy_cursor_binding_failure_reports_structured_metadata()
    {
        await using var runtime = new SqliteRuntime();
        var store = runtime.Hierarchy("tenant-a");
        await store.SaveAsync(Record("wf-binding", "root", "root", null, 1, boundary: true));
        await store.SaveAsync(Record("wf-binding", "child-a", "root", "root", 2));
        await store.SaveAsync(Record("wf-binding", "child-b", "root", "root", 3));

        var first = await store.ReadPageAsync(Query("wf-binding", "root", 1));
        var exception = await Assert.ThrowsAsync<ActivityExecutionHierarchyCursorException>(() =>
            store.ReadPageAsync(Query(
                "wf-binding",
                "root",
                1,
                first!.NextCursor,
                authorizationProfile: "structure+values")).AsTask());

        Assert.Equal(ActivityExecutionHierarchyCursorFailure.BindingMismatch, exception.Failure);
        Assert.Equal(ActivityExecutionCursorBindingState.Matched, exception.Metadata!.BoundaryBinding);
        Assert.Equal(ActivityExecutionCursorBindingState.Matched, exception.Metadata.QueryBinding);
        Assert.Equal(ActivityExecutionCursorBindingState.Mismatched, exception.Metadata.AccessBinding);
        Assert.True(exception.Metadata.Recoverable);
        Assert.Equal("restart-from-first-page", exception.Metadata.RecoveryAction);
    }

    [Fact]
    public async Task Hierarchy_expired_cursor_reports_structured_metadata()
    {
        var root = Record("wf-expired", "root", "root", null, 1, boundary: true);
        var child = Record("wf-expired", "child", "root", "root", 2);
        var unit = ElsaRuntimeV2StorageManifest.Require(
            ElsaRuntimeV2StorageManifest.ActivityExecutionHierarchyDocumentKind);
        var rootValues = GroundworkV2ActivityExecutionHierarchyStorageConventions.Values(root);
        var childValues = GroundworkV2ActivityExecutionHierarchyStorageConventions.Values(child);
        var session = new ScriptedHierarchySession(
            unit,
            new StoredEntry(rootValues, 1),
            [
                new QueryMaterializedResult([childValues.Values], null, null),
                new QueryMaterializedResult([childValues.Values], null, "next"),
                new QueryMaterializedResult([rootValues.Values], null, null)
            ]);
        var store = new GroundworkV2ActivityExecutionHierarchyStore(
            new ScriptedHierarchySource(session, unit),
            new FixedAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))),
            new TestCursorCodec());

        var first = await store.ReadPageAsync(Query("wf-expired", "root", 1));
        var exception = await Assert.ThrowsAsync<ActivityExecutionHierarchyCursorException>(() =>
            store.ReadPageAsync(Query("wf-expired", "root", 1, first!.NextCursor)).AsTask());

        Assert.Equal(ActivityExecutionHierarchyCursorFailure.Expired, exception.Failure);
        AssertCursorRestartMetadata(exception.Metadata);
    }

    [Fact]
    public async Task Hierarchy_read_page_rejects_empty_provider_page_with_continuation()
    {
        var root = Record("wf-malformed", "root", "root", null, 1, boundary: true);
        var unit = ElsaRuntimeV2StorageManifest.Require(
            ElsaRuntimeV2StorageManifest.ActivityExecutionHierarchyDocumentKind);
        var session = new ScriptedHierarchySession(
            unit,
            new StoredEntry(GroundworkV2ActivityExecutionHierarchyStorageConventions.Values(root), 1),
            [
                new QueryMaterializedResult([GroundworkV2ActivityExecutionHierarchyStorageConventions.Values(root).Values], null, null),
                new QueryMaterializedResult([], null, "next")
            ]);
        var source = new ScriptedHierarchySource(session, unit);
        var store = new GroundworkV2ActivityExecutionHierarchyStore(
            source,
            new FixedAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))),
            new TestCursorCodec());

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            store.ReadPageAsync(Query("wf-malformed", "root", 1)).AsTask());

        Assert.Contains("empty page", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Hierarchy_read_page_rejects_non_advancing_provider_continuation()
    {
        var root = Record("wf-repeated", "root", "root", null, 1, boundary: true);
        var child = Record("wf-repeated", "child", "root", "root", 2);
        var unit = ElsaRuntimeV2StorageManifest.Require(
            ElsaRuntimeV2StorageManifest.ActivityExecutionHierarchyDocumentKind);
        var rootValues = GroundworkV2ActivityExecutionHierarchyStorageConventions.Values(root);
        var childValues = GroundworkV2ActivityExecutionHierarchyStorageConventions.Values(child);
        var session = new ScriptedHierarchySession(
            unit,
            new StoredEntry(rootValues, 1),
            [
                new QueryMaterializedResult([childValues.Values], null, null),
                new QueryMaterializedResult([childValues.Values], null, "next"),
                new QueryMaterializedResult([childValues.Values], null, null),
                new QueryMaterializedResult([childValues.Values], null, "next")
            ]);
        var store = new GroundworkV2ActivityExecutionHierarchyStore(
            new ScriptedHierarchySource(session, unit),
            new FixedAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))),
            new TestCursorCodec());

        var first = await store.ReadPageAsync(Query("wf-repeated", "root", 1));
        Assert.NotNull(first?.NextCursor);
        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            store.ReadPageAsync(Query("wf-repeated", "root", 1, first!.NextCursor)).AsTask());

        Assert.Contains("did not advance", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Hierarchy_boundary_rejects_empty_provider_page_with_continuation()
    {
        var root = Record("wf-boundary-malformed", "root", "root", null, 1, boundary: true);
        var unit = ElsaRuntimeV2StorageManifest.Require(
            ElsaRuntimeV2StorageManifest.ActivityExecutionHierarchyDocumentKind);
        var rootValues = GroundworkV2ActivityExecutionHierarchyStorageConventions.Values(root);
        var session = new ScriptedHierarchySession(
            unit,
            new StoredEntry(rootValues, 1),
            [
                new QueryMaterializedResult([rootValues.Values], null, null),
                new QueryMaterializedResult([], null, "next")
            ]);
        var store = new GroundworkV2ActivityExecutionHierarchyStore(
            new ScriptedHierarchySource(session, unit),
            new FixedAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))),
            new TestCursorCodec());

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            store.FindBoundaryAsync("wf-boundary-malformed", "root").AsTask());

        Assert.Contains("empty page", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Hierarchy_attempt_navigation_rejects_empty_provider_page_with_continuation()
    {
        var unit = ElsaRuntimeV2StorageManifest.Require(
            ElsaRuntimeV2StorageManifest.ActivityExecutionHierarchyDocumentKind);
        var session = new ScriptedHierarchySession(
            unit,
            root: null,
            [new QueryMaterializedResult([], null, "next")]);
        var store = new GroundworkV2ActivityExecutionHierarchyStore(
            new ScriptedHierarchySource(session, unit),
            new FixedAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))),
            new TestCursorCodec());

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            store.FindAttemptNavigationAsync("wf-navigation-malformed", "activity").AsTask());

        Assert.Contains("empty page", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Hierarchy_query_scope_must_match_authoritative_access_scope_before_provider_open()
    {
        var unit = ElsaRuntimeV2StorageManifest.Require(
            ElsaRuntimeV2StorageManifest.ActivityExecutionHierarchyDocumentKind);
        var source = new CountingHierarchySource(unit);
        var store = new GroundworkV2ActivityExecutionHierarchyStore(
            source,
            new FixedAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))),
            new TestCursorCodec());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.ReadPageAsync(Query("wf-scope-mismatch", "root", 1, tenantScope: "tenant:tenant-b")).AsTask());

        Assert.Equal(0, source.OpenCount);
    }

    [Fact]
    public async Task Checkpoint_and_direct_stores_share_composite_identity_and_delete_convergence()
    {
        await using var runtime = new SqliteRuntime();
        var projection = Projection("wf-checkpoint", "activity-checkpoint", 1, boundary: true);
        var commit = Commit(
            "inspection-checkpoint",
            projection,
            RuntimeStateChangeOperation.Upsert);
        var writer = runtime.Writer("tenant-a");

        await writer.CommitAsync(
            commit,
            new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate));

        var physicalId = GroundworkV2ActivityExecutionInspectionStorageConventions.PhysicalId(
            projection.WorkflowExecutionId,
            projection.ActivityExecutionId);
        var inspectionEntry = runtime.Read(
            ElsaRuntimeV2StorageManifest.ActivityExecutionInspectionDocumentKind,
            physicalId,
            "tenant-a");
        var hierarchyEntry = runtime.Read(
            ElsaRuntimeV2StorageManifest.ActivityExecutionHierarchyDocumentKind,
            physicalId,
            "tenant-a");
        Assert.NotNull(inspectionEntry);
        Assert.NotNull(hierarchyEntry);
        Assert.Equal(physicalId, inspectionEntry!.Values.Values[ElsaRuntimeV2StorageManifest.IdField]);
        Assert.Equal(physicalId, hierarchyEntry!.Values.Values[ElsaRuntimeV2StorageManifest.IdField]);
        Assert.NotNull(await runtime.Inspection("tenant-a").FindAsync(
            projection.WorkflowExecutionId,
            projection.ActivityExecutionId));
        Assert.NotNull(await runtime.Hierarchy("tenant-a").FindBoundaryAsync(
            projection.WorkflowExecutionId,
            projection.ActivityExecutionId));

        await writer.CommitAsync(
            Commit("inspection-delete", projection, RuntimeStateChangeOperation.Delete),
            new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate));

        Assert.Null(await runtime.Inspection("tenant-a").FindAsync(
            projection.WorkflowExecutionId,
            projection.ActivityExecutionId));
        Assert.Null(await runtime.Hierarchy("tenant-a").FindBoundaryAsync(
            projection.WorkflowExecutionId,
            projection.ActivityExecutionId));
    }

    [Fact]
    public async Task Checkpoint_scope_loss_updates_inspection_and_removes_stale_hierarchy()
    {
        await using var runtime = new SqliteRuntime();
        var initial = Projection("wf-scope-loss", "activity-scope-loss", 1, boundary: true);
        var writer = runtime.Writer("tenant-a");

        await writer.CommitAsync(
            Commit("inspection-scope-loss-initial", initial, RuntimeStateChangeOperation.Upsert),
            new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate));

        var scopeLess = initial with
        {
            ExecutionSequence = 2,
            ExecutionScopeId = null,
            Provenance = initial.Provenance with { ExecutionScopeId = null },
            CompletedAt = Now.AddSeconds(2),
            StartedAt = Now.AddSeconds(2),
            LastCommittedAt = Now.AddSeconds(2)
        };
        await writer.CommitAsync(
            Commit("inspection-scope-loss-update", scopeLess, RuntimeStateChangeOperation.Upsert),
            new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate));

        var inspection = await runtime.Inspection("tenant-a").FindAsync(
            scopeLess.WorkflowExecutionId,
            scopeLess.ActivityExecutionId);
        Assert.NotNull(inspection);
        Assert.Equal(2, inspection!.ExecutionSequence);
        Assert.Null(await runtime.Hierarchy("tenant-a").FindBoundaryAsync(
            scopeLess.WorkflowExecutionId,
            scopeLess.ActivityExecutionId));
    }

    [Fact]
    public async Task Global_and_across_scope_reads_are_refused_before_provider_open()
    {
        await using var runtime = new SqliteRuntime();
        var inspectionGlobal = runtime.Inspection(PersistenceAccessContext.Global);
        var hierarchyAcrossScopes = runtime.Hierarchy(PersistenceAccessContext.PrivilegedAcrossScopes(
            new PersistenceAccessPurpose("test-activity-inspection-hierarchy-refusal")));
        var opensBefore = runtime.OpenCount;

        await Assert.ThrowsAsync<InvalidOperationException>(() => inspectionGlobal.FindAsync("wf", "activity").AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => hierarchyAcrossScopes.FindBoundaryAsync("wf", "activity").AsTask());
        Assert.Equal(opensBefore, runtime.OpenCount);
    }

    [Fact]
    public async Task Hierarchy_attempt_navigation_is_read_from_the_bounded_workflow_route()
    {
        await using var runtime = new SqliteRuntime();
        var store = runtime.Hierarchy("tenant-a");
        await store.SaveAsync(Record("wf", "attempt-1", "attempt-1", null, 1, boundary: true) with
        {
            Item = Record("wf", "attempt-1", "attempt-1", null, 1, boundary: true).Item with
            {
                Attempt = new ActivityExecutionAttemptLineage(1, "attempt-1", null)
            }
        });
        await store.SaveAsync(Record("wf", "attempt-2", "attempt-2", null, 2) with
        {
            Item = Record("wf", "attempt-2", "attempt-2", null, 2).Item with
            {
                Attempt = new ActivityExecutionAttemptLineage(2, "attempt-1", "attempt-1")
            }
        });
        await store.SaveAsync(Record("wf", "attempt-3", "attempt-3", null, 3) with
        {
            Item = Record("wf", "attempt-3", "attempt-3", null, 3).Item with
            {
                Attempt = new ActivityExecutionAttemptLineage(3, "attempt-1", "attempt-2")
            }
        });

        var navigation = await store.FindAttemptNavigationAsync("wf", "attempt-2");

        Assert.NotNull(navigation);
        Assert.Equal("attempt-1", navigation!.Lineage.PreviousAttemptActivityExecutionId);
        Assert.Equal("attempt-3", navigation.NextAttemptActivityExecutionId);
        Assert.Equal(3, navigation.TotalAttempts);
    }

    [SkippableTheory]
    [InlineData("postgresql")]
    [InlineData("sqlserver")]
    [InlineData("mongodb")]
    public async Task Configured_native_provider_preserves_inspection_and_hierarchy_contract(
        string providerName)
    {
        var connectionString = Environment.GetEnvironmentVariable(
            $"GROUNDWORK_V2_{providerName.ToUpperInvariant()}_CONNECTION_STRING");
        Skip.If(
            string.IsNullOrWhiteSpace(connectionString),
            $"Set GROUNDWORK_V2_{providerName.ToUpperInvariant()}_CONNECTION_STRING to run the {providerName} inspection/hierarchy gate.");

        using var connection = CreateConnection(providerName, connectionString!);
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var inspectionUnit = WithSuffix(
            ElsaRuntimeV2StorageManifest.Require(
                ElsaRuntimeV2StorageManifest.ActivityExecutionInspectionDocumentKind),
            suffix);
        var hierarchyUnit = WithSuffix(
            ElsaRuntimeV2StorageManifest.Require(
                ElsaRuntimeV2StorageManifest.ActivityExecutionHierarchyDocumentKind),
            suffix);
        connection.Schema.Apply(inspectionUnit);
        connection.Schema.Apply(hierarchyUnit);
        var units = new Dictionary<string, StorageUnit>(StringComparer.Ordinal)
        {
            [ElsaRuntimeV2StorageManifest.ActivityExecutionInspectionDocumentKind] = inspectionUnit,
            [ElsaRuntimeV2StorageManifest.ActivityExecutionHierarchyDocumentKind] = hierarchyUnit
        };
        units[inspectionUnit.Id.Value] = inspectionUnit;
        units[hierarchyUnit.Id.Value] = hierarchyUnit;
        var source = new NativeSessionSource(connection, units);
        var access = new FixedAccessContextAccessor(
            PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var inspection = new GroundworkV2ActivityExecutionInspectionStore(source, access);
        var hierarchy = new GroundworkV2ActivityExecutionHierarchyStore(
            source,
            access,
            new TestCursorCodec());
        var projection = Projection(
            "wf-native",
            "activity-native",
            1,
            boundary: true);

        await inspection.SaveAsync(projection);
        await hierarchy.SaveAsync(ActivityExecutionHierarchyProjector.FromInspection(projection));

        Assert.NotNull(await inspection.FindAsync("wf-native", "activity-native"));
        Assert.Single((await inspection.ListSummariesPageAsync(
            new ActivityExecutionInspectionSummaryPageQuery("wf-native"))).Items);
        Assert.NotNull(await hierarchy.FindBoundaryAsync("wf-native", "activity-native"));
    }

    private static ActivityExecutionInspectionProjection Projection(
        string workflowExecutionId,
        string activityExecutionId,
        long executionSequence,
        string? executionScopeId = null,
        string? parentActivityExecutionId = null,
        bool boundary = false)
    {
        var scope = executionScopeId ?? activityExecutionId;
        var metadata = boundary
            ? new Dictionary<string, string>
            {
                ["activity.definitionId"] = $"def-{activityExecutionId}",
                ["activity.definitionVersionId"] = $"ver-{activityExecutionId}",
                ["activity.version"] = "1.0.0",
                ["activity.templateHash"] = $"hash-{activityExecutionId}"
            }
            : new Dictionary<string, string>();
        return new(
            activityExecutionId,
            workflowExecutionId,
            $"node-{activityExecutionId}",
            $"authored-{activityExecutionId}",
            "Test.Activity",
            "1.0",
            ActivityExecutionStatus.Completed,
            null,
            executionSequence,
            Now.AddSeconds(executionSequence),
            Now.AddSeconds(executionSequence),
            Now.AddSeconds(executionSequence),
            "first-checkpoint",
            "last-checkpoint",
            Now.AddSeconds(executionSequence),
            ActivitySchedulingProvenance.From(
                workflowExecutionId,
                parentActivityExecutionId,
                parentActivityExecutionId,
                null,
                null,
                null,
                scope,
                "test"),
            ["Done"],
            [],
            [],
            [],
            metadata,
            scope);
    }

    private static ActivityExecutionHierarchyRecord Record(
        string workflowExecutionId,
        string activityExecutionId,
        string executionScopeId,
        string? parentActivityExecutionId,
        long sequence,
        bool boundary = false) =>
        ActivityExecutionHierarchyProjector.FromInspection(
            Projection(
                workflowExecutionId,
                activityExecutionId,
                sequence,
                executionScopeId,
                parentActivityExecutionId,
                boundary));

    private static ActivityExecutionHierarchyQuery Query(
        string workflowExecutionId,
        string rootActivityExecutionId,
        int limit,
        string? cursor = null,
        string tenantScope = "tenant:tenant-a",
        string authorizationProfile = "structure") =>
        new(
            workflowExecutionId,
            rootActivityExecutionId,
            cursor,
            limit,
            new HashSet<ActivityExecutionHierarchyInclude>(),
            authorizationProfile,
            tenantScope);

    private static void AssertCursorRestartMetadata(ActivityExecutionCursorFailureMetadata? metadata)
    {
        Assert.NotNull(metadata);
        Assert.Equal("activity-execution-hierarchy", metadata!.CursorClass);
        Assert.Equal(ActivityExecutionCursorBindingState.Matched, metadata.BoundaryBinding);
        Assert.Equal(ActivityExecutionCursorBindingState.Matched, metadata.QueryBinding);
        Assert.Equal(ActivityExecutionCursorBindingState.Matched, metadata.AccessBinding);
        Assert.True(metadata.Recoverable);
        Assert.Equal("restart-from-first-page", metadata.RecoveryAction);
    }

    private static RuntimeCheckpointCommit Commit(
        string commitId,
        ActivityExecutionInspectionProjection projection,
        RuntimeStateChangeOperation operation) =>
        new(
            commitId,
            new RuntimeCheckpoint(
                $"checkpoint-{commitId}",
                "runtime",
                projection.WorkflowExecutionId,
                Now,
                [projection.ActivityExecutionId],
                new Dictionary<string, string>()),
            new RuntimeCheckpointStateChangeSet(
                null,
                null,
                [],
                [],
                [],
                [],
                [],
                workflowDispatches: null,
                activityExecutionInspections:
                [
                    new RuntimeStateChange<ActivityExecutionInspectionProjection>(
                        projection.ActivityExecutionId,
                        operation,
                        projection,
                        new Dictionary<string, string>())
                ]),
            [],
            new Dictionary<string, string>());

    private sealed class SqliteRuntime : IAsyncDisposable
    {
        private readonly string database = Path.Combine(
            Path.GetTempPath(),
            $"elsa-runtime-inspection-hierarchy-{Guid.NewGuid():N}.db");
        private readonly IStorageProviderConnection connection;
        private readonly NativeSessionSource source;

        public SqliteRuntime()
        {
            connection = new SqliteProviderFactory().Create($"Data Source={database}");
            foreach (var unit in ElsaRuntimeV2StorageManifest.CreateUnits())
                connection.Schema.Apply(unit);
            source = new NativeSessionSource(connection);
        }

        public int OpenCount => source.OpenCount;

        public GroundworkV2ActivityExecutionInspectionStore Inspection(string scope) =>
            Inspection(PersistenceAccessContext.Scoped(new PersistenceScope(scope)));

        public GroundworkV2ActivityExecutionInspectionStore Inspection(PersistenceAccessContext context) =>
            new(source, new FixedAccessContextAccessor(context));

        public GroundworkV2ActivityExecutionHierarchyStore Hierarchy(string scope) =>
            Hierarchy(PersistenceAccessContext.Scoped(new PersistenceScope(scope)));

        public GroundworkV2ActivityExecutionHierarchyStore Hierarchy(PersistenceAccessContext context) =>
            new(
                source,
                new FixedAccessContextAccessor(context),
                new TestCursorCodec());

        public GroundworkV2RuntimeCheckpointWriter Writer(string scope) =>
            new(source, new FixedAccessContextAccessor(
                PersistenceAccessContext.Scoped(new PersistenceScope(scope))));

        public StoredEntry? Read(string unitId, string id, string scope) =>
            connection.OpenSession(
                    ElsaRuntimeV2StorageManifest.Require(unitId),
                    StorageAccess.Scoped(new StorageScope(scope)))
                .Read(GroundworkRuntimeRowStore.Key(id));

        public ValueTask DisposeAsync()
        {
            connection.Dispose();
            foreach (var path in new[]
                     {
                         database,
                         $"{database}-shm",
                         $"{database}-wal",
                         $"{database}-journal",
                         $"{database}.schema.lock"
                     })
            {
                if (File.Exists(path))
                    File.Delete(path);
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class ScriptedHierarchySource(
        IStorageSession session,
        StorageUnit unit) : IGroundworkStorageSessionSource
    {
        public int OpenCount { get; private set; }

        public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null)
        {
            OpenCount++;
            return session;
        }

        public IUnitOfWork BeginUnitOfWork(
            StorageAccess access,
            BatchWriteOptions options,
            IReadOnlyList<string> unitIds,
            string? targetName = null) => throw new NotSupportedException();

        public StorageUnit Unit(string unitId, string? targetName = null) => unit;
    }

    private sealed class CountingHierarchySource(StorageUnit unit) : IGroundworkStorageSessionSource
    {
        public int OpenCount { get; private set; }

        public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null)
        {
            OpenCount++;
            throw new InvalidOperationException("provider open should not occur");
        }

        public IUnitOfWork BeginUnitOfWork(
            StorageAccess access,
            BatchWriteOptions options,
            IReadOnlyList<string> unitIds,
            string? targetName = null) => throw new NotSupportedException();

        public StorageUnit Unit(string unitId, string? targetName = null) => unit;
    }

    private sealed class ScriptedHierarchySession(
        StorageUnit unit,
        StoredEntry? root,
        IReadOnlyList<QueryMaterializedResult> pages) : SynchronousStorageSessionTestDouble, IStorageSession
    {
        private int queryIndex;

        public StorageUnit Unit { get; } = unit;
        public StorageAccess Access { get; } = StorageAccess.Scoped(new StorageScope("tenant-a"));

        public StoredEntry? Read(StorageKey key) => root;

        public QueryMaterializedResult Query(QueryRequest request, QueryRenderOptions? options = null)
        {
            if (queryIndex >= pages.Count)
                throw new InvalidOperationException("The scripted hierarchy session received an unexpected query.");
            return pages[queryIndex++];
        }

        public AggregationResult Aggregate(AggregationQuery query) => throw new NotSupportedException();
        public WriteOutcome Insert(StorageValues values, WriteOptions? options = null) => throw new NotSupportedException();
        public WriteOutcome Update(StorageValues values, WriteOptions? options = null) => throw new NotSupportedException();
        public WriteOutcome Upsert(StorageValues values, WriteOptions? options = null) => throw new NotSupportedException();
        public WriteOutcome Delete(StorageKey key, WriteOptions? options = null) => throw new NotSupportedException();
        public WriteOutcome Append(OperationId operationId, IReadOnlyList<StorageValues> values) => throw new NotSupportedException();
    }

    private sealed class NativeSessionSource(IStorageProviderConnection connection)
        : IGroundworkStorageSessionSource, IGroundworkStorageCapabilitySource
    {
        private readonly IReadOnlyDictionary<string, StorageUnit> units =
            new Dictionary<string, StorageUnit>(StringComparer.Ordinal);

        public NativeSessionSource(
            IStorageProviderConnection connection,
            IReadOnlyDictionary<string, StorageUnit> units)
            : this(connection)
        {
            this.units = units;
        }

        public int OpenCount { get; private set; }

        public IReadOnlyList<CapabilityDescriptor> Capabilities(string? targetName = null) =>
            connection.Capabilities;

        public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null)
        {
            OpenCount++;
            return connection.OpenSession(ResolveUnit(unitId), access);
        }

        public IUnitOfWork BeginUnitOfWork(
            StorageAccess access,
            BatchWriteOptions options,
            IReadOnlyList<string> unitIds,
            string? targetName = null) =>
            connection.BeginUnitOfWork(
                access,
                options,
                unitIds.Select(ResolveUnit).ToArray());

        public StorageUnit Unit(string unitId, string? targetName = null) =>
            ResolveUnit(unitId);

        private StorageUnit ResolveUnit(string unitId) =>
            units.TryGetValue(unitId, out var unit)
                ? unit
                : ElsaRuntimeV2StorageManifest.Require(unitId);
    }

    private sealed class FixedAccessContextAccessor(PersistenceAccessContext current)
        : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = current;
    }

    private sealed class TestCursorCodec : IActivityExecutionHierarchyCursorCodec
    {
        public string Encode(ActivityExecutionHierarchyCursorState state) =>
            Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(state));

        public ActivityExecutionHierarchyCursorState Decode(string cursor) =>
            JsonSerializer.Deserialize<ActivityExecutionHierarchyCursorState>(
                Convert.FromBase64String(cursor)) ??
            throw new ActivityExecutionHierarchyCursorException(
                ActivityExecutionHierarchyCursorFailure.Invalid,
                "The test hierarchy cursor is invalid.");
    }

    private static StorageUnit WithSuffix(StorageUnit unit, string suffix) => unit with
    {
        Id = new StorageUnitId($"{unit.Id.Value}-{suffix}"),
        Name = $"{unit.Name}_{suffix}"
    };

    private static IStorageProviderConnection CreateConnection(
        string providerName,
        string connectionString) => providerName switch
        {
            "postgresql" => new PostgreSqlProviderFactory().Create(connectionString),
            "sqlserver" => new SqlServerProviderFactory().Create(connectionString),
            "mongodb" => new MongoProviderFactory().Create(connectionString),
            _ => throw new ArgumentOutOfRangeException(nameof(providerName), providerName, null)
        };
}
