using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Api.Models;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public class ActivityExecutionHierarchyTests
{
    [Fact]
    public async Task Pages_TenThousand_Descendants_At_A_Fixed_Watermark_With_Iterative_Depths()
    {
        var store = Store();
        await store.SaveAsync(Record(ActivityExecutionInspectionProjectionTests.Projection("outer", "outer", null, 1, ActivityExecutionStatus.Running, boundary: true)));
        var parent = "outer";
        for (var index = 1; index <= 10_000; index++)
        {
            var id = $"child-{index:D5}";
            await store.SaveAsync(Record(ActivityExecutionInspectionProjectionTests.Projection(id, "outer", parent, index + 1, ActivityExecutionStatus.Completed)));
            parent = id;
        }

        var first = await store.ReadPageAsync(Query(limit: 257));
        Assert.NotNull(first);
        Assert.Equal(10_001, first.CommittedThroughSequence);
        Assert.Equal(257, first.Items.Count);
        Assert.Equal(257, first.Items[^1].RelativeDepth);

        await store.SaveAsync(Record(ActivityExecutionInspectionProjectionTests.Projection("late", "outer", "outer", 20_000, ActivityExecutionStatus.Completed)));
        var ids = new HashSet<string>(first.Items.Select(x => x.ActivityExecutionId), StringComparer.Ordinal);
        var page = first;
        while (page!.NextCursor is not null)
        {
            page = await store.ReadPageAsync(Query(limit: 257, cursor: page.NextCursor));
            Assert.NotNull(page);
            Assert.Equal(10_001, page.CommittedThroughSequence);
            Assert.All(page.Items, item => Assert.True(ids.Add(item.ActivityExecutionId)));
        }

        Assert.Equal(10_000, ids.Count);
        Assert.DoesNotContain("late", ids);
        var fresh = await store.ReadPageAsync(Query(limit: 500));
        Assert.Equal(20_000, fresh!.CommittedThroughSequence);
    }

    [Fact]
    public async Task Cursor_Is_Signed_And_Bound_To_Tenant_Root_Query_And_Permission_Profile()
    {
        var store = Store();
        await store.SaveAsync(Record(ActivityExecutionInspectionProjectionTests.Projection("outer", "outer", null, 1, ActivityExecutionStatus.Running, boundary: true)));
        await store.SaveAsync(Record(ActivityExecutionInspectionProjectionTests.Projection("one", "outer", "outer", 2, ActivityExecutionStatus.Completed)));
        await store.SaveAsync(Record(ActivityExecutionInspectionProjectionTests.Projection("two", "outer", "outer", 3, ActivityExecutionStatus.Completed)));
        var first = await store.ReadPageAsync(Query(limit: 1));
        var nextCursor = Assert.IsType<string>(first!.NextCursor);

        var mismatch = await Assert.ThrowsAsync<ActivityExecutionHierarchyCursorException>(async () =>
            await store.ReadPageAsync(Query(limit: 1, cursor: nextCursor, tenantScope: "tenant:b")));
        Assert.Equal(ActivityExecutionCursorBindingState.Mismatched, mismatch.Metadata!.AccessBinding);
        Assert.Equal(ActivityExecutionCursorBindingState.Matched, mismatch.Metadata.BoundaryBinding);
        Assert.True(mismatch.Metadata.Recoverable);
        var permissionChange = await Assert.ThrowsAsync<ActivityExecutionHierarchyCursorException>(async () =>
            await store.ReadPageAsync(Query(limit: 1, cursor: nextCursor, authorizationProfile: "structure+values")));
        Assert.Equal(ActivityExecutionCursorBindingState.Mismatched, permissionChange.Metadata!.AccessBinding);

        var freshAfterPermissionChange = await store.ReadPageAsync(Query(limit: 100, authorizationProfile: "structure+values"));
        Assert.Equal(2, freshAfterPermissionChange!.Items.Count);
        var tampered = nextCursor[..^1] + (nextCursor[^1] == 'A' ? "B" : "A");
        var exception = await Assert.ThrowsAsync<ActivityExecutionHierarchyCursorException>(async () =>
            await store.ReadPageAsync(Query(limit: 1, cursor: tampered)));
        Assert.Equal(ActivityExecutionHierarchyCursorFailure.Invalid, exception.Failure);
    }

    [Fact]
    public async Task Cursor_boundary_mismatch_identifies_only_the_boundary_binding()
    {
        var store = Store();
        await store.SaveAsync(Record(ActivityExecutionInspectionProjectionTests.Projection("outer", "outer", null, 1, ActivityExecutionStatus.Running, boundary: true)));
        await store.SaveAsync(Record(ActivityExecutionInspectionProjectionTests.Projection("outer-child", "outer", "outer", 2, ActivityExecutionStatus.Completed)));
        await store.SaveAsync(Record(ActivityExecutionInspectionProjectionTests.Projection("outer-child-2", "outer", "outer", 3, ActivityExecutionStatus.Completed)));
        await store.SaveAsync(Record(ActivityExecutionInspectionProjectionTests.Projection("other", "other", null, 4, ActivityExecutionStatus.Running, boundary: true)));
        await store.SaveAsync(Record(ActivityExecutionInspectionProjectionTests.Projection("other-child", "other", "other", 5, ActivityExecutionStatus.Completed)));
        var cursor = Assert.IsType<string>((await store.ReadPageAsync(Query(limit: 1)))!.NextCursor);

        var exception = await Assert.ThrowsAsync<ActivityExecutionHierarchyCursorException>(async () =>
            await store.ReadPageAsync(Query(limit: 1, cursor: cursor, rootActivityExecutionId: "other")));

        Assert.Equal(ActivityExecutionHierarchyCursorFailure.BindingMismatch, exception.Failure);
        Assert.Equal("activity-execution-hierarchy", exception.Metadata!.CursorClass);
        Assert.Equal(ActivityExecutionCursorBindingState.Mismatched, exception.Metadata.BoundaryBinding);
        Assert.Equal(ActivityExecutionCursorBindingState.Matched, exception.Metadata.QueryBinding);
        Assert.Equal(ActivityExecutionCursorBindingState.Matched, exception.Metadata.AccessBinding);
        Assert.True(exception.Metadata.Recoverable);
        Assert.Equal("restart-from-first-page", exception.Metadata.RecoveryAction);
    }

    [Fact]
    public async Task Cursor_query_mismatch_identifies_only_the_query_binding()
    {
        var store = Store();
        await store.SaveAsync(Record(ActivityExecutionInspectionProjectionTests.Projection("outer", "outer", null, 1, ActivityExecutionStatus.Running, boundary: true)));
        await store.SaveAsync(Record(ActivityExecutionInspectionProjectionTests.Projection("one", "outer", "outer", 2, ActivityExecutionStatus.Completed)));
        await store.SaveAsync(Record(ActivityExecutionInspectionProjectionTests.Projection("two", "outer", "outer", 3, ActivityExecutionStatus.Completed)));
        var cursor = Assert.IsType<string>((await store.ReadPageAsync(Query(limit: 1)))!.NextCursor);

        var exception = await Assert.ThrowsAsync<ActivityExecutionHierarchyCursorException>(async () =>
            await store.ReadPageAsync(Query(
                limit: 1,
                cursor: cursor,
                include: new HashSet<ActivityExecutionHierarchyInclude> { ActivityExecutionHierarchyInclude.Outcomes })));

        Assert.Equal(ActivityExecutionHierarchyCursorFailure.BindingMismatch, exception.Failure);
        Assert.Equal(ActivityExecutionCursorBindingState.Matched, exception.Metadata!.BoundaryBinding);
        Assert.Equal(ActivityExecutionCursorBindingState.Mismatched, exception.Metadata.QueryBinding);
        Assert.Equal(ActivityExecutionCursorBindingState.Matched, exception.Metadata.AccessBinding);
        Assert.True(exception.Metadata.Recoverable);
        Assert.Equal("restart-from-first-page", exception.Metadata.RecoveryAction);
    }

    [Fact]
    public async Task Expired_cursor_reports_matched_bindings_and_restart_metadata()
    {
        var original = Store();
        await original.SaveAsync(Record(ActivityExecutionInspectionProjectionTests.Projection("outer", "outer", null, 1, ActivityExecutionStatus.Running, boundary: true)));
        await original.SaveAsync(Record(ActivityExecutionInspectionProjectionTests.Projection("one", "outer", "outer", 2, ActivityExecutionStatus.Completed)));
        await original.SaveAsync(Record(ActivityExecutionInspectionProjectionTests.Projection("two", "outer", "outer", 3, ActivityExecutionStatus.Completed)));
        var cursor = Assert.IsType<string>((await original.ReadPageAsync(Query(limit: 1)))!.NextCursor);
        var trimmed = Store();
        await trimmed.SaveAsync(Record(ActivityExecutionInspectionProjectionTests.Projection("outer", "outer", null, 1, ActivityExecutionStatus.Running, boundary: true)));
        await trimmed.SaveAsync(Record(ActivityExecutionInspectionProjectionTests.Projection("one", "outer", "outer", 2, ActivityExecutionStatus.Completed)));

        var exception = await Assert.ThrowsAsync<ActivityExecutionHierarchyCursorException>(async () =>
            await trimmed.ReadPageAsync(Query(limit: 1, cursor: cursor)));

        Assert.Equal(ActivityExecutionHierarchyCursorFailure.Expired, exception.Failure);
        Assert.Equal("activity-execution-hierarchy", exception.Metadata!.CursorClass);
        Assert.Equal(ActivityExecutionCursorBindingState.Matched, exception.Metadata.BoundaryBinding);
        Assert.Equal(ActivityExecutionCursorBindingState.Matched, exception.Metadata.QueryBinding);
        Assert.Equal(ActivityExecutionCursorBindingState.Matched, exception.Metadata.AccessBinding);
        Assert.True(exception.Metadata.Recoverable);
        Assert.Equal("restart-from-first-page", exception.Metadata.RecoveryAction);
    }

    [Fact]
    public void Configured_Signing_Key_Produces_Restart_Stable_Cursors()
    {
        var options = Options.Create(new ActivityExecutionHierarchyCursorOptions
        {
            SigningKey = "shared-production-key-that-is-at-least-thirty-two-bytes"
        });
        var firstHost = new HmacActivityExecutionHierarchyCursorCodec(options);
        var secondHost = new HmacActivityExecutionHierarchyCursorCodec(options);
        var state = new ActivityExecutionHierarchyCursorState(
            "tenant:a", "structure", "wf", "outer", [], 100, 42, 12, "child");

        var decoded = secondHost.Decode(firstHost.Encode(state));
        Assert.Equal(state.TenantScope, decoded.TenantScope);
        Assert.Equal(state.CommittedThroughSequence, decoded.CommittedThroughSequence);
        Assert.Equal(state.LastActivityExecutionId, decoded.LastActivityExecutionId);
    }

    [Fact]
    public async Task Nested_Boundary_Is_Compact_In_Parent_And_Expands_Through_Its_Own_Scope()
    {
        var store = Store();
        await store.SaveAsync(Record(ActivityExecutionInspectionProjectionTests.Projection("outer", "outer", null, 1, ActivityExecutionStatus.Completed, boundary: true)));
        await store.SaveAsync(Record(ActivityExecutionInspectionProjectionTests.Projection("nested", "outer", "outer", 2, ActivityExecutionStatus.Completed, boundary: true)));
        await store.SaveAsync(Record(ActivityExecutionInspectionProjectionTests.Projection("nested-child", "nested", "nested", 3, ActivityExecutionStatus.Completed)));

        var outerPage = await store.ReadPageAsync(Query(limit: 100));
        var nested = Assert.Single(outerPage!.Items);
        Assert.NotNull(nested.Boundary);
        Assert.Equal(1, nested.Boundary!.CommittedDescendantCount);
        Assert.DoesNotContain(outerPage.Items, x => x.ActivityExecutionId == "nested-child");

        var nestedPage = await store.ReadPageAsync(new("wf", "nested", null, 100, new HashSet<ActivityExecutionHierarchyInclude>(), "structure", "tenant:a"));
        Assert.Equal("nested-child", Assert.Single(nestedPage!.Items).ActivityExecutionId);
    }

    [Fact]
    public async Task Attempt_navigation_exposes_authoritative_previous_and_next_executions()
    {
        var store = Store();
        var first = ActivityExecutionInspectionProjectionTests.Projection("attempt-1", "attempt-1", null, 1, ActivityExecutionStatus.Faulted, boundary: true) with
        {
            Attempt = new(1, "attempt-1", null)
        };
        var second = ActivityExecutionInspectionProjectionTests.Projection("attempt-2", "attempt-2", null, 2, ActivityExecutionStatus.Faulted, boundary: true) with
        {
            Attempt = new(2, "attempt-1", "attempt-1")
        };
        var third = ActivityExecutionInspectionProjectionTests.Projection("attempt-3", "attempt-3", null, 3, ActivityExecutionStatus.Completed, boundary: true) with
        {
            Attempt = new(3, "attempt-1", "attempt-2")
        };
        await store.SaveAsync(Record(first));
        await store.SaveAsync(Record(second));
        await store.SaveAsync(Record(third));

        var navigation = await store.FindAttemptNavigationAsync("wf", "attempt-2");

        Assert.NotNull(navigation);
        Assert.Equal("attempt-1", navigation!.Lineage.PreviousAttemptActivityExecutionId);
        Assert.Equal("attempt-3", navigation.NextAttemptActivityExecutionId);
        Assert.Equal(3, navigation.TotalAttempts);
    }

    [Fact]
    public async Task Loop_iterations_of_the_same_pinned_node_remain_distinct_across_bounded_pages()
    {
        var store = Store();
        await store.SaveAsync(Record(ActivityExecutionInspectionProjectionTests.Projection(
            "outer",
            "outer",
            null,
            1,
            ActivityExecutionStatus.Running,
            boundary: true)));
        var firstIteration = ActivityExecutionInspectionProjectionTests.Projection(
            "loop-1",
            "outer",
            "outer",
            2,
            ActivityExecutionStatus.Completed) with
        {
            ExecutableNodeId = "node-loop",
            Provenance = ActivitySchedulingProvenance.From("wf", "outer", "outer", null, "loop:1", "path:loop", "outer", "loop")
        };
        var secondIteration = ActivityExecutionInspectionProjectionTests.Projection(
            "loop-2",
            "outer",
            "outer",
            3,
            ActivityExecutionStatus.Completed) with
        {
            ExecutableNodeId = "node-loop",
            Provenance = ActivitySchedulingProvenance.From("wf", "outer", "outer", null, "loop:2", "path:loop", "outer", "loop")
        };
        await store.SaveAsync(Record(firstIteration));
        await store.SaveAsync(Record(secondIteration));

        var firstPage = await store.ReadPageAsync(Query(limit: 1));
        var secondPage = await store.ReadPageAsync(Query(limit: 1, cursor: firstPage!.NextCursor));

        Assert.Equal("loop:1", Assert.Single(firstPage.Items).IterationId);
        Assert.Equal("loop:2", Assert.Single(secondPage!.Items).IterationId);
        Assert.All(firstPage.Items.Concat(secondPage.Items), item => Assert.Equal("node-loop", item.ExecutableNodeId));
    }

    [Fact]
    public async Task Hierarchy_View_Never_Exposes_Execution_Metadata()
    {
        var store = Store();
        await store.SaveAsync(Record(ActivityExecutionInspectionProjectionTests.Projection("outer", "outer", null, 1, ActivityExecutionStatus.Running, boundary: true)));
        var child = ActivityExecutionInspectionProjectionTests.Projection("child", "outer", "outer", 2, ActivityExecutionStatus.Completed) with
        {
            Metadata = new Dictionary<string, string> { ["runtime.scopedVariableValues"] = "secret" }
        };
        await store.SaveAsync(Record(child));

        var page = await store.ReadPageAsync(Query(limit: 100));
        var view = ActivityExecutionHierarchyPageView.From(page!);

        Assert.Empty(Assert.Single(view.Items).Metadata);
    }

    private static RuntimeInMemoryActivityExecutionHierarchyStore Store() =>
        new(new HmacActivityExecutionHierarchyCursorCodec(Options.Create(new ActivityExecutionHierarchyCursorOptions
        {
            SigningKey = "test-signing-key-that-is-at-least-thirty-two-bytes"
        })));

    private static ActivityExecutionHierarchyRecord Record(ActivityExecutionInspectionProjection projection) =>
        ActivityExecutionHierarchyProjector.FromInspection(projection);

    private static ActivityExecutionHierarchyQuery Query(
        int limit,
        string? cursor = null,
        string tenantScope = "tenant:a",
        string authorizationProfile = "structure",
        string rootActivityExecutionId = "outer",
        IReadOnlySet<ActivityExecutionHierarchyInclude>? include = null) =>
        new(
            "wf",
            rootActivityExecutionId,
            cursor,
            limit,
            include ?? new HashSet<ActivityExecutionHierarchyInclude>
            {
                ActivityExecutionHierarchyInclude.Outcomes,
                ActivityExecutionHierarchyInclude.Bookmarks,
                ActivityExecutionHierarchyInclude.Incidents
            },
            authorizationProfile,
            tenantScope);
}
