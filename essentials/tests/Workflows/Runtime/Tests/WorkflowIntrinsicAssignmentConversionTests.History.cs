using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed partial class WorkflowIntrinsicAssignmentConversionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Set_preserves_write_scope_and_replay_identity_under_the_retained_history_policy(bool captureHistory)
    {
        var commit = await ExecuteAsync(Node("request"), 7, external: false, captureHistory: captureHistory);
        var activity = Assert.Single(commit.StateChanges.ActivityExecutions).State;
        var write = Assert.Single(activity.VariableWrites!);
        var inspection = Assert.Single(commit.StateChanges.ActivityExecutionInspections).State;
        var snapshot = Assert.Single(inspection.ValueSnapshots);

        Assert.Equal(ActivityExecutionInspectionValueSubject.VariableWrite, snapshot.Subject);
        Assert.Equal("counter", snapshot.Name);
        Assert.Equal(write.ScopeId, snapshot.Phase);
        Assert.Equal(write.NewVersion, snapshot.Sequence);
        Assert.Equal(7, commit.StateChanges.WorkflowExecution!.State.RootVariableFrame!.Values["counter"].InlineValue!.Value.GetInt32());
        if (captureHistory)
        {
            Assert.NotNull(inspection.CheckpointValueCapture);
            Assert.Equal("start-intrinsic", inspection.CheckpointValueCapture.SchedulerWorkItemId);
            Assert.False(string.IsNullOrWhiteSpace(snapshot.EvidenceId));
            Assert.Equal(snapshot.EvidenceId, Assert.Single(inspection.CheckpointValueCapture.EvidenceIds));
        }
        else
        {
            Assert.Null(inspection.CheckpointValueCapture);
            Assert.Null(snapshot.EvidenceId);
        }

        await _inspectionStore.SaveAsync(inspection);
        var replay = await ExecuteAsync(Node("request"), 7, external: false, captureHistory: captureHistory);
        var replayInspection = Assert.Single(replay.StateChanges.ActivityExecutionInspections).State;
        Assert.Equal(snapshot.EvidenceId, Assert.Single(replayInspection.ValueSnapshots).EvidenceId);
        Assert.Equal(inspection.CheckpointValueCapture?.EvidenceIds, replayInspection.CheckpointValueCapture?.EvidenceIds);
    }
}
