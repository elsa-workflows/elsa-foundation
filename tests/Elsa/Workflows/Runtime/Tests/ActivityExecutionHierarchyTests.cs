using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
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

        await Assert.ThrowsAsync<ActivityExecutionHierarchyCursorException>(async () =>
            await store.ReadPageAsync(Query(limit: 1, cursor: first!.NextCursor, tenantScope: "tenant:b")));
        var tampered = first!.NextCursor![..^1] + (first.NextCursor[^1] == 'A' ? "B" : "A");
        var exception = await Assert.ThrowsAsync<ActivityExecutionHierarchyCursorException>(async () =>
            await store.ReadPageAsync(Query(limit: 1, cursor: tampered)));
        Assert.Equal(ActivityExecutionHierarchyCursorFailure.Invalid, exception.Failure);
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

    private static RuntimeInMemoryActivityExecutionHierarchyStore Store() =>
        new(new HmacActivityExecutionHierarchyCursorCodec(Options.Create(new ActivityExecutionHierarchyCursorOptions
        {
            SigningKey = "test-signing-key-that-is-at-least-thirty-two-bytes"
        })));

    private static ActivityExecutionHierarchyRecord Record(ActivityExecutionInspectionProjection projection) =>
        ActivityExecutionHierarchyProjector.FromInspection(projection);

    private static ActivityExecutionHierarchyQuery Query(int limit, string? cursor = null, string tenantScope = "tenant:a") =>
        new("wf", "outer", cursor, limit, new HashSet<ActivityExecutionHierarchyInclude>
        {
            ActivityExecutionHierarchyInclude.Outcomes,
            ActivityExecutionHierarchyInclude.Bookmarks,
            ActivityExecutionHierarchyInclude.Incidents
        }, "structure", tenantScope);
}
