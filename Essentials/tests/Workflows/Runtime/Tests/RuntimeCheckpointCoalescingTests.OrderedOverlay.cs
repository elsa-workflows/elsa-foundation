using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed partial class RuntimeCheckpointCoalescingTests
{
    [Fact]
    public async Task Large_overlay_page_has_a_linear_allocation_budget()
    {
        var (_, session, store) = CreateCountingActivityExecutionStateStore();
        var states = Enumerable.Range(0, 1024).Select(i => Activity($"activity-{i:D4}")).Reverse().ToArray();
        session.BufferDeferred(NewEmptyCommit("wfexec-1", 201, "large-overlay") with { StateChanges = ActivityUpsert(states) });
        var query = new ActivityExecutionStatePageQuery("wfexec-1", limit: 500);

        var before = GC.GetAllocatedBytesForCurrentThread();
        var read = store.ListPageAsync(query);
        Assert.True(read.IsCompletedSuccessfully); // Counting provider is synchronous; no thread migration in this budget.
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        var page = await read;

        Assert.Equal(states.Reverse().Take(500).Select(x => x.Execution.ActivityExecutionId), page.Items.Select(x => x.Execution.ActivityExecutionId));
        Assert.NotNull(page.NextContinuationToken);
        // A generous structural budget: cloning 1,024 rows for each of 500 emitted rows alone exceeds 4 MiB.
        Assert.InRange(allocated, 0, 2 * 1024 * 1024);
    }

    [Fact]
    public async Task Overlay_order_and_parent_selection_refresh_after_replacement_deletion_and_cap_flush()
    {
        var (inner, session, store) = CreateCountingActivityExecutionStateStore();
        var a = Activity("a") with { ParentActivityExecutionId = "parent-a" };
        var b = Activity("b") with { ParentActivityExecutionId = "parent-b" };
        session.BufferDeferred(NewEmptyCommit("wfexec-1", 202, "first") with { StateChanges = ActivityUpsert(a, b) });
        Assert.Equal(["a", "b"], (await store.ListPageAsync(new("wfexec-1"))).Items.Select(x => x.InvocationId));
        Assert.Equal("a", Assert.Single((await store.ListByParentPageAsync(new("wfexec-1", "parent-a"))).Items).InvocationId);

        var moved = a with { ParentActivityExecutionId = "parent-b", Status = ActivityExecutionStatus.Completed };
        session.RecordCapFlushState(ActivityChanges(ActivityChange(moved), ActivityDelete("b"), ActivityChange("c")));
        session.ClearBuffer();
        await inner.SaveAsync(Activity("d")); // A new durable row must remain visible; only overlay preparation is reused.

        var page = await store.ListPageAsync(new("wfexec-1"));
        Assert.Equal(["a", "c", "d"], page.Items.Select(x => x.InvocationId));
        Assert.Same(moved, page.Items[0]);
        Assert.Empty((await store.ListByParentPageAsync(new("wfexec-1", "parent-a"))).Items);
        Assert.Same(moved, Assert.Single((await store.ListByParentPageAsync(new("wfexec-1", "parent-b"))).Items));

        session.RecordDurableBoundaryState(ActivityChanges(ActivityDelete("a"), ActivityChange(b)));
        Assert.Equal("b", Assert.Single((await store.ListByParentPageAsync(new("wfexec-1", "parent-b"))).Items).InvocationId);
    }
}
