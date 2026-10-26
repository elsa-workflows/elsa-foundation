using System.Text.Json;
using System.Text.Json.Nodes;
using System.Data;
using System.Data.Common;
using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.Tests;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Exceptions;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Elsa.Workflows.Runtime.Services.Recovery;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed class EfActivityExecutionStoresTests
{
    [Fact]
    public async Task Browsing_keeps_nested_parallel_and_reusable_parent_contexts_separate()
    {
        await using var fixture = await Fixture.CreateAsync("tenant-a");
        var store = fixture.Inspection;
        long sequence = 0;
        async Task Save(string id, string node, string? parent, string? iteration = null, string? branch = null)
        {
            sequence++;
            var value = Projection("wf", id, sequence, "scope");
            await store.SaveAsync(value with { ExecutableNodeId = node, ScheduledAt = DateTimeOffset.UnixEpoch.AddSeconds(sequence),
                Provenance = value.Provenance with { ParentActivityExecutionId = parent, IterationId = iteration, BranchId = branch } });
        }
        await Save("owner", "owner-node", null);
        await Save("pass-a", "body", "owner", "outer-a");
        await Save("pass-b", "body", "owner", "outer-b");
        await Save("inner-a", "inner-loop", "pass-a");
        await Save("inner-b", "inner-loop", "pass-b");
        await Save("branch-a", "branch", "inner-a", "same-local-iteration");
        await Save("branch-b", "branch", "inner-b", "same-local-iteration");
        await Save("left", "leaf", "branch-a", branch: "left");
        await Save("right", "leaf", "branch-a", branch: "right");
        await Save("other", "leaf", "branch-b");
        await Save("call-a", "reusable", "pass-a");
        await Save("call-b", "reusable", "pass-b");
        await Save("internal-a", "template-leaf", "call-a");
        await Save("internal-b", "template-leaf", "call-b");
        var parallel = await store.BrowseAsync(new ActivityExecutionBrowseQuery("wf", executableNodeId: "leaf", ancestorActivityExecutionId: "pass-a"));
        Assert.Equal(new[] { "left", "right" }, parallel.Items.Select(x => x.Execution.ActivityExecutionId));
        var other = await store.BrowseAsync(new ActivityExecutionBrowseQuery("wf", executableNodeId: "leaf", parentActivityExecutionId: "branch-b"));
        Assert.Equal("other", Assert.Single(other.Items).Execution.ActivityExecutionId);
        Assert.Empty((await store.BrowseAsync(new ActivityExecutionBrowseQuery("wf", parentActivityExecutionId: "branch-a", ancestorActivityExecutionId: "pass-b"))).Items);
        var reusable = await store.BrowseAsync(new ActivityExecutionBrowseQuery("wf", executableNodeId: "template-leaf", ancestorActivityExecutionId: "call-a"));
        Assert.Equal("internal-a", Assert.Single(reusable.Items).Execution.ActivityExecutionId);
        var iterations = await store.BrowseAsync(new ActivityExecutionBrowseQuery("wf", executableNodeId: "branch"));
        Assert.Equal(new long?[] { 1, 1 }, iterations.Items.Select(x => x.IterationPosition));
    }

    [Fact]
    public async Task Browsing_iteration_order_uses_the_actual_first_attempt_tuple()
    {
        await using var fixture = await Fixture.CreateAsync("tenant-a");
        var store = fixture.Inspection;
        async Task Save(string id, long sequence, int time, string iteration)
        {
            var value = Projection("wf", id, sequence, "scope");
            await store.SaveAsync(value with { ExecutableNodeId = "body", ScheduledAt = DateTimeOffset.UnixEpoch.AddSeconds(time),
                Provenance = value.Provenance with { IterationId = iteration, ParentActivityExecutionId = "loop" } });
        }
        await Save("z", 100, 100, "a");
        await Save("m", 50, 100, "b");
        await Save("a", 1, 200, "a");
        var result = await store.BrowseAsync(new ActivityExecutionBrowseQuery("wf", executableNodeId: "body"));
        Assert.Equal(new[] { "m", "z", "a" }, result.Items.Select(x => x.Execution.ActivityExecutionId));
        Assert.Equal(new long?[] { 1, 2, 2 }, result.Items.Select(x => x.IterationPosition));
    }

    [Fact]
    public async Task Browsing_uses_scheduling_chronology_and_counts_distinct_iterations_across_retries()
    {
        await using var fixture = await Fixture.CreateAsync("tenant-a");
        var store = fixture.Inspection;
        async Task Save(string id, long sequence, int time, string iteration)
        {
            var value = Projection("wf", id, sequence, "scope");
            await store.SaveAsync(value with { ExecutableNodeId = "body", ScheduledAt = DateTimeOffset.UnixEpoch.AddSeconds(time),
                Provenance = value.Provenance with { IterationId = iteration, ParentActivityExecutionId = "loop" } });
        }
        await Save("first", 900, 1, "iteration-a");
        await Save("retry", 100, 2, "iteration-a");
        await Save("second", 200, 3, "iteration-b");
        var result = await store.BrowseAsync(new ActivityExecutionBrowseQuery("wf", executableNodeId: "body"));
        Assert.Equal(new[] { "first", "retry", "second" }, result.Items.Select(x => x.Execution.ActivityExecutionId));
        Assert.Equal(new long?[] { 1, 1, 2 }, result.Items.Select(x => x.IterationPosition));
        Assert.Equal(new long[] { 1, 2, 3 }, result.Items.Select(x => x.Position));
        Assert.Equal("second", Assert.Single((await store.BrowseAsync(new ActivityExecutionBrowseQuery("wf", 1, executableNodeId: "body"))).Items).Execution.ActivityExecutionId);
    }

    [Fact]
    public async Task Browsing_filters_before_paging_and_preserves_exact_parent_occurrences()
    {
        await using var fixture = await Fixture.CreateAsync("tenant-a");
        var store = fixture.Inspection;
        async Task Save(string id, long sequence, string node, string? parent = null)
        {
            var value = Projection("wf", id, sequence, "scope");
            await store.SaveAsync(value with { ExecutableNodeId = node,
                Provenance = value.Provenance with { ParentActivityExecutionId = parent } });
        }
        await Save("loop", 1, "loop-node");
        for (var i = 1; i <= 12; i++)
        {
            await Save($"body-{i:D2}", 2 * i, "body", "loop");
            await Save($"write-{i:D2}", 2 * i + 1, "write", $"body-{i:D2}");
        }
        var latest = await store.BrowseAsync(new ActivityExecutionBrowseQuery("wf", 3, executableNodeId: "write", ancestorActivityExecutionId: "loop"));
        Assert.Equal(12, latest.TotalCount);
        Assert.Equal(new long[] { 10, 11, 12 }, latest.Items.Select(x => x.Position));
        Assert.Equal("write-09", latest.Items[0].PreviousActivityExecutionId);
        Assert.Null(latest.Items[^1].NextActivityExecutionId);
        var anchored = await store.BrowseAsync(new ActivityExecutionBrowseQuery("wf", 3, executableNodeId: "write", ancestorActivityExecutionId: "loop", anchorActivityExecutionId: "write-04"));
        Assert.Equal("write-04", anchored.Items[0].Execution.ActivityExecutionId);
        Assert.Equal(4, anchored.Items[0].Position);
        var scoped = await store.BrowseAsync(new ActivityExecutionBrowseQuery("wf", executableNodeId: "write", ancestorActivityExecutionId: "body-04"));
        Assert.Equal("write-04", Assert.Single(scoped.Items).Execution.ActivityExecutionId);
        Assert.Equal(1, scoped.TotalCount);
        var children = await store.BrowseAsync(new ActivityExecutionBrowseQuery("wf", parentActivityExecutionId: "loop"));
        Assert.Equal(12, children.TotalCount);
        Assert.All(children.Items, x => Assert.Equal("body", x.Execution.ExecutableNodeId));
        Assert.Empty((await store.BrowseAsync(new ActivityExecutionBrowseQuery("wf", ancestorActivityExecutionId: "missing"))).Items);
        Assert.Empty((await store.BrowseAsync(new ActivityExecutionBrowseQuery("wf", executableNodeId: "write", anchorActivityExecutionId: "loop"))).Items);
        Assert.Empty((await store.BrowseAsync(new ActivityExecutionBrowseQuery("other"))).Items);
    }

    [Fact]
    public async Task State_store_round_trips_scoped_rows_and_provider_count_with_bounded_pages()
    {
        await using var fixture = await Fixture.CreateAsync("tenant-a");
        await fixture.State.SaveAsync(State("wf", "b", 2));
        await fixture.State.SaveAsync(State("wf", "a", 1));
        await fixture.State.SaveAsync(State("wf", "c", 3, "parent"));
        await fixture.State.SaveAsync(State("other", "a", 1));

        Assert.Equal(3, await fixture.State.CountAsync("wf"));
        var first = await fixture.State.ListPageAsync(new ActivityExecutionStatePageQuery("wf", 1));
        Assert.Equal("a", first.Items[0].Execution.ActivityExecutionId);
        Assert.NotNull(first.NextContinuationToken);
        var second = await fixture.State.ListPageAsync(new ActivityExecutionStatePageQuery("wf", 1, first.NextContinuationToken));
        Assert.Equal("b", second.Items[0].Execution.ActivityExecutionId);
        var third = await fixture.State.ListPageAsync(new ActivityExecutionStatePageQuery("wf", 1, second.NextContinuationToken));
        Assert.Equal("c", third.Items[0].Execution.ActivityExecutionId);
        Assert.Null(third.NextContinuationToken);
        Assert.Single((await fixture.State.ListByParentPageAsync(new ActivityExecutionStateParentPageQuery("wf", "parent"))).Items);

        await using var otherScope = await fixture.ReopenAsync("tenant-b");
        Assert.Null(await otherScope.State.FindAsync("wf", "a"));
    }

    [Fact]
    public async Task Inspection_store_round_trips_projection_and_summary_cursor()
    {
        await using var fixture = await Fixture.CreateAsync("tenant-a");
        await fixture.Inspection.SaveAsync(Projection("wf", "b", 2, "b"));
        await fixture.Inspection.SaveAsync(Projection("wf", "a", 1, "a"));

        var first = await fixture.Inspection.ListSummariesPageAsync(new ActivityExecutionInspectionSummaryPageQuery("wf", 1));
        Assert.Equal(2, first.TotalCount);
        Assert.Equal("a", first.Items[0].ActivityExecutionId);
        Assert.NotNull(first.NextContinuationToken);
        var second = await fixture.Inspection.ListSummariesPageAsync(new ActivityExecutionInspectionSummaryPageQuery("wf", 1, first.NextContinuationToken));
        Assert.Equal("b", second.Items[0].ActivityExecutionId);
        Assert.NotNull(await fixture.Inspection.FindAsync("wf", "a"));
    }

    [Fact]
    public async Task Inspection_summary_page_scopes_count_items_and_cursor_to_one_workflow()
    {
        await using var fixture = await Fixture.CreateAsync("tenant-a");
        await fixture.Inspection.SaveAsync(Projection("wf", "a", 1, "a"));
        await fixture.Inspection.SaveAsync(Projection("wf", "b", 1, "b"));
        await fixture.Inspection.SaveAsync(Projection("other-wf", "a", 1, "a"));
        var foreignScope = new EfActivityExecutionInspectionStore(fixture.Context, new Accessor("tenant-b"), fixture.RecoveryContinuationCodec);
        await foreignScope.SaveAsync(Projection("wf", "a", 1, "a"));

        var first = await fixture.Inspection.ListSummariesPageAsync(new ActivityExecutionInspectionSummaryPageQuery("wf", 1));
        Assert.Equal(2, first.TotalCount);
        Assert.Equal(["a"], ActivityIds(first));
        var cursor = Assert.IsType<string>(first.NextContinuationToken);
        var second = await fixture.Inspection.ListSummariesPageAsync(new ActivityExecutionInspectionSummaryPageQuery("wf", 1, cursor));
        Assert.Equal(2, second.TotalCount);
        Assert.Equal(["b"], ActivityIds(second));
        Assert.Null(second.NextContinuationToken);

        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Inspection.ListSummariesPageAsync(
            new ActivityExecutionInspectionSummaryPageQuery("other-wf", 1, cursor)).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => foreignScope.ListSummariesPageAsync(
            new ActivityExecutionInspectionSummaryPageQuery("wf", 1, cursor)).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Inspection.ListSummariesPageAsync(
            new ActivityExecutionInspectionSummaryPageQuery("wf", 1, cursor + "tampered")).AsTask());
    }

    [Fact]
    public async Task Inspection_summary_page_keeps_empty_view_consistent_when_a_row_is_inserted()
    {
        await using var fixture = await InspectionPageRaceFixture.CreateAsync("tenant-inspection-page-empty", "wf-inspection-page-empty");

        var interleaved = await fixture.ReadPageWithMutationAsync(10, null,
            cancellationToken => fixture.InsertAsync("final", 1, cancellationToken));
        AssertMutationWasApplied(interleaved.Mutation);

        Assert.Null(interleaved.Page.NextContinuationToken);
        if (interleaved.Page.TotalCount == 0)
            Assert.Empty(interleaved.Page.Items);
        else
        {
            Assert.Equal(1, interleaved.Page.TotalCount);
            Assert.Equal(["final"], ActivityIds(interleaved.Page));
        }

        var current = await fixture.ReadPageAsync(10);
        Assert.Equal(1, current.TotalCount);
        Assert.Equal(["final"], ActivityIds(current));
        Assert.Null(current.NextContinuationToken);
    }

    [Fact]
    public async Task Inspection_summary_page_keeps_nonempty_insert_and_continuation_consistent()
    {
        await using var fixture = await InspectionPageRaceFixture.CreateAsync("tenant-inspection-page-insert", "wf-inspection-page-insert");
        await fixture.InsertAsync("existing", 1);

        var interleaved = await fixture.ReadPageWithMutationAsync(1, null,
            cancellationToken => fixture.InsertAsync("final", 2, cancellationToken));
        AssertMutationWasApplied(interleaved.Mutation);

        Assert.Equal(["existing"], ActivityIds(interleaved.Page));
        if (interleaved.Page.NextContinuationToken is null)
            Assert.Equal(1, interleaved.Page.TotalCount);
        else
        {
            Assert.Equal(2, interleaved.Page.TotalCount);
            var continuation = await fixture.ReadPageAsync(1, interleaved.Page.NextContinuationToken);
            Assert.Equal(2, continuation.TotalCount);
            Assert.Equal(["final"], ActivityIds(continuation));
            Assert.Null(continuation.NextContinuationToken);
        }

        var current = await fixture.ReadPageAsync(1);
        Assert.Equal(2, current.TotalCount);
        Assert.Equal(["existing"], ActivityIds(current));
        Assert.NotNull(current.NextContinuationToken);
        var currentContinuation = await fixture.ReadPageAsync(1, current.NextContinuationToken);
        Assert.Equal(2, currentContinuation.TotalCount);
        Assert.Equal(["final"], ActivityIds(currentContinuation));
        Assert.Null(currentContinuation.NextContinuationToken);
    }

    [Fact]
    public async Task Inspection_summary_page_keeps_first_page_consistent_when_its_first_row_is_deleted()
    {
        await using var fixture = await InspectionPageRaceFixture.CreateAsync("tenant-inspection-page-delete-first", "wf-inspection-page-delete-first");
        await fixture.InsertAsync("first", 1);
        await fixture.InsertAsync("second", 2);

        var interleaved = await fixture.ReadPageWithMutationAsync(1, null,
            cancellationToken => fixture.DeleteAsync("first", cancellationToken));
        AssertMutationWasApplied(interleaved.Mutation);

        var ids = ActivityIds(interleaved.Page);
        if (ids.Length == 1 && ids[0] == "first")
        {
            Assert.Equal(2, interleaved.Page.TotalCount);
            Assert.NotNull(interleaved.Page.NextContinuationToken);
            var continuation = await fixture.ReadPageAsync(1, interleaved.Page.NextContinuationToken);
            Assert.Equal(1, continuation.TotalCount);
            Assert.Equal(["second"], ActivityIds(continuation));
            Assert.Null(continuation.NextContinuationToken);
        }
        else
        {
            Assert.Equal(["second"], ids);
            Assert.Equal(1, interleaved.Page.TotalCount);
            Assert.Null(interleaved.Page.NextContinuationToken);
        }

        var current = await fixture.ReadPageAsync(1);
        Assert.Equal(1, current.TotalCount);
        Assert.Equal(["second"], ActivityIds(current));
        Assert.Null(current.NextContinuationToken);
    }

    [Fact]
    public async Task Inspection_summary_page_keeps_continuation_consistent_when_its_row_is_deleted()
    {
        await using var fixture = await InspectionPageRaceFixture.CreateAsync("tenant-inspection-page-delete-continuation", "wf-inspection-page-delete-continuation");
        await fixture.InsertAsync("first", 1);
        await fixture.InsertAsync("second", 2);
        var first = await fixture.ReadPageAsync(1);
        Assert.Equal(2, first.TotalCount);
        Assert.Equal(["first"], ActivityIds(first));
        var cursor = Assert.IsType<string>(first.NextContinuationToken);

        var interleaved = await fixture.ReadPageWithMutationAsync(1, cursor,
            cancellationToken => fixture.DeleteAsync("second", cancellationToken));
        AssertMutationWasApplied(interleaved.Mutation);

        var ids = ActivityIds(interleaved.Page);
        if (ids.Length == 0)
        {
            Assert.Equal(1, interleaved.Page.TotalCount);
            Assert.Null(interleaved.Page.NextContinuationToken);
        }
        else
        {
            Assert.Equal(["second"], ids);
            Assert.Equal(2, interleaved.Page.TotalCount);
            Assert.Null(interleaved.Page.NextContinuationToken);
        }

        var emptiedContinuation = await fixture.ReadPageAsync(1, cursor);
        Assert.Equal(1, emptiedContinuation.TotalCount);
        Assert.Empty(emptiedContinuation.Items);
        Assert.Null(emptiedContinuation.NextContinuationToken);
    }

    [Fact]
    public async Task Inspection_summary_page_does_not_commit_or_dispose_a_caller_serializable_transaction()
    {
        await using var fixture = await InspectionPageRaceFixture.CreateAsync("tenant-inspection-page-caller-transaction", "wf-inspection-page-caller-transaction");
        await fixture.InsertAsync("existing", 1);
        await using var context = fixture.OpenContext();
        var store = fixture.CreateStore(context);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);

        var page = await store.ListSummariesPageAsync(new ActivityExecutionInspectionSummaryPageQuery(fixture.WorkflowExecutionId));
        Assert.Equal(["existing"], ActivityIds(page));
        Assert.Same(transaction, context.Database.CurrentTransaction);

        await store.SaveAsync(Projection(fixture.WorkflowExecutionId, "rolled-back", 2, fixture.Scope));
        Assert.Same(transaction, context.Database.CurrentTransaction);
        await transaction.RollbackAsync();

        var afterRollback = await fixture.ReadPageAsync(10);
        Assert.Equal(1, afterRollback.TotalCount);
        Assert.Equal(["existing"], ActivityIds(afterRollback));
        Assert.Null(afterRollback.NextContinuationToken);
    }

    [Fact]
    public async Task Inspection_summary_page_rejects_an_ambient_transaction()
    {
        await using var fixture = await InspectionPageRaceFixture.CreateAsync("tenant-inspection-page-ambient", "wf-inspection-page-ambient");
        await using var context = fixture.OpenContext();
        var store = fixture.CreateStore(context);
        using var transaction = new System.Transactions.TransactionScope(System.Transactions.TransactionScopeAsyncFlowOption.Enabled);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => store.ListSummariesPageAsync(
            new ActivityExecutionInspectionSummaryPageQuery(fixture.WorkflowExecutionId)).AsTask());
        Assert.Contains("ambient or externally enlisted", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Activity_execution_rows_survive_an_independent_file_backed_sqlite_restart()
    {
        await using var database = new TemporarySqliteDatabase("runtime");
        var path = database.Path;
        const string scope = "tenant-restart";
        const string workflow = "wf-restart";
        await using (var connection = new SqliteConnection($"Data Source={path}"))
        {
            await connection.OpenAsync();
            await using var context = new RuntimeSqliteDbContext(new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(connection).Options);
            await context.Database.EnsureCreatedAsync();
            var accessor = new Accessor(scope);
            var continuationCodec = new HmacRuntimeRecoveryContinuationCodec(Microsoft.Extensions.Options.Options.Create(new RuntimeRecoveryContinuationOptions { SigningKey = "ef-runtime-restart-signing-key-32-bytes" }));
            var hierarchyCodec = new HmacActivityExecutionHierarchyCursorCodec(Microsoft.Extensions.Options.Options.Create(new ActivityExecutionHierarchyCursorOptions { SigningKey = "ef-runtime-restart-signing-key-32-bytes" }));
            var state = new EfActivityExecutionStateStore(context, accessor, continuationCodec);
            var inspection = new EfActivityExecutionInspectionStore(context, accessor, continuationCodec);
            var hierarchy = new EfActivityExecutionHierarchyStore(context, accessor, hierarchyCodec);
            await state.SaveAsync(State(workflow, "activity", 1));
            await inspection.SaveAsync(Projection(workflow, "root", 1, "root", null, true));
            await hierarchy.SaveAsync(HierarchyProjection(workflow, "root", 1, "root", null, true));
            await hierarchy.SaveAsync(HierarchyProjection(workflow, "child", 2, "root", "root"));
        }

        await using (var connection = new SqliteConnection($"Data Source={path}"))
        {
            await connection.OpenAsync();
            await using var context = new RuntimeSqliteDbContext(new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(connection).Options);
            var accessor = new Accessor(scope);
            var continuationCodec = new HmacRuntimeRecoveryContinuationCodec(Microsoft.Extensions.Options.Options.Create(new RuntimeRecoveryContinuationOptions { SigningKey = "ef-runtime-restart-signing-key-32-bytes" }));
            var hierarchyCodec = new HmacActivityExecutionHierarchyCursorCodec(Microsoft.Extensions.Options.Options.Create(new ActivityExecutionHierarchyCursorOptions { SigningKey = "ef-runtime-restart-signing-key-32-bytes" }));
            var state = new EfActivityExecutionStateStore(context, accessor, continuationCodec);
            var inspection = new EfActivityExecutionInspectionStore(context, accessor, continuationCodec);
            var hierarchy = new EfActivityExecutionHierarchyStore(context, accessor, hierarchyCodec);
            Assert.NotNull(await state.FindAsync(workflow, "activity"));
            Assert.NotNull(await inspection.FindAsync(workflow, "root"));
            var page = await hierarchy.ReadPageAsync(new ActivityExecutionHierarchyQuery(workflow, "root", null, 10,
                new HashSet<ActivityExecutionHierarchyInclude>(), "profile", $"tenant:{scope}"));
            Assert.Equal("child", Assert.Single(page!.Items).ActivityExecutionId);
        }
    }

    [Fact]
    public async Task Two_context_create_races_normalize_conflicts_through_each_activity_execution_store()
    {
        await using var database = new TemporarySqliteDatabase("runtime-race");
        var path = database.Path;
        const string scope = "tenant-race";
        await using (var connection = new SqliteConnection($"Data Source={path}"))
        {
            await connection.OpenAsync();
            await using var context = new RuntimeSqliteDbContext(new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(connection).Options);
            await context.Database.EnsureCreatedAsync();
        }

        var stateResults = await RunCreateRaceAsync(path, "elsa_runtime_activity_execution_state", context =>
            new EfActivityExecutionStateStore(context, new Accessor(scope), RecoveryCodec()).SaveAsync(State("wf-race-state", "activity", 1)).AsTask());
        var inspectionResults = await RunCreateRaceAsync(path, "elsa_runtime_activity_execution_inspection", context =>
            new EfActivityExecutionInspectionStore(context, new Accessor(scope), RecoveryCodec()).SaveAsync(Projection("wf-race-inspection", "activity", 1, "activity")).AsTask());
        var hierarchyResults = await RunCreateRaceAsync(path, "elsa_runtime_activity_execution_hierarchy", context =>
            new EfActivityExecutionHierarchyStore(context, new Accessor(scope), HierarchyCodec()).SaveAsync(HierarchyProjection("wf-race-hierarchy", "root", 1, "root", null, true)).AsTask());

        AssertCreateRaceConflict(stateResults, "activity");
        AssertCreateRaceConflict(inspectionResults, "activity");
        AssertCreateRaceConflict(hierarchyResults, "root");
    }

    [Fact]
    public async Task Provider_read_and_write_failures_normalize_through_each_activity_execution_store()
    {
        await using var database = new TemporarySqliteDatabase("runtime-provider");
        var path = database.Path;
        const string scope = "tenant-provider";
        await using (var connection = new SqliteConnection($"Data Source={path}"))
        {
            await connection.OpenAsync();
            await using var context = new RuntimeSqliteDbContext(new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(connection).Options);
            await context.Database.EnsureCreatedAsync();
        }

        await AssertProviderFailuresAsync(path, scope, "state", context => new EfActivityExecutionStateStore(context, new Accessor(scope), RecoveryCodec()),
            store => store.FindAsync("wf-provider-state", "activity").AsTask(),
            context => new EfActivityExecutionStateStore(context, new Accessor(scope), RecoveryCodec()).SaveAsync(State("wf-provider-state", "activity", 1)).AsTask());
        await AssertProviderFailuresAsync(path, scope, "inspection", context => new EfActivityExecutionInspectionStore(context, new Accessor(scope), RecoveryCodec()),
            store => store.FindAsync("wf-provider-inspection", "activity").AsTask(),
            context => new EfActivityExecutionInspectionStore(context, new Accessor(scope), RecoveryCodec()).SaveAsync(Projection("wf-provider-inspection", "activity", 1, "activity")).AsTask());
        await AssertProviderFailuresAsync(path, scope, "hierarchy", context => new EfActivityExecutionHierarchyStore(context, new Accessor(scope), HierarchyCodec()),
            store => store.FindBoundaryAsync("wf-provider-hierarchy", "root").AsTask(),
            context => new EfActivityExecutionHierarchyStore(context, new Accessor(scope), HierarchyCodec()).SaveAsync(HierarchyProjection("wf-provider-hierarchy", "root", 1, "root", null, true)).AsTask());
    }

    [Fact]
    public async Task State_store_rejects_undefined_numeric_status_in_persisted_json()
    {
        await using var fixture = await Fixture.CreateAsync("tenant-a");
        await fixture.State.SaveAsync(State("wf-undefined-status", "activity", 1));

        var row = await fixture.Context.ActivityExecutionStates.SingleAsync();
        var payload = JsonNode.Parse(row.ContentJson)!.AsObject();
        payload["status"] = 999;
        row.ContentJson = payload.ToJsonString();
        row.Status = "999";
        fixture.Context.ChangeTracker.Clear();
        fixture.Context.ActivityExecutionStates.Update(row);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.State.FindAsync("wf-undefined-status", "activity").AsTask());
    }

    [Fact]
    public async Task Inspection_store_rejects_undefined_numeric_status_in_persisted_json()
    {
        await using var fixture = await Fixture.CreateAsync("tenant-a");
        await fixture.Inspection.SaveAsync(Projection("wf-undefined-status", "activity", 1, "activity"));

        var row = await fixture.Context.ActivityExecutionInspections.SingleAsync();
        var payload = JsonNode.Parse(row.ContentJson)!.AsObject();
        payload["status"] = 999;
        row.ContentJson = payload.ToJsonString();
        row.Status = "999";
        fixture.Context.ChangeTracker.Clear();
        fixture.Context.ActivityExecutionInspections.Update(row);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Inspection.FindAsync("wf-undefined-status", "activity").AsTask());
    }

    [Fact]
    public async Task Activity_execution_rows_reject_stale_revision_writes()
    {
        await using var fixture = await Fixture.CreateAsync("tenant-a");
        await fixture.State.SaveAsync(State("wf", "activity", 1));
        var stale = await fixture.Context.ActivityExecutionStates.AsNoTracking().SingleAsync();

        await fixture.State.SaveAsync(State("wf", "activity", 2));
        fixture.Context.ChangeTracker.Clear();
        fixture.Context.ActivityExecutionStates.Update(stale);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => fixture.Context.SaveChangesAsync());
    }

    [Fact]
    public async Task Hierarchy_store_keeps_watermark_cursor_and_boundary_scope_safe()
    {
        await using var fixture = await Fixture.CreateAsync("tenant-a");
        await fixture.Hierarchy.SaveAsync(HierarchyProjection("wf", "root", 1, "root", null, true));
        await fixture.Hierarchy.SaveAsync(HierarchyProjection("wf", "child-a", 2, "root", "root"));
        await fixture.Hierarchy.SaveAsync(HierarchyProjection("wf", "child-b", 3, "root", "root"));
        await fixture.Hierarchy.SaveAsync(HierarchyProjection("wf", "other-root", 4, "other-root", null, true));

        var first = await fixture.Hierarchy.ReadPageAsync(new ActivityExecutionHierarchyQuery(
            "wf", "root", null, 1, new HashSet<ActivityExecutionHierarchyInclude>(), "profile", "tenant:tenant-a"));
        Assert.NotNull(first);
        Assert.Equal(4, first!.CommittedThroughSequence);
        Assert.Single(first.Items);
        Assert.Equal("child-a", first.Items[0].ActivityExecutionId);
        Assert.NotNull(first.NextCursor);
        var second = await fixture.Hierarchy.ReadPageAsync(new ActivityExecutionHierarchyQuery(
            "wf", "root", first.NextCursor, 1, new HashSet<ActivityExecutionHierarchyInclude>(), "profile", "tenant:tenant-a"));
        Assert.Equal("child-b", second!.Items[0].ActivityExecutionId);
        Assert.Null(second.NextCursor);

        var boundary = await fixture.Hierarchy.FindBoundaryAsync("wf", "root");
        Assert.NotNull(boundary);
        Assert.Equal(2, boundary!.CommittedDescendantCount);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Hierarchy.ReadPageAsync(new ActivityExecutionHierarchyQuery(
            "wf", "root", null, 1, new HashSet<ActivityExecutionHierarchyInclude>(), "profile", "tenant:tenant-b")).AsTask());
    }

    [Fact]
    public async Task Hierarchy_store_expands_a_nested_boundary_through_its_own_execution_scope()
    {
        await using var fixture = await Fixture.CreateAsync("tenant-a");
        await fixture.Hierarchy.SaveAsync(HierarchyProjection("wf-nested", "outer", 1, "outer", null, true));
        await fixture.Hierarchy.SaveAsync(HierarchyProjection("wf-nested", "nested", 2, "outer", "outer", true));
        await fixture.Hierarchy.SaveAsync(HierarchyProjection("wf-nested", "nested-child", 3, "nested", "nested"));

        var outerPage = await fixture.Hierarchy.ReadPageAsync(new ActivityExecutionHierarchyQuery(
            "wf-nested", "outer", null, 100, new HashSet<ActivityExecutionHierarchyInclude>(), "profile", "tenant:tenant-a"));
        var nestedBoundary = Assert.Single(outerPage!.Items);
        Assert.Equal("nested", nestedBoundary.ActivityExecutionId);
        Assert.NotNull(nestedBoundary.Boundary);
        Assert.Equal(1, nestedBoundary.Boundary!.CommittedDescendantCount);
        Assert.DoesNotContain(outerPage.Items, item => item.ActivityExecutionId == "nested-child");

        var nestedPage = await fixture.Hierarchy.ReadPageAsync(new ActivityExecutionHierarchyQuery(
            "wf-nested", "nested", null, 100, new HashSet<ActivityExecutionHierarchyInclude>(), "profile", "tenant:tenant-a"));
        var nestedChild = Assert.Single(nestedPage!.Items);
        Assert.Equal("nested-child", nestedChild.ActivityExecutionId);
        Assert.Equal(1, nestedChild.RelativeDepth);
    }

    [Fact]
    public async Task Hierarchy_store_continues_equal_sequence_boundaries_and_attempts_by_activity_id()
    {
        await using var fixture = await Fixture.CreateAsync("tenant-a");
        await fixture.Hierarchy.SaveAsync(HierarchyProjection("wf-equal", "root", 0, "root", null, true));

        const int attemptCount = 501;
        for (var attemptNumber = 1; attemptNumber <= attemptCount; attemptNumber++)
        {
            var activityExecutionId = $"attempt-{attemptNumber:D4}";
            var projection = Projection("wf-equal", activityExecutionId, 1, "root", "root") with
            {
                Attempt = new(
                    attemptNumber,
                    "attempt-0001",
                    attemptNumber == 1 ? null : $"attempt-{attemptNumber - 1:D4}")
            };
            await fixture.Hierarchy.SaveAsync(ActivityExecutionHierarchyProjector.FromInspection(projection));
        }

        var boundary = await fixture.Hierarchy.FindBoundaryAsync("wf-equal", "root");
        Assert.NotNull(boundary);
        Assert.Equal(attemptCount, boundary!.CommittedDescendantCount);

        var navigation = await fixture.Hierarchy.FindAttemptNavigationAsync("wf-equal", "attempt-0250");
        Assert.NotNull(navigation);
        Assert.Equal("attempt-0251", navigation!.NextAttemptActivityExecutionId);
        Assert.Equal(attemptCount, navigation.TotalAttempts);
    }

    [Fact]
    public async Task Hierarchy_store_rejects_malformed_persisted_item_json_and_projection_drift()
    {
        await using var fixture = await Fixture.CreateAsync("tenant-a");
        await fixture.Hierarchy.SaveAsync(HierarchyProjection("wf-malformed", "root", 1, "root", null, true));

        var row = await fixture.Context.ActivityExecutionHierarchies.SingleAsync();
        var payload = JsonNode.Parse(row.ContentJson)!.AsObject();
        payload["item"]!.AsObject().Remove("executableNodeId");
        row.ContentJson = payload.ToJsonString();
        fixture.Context.ChangeTracker.Clear();
        fixture.Context.ActivityExecutionHierarchies.Update(row);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Hierarchy.FindBoundaryAsync("wf-malformed", "root").AsTask());

        await fixture.Hierarchy.SaveAsync(HierarchyProjection("wf-drift", "root", 1, "root", null, true));
        var drifted = await fixture.Context.ActivityExecutionHierarchies.SingleAsync(row => row.WorkflowExecutionIdHash == Elsa.Persistence.EntityFramework.EfRelationalIdentity.Hash("wf-drift"));
        drifted.ActivityExecutionIdOrderKey = "drifted";
        fixture.Context.ChangeTracker.Clear();
        fixture.Context.ActivityExecutionHierarchies.Update(drifted);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Hierarchy.FindBoundaryAsync("wf-drift", "root").AsTask());
    }

    [Theory]
    [InlineData("outcomeNames")]
    [InlineData("metadata")]
    public async Task Hierarchy_store_rejects_null_required_collections_in_persisted_item_json(string propertyName)
    {
        await using var fixture = await Fixture.CreateAsync("tenant-a");
        await fixture.Hierarchy.SaveAsync(HierarchyProjection("wf-malformed-collections", "root", 1, "root", null, true));

        var row = await fixture.Context.ActivityExecutionHierarchies.SingleAsync();
        var payload = JsonNode.Parse(row.ContentJson)!.AsObject();
        payload["item"]!.AsObject()[propertyName] = null;
        row.ContentJson = payload.ToJsonString();
        fixture.Context.ChangeTracker.Clear();
        fixture.Context.ActivityExecutionHierarchies.Update(row);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Hierarchy.FindBoundaryAsync("wf-malformed-collections", "root").AsTask());
    }

    [Fact]
    public async Task Hierarchy_store_rejects_parent_cycles_and_anomalous_watermark_cursors()
    {
        await using var fixture = await Fixture.CreateAsync("tenant-a");
        await fixture.Hierarchy.SaveAsync(HierarchyProjection("wf-cycle", "root", 1, "root", null, true));
        await fixture.Hierarchy.SaveAsync(HierarchyProjection("wf-cycle", "child-a", 2, "root", "child-b"));
        await fixture.Hierarchy.SaveAsync(HierarchyProjection("wf-cycle", "child-b", 3, "root", "child-a"));

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Hierarchy.ReadPageAsync(new ActivityExecutionHierarchyQuery(
            "wf-cycle", "root", null, 2, new HashSet<ActivityExecutionHierarchyInclude>(), "profile", "tenant:tenant-a")).AsTask());

        await fixture.Hierarchy.SaveAsync(HierarchyProjection("wf-watermark", "root", 1, "root", null, true));
        await fixture.Hierarchy.SaveAsync(HierarchyProjection("wf-watermark", "child-a", 2, "root", "root"));
        await fixture.Hierarchy.SaveAsync(HierarchyProjection("wf-watermark", "child-b", 3, "root", "root"));
        var first = await fixture.Hierarchy.ReadPageAsync(new ActivityExecutionHierarchyQuery(
            "wf-watermark", "root", null, 1, new HashSet<ActivityExecutionHierarchyInclude>(), "profile", "tenant:tenant-a"));
        var cursor = fixture.HierarchyCursorCodec.Decode(first!.NextCursor!);
        var anomalous = fixture.HierarchyCursorCodec.Encode(cursor with { CommittedThroughSequence = cursor.CommittedThroughSequence + 1 });
        var exception = await Assert.ThrowsAsync<ActivityExecutionHierarchyCursorException>(() => fixture.Hierarchy.ReadPageAsync(new ActivityExecutionHierarchyQuery(
            "wf-watermark", "root", anomalous, 1, new HashSet<ActivityExecutionHierarchyInclude>(), "profile", "tenant:tenant-a")).AsTask());
        Assert.Equal(ActivityExecutionHierarchyCursorFailure.Expired, exception.Failure);
    }

    [Fact]
    public async Task Hierarchy_store_rejects_missing_parent_during_depth_resolution()
    {
        await using var fixture = await Fixture.CreateAsync("tenant-a");
        await fixture.Hierarchy.SaveAsync(HierarchyProjection("wf-missing-parent", "root", 1, "root", null, true));
        await fixture.Hierarchy.SaveAsync(HierarchyProjection("wf-missing-parent", "child", 2, "root", "missing-parent"));

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Hierarchy.ReadPageAsync(new ActivityExecutionHierarchyQuery(
            "wf-missing-parent", "root", null, 1, new HashSet<ActivityExecutionHierarchyInclude>(), "profile", "tenant:tenant-a")).AsTask());
    }

    [Fact]
    public async Task Hierarchy_store_rejects_parent_from_a_different_execution_scope_during_depth_resolution()
    {
        await using var fixture = await Fixture.CreateAsync("tenant-a");
        await fixture.Hierarchy.SaveAsync(HierarchyProjection("wf-foreign-parent", "root", 1, "root", null, true));
        await fixture.Hierarchy.SaveAsync(HierarchyProjection("wf-foreign-parent", "foreign-parent", 2, "foreign-root", null));
        await fixture.Hierarchy.SaveAsync(HierarchyProjection("wf-foreign-parent", "child", 3, "root", "foreign-parent"));

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Hierarchy.ReadPageAsync(new ActivityExecutionHierarchyQuery(
            "wf-foreign-parent", "root", null, 1, new HashSet<ActivityExecutionHierarchyInclude>(), "profile", "tenant:tenant-a")).AsTask());
    }

    [Fact]
    public async Task Hierarchy_store_rejects_ancestor_chain_that_does_not_reach_requested_root()
    {
        await using var fixture = await Fixture.CreateAsync("tenant-a");
        await fixture.Hierarchy.SaveAsync(HierarchyProjection("wf-orphan-chain", "root", 1, "root", null, true));
        await fixture.Hierarchy.SaveAsync(HierarchyProjection("wf-orphan-chain", "orphan", 2, "root", null));
        await fixture.Hierarchy.SaveAsync(HierarchyProjection("wf-orphan-chain", "child", 3, "root", "orphan"));

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Hierarchy.ReadPageAsync(new ActivityExecutionHierarchyQuery(
            "wf-orphan-chain", "root", null, 1, new HashSet<ActivityExecutionHierarchyInclude>(), "profile", "tenant:tenant-a")).AsTask());
    }

    [Fact]
    public async Task Hierarchy_store_rejects_cursor_binding_and_provider_continuation_mismatch()
    {
        await using var fixture = await Fixture.CreateAsync("tenant-a");
        await fixture.Hierarchy.SaveAsync(HierarchyProjection("wf-cursor", "root", 1, "root", null, true));
        await fixture.Hierarchy.SaveAsync(HierarchyProjection("wf-cursor", "child-a", 2, "root", "root"));
        await fixture.Hierarchy.SaveAsync(HierarchyProjection("wf-cursor", "child-b", 3, "root", "root"));

        var first = await fixture.Hierarchy.ReadPageAsync(new ActivityExecutionHierarchyQuery(
            "wf-cursor", "root", null, 1, new HashSet<ActivityExecutionHierarchyInclude>(), "profile", "tenant:tenant-a"));
        var bound = fixture.HierarchyCursorCodec.Decode(first!.NextCursor!);
        var bindingException = await Assert.ThrowsAsync<ActivityExecutionHierarchyCursorException>(() => fixture.Hierarchy.ReadPageAsync(new ActivityExecutionHierarchyQuery(
            "wf-cursor", "root", fixture.HierarchyCursorCodec.Encode(bound with { AuthorizationProfile = "other-profile" }), 1,
            new HashSet<ActivityExecutionHierarchyInclude>(), "profile", "tenant:tenant-a")).AsTask());
        Assert.Equal(ActivityExecutionHierarchyCursorFailure.BindingMismatch, bindingException.Failure);

        var providerException = await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Hierarchy.ReadPageAsync(new ActivityExecutionHierarchyQuery(
            "wf-cursor", "root", fixture.HierarchyCursorCodec.Encode(bound with { ProviderContinuation = "provider-token" }), 1,
            new HashSet<ActivityExecutionHierarchyInclude>(), "profile", "tenant:tenant-a")).AsTask());
        Assert.Contains("provider continuation", providerException.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Direct_state_and_inspection_writes_reject_provenance_scope_mismatch()
    {
        await using var fixture = await Fixture.CreateAsync("tenant-a");
        var state = State("wf-provenance", "activity", 1, scope: "scope-a") with
        {
            Provenance = State("wf-provenance", "activity", 1, scope: "scope-b").Provenance
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.State.SaveAsync(state).AsTask());

        var projection = Projection("wf-provenance", "activity", 1, "scope-a") with
        {
            Provenance = Projection("wf-provenance", "activity", 1, "scope-b").Provenance
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Inspection.SaveAsync(projection).AsTask());

        var stateAttempt = State("wf-provenance", "attempted", 1, scope: "scope-a") with
        {
            Attempt = new(2, "attempt-1", "attempt-0"),
            Provenance = State("wf-provenance", "attempted", 1, scope: "scope-a").Provenance with
            {
                Attempt = new(1, "attempt-1", null)
            }
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.State.SaveAsync(stateAttempt).AsTask());

        var projectionAttempt = Projection("wf-provenance", "inspected", 1, "scope-a") with
        {
            Attempt = new(2, "attempt-1", "attempt-0"),
            Provenance = Projection("wf-provenance", "inspected", 1, "scope-a").Provenance with
            {
                Attempt = new(1, "attempt-1", null)
            }
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Inspection.SaveAsync(projectionAttempt).AsTask());
    }

    [Fact]
    public async Task Inspection_store_preserves_merge_and_retraction_semantics()
    {
        await using var fixture = await Fixture.CreateAsync("tenant-a");
        var bookmark = new ActivityExecutionBookmarkSummary(
            new("bookmark", "wf-merge", "activity", "resume"), "stimulus", "hash", DateTimeOffset.UnixEpoch, null);
        var initial = Projection("wf-merge", "activity", 1, "activity") with
        {
            Bookmarks = [bookmark],
            Metadata = new Dictionary<string, string> { ["initial"] = "yes" }
        };
        await fixture.Inspection.SaveAsync(initial);

        var superseded = State("wf-merge", "activity", 2, scope: "activity") with
        {
            Status = ActivityExecutionStatus.Superseded,
            SupersededByActivityExecutionId = "successor",
            SupersededAt = DateTimeOffset.UnixEpoch.AddSeconds(3),
            Metadata = new Dictionary<string, string> { ["updated"] = "yes" }
        };
        var accumulator = new RuntimeActivityExecutionInspectionAccumulator(fixture.Inspection);
        var merged = await accumulator.BuildProjectionAsync(
            superseded,
            "checkpoint-2",
            DateTimeOffset.UnixEpoch.AddSeconds(4),
            outcomeNames: ["Retried"],
            metadata: new Dictionary<string, string> { ["merge"] = "yes" });
        await fixture.Inspection.SaveAsync(merged);

        var stored = await fixture.Inspection.FindAsync("wf-merge", "activity");
        Assert.NotNull(stored);
        Assert.Equal(ActivityExecutionStatus.Superseded, stored!.Status);
        Assert.Equal("successor", stored.SupersededByActivityExecutionId);
        Assert.Equal("checkpoint-2", stored.LastCheckpointId);
        Assert.Equal("yes", stored.Metadata["initial"]);
        Assert.Equal("yes", stored.Metadata["updated"]);
        Assert.Equal("yes", stored.Metadata["merge"]);
        Assert.Equal("bookmark", Assert.Single(stored.Bookmarks).Identity.BookmarkId);
        Assert.Equal("Retried", Assert.Single(stored.OutcomeNames));
    }

    [Fact]
    public void Activity_execution_registration_replaces_only_runtime_defaults_and_is_idempotent()
    {
        var services = new ServiceCollection();
        services.AddWorkflowRuntime();
        services.AddRuntimeActivityExecutionEntityFrameworkCore(new RuntimeActivityExecutionEntityFrameworkCoreOptions
        {
            Provider = "sqlite",
            ConnectionString = "Data Source=:memory:",
            HierarchyCursorSigningKey = "ef-r07-r09-registration-hierarchy-key-32-bytes",
            RecoveryContinuationSigningKey = "ef-r07-r09-registration-signing-key-32-bytes"
        });
        services.AddRuntimeActivityExecutionEntityFrameworkCore(new RuntimeActivityExecutionEntityFrameworkCoreOptions
        {
            Provider = "SQLite",
            ConnectionString = "Data Source=:memory:",
            HierarchyCursorSigningKey = "ef-r07-r09-registration-hierarchy-key-32-bytes",
            RecoveryContinuationSigningKey = "ef-r07-r09-registration-signing-key-32-bytes"
        });

        Assert.Equal(RuntimeActivityExecutionStoreBackend.EntityFramework, RuntimeActivityExecutionStoreBackend.Find(services)!.Name);
        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();
        Assert.IsType<EfActivityExecutionStateStore>(scope.ServiceProvider.GetRequiredService<IActivityExecutionStateStore>());
        Assert.IsType<EfActivityExecutionInspectionStore>(scope.ServiceProvider.GetRequiredService<IActivityExecutionInspectionStore>());
        Assert.IsType<EfActivityExecutionHierarchyStore>(scope.ServiceProvider.GetRequiredService<IActivityExecutionHierarchyStore>());
    }

    [Fact]
    public void Activity_execution_context_can_be_shared_with_bookmarks_and_artifacts_when_options_match()
    {
        var services = new ServiceCollection();
        services.AddWorkflowRuntime();
        var options = new RuntimeActivityExecutionEntityFrameworkCoreOptions { Provider = "Sqlite", ConnectionString = "Data Source=shared-runtime.db" };
        services.AddRuntimeActivityExecutionEntityFrameworkCore(options);
        services.AddRuntimeBookmarksEntityFrameworkCore(new RuntimeBookmarksEntityFrameworkCoreOptions { Provider = "sqlite", ConnectionString = options.ConnectionString });
        services.AddRuntimeArtifactsEntityFrameworkCore(new RuntimeArtifactsEntityFrameworkCoreOptions { Provider = "SQLite", ConnectionString = options.ConnectionString });

        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(RuntimeSqliteDbContext));
        Assert.Equal(RuntimeActivityExecutionStoreBackend.EntityFramework, RuntimeActivityExecutionStoreBackend.Find(services)!.Name);
        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();
        Assert.IsType<EfBookmarkStateStore>(scope.ServiceProvider.GetRequiredService<IBookmarkStateStore>());
        Assert.IsType<EfWorkflowExecutableStore>(scope.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>());
    }

    [Fact]
    public void Activity_execution_withdrawal_preserves_workflow_execution_shared_context()
    {
        const string connectionString = "Data Source=shared-runtime-workflow.db";
        var services = new ServiceCollection();
        services.AddWorkflowRuntime();
        services.AddRuntimeWorkflowExecutionEntityFrameworkCore(new RuntimeWorkflowExecutionEntityFrameworkCoreOptions
        {
            Provider = "Sqlite",
            ConnectionString = connectionString,
            RecoveryContinuationSigningKey = "ef-runtime-workflow-withdrawal-signing-key-32-bytes"
        });
        services.AddRuntimeActivityExecutionEntityFrameworkCore(new RuntimeActivityExecutionEntityFrameworkCoreOptions
        {
            Provider = "Sqlite",
            ConnectionString = connectionString,
            HierarchyCursorSigningKey = "ef-runtime-activity-withdrawal-hierarchy-key-32-bytes",
            RecoveryContinuationSigningKey = "ef-runtime-workflow-withdrawal-signing-key-32-bytes"
        });

        var activityBackend = Assert.IsType<RuntimeActivityExecutionStoreBackend>(RuntimeActivityExecutionStoreBackend.Find(services));
        activityBackend.RemoveOwnedArtifacts(services);

        Assert.Equal(WorkflowExecutionStateStoreBackend.EntityFramework, WorkflowExecutionStateStoreBackend.Find(services)!.Name);
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(RuntimeDbContext));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(RuntimeSqliteDbContext));
        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();
        Assert.IsType<EfWorkflowExecutionStateStore>(scope.ServiceProvider.GetRequiredService<IWorkflowExecutionStateStore>());
        Assert.IsType<RuntimeSqliteDbContext>(scope.ServiceProvider.GetRequiredService<RuntimeDbContext>());
    }

    [Fact]
    public void Activity_execution_ef_owns_only_ef_surfaces_in_every_registration_order()
    {
        var orders = new Action<ServiceCollection>[]
        {
            services =>
            {
                services.AddRuntimeActivityExecutionEntityFrameworkCore(ActivityEfOptions());
                services.AddWorkflowRuntime();
            },
            services =>
            {
                services.AddWorkflowRuntime();
                services.AddRuntimeActivityExecutionEntityFrameworkCore(ActivityEfOptions());
            },
            services =>
            {
                services.AddRuntimeActivityExecutionEntityFrameworkCore(ActivityEfOptions());
                services.AddRuntimeActivityExecutionEntityFrameworkCore(ActivityEfOptions());
            }
        };

        foreach (var configure in orders)
        {
            var services = new ServiceCollection();
            configure(services);

            var backend = Assert.IsType<RuntimeActivityExecutionStoreBackend>(Assert.Single(services, descriptor => descriptor.ImplementationInstance is RuntimeActivityExecutionStoreBackend).ImplementationInstance);
            Assert.Equal(RuntimeActivityExecutionStoreBackend.EntityFramework, backend.Name);
            backend.EnsureOwnsRegisteredContracts(services);
            Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(InMemoryActivityExecutionInspectionStore));
            Assert.DoesNotContain(services, descriptor => descriptor.ImplementationType == typeof(InMemoryActivityExecutionInspectionStore));
            Assert.DoesNotContain(services, descriptor => descriptor.ImplementationInstance?.GetType() == typeof(InMemoryActivityExecutionInspectionStore));
            Assert.Single(services, descriptor => descriptor.ServiceType == typeof(EfActivityExecutionInspectionStore));
        }
    }

    private static ActivityExecutionState State(string workflow, string id, long sequence, string? parent = null, string? scope = null) =>
        new(
            new ActivityExecution(id, workflow, $"node-{id}", $"authored-{id}", "Test.Activity", "1"),
            ActivityExecutionStatus.Completed,
            null,
            sequence,
            DateTimeOffset.UnixEpoch.AddSeconds(sequence),
            DateTimeOffset.UnixEpoch.AddSeconds(sequence),
            DateTimeOffset.UnixEpoch.AddSeconds(sequence),
            parent,
            parent,
            null,
            null,
            ActivitySchedulingProvenance.From(workflow, parent, parent, null, null, null, scope, "test"),
            null,
            [],
            [],
            0,
            0,
            new Dictionary<string, string>(),
            ExecutionScopeId: scope);

    private static ActivityExecutionInspectionProjection Projection(string workflow, string id, long sequence, string scope, string? parent = null, bool boundary = false) =>
        new(
            id,
            workflow,
            $"node-{id}",
            $"authored-{id}",
            "Test.Activity",
            "1",
            ActivityExecutionStatus.Completed,
            null,
            sequence,
            DateTimeOffset.UnixEpoch.AddSeconds(sequence),
            DateTimeOffset.UnixEpoch.AddSeconds(sequence),
            DateTimeOffset.UnixEpoch.AddSeconds(sequence),
            "checkpoint-1",
            "checkpoint-1",
            DateTimeOffset.UnixEpoch.AddSeconds(sequence),
            ActivitySchedulingProvenance.From(workflow, parent, parent, null, null, null, scope, "test"),
            ["Done"],
            [],
            [],
            [],
            boundary ? new Dictionary<string, string>
            {
                ["activity.definitionId"] = "definition",
                ["activity.definitionVersionId"] = "version",
                ["activity.version"] = "1",
                ["activity.templateHash"] = "template"
            } : new Dictionary<string, string>(),
            scope);

    private static ActivityExecutionHierarchyRecord HierarchyProjection(string workflow, string id, long sequence, string scope, string? parent, bool boundary = false) =>
        ActivityExecutionHierarchyProjector.FromInspection(Projection(workflow, id, sequence, scope, parent, boundary));

    private static RuntimeActivityExecutionEntityFrameworkCoreOptions ActivityEfOptions() => new()
    {
        Provider = "Sqlite",
        ConnectionString = "Data Source=:memory:",
        HierarchyCursorSigningKey = "ef-r07-r09-registration-hierarchy-key-32-bytes",
        RecoveryContinuationSigningKey = "ef-r07-r09-registration-signing-key-32-bytes"
    };

    private static IRuntimeRecoveryContinuationCodec RecoveryCodec() =>
        new HmacRuntimeRecoveryContinuationCodec(Microsoft.Extensions.Options.Options.Create(new RuntimeRecoveryContinuationOptions { SigningKey = "ef-r07-r09-race-recovery-signing-key-32-bytes" }));

    private static IActivityExecutionHierarchyCursorCodec HierarchyCodec() =>
        new HmacActivityExecutionHierarchyCursorCodec(Microsoft.Extensions.Options.Options.Create(new ActivityExecutionHierarchyCursorOptions { SigningKey = "ef-r07-r09-race-hierarchy-signing-key-32-bytes" }));

    private static async Task<Exception?[]> RunCreateRaceAsync(string path, string tableName, Func<RuntimeDbContext, Task> save)
    {
        var barrier = new ConcurrentCreateBarrier(tableName);
        await using var firstConnection = new SqliteConnection($"Data Source={path}");
        await using var secondConnection = new SqliteConnection($"Data Source={path}");
        await Task.WhenAll(firstConnection.OpenAsync(), secondConnection.OpenAsync());
        await using var firstContext = new RuntimeSqliteDbContext(new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(firstConnection).AddInterceptors(barrier).Options);
        await using var secondContext = new RuntimeSqliteDbContext(new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(secondConnection).AddInterceptors(barrier).Options);
        return await Task.WhenAll(CaptureAsync(() => save(firstContext)), CaptureAsync(() => save(secondContext)));
    }

    private static async Task AssertProviderFailuresAsync<TStore>(
        string path,
        string scope,
        string identity,
        Func<RuntimeDbContext, TStore> createStore,
        Func<TStore, Task> read,
        Func<RuntimeDbContext, Task> save)
    {
        await using (var readConnection = new SqliteConnection($"Data Source={path}"))
        {
            await readConnection.OpenAsync();
            await using var readContext = new RuntimeSqliteDbContext(new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(readConnection).AddInterceptors(new ThrowingReaderInterceptor()).Options);
            var exception = await Assert.ThrowsAsync<RuntimeActivityExecutionEntityFrameworkPersistenceException>(() => read(createStore(readContext)));
            Assert.Equal("reading", exception.Operation);
            Assert.False(string.IsNullOrWhiteSpace(exception.Identity));
        }

        await using (var saveConnection = new SqliteConnection($"Data Source={path}"))
        {
            await saveConnection.OpenAsync();
            await using var saveContext = new RuntimeSqliteDbContext(new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(saveConnection).AddInterceptors(new ThrowingSaveInterceptor()).Options);
            var exception = await Assert.ThrowsAsync<RuntimeActivityExecutionEntityFrameworkPersistenceException>(() => save(saveContext));
            Assert.Equal("saving", exception.Operation);
            Assert.Equal(identity == "hierarchy" ? "root" : "activity", exception.Identity);
        }
    }

    private static async Task<Exception?> CaptureAsync(Func<Task> operation)
    {
        try
        {
            await operation();
            return null;
        }
        catch (RuntimeActivityExecutionEntityFrameworkPersistenceException exception)
        {
            return exception;
        }
    }

    private static void AssertCreateRaceConflict(IReadOnlyCollection<Exception?> results, string identity)
    {
        Assert.Equal(2, results.Count);
        Assert.Single(results, exception => exception is null);
        var failure = Assert.Single(results, exception => exception is not null);
        var normalized = Assert.IsType<RuntimeActivityExecutionEntityFrameworkPersistenceException>(failure);
        Assert.Equal("saving", normalized.Operation);
        Assert.Equal(identity, normalized.Identity);
    }

    private sealed class Accessor(string scope) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new PersistenceScope(scope));
    }

    private sealed class ConcurrentCreateBarrier(string tableName) : DbCommandInterceptor
    {
        private readonly TaskCompletionSource bothReadersArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int arrivals;

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains(tableName, StringComparison.OrdinalIgnoreCase) &&
                command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            {
                if (Interlocked.Increment(ref arrivals) >= 2)
                    bothReadersArrived.TrySetResult();
                await bothReadersArrived.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            }
            return result;
        }
    }

    private sealed class InspectionPageRaceFixture : IAsyncDisposable
    {
        private readonly TemporarySqliteDatabase database;
        private readonly string connectionString;

        public string Scope { get; }
        public string WorkflowExecutionId { get; }

        private InspectionPageRaceFixture(TemporarySqliteDatabase database, string scope, string workflowExecutionId)
        {
            this.database = database;
            Scope = scope;
            WorkflowExecutionId = workflowExecutionId;
            connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = database.Path,
                DefaultTimeout = 1,
                Pooling = false
            }.ToString();
        }

        public static async Task<InspectionPageRaceFixture> CreateAsync(string scope, string workflowExecutionId)
        {
            var database = new TemporarySqliteDatabase("runtime-inspection-page-race");
            var fixture = new InspectionPageRaceFixture(database, scope, workflowExecutionId);
            await using var context = fixture.OpenContext();
            await context.Database.EnsureCreatedAsync();
            return fixture;
        }

        public RuntimeSqliteDbContext OpenContext(DbCommandInterceptor? interceptor = null)
        {
            var options = new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(connectionString);
            if (interceptor is not null)
                options.AddInterceptors(interceptor);
            return new RuntimeSqliteDbContext(options.Options);
        }

        public EfActivityExecutionInspectionStore CreateStore(RuntimeDbContext context) =>
            new(context, new Accessor(Scope), RecoveryCodec());

        public async Task InsertAsync(string activityExecutionId, long sequence, CancellationToken cancellationToken = default)
        {
            await using var context = OpenContext();
            await CreateStore(context).SaveAsync(Projection(WorkflowExecutionId, activityExecutionId, sequence, Scope), cancellationToken);
        }

        public async Task DeleteAsync(string activityExecutionId, CancellationToken cancellationToken = default)
        {
            await using var context = OpenContext();
            var row = await context.ActivityExecutionInspections.SingleAsync(candidate =>
                candidate.ScopeKey == EfRelationalIdentity.Encode(Scope) &&
                candidate.WorkflowExecutionId == EfRelationalIdentity.Encode(WorkflowExecutionId) &&
                candidate.ActivityExecutionId == EfRelationalIdentity.Encode(activityExecutionId), cancellationToken);
            context.ActivityExecutionInspections.Remove(row);
            await context.SaveChangesAsync(cancellationToken);
        }

        public async Task<ActivityExecutionInspectionSummaryPage> ReadPageAsync(int limit, string? continuationToken = null)
        {
            await using var context = OpenContext();
            return await CreateStore(context).ListSummariesPageAsync(
                new ActivityExecutionInspectionSummaryPageQuery(WorkflowExecutionId, limit, continuationToken));
        }

        public async Task<InterleavedInspectionPage> ReadPageWithMutationAsync(
            int limit,
            string? continuationToken,
            Func<CancellationToken, Task> mutation)
        {
            var interleaver = new InspectionPageMutationInterceptor(mutation);
            ActivityExecutionInspectionSummaryPage page;
            await using (var context = OpenContext(interleaver))
                page = await CreateStore(context).ListSummariesPageAsync(
                    new ActivityExecutionInspectionSummaryPageQuery(WorkflowExecutionId, limit, continuationToken));

            if (interleaver.Mutation.WasBlocked)
            {
                await mutation(CancellationToken.None);
                interleaver.Mutation.DeferredMutationCommitted = true;
            }

            return new InterleavedInspectionPage(page, interleaver.Mutation);
        }

        public ValueTask DisposeAsync() => database.DisposeAsync();
    }

    private sealed class InspectionPageMutationInterceptor(Func<CancellationToken, Task> mutation) : DbCommandInterceptor
    {
        private int attempted;
        public MutationAttempt Mutation { get; } = new();

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("elsa_runtime_activity_execution_inspection", StringComparison.OrdinalIgnoreCase) &&
                command.CommandText.Contains("ORDER BY", StringComparison.OrdinalIgnoreCase) &&
                Interlocked.Exchange(ref attempted, 1) == 0)
            {
                Mutation.Attempted = true;
                try
                {
                    await mutation(cancellationToken);
                    Mutation.InitialMutationCommitted = true;
                }
                catch (Exception exception) when (FindSqliteLockFailure(exception) is { } sqliteException)
                {
                    Mutation.SqliteErrorCode = sqliteException.SqliteErrorCode;
                    Mutation.SqliteExtendedErrorCode = sqliteException.SqliteExtendedErrorCode;
                    Mutation.LockFailureMessage = sqliteException.Message;
                    Mutation.WasBlocked = true;
                }
            }

            return result;
        }

        private static SqliteException? FindSqliteLockFailure(Exception exception)
        {
            for (var candidate = exception; candidate is not null; candidate = candidate.InnerException!)
            {
                if (candidate is SqliteException sqliteException && sqliteException.SqliteErrorCode is 5 or 6)
                    return sqliteException;
            }
            return null;
        }
    }

    private sealed record InterleavedInspectionPage(ActivityExecutionInspectionSummaryPage Page, MutationAttempt Mutation);

    private sealed class MutationAttempt
    {
        public bool Attempted { get; set; }
        public bool InitialMutationCommitted { get; set; }
        public bool WasBlocked { get; set; }
        public bool DeferredMutationCommitted { get; set; }
        public int? SqliteErrorCode { get; set; }
        public int? SqliteExtendedErrorCode { get; set; }
        public string? LockFailureMessage { get; set; }
    }

    private static string[] ActivityIds(ActivityExecutionInspectionSummaryPage page) =>
        page.Items.Select(item => item.ActivityExecutionId).ToArray();

    private static void AssertMutationWasApplied(MutationAttempt mutation)
    {
        Assert.True(mutation.Attempted);
        Assert.True(mutation.InitialMutationCommitted || mutation.WasBlocked);
        if (mutation.WasBlocked)
        {
            Assert.Contains(mutation.SqliteErrorCode, new int?[] { 5, 6 });
            Assert.False(string.IsNullOrWhiteSpace(mutation.LockFailureMessage));
            Assert.True(mutation.DeferredMutationCommitted);
        }
        else
        {
            Assert.Null(mutation.SqliteErrorCode);
            Assert.False(mutation.DeferredMutationCommitted);
        }
    }

    private sealed class ThrowingReaderInterceptor : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<InterceptionResult<DbDataReader>>(new SqliteException("provider read failure", 1, 1));
    }

    private sealed class ThrowingSaveInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<InterceptionResult<int>>(new DbUpdateException("provider save failure"));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private readonly RuntimeDbContext context;
        private readonly Accessor accessor;
        public EfActivityExecutionStateStore State { get; }
        public EfActivityExecutionInspectionStore Inspection { get; }
        public EfActivityExecutionHierarchyStore Hierarchy { get; }
        public RuntimeDbContext Context => context;
        public IActivityExecutionHierarchyCursorCodec HierarchyCursorCodec { get; }
        public IRuntimeRecoveryContinuationCodec RecoveryContinuationCodec { get; }

        private Fixture(SqliteConnection connection, RuntimeDbContext context, Accessor accessor)
        {
            this.connection = connection;
            this.context = context;
            this.accessor = accessor;
            RecoveryContinuationCodec = new HmacRuntimeRecoveryContinuationCodec(Microsoft.Extensions.Options.Options.Create(new RuntimeRecoveryContinuationOptions { SigningKey = "ef-r07-r09-test-signing-key-32-bytes" }));
            State = new EfActivityExecutionStateStore(context, accessor, RecoveryContinuationCodec);
            Inspection = new EfActivityExecutionInspectionStore(context, accessor, RecoveryContinuationCodec);
            HierarchyCursorCodec = new Elsa.Workflows.Runtime.Services.ActivityExecutions.HmacActivityExecutionHierarchyCursorCodec(Microsoft.Extensions.Options.Options.Create(new Elsa.Workflows.Runtime.Services.ActivityExecutions.ActivityExecutionHierarchyCursorOptions { SigningKey = "ef-r07-r09-test-hierarchy-signing-key-32-bytes" }));
            Hierarchy = new EfActivityExecutionHierarchyStore(context, accessor, HierarchyCursorCodec);
        }

        public static async Task<Fixture> CreateAsync(string scope)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var context = new RuntimeSqliteDbContext(new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(connection).Options);
            await context.Database.EnsureCreatedAsync();
            return new Fixture(connection, context, new Accessor(scope));
        }

        public async Task<Fixture> ReopenAsync(string scope)
        {
            await context.DisposeAsync();
            var reopened = new RuntimeSqliteDbContext(new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(connection).Options);
            return new Fixture(connection, reopened, new Accessor(scope));
        }

        public async ValueTask DisposeAsync()
        {
            await context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
