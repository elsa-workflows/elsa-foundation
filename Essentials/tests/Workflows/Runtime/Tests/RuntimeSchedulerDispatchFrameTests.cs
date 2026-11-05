using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

// #3275: the frame is the drainer's only evidence that a faulted dispatch sent no non-retaining checkpoint to the
// store, so an attempt must reach exactly the dispatches it happened during.
public sealed class RuntimeSchedulerDispatchFrameTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void An_attempt_marks_every_open_frame_for_its_execution_and_no_other()
    {
        using var outer = RuntimeSchedulerDispatchFrame.Open("wfexec-1");
        using (var inner = RuntimeSchedulerDispatchFrame.Open("wfexec-1"))
        {
            // An inline dispatch of another execution, such as a nested drain of a child.
            var other = RuntimeSchedulerDispatchFrame.Open("wfexec-2");
            RuntimeSchedulerDispatchFrame.RecordCheckpointAttempt(Commit("wfexec-2"));
            other.Dispose();
            Assert.True(other.NonRetainingCheckpointAttempted);
            Assert.False(inner.NonRetainingCheckpointAttempted);
            Assert.False(outer.NonRetainingCheckpointAttempted);

            // The same execution dispatched inline counts against the dispatch it ran inside.
            RuntimeSchedulerDispatchFrame.RecordCheckpointAttempt(Commit("wfexec-1"));
            Assert.True(inner.NonRetainingCheckpointAttempted);
            Assert.True(outer.NonRetainingCheckpointAttempted);
        }
    }

    [Fact]
    public void A_closed_frame_is_not_marked_by_a_later_attempt_on_its_flow()
    {
        var first = RuntimeSchedulerDispatchFrame.Open("wfexec-1");
        first.Dispose();

        RuntimeSchedulerDispatchFrame.RecordCheckpointAttempt(Commit("wfexec-1"));

        Assert.False(first.NonRetainingCheckpointAttempted);
    }

    [Fact]
    public void A_retaining_attempt_is_not_recorded()
    {
        using var frame = RuntimeSchedulerDispatchFrame.Open("wfexec-1");

        RuntimeSchedulerDispatchFrame.RecordCheckpointAttempt(Commit("wfexec-1") with { RetainSchedulerWork = true });

        Assert.False(frame.NonRetainingCheckpointAttempted);
    }

    [Fact]
    public void Withdrawing_a_refused_attempt_takes_back_that_attempt_alone()
    {
        // #3683: a store's unavailable refusal is withdrawn, but every other attempt on the same frames still counts.
        using var outer = RuntimeSchedulerDispatchFrame.Open("wfexec-1");
        using var inner = RuntimeSchedulerDispatchFrame.Open("wfexec-1");

        RuntimeSchedulerDispatchFrame.RecordCheckpointAttempt(Commit("wfexec-1") with { RetainSchedulerWork = true }).Withdraw();
        var refused = RuntimeSchedulerDispatchFrame.RecordCheckpointAttempt(Commit("wfexec-1"));
        refused.Withdraw();
        Assert.False(inner.NonRetainingCheckpointAttempted);
        Assert.False(outer.NonRetainingCheckpointAttempted);

        RuntimeSchedulerDispatchFrame.RecordCheckpointAttempt(Commit("wfexec-1"));
        RuntimeSchedulerDispatchFrame.RecordCheckpointAttempt(Commit("wfexec-1")).Withdraw();
        Assert.True(inner.NonRetainingCheckpointAttempted);
        Assert.True(outer.NonRetainingCheckpointAttempted);
    }

    private static RuntimeCheckpointCommit Commit(string workflowExecutionId) =>
        new(
            $"commit-{workflowExecutionId}",
            new RuntimeCheckpoint($"checkpoint-{workflowExecutionId}", RuntimeCheckpointNames.ActivitySuspended, workflowExecutionId, Now, [],
                new Dictionary<string, string>()),
            new RuntimeCheckpointStateChangeSet(null, null, [], [], [], [], []),
            [],
            new Dictionary<string, string>());
}
