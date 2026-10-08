using System.Diagnostics;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Diagnostics;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Diagnostics;

namespace Elsa.Activities.Http.IntegrationTests;

public sealed class ResponseReplayObservationTests
{
    [Fact]
    public async Task EndCaptureAsync_DrainsAdmittedCommandAndCheckpointCallbacks_AndExcludesLateStarts()
    {
        var observation = CreateObservation();
        var carryInCommand = Guid.NewGuid();
        var lateCarryInCommand = Guid.NewGuid();
        observation.RecordCommandStart(carryInCommand, activity: null, "Reader|CarryIn");
        observation.RecordCommandStart(lateCarryInCommand, activity: null, "Reader|LateCarryIn");
        observation.BeginCapture();
        observation.RecordCommandOutcome(carryInCommand, ResponseReplayAttemptOutcome.Succeeded);

        using var requestActivity = StartRequestActivity();

        var claim = CreateCommit(
            "execution-target",
            "http-in",
            RuntimeCheckpointNames.ActivityAttemptClaimed,
            carriesClaimMarker: true);
        var logicalClaim = observation.BeginLogicalCheckpoint(
            claim,
            new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Deferred));
        var logicalResponse = observation.BeginLogicalCheckpoint(
            CreateCommit(
                "execution-target",
                "write-response",
                RuntimeCheckpointNames.ActivityCompleted,
                carriesClaimMarker: true),
            new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Deferred));
        var durableResponse = observation.BeginDurableCheckpoint(
            CreateCommit(
                "execution-target",
                "write-response",
                RuntimeCheckpointNames.ActivityCompleted,
                carriesClaimMarker: true),
            new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Deferred),
            hasBufferedSegment: true);

        var duplicateCommandId = Guid.NewGuid();
        observation.RecordCommandStart(duplicateCommandId, Activity.Current, "Reader|First");
        observation.RecordCommandStart(duplicateCommandId, Activity.Current, "Reader|Second");
        var canceledCommandId = Guid.NewGuid();
        observation.RecordCommandStart(canceledCommandId, Activity.Current, "Reader|Canceled");

        var finalizationTask = observation.EndCaptureAsync(TimeSpan.FromSeconds(30));
        Assert.False(finalizationTask.IsCompleted);

        observation.RecordCommandOutcome(lateCarryInCommand, ResponseReplayAttemptOutcome.Succeeded);
        var lateCommandId = Guid.NewGuid();
        observation.RecordCommandStart(lateCommandId, Activity.Current, "Reader|Late");
        Assert.Null(observation.BeginLogicalCheckpoint(
            claim,
            new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Deferred)));

        observation.RecordCommandOutcome(duplicateCommandId, ResponseReplayAttemptOutcome.Succeeded);
        observation.RecordCommandOutcome(duplicateCommandId, ResponseReplayAttemptOutcome.Failed);
        observation.RecordCommandOutcome(canceledCommandId, ResponseReplayAttemptOutcome.Canceled);
        observation.CompleteLogicalCheckpoint(logicalClaim, ResponseReplayAttemptOutcome.Succeeded);
        observation.CompleteLogicalCheckpoint(logicalResponse, ResponseReplayAttemptOutcome.Failed);
        observation.CompleteDurableCheckpoint(durableResponse, ResponseReplayAttemptOutcome.Canceled);

        var finalization = await finalizationTask;
        var stopped = finalization.CaptureStoppedObservation;
        var drained = finalization.CallbackDrainedObservation;
        var request = drained.Buckets["requestDrain"];

        Assert.Equal(3, stopped.InFlightCommandAttempts);
        Assert.Equal(1, stopped.CarryInCommandOutcomes);
        Assert.Equal(0, stopped.OrphanCommandOutcomes);
        Assert.Equal(2, stopped.Buckets["requestDrain"].LogicalCheckpointAttempts);
        Assert.Equal(1, stopped.Buckets["requestDrain"].DurableCheckpointAttempts);
        Assert.Equal(0, stopped.Buckets["requestDrain"].LogicalCheckpointSucceeded);
        Assert.Equal(0, stopped.Buckets["requestDrain"].LogicalCheckpointFailed);
        Assert.Equal(0, stopped.Buckets["requestDrain"].DurableCheckpointCanceled);
        Assert.Equal(0, stopped.Buckets["requestDrain"].Commands["Reader|First"].Succeeded);

        Assert.Equal(0, drained.InFlightCommandAttempts);
        Assert.Equal(1, drained.CarryInCommandOutcomes);
        Assert.Equal(0, drained.OrphanCommandOutcomes);
        Assert.Equal(stopped.Buckets["requestDrain"].LogicalCheckpointAttempts,
            drained.Buckets["requestDrain"].LogicalCheckpointAttempts);
        Assert.Equal(stopped.Buckets["requestDrain"].DurableCheckpointAttempts,
            drained.Buckets["requestDrain"].DurableCheckpointAttempts);
        Assert.Equal(1, request.LogicalCheckpointSucceeded);
        Assert.Equal(1, request.LogicalCheckpointFailed);
        Assert.Equal(1, request.DurableCheckpointCanceled);
        Assert.Equal(1, request.SegmentFlushAttempts);
        Assert.Equal(0, request.SegmentFlushes);
        Assert.Equal(1, request.Commands["Reader|First"].Succeeded);
        Assert.Equal(1, request.Commands["Reader|Second"].Failed);
        Assert.Equal(1, request.Commands["Reader|Canceled"].Canceled);

        // Completion callbacks and late starts cannot mutate either immutable snapshot after finalization.
        observation.RecordCommandStart(lateCommandId, Activity.Current, "Reader|AfterDrain");
        observation.RecordCommandOutcome(lateCommandId, ResponseReplayAttemptOutcome.Succeeded);
        Assert.Equal(3, stopped.InFlightCommandAttempts);
        Assert.Equal(0, stopped.Buckets["requestDrain"].Commands["Reader|First"].Succeeded);
        Assert.Equal(0, drained.InFlightCommandAttempts);
        Assert.False(drained.Buckets["requestDrain"].Commands.ContainsKey("Reader|AfterDrain"));
    }

    [Fact]
    public async Task EndCaptureAsync_ReportsTimeoutWithoutReturningPartialSnapshot()
    {
        var observation = CreateObservation();
        observation.BeginCapture();
        using var requestActivity = StartRequestActivity();
        var attempt = observation.BeginLogicalCheckpoint(
            CreateCommit(
                "execution-target",
                "http-in",
                RuntimeCheckpointNames.ActivityAttemptClaimed,
                carriesClaimMarker: true),
            new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Deferred));

        await Assert.ThrowsAsync<TimeoutException>(() => observation.EndCaptureAsync(TimeSpan.FromMilliseconds(1)));
        Assert.Null(observation.BeginLogicalCheckpoint(
            CreateCommit(
                "execution-target",
                "write-response",
                RuntimeCheckpointNames.ActivityCompleted,
                carriesClaimMarker: true),
            new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Deferred)));
        observation.CompleteLogicalCheckpoint(attempt, ResponseReplayAttemptOutcome.Succeeded);
    }

    [Fact]
    public async Task EndCaptureAsync_ReportsCancellationWithoutReturningPartialSnapshot()
    {
        var observation = CreateObservation();
        observation.BeginCapture();
        using var requestActivity = StartRequestActivity();
        var attempt = observation.BeginLogicalCheckpoint(
            CreateCommit(
                "execution-target",
                "http-in",
                RuntimeCheckpointNames.ActivityAttemptClaimed,
                carriesClaimMarker: true),
            new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Deferred));
        using var cancellation = new CancellationTokenSource();
        var finalization = observation.EndCaptureAsync(Timeout.InfiniteTimeSpan, cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => finalization);
        observation.CompleteLogicalCheckpoint(attempt, ResponseReplayAttemptOutcome.Canceled);
    }

    [Fact]
    public async Task Observation_SeparatesRequestBackgroundAndUnattributedAttempts()
    {
        const string workflowExecutionId = "execution-target";
        const string endpointNodeId = "http-in";
        const string correlationId = "request-correlation";
        var observation = CreateObservation();

        var carryInCommand = Guid.NewGuid();
        observation.RecordCommandStart(carryInCommand, activity: null, "Reader|Startup");
        observation.BeginCapture();
        observation.RecordCommandOutcome(carryInCommand, ResponseReplayAttemptOutcome.Succeeded);
        ResponseReplayObservationSnapshot responseReceivedSnapshot;

        using (var requestActivity = new Activity("measured-http-request"))
        {
            requestActivity.SetTag(ResponseReplayObservation.RequestCorrelationActivityTag, correlationId);
            requestActivity.Start();

            var claim = CreateCommit(
                workflowExecutionId,
                endpointNodeId,
                RuntimeCheckpointNames.ActivityAttemptClaimed,
                carriesClaimMarker: true);
            var logicalClaim = observation.BeginLogicalCheckpoint(
                claim,
                new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Deferred));
            observation.CompleteLogicalCheckpoint(logicalClaim, ResponseReplayAttemptOutcome.Succeeded);

            var requestCommand = Guid.NewGuid();
            observation.RecordCommandStart(requestCommand, Activity.Current, "Reader|ExecuteReader");
            observation.RecordCommandOutcome(requestCommand, ResponseReplayAttemptOutcome.Succeeded);
            responseReceivedSnapshot = observation.CaptureSnapshot();

            // Later writes may retain the attempt marker. They are not another claim boundary unless the named
            // checkpoint and its matching commit identity identify ActivityAttemptClaimed.
            var completion = CreateCommit(
                workflowExecutionId,
                "write-response",
                RuntimeCheckpointNames.ActivityCompleted,
                carriesClaimMarker: true);
            Assert.False(completion.Metadata.ContainsKey(RuntimeMetadataKeys.CheckpointSideEffectProfile));
            var logicalCompletion = observation.BeginLogicalCheckpoint(
                completion,
                new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Deferred));
            observation.CompleteLogicalCheckpoint(logicalCompletion, ResponseReplayAttemptOutcome.Succeeded);

            var failedDurableClaim = observation.BeginDurableCheckpoint(
                claim,
                new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate),
                hasBufferedSegment: true);
            observation.CompleteDurableCheckpoint(failedDurableClaim, ResponseReplayAttemptOutcome.Failed);
        }

        using (var targetBackground = new Activity("target-background-resumption"))
        {
            targetBackground.SetTag(WorkflowEngineTelemetry.WorkflowExecutionIdTag, workflowExecutionId);
            targetBackground.Start();
            var backgroundCommand = Guid.NewGuid();
            observation.RecordCommandStart(backgroundCommand, Activity.Current, "NonQuery|ExecuteNonQuery");
            observation.RecordCommandOutcome(backgroundCommand, ResponseReplayAttemptOutcome.Failed);
        }

        using (var unrelatedExecution = new Activity("unrelated-execution"))
        {
            unrelatedExecution.SetTag(WorkflowEngineTelemetry.WorkflowExecutionIdTag, "execution-other");
            unrelatedExecution.Start();
            var unrelatedCommand = Guid.NewGuid();
            observation.RecordCommandStart(unrelatedCommand, Activity.Current, "Scalar|ExecuteScalar");
            observation.RecordCommandOutcome(unrelatedCommand, ResponseReplayAttemptOutcome.Canceled);
        }

        var unownedCommand = Guid.NewGuid();
        observation.RecordCommandStart(unownedCommand, activity: null, "Reader|NoActivity");
        observation.RecordCommandOutcome(unownedCommand, ResponseReplayAttemptOutcome.Succeeded);
        observation.RecordCommandOutcome(Guid.NewGuid(), ResponseReplayAttemptOutcome.Canceled);

        var snapshot = (await observation.EndCaptureAsync(TimeSpan.FromSeconds(1))).CallbackDrainedObservation;
        var request = snapshot.Buckets["requestDrain"];
        var background = snapshot.Buckets["backgroundResumption"];
        var unattributed = snapshot.Buckets["unattributed"];

        Assert.Equal(workflowExecutionId, snapshot.TargetExecutionId);
        Assert.Equal(workflowExecutionId, responseReceivedSnapshot.TargetExecutionId);
        Assert.Empty(responseReceivedSnapshot.Buckets["backgroundResumption"].Commands);
        Assert.Contains("NonQuery|ExecuteNonQuery", snapshot.Buckets["backgroundResumption"].Commands.Keys);
        Assert.Empty(responseReceivedSnapshot.ResponseCompletionAttempts);
        Assert.Single(snapshot.ResponseCompletionAttempts);
        Assert.Equal(RuntimeCheckpointNames.ActivityAttemptClaimed, snapshot.TargetClaim!.CheckpointName);
        Assert.Equal(endpointNodeId, snapshot.TargetClaim.NodeId);
        Assert.Equal("External", snapshot.TargetClaim.ClaimActivityProfile);
        Assert.Equal("write-response", snapshot.TargetClaim.ResponseNodeId);
        Assert.Equal("ReplaySafe", snapshot.TargetClaim.ResponseProfile);
        var responseCompletion = Assert.Single(snapshot.ResponseCompletionAttempts);
        Assert.Equal(RuntimeCheckpointNames.ActivityCompleted, responseCompletion.CheckpointName);
        Assert.Equal(workflowExecutionId, responseCompletion.WorkflowExecutionId);
        Assert.Equal("artifact-target", responseCompletion.ArtifactId);
        Assert.Equal("artifact-hash-target", responseCompletion.ArtifactHash);
        Assert.Equal("write-response", responseCompletion.NodeId);
        Assert.Equal("ReplaySafe", responseCompletion.PinnedResponseProfile);
        Assert.Equal(RuntimeCheckpointPersistenceMode.Deferred.ToString(), responseCompletion.PersistenceDecision);
        Assert.Equal(1, request.LogicalClaimCheckpointAttempts);
        Assert.Equal(1, request.LogicalClaimCheckpointSucceeded);
        Assert.Equal(1, request.DurableClaimCheckpointAttempts);
        Assert.Equal(0, request.DurableClaimCheckpointSucceeded);
        Assert.Equal(1, request.SegmentFlushAttempts);
        Assert.Equal(0, request.SegmentFlushes);
        Assert.Equal(1, request.Commands["Reader|ExecuteReader"].Succeeded);
        Assert.Equal(1, background.Commands["NonQuery|ExecuteNonQuery"].Failed);
        Assert.Equal(1, unattributed.Commands["Scalar|ExecuteScalar"].Canceled);
        Assert.Equal(1, unattributed.Commands["Reader|NoActivity"].Succeeded);
        Assert.Equal(1, snapshot.CarryInCommandOutcomes);
        Assert.Equal(1, snapshot.OrphanCommandOutcomes);
        Assert.Equal(0, snapshot.InFlightCommandAttempts);
    }

    private static ResponseReplayObservation CreateObservation() => new(new ResponseReplayMeasurementOptions(
        "/workflows/http/replay",
        "request-correlation",
        "artifact-target",
        "artifact-hash-target",
        "ReplaySafe",
        "http-in",
        "write-response"));

    private static Activity StartRequestActivity()
    {
        var activity = new Activity("measured-http-request");
        activity.SetTag(ResponseReplayObservation.RequestCorrelationActivityTag, "request-correlation");
        return activity.Start()!;
    }

    private static RuntimeCheckpointCommit CreateCommit(
        string workflowExecutionId,
        string nodeId,
        string checkpointName,
        bool carriesClaimMarker)
    {
        const string activityExecutionId = "activity-execution-http";
        const string workItemId = "scheduler-work-item-http";
        const string commandId = "command-http";
        const string attemptId = "attempt-http";
        var now = DateTimeOffset.UtcNow;
        var stateMetadata = new Dictionary<string, string>(StringComparer.Ordinal);
        if (carriesClaimMarker)
        {
            stateMetadata[RuntimeMetadataKeys.ActivityAttemptActivationClaim] = attemptId;
            stateMetadata[RuntimeMetadataKeys.ActivityAttemptActivationClaimWorkItemId] = workItemId;
        }

        var isCompleted = StringComparer.Ordinal.Equals(checkpointName, RuntimeCheckpointNames.ActivityCompleted);
        var state = new ActivityExecutionState(
            new ActivityExecution(activityExecutionId, workflowExecutionId, nodeId, nodeId, "test.activity", "1"),
            Status: isCompleted ? ActivityExecutionStatus.Completed : ActivityExecutionStatus.Running,
            SubStatus: null,
            ExecutionSequence: 1,
            ScheduledAt: now,
            StartedAt: now,
            CompletedAt: isCompleted ? now : null,
            SchedulingActivityExecutionId: null,
            ParentActivityExecutionId: null,
            BranchId: null,
            IterationId: null,
            Provenance: ActivitySchedulingProvenance.Empty,
            CallStackDepth: 0,
            BookmarkIds: [],
            IncidentIds: [],
            FaultCount: 0,
            AggregateFaultCount: 0,
            Metadata: stateMetadata)
        {
            Attempts = carriesClaimMarker
                ? [new ActivityAttempt(
                    attemptId,
                    activityExecutionId,
                    1,
                    ActivityAttemptReason.Initial,
                    now,
                    endedAt: isCompleted ? now : null,
                    transitionKind: isCompleted ? ActivityTransitionKind.Complete : null)]
                : []
        };
        var changes = new RuntimeCheckpointStateChangeSet(
            null,
            null,
            [new RuntimeStateChange<ActivityExecutionState>(activityExecutionId, RuntimeStateChangeOperation.Upsert, state, stateMetadata)],
            [], [], [], []);
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [RuntimeMetadataKeys.ExecutableArtifactId] = "artifact-target",
            [RuntimeMetadataKeys.ExecutableArtifactHash] = "artifact-hash-target",
            [RuntimeMetadataKeys.ExecutableNodeId] = nodeId,
            [RuntimeMetadataKeys.ActivityExecutionId] = activityExecutionId,
            [RuntimeMetadataKeys.SchedulerWorkItemId] = workItemId,
            [RuntimeMetadataKeys.CommandId] = commandId,
            [RuntimeMetadataKeys.ActivityAttemptActivationClaim] = attemptId
        };
        if (StringComparer.Ordinal.Equals(checkpointName, RuntimeCheckpointNames.ActivityAttemptClaimed))
            metadata[RuntimeMetadataKeys.CheckpointSideEffectProfile] = "External";
        var checkpoint = new RuntimeCheckpoint(
            $"checkpoint-{checkpointName}",
            checkpointName,
            workflowExecutionId,
            now,
            [activityExecutionId],
            new Dictionary<string, string>(StringComparer.Ordinal));
        return new RuntimeCheckpointCommit($"commit-{checkpointName}", checkpoint, changes, [], metadata);
    }
}
