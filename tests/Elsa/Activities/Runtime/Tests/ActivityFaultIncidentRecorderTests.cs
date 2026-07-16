using Elsa.Activities.Runtime.Services;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

public sealed class ActivityFaultIncidentRecorderTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 9, 10, 0, 0, TimeSpan.Zero);
    private readonly CapturingCheckpointCommitStore _store = new();

    [Fact]
    public async Task CommitAsync_DoesNotCaptureStackTrace_ByDefault()
    {
        var recorder = new ActivityFaultIncidentRecorder(TimeProvider.System);

        await recorder.CommitAsync(NewRequest(CapturedException()));

        var incident = CapturedIncident();
        Assert.False(incident.Metadata.ContainsKey(RuntimeMetadataKeys.FaultStackTrace));
    }

    [Fact]
    public async Task CommitAsync_CapturesStackTrace_WhenPolicyEnablesIt()
    {
        var policy = new DefaultRuntimeFaultCapturePolicy(Options.Create(new RuntimeFaultCaptureOptions { CaptureStackTrace = true }));
        var recorder = new ActivityFaultIncidentRecorder(TimeProvider.System, inspectionAccumulator: null, policy);

        await recorder.CommitAsync(NewRequest(CapturedException()));

        var incident = CapturedIncident();
        Assert.Equal("System.InvalidOperationException", incident.Metadata[RuntimeMetadataKeys.FaultType]);
        Assert.Equal("boom", incident.Metadata[RuntimeMetadataKeys.FaultMessage]);
        Assert.False(string.IsNullOrWhiteSpace(incident.Metadata[RuntimeMetadataKeys.FaultStackTrace]));
    }

    [Fact]
    public async Task CommitAsync_EndsOpenAttemptAndPersistsNormalizedFault()
    {
        var recorder = new ActivityFaultIncidentRecorder(TimeProvider.System);
        var attempt = new ActivityAttempt(
            "attempt-1",
            "actexec-1",
            1,
            ActivityAttemptReason.Initial,
            Now);

        await recorder.CommitAsync(NewRequest(
            new InvalidOperationException("boom"),
            NewRunningState() with { Attempts = [attempt] }));

        var commit = Assert.IsType<RuntimeCheckpointCommit>(_store.Commit);
        var state = Assert.Single(commit.StateChanges.ActivityExecutions).State;
        Assert.Equal(ActivityExecutionStatus.Faulted, state.Status);
        Assert.Equal("InputMaterializationFailed", state.Fault!.Code);
        Assert.Equal(typeof(InvalidOperationException).FullName, state.Fault.ExceptionType);
        Assert.Equal("boom", state.Fault.Message);
        var endedAttempt = Assert.Single(state.Attempts!);
        Assert.NotNull(endedAttempt.EndedAt);
        Assert.Equal(ActivityTransitionKind.Fault, endedAttempt.TransitionKind);
        Assert.Equal(Assert.Single(state.IncidentIds), endedAttempt.IncidentId);
    }

    private ActivityFaultIncidentRecordRequest NewRequest(Exception exception, ActivityExecutionState? state = null)
    {
        var committer = new RuntimeCheckpointCommitter(new ImmediateRuntimeCheckpointPersistencePolicy(), _store);
        return new ActivityFaultIncidentRecordRequest(
            CheckpointCommitter: committer,
            WorkItem: NewWorkItem(),
            ActivityExecutionId: "actexec-1",
            ExecutableNodeId: "node-1",
            State: state ?? NewRunningState(),
            Exception: exception,
            SubStatus: "InputMaterializationFailed",
            ActivityMetadata: new Dictionary<string, string>(),
            IncidentMetadata: new Dictionary<string, string>());
    }

    private IncidentState CapturedIncident()
    {
        var commit = Assert.IsType<RuntimeCheckpointCommit>(_store.Commit);
        var change = Assert.Single(commit.StateChanges.Incidents);
        return change.State;
    }

    private static RuntimeSchedulerWorkItem NewWorkItem() =>
        new(
            workItemId: "work-1",
            workflowExecutionId: "wfexec-1",
            commandId: "command-1",
            commandKind: WorkflowExecutionCommandKind.InvokeActivity,
            envelopeId: "envelope-1",
            idempotencyKey: "wfexec-1:invoke:actexec-1",
            enqueuedAt: Now,
            recordedAt: Now,
            sequence: 1,
            payload: null,
            commandMetadata: new Dictionary<string, string>(),
            envelopeMetadata: new Dictionary<string, string>());

    private static ActivityExecutionState NewRunningState() =>
        new(
            Execution: new ActivityExecution(
                ActivityExecutionId: "actexec-1",
                WorkflowExecutionId: "wfexec-1",
                ExecutableNodeId: "node-1",
                AuthoredActivityId: "authored-node-1",
                ActivityType: "test/activity",
                ActivityTypeVersion: "1.0.0"),
            Status: ActivityExecutionStatus.Running,
            SubStatus: null,
            ScheduledAt: Now,
            StartedAt: Now,
            CompletedAt: null,
            SchedulingActivityExecutionId: null,
            ParentActivityExecutionId: null,
            BranchId: null,
            IterationId: null,
            CallStackDepth: null,
            BookmarkIds: [],
            IncidentIds: [],
            FaultCount: 0,
            AggregateFaultCount: 0,
            Metadata: new Dictionary<string, string>());

    private static Exception CapturedException()
    {
        try
        {
            throw new InvalidOperationException("boom");
        }
        catch (InvalidOperationException exception)
        {
            return exception;
        }
    }

    private sealed class CapturingCheckpointCommitStore : IRuntimeCheckpointCommitStore
    {
        public RuntimeCheckpointCommit? Commit { get; private set; }

        public ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(
            RuntimeCheckpointCommit commit,
            RuntimeCheckpointPersistenceDecision decision,
            CancellationToken cancellationToken = default)
        {
            Commit = commit;
            return ValueTask.FromResult(new RuntimeCheckpointCommitStoreResult([]));
        }
    }
}
