using System.Diagnostics;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Diagnostics;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Diagnostics;

namespace Elsa.Activities.Http.IntegrationTests;

public sealed class ResponseReplayObservationTests
{
    [Fact]
    public void Observation_SeparatesRequestBackgroundAndUnattributedAttempts()
    {
        const string workflowExecutionId = "execution-target";
        const string endpointNodeId = "http-in";
        const string correlationId = "request-correlation";
        var observation = new ResponseReplayObservation(new ResponseReplayMeasurementOptions(
            "/workflows/http/replay",
            correlationId,
            "artifact-target",
            "artifact-hash-target",
            "ReplaySafe",
            endpointNodeId,
            "write-response"));

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

        var snapshot = observation.EndCapture();
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
