using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services.Coalescing;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

// Unit coverage for the coalescing persistence policy's decision mapping (W9, E3-6/RT-10).
public sealed class RuntimeCheckpointCoalescingPolicyTests
{
    private readonly CoalescingRuntimeCheckpointPersistencePolicy _policy = new();

    [Theory]
    [InlineData(RuntimeCheckpointNames.WorkflowSuspended)]
    [InlineData(RuntimeCheckpointNames.WorkflowCompleted)]
    [InlineData(RuntimeCheckpointNames.WorkflowFaulted)]
    [InlineData(RuntimeCheckpointNames.WorkflowCancelled)]
    [InlineData(RuntimeCheckpointNames.IncidentRecorded)]
    [InlineData(RuntimeCheckpointNames.ActivitySuspended)]
    [InlineData(RuntimeCheckpointNames.ActivityCancelled)]
    [InlineData(RuntimeCheckpointNames.BookmarkCreated)]
    public async Task MandatoryBoundaryCheckpoints_FlushImmediately(string checkpointName)
    {
        var decision = await _policy.DecideAsync(NewCheckpoint(checkpointName));
        Assert.Equal(RuntimeCheckpointPersistenceMode.Immediate, decision.Mode);
    }

    [Theory]
    [InlineData(RuntimeCheckpointNames.WorkflowStarted)]
    [InlineData(RuntimeCheckpointNames.ActivityScheduled)]
    [InlineData(RuntimeCheckpointNames.ActivityStarted)]
    [InlineData(RuntimeCheckpointNames.ActivityCompleted)]
    [InlineData(RuntimeCheckpointNames.ActivityRecovered)]
    [InlineData(RuntimeCheckpointNames.DurableValueCaptured)]
    [InlineData(RuntimeCheckpointNames.BookmarkConsumed)]
    [InlineData(RuntimeCheckpointNames.PostCommitIntentRecorded)]
    public async Task NonBoundaryCheckpoints_AreDeferred(string checkpointName)
    {
        var decision = await _policy.DecideAsync(NewCheckpoint(checkpointName));
        Assert.Equal(RuntimeCheckpointPersistenceMode.Deferred, decision.Mode);
    }

    [Fact]
    public async Task CoalescedFlushMarker_ForcesImmediate_EvenForOtherwiseDeferrableName()
    {
        var checkpoint = NewCheckpoint(
            RuntimeCheckpointNames.ActivityCompleted,
            new Dictionary<string, string> { [RuntimeCoalescingMetadataKeys.CoalescedFlush] = "true" });

        var decision = await _policy.DecideAsync(checkpoint);

        Assert.Equal(RuntimeCheckpointPersistenceMode.Immediate, decision.Mode);
    }

    private static RuntimeCheckpoint NewCheckpoint(string name, IReadOnlyDictionary<string, string>? metadata = null) =>
        new(
            CheckpointId: "checkpoint-1",
            Name: name,
            WorkflowExecutionId: "wfexec-1",
            OccurredAt: new DateTimeOffset(2026, 7, 3, 12, 0, 0, TimeSpan.Zero),
            ActivityExecutionIds: [],
            Metadata: metadata ?? new Dictionary<string, string>());
}
