using System.Diagnostics;
using Elsa.Workflows.Runtime.Api.Coalescing;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Diagnostics;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Diagnostics;
using Elsa.Workflows.Runtime.Services.Coalescing;

namespace Elsa.Activities.Http.IntegrationTests;

internal sealed record ResponseReplayMeasurementOptions(
    string RequestPath,
    string CorrelationId,
    string TargetArtifactId,
    string TargetArtifactHash,
    string TargetProfile,
    string EndpointNodeId,
    string ResponseNodeId);

internal enum ResponseReplayWorkBucket
{
    RequestDrain,
    BackgroundResumption,
    Unattributed
}

internal enum ResponseReplayAttemptOutcome
{
    Succeeded,
    Failed,
    Canceled
}

internal sealed record ResponseReplayObservationSnapshot(
    string CorrelationId,
    string TargetExecutionId,
    string TargetArtifactId,
    string TargetArtifactHash,
    string TargetProfile,
    ResponseReplayClaimBoundarySnapshot? TargetClaim,
    IReadOnlyList<ResponseReplayResponseBoundarySnapshot> ResponseCompletionAttempts,
    bool DurableValuePageReuseEligible,
    bool EquivalentStartupPreparation,
    IReadOnlyDictionary<string, ResponseReplayBucketSnapshot> Buckets,
    int InFlightCommandAttempts,
    int CarryInCommandOutcomes,
    int OrphanCommandOutcomes);

internal sealed record ResponseReplayMeasurementResult(
    string WorkflowExecutionId,
    int HttpStatus,
    string ResponseBody,
    string ResponseContentType,
    IReadOnlyDictionary<string, string[]> AuthoredHeaders,
    ResponseReplayObservationSnapshot ResponseReceivedObservation,
    ResponseReplayObservationSnapshot Observation);

internal sealed record ResponseReplayClaimBoundarySnapshot(
    string CheckpointName,
    string WorkflowExecutionId,
    string ArtifactId,
    string ArtifactHash,
    string NodeId,
    string ClaimActivityProfile,
    string ResponseNodeId,
    string ResponseProfile);

internal sealed record ResponseReplayResponseBoundarySnapshot(
    string CheckpointName,
    string WorkflowExecutionId,
    string ArtifactId,
    string ArtifactHash,
    string NodeId,
    string PinnedResponseProfile,
    string PersistenceDecision);

internal sealed record ResponseReplayBucketSnapshot(
    long Dispatches,
    long DrainCycles,
    long ActivityExecutions,
    long LogicalCheckpointAttempts,
    long LogicalCheckpointSucceeded,
    long LogicalCheckpointFailed,
    long LogicalCheckpointCanceled,
    long LogicalClaimCheckpointAttempts,
    long LogicalClaimCheckpointSucceeded,
    long DeferredDecisionAttempts,
    long ImmediateDecisionAttempts,
    long DurableCheckpointAttempts,
    long DurableCheckpointSucceeded,
    long DurableCheckpointFailed,
    long DurableCheckpointCanceled,
    long DurableClaimCheckpointAttempts,
    long DurableClaimCheckpointSucceeded,
    long SegmentFlushAttempts,
    long SegmentFlushes,
    IReadOnlyDictionary<string, ResponseReplayCommandSnapshot> Commands);

internal sealed record ResponseReplayCommandSnapshot(
    long Started,
    long Succeeded,
    long Failed,
    long Canceled);

internal sealed class ResponseReplayObservation(ResponseReplayMeasurementOptions options)
{
    public const string RequestCorrelationActivityTag = "elsa.response_replay.request_correlation";

    private readonly object _sync = new();
    private readonly Dictionary<ResponseReplayWorkBucket, MutableBucket> _buckets = Enum.GetValues<ResponseReplayWorkBucket>()
        .ToDictionary(bucket => bucket, _ => new MutableBucket());
    private readonly Dictionary<Guid, Queue<CommandAttempt>> _inFlightCommands = [];
    private readonly List<ResponseReplayResponseBoundarySnapshot> _responseCompletionAttempts = [];
    private bool _capturing;
    private string? _targetExecutionId;
    private ResponseReplayClaimBoundarySnapshot? _targetClaim;
    private bool _pageReuseEligible;
    private bool _equivalentStartupPreparation;
    private int _carryInCommandOutcomes;
    private int _orphanCommandOutcomes;

    public ResponseReplayMeasurementOptions Options { get; } = options;

    public string? TargetExecutionId
    {
        get
        {
            lock (_sync)
                return _targetExecutionId;
        }
    }

    public void RecordHostComposition(bool pageReuseEligible, bool equivalentStartupPreparation)
    {
        lock (_sync)
        {
            _pageReuseEligible = pageReuseEligible;
            _equivalentStartupPreparation = equivalentStartupPreparation;
        }
    }

    public void BeginCapture()
    {
        lock (_sync)
        {
            if (_capturing)
                throw new InvalidOperationException("The response-replay observation window is already active.");
            if (_targetExecutionId is not null)
                throw new InvalidOperationException("The target execution was observed before the response-replay window started.");
            _capturing = true;
        }
    }

    public ResponseReplayObservationSnapshot EndCapture()
    {
        lock (_sync)
        {
            if (!_capturing)
                throw new InvalidOperationException("The response-replay observation window is not active.");
            _capturing = false;
            return CreateSnapshot();
        }
    }

    public ResponseReplayObservationSnapshot CaptureSnapshot()
    {
        lock (_sync)
        {
            if (!_capturing)
                throw new InvalidOperationException("The response-replay observation window is not active.");
            return CreateSnapshot();
        }
    }

    public CheckpointAttempt? BeginLogicalCheckpoint(RuntimeCheckpointCommit commit, RuntimeCheckpointPersistenceDecision decision) =>
        BeginCheckpoint(commit, decision, durable: false, hasBufferedSegment: false);

    public CheckpointAttempt? BeginDurableCheckpoint(
        RuntimeCheckpointCommit commit,
        RuntimeCheckpointPersistenceDecision decision,
        bool hasBufferedSegment) =>
        BeginCheckpoint(commit, decision, durable: true, hasBufferedSegment);

    public void CompleteLogicalCheckpoint(CheckpointAttempt? attempt, ResponseReplayAttemptOutcome outcome)
    {
        if (attempt is null)
            return;

        lock (_sync)
            _buckets[attempt.Bucket].CompleteLogical(outcome, attempt.IsClaimBoundary);
    }

    public void CompleteDurableCheckpoint(CheckpointAttempt? attempt, ResponseReplayAttemptOutcome outcome)
    {
        if (attempt is null)
            return;

        lock (_sync)
            _buckets[attempt.Bucket].CompleteDurable(outcome, attempt.IsClaimBoundary, attempt.IsSegmentFlush);
    }

    public void RecordDispatch(RuntimeSchedulerWorkItem workItem) =>
        RecordEngineEvent(workItem.WorkflowExecutionId, EngineEvent.Dispatch);

    public void RecordDrain(RuntimeSchedulerDrainRequest request) =>
        RecordEngineEvent(request.WorkflowExecutionId, EngineEvent.Drain);

    public void RecordActivityExecution(RuntimeSchedulerWorkItem workItem) =>
        RecordEngineEvent(workItem.WorkflowExecutionId, EngineEvent.ActivityExecution);

    public void RecordCommandStart(Guid commandId, Activity? activity, string commandCategory)
    {
        lock (_sync)
        {
            if (!_inFlightCommands.TryGetValue(commandId, out var attempts))
            {
                attempts = new Queue<CommandAttempt>();
                _inFlightCommands.Add(commandId, attempts);
            }

            if (!_capturing)
            {
                // Preserve an out-of-window start so an outcome crossing into the capture window is
                // reported as carry-in instead of being mistaken for a missing callback.
                attempts.Enqueue(new CommandAttempt(null));
                return;
            }

            var bucket = Classify(activity, workflowExecutionId: null);
            var commandKey = new CommandAttemptKey(bucket, commandCategory);
            _buckets[bucket].StartCommand(commandKey.Name);
            attempts.Enqueue(new CommandAttempt(commandKey));
        }
    }

    public void RecordCommandOutcome(Guid commandId, ResponseReplayAttemptOutcome outcome)
    {
        lock (_sync)
        {
            if (!_inFlightCommands.TryGetValue(commandId, out var attempts) || attempts.Count == 0)
            {
                if (_capturing)
                    _orphanCommandOutcomes++;
                return;
            }

            var attempt = attempts.Dequeue();
            if (attempt.Key is { } key)
                _buckets[key.Bucket].CompleteCommand(key.Name, outcome);
            else if (_capturing)
                _carryInCommandOutcomes++;
            if (attempts.Count == 0)
                _inFlightCommands.Remove(commandId);
        }
    }

    private CheckpointAttempt? BeginCheckpoint(
        RuntimeCheckpointCommit commit,
        RuntimeCheckpointPersistenceDecision decision,
        bool durable,
        bool hasBufferedSegment)
    {
        lock (_sync)
        {
            if (!_capturing)
                return null;

            var claimBoundary = IsClaimBoundary(commit);
            var isRequest = HasRequestCorrelation(Activity.Current);
            if (claimBoundary && isRequest && IsTargetArtifactClaim(commit))
            {
                if (_targetExecutionId is null)
                {
                    _targetExecutionId = commit.WorkflowExecutionId;
                    _targetClaim = new ResponseReplayClaimBoundarySnapshot(
                        commit.Checkpoint.Name,
                        commit.WorkflowExecutionId,
                        commit.Metadata[RuntimeMetadataKeys.ExecutableArtifactId],
                        commit.Metadata[RuntimeMetadataKeys.ExecutableArtifactHash],
                        commit.Metadata[RuntimeMetadataKeys.ExecutableNodeId],
                        commit.Metadata.GetValueOrDefault(RuntimeMetadataKeys.CheckpointSideEffectProfile) ?? "<absent>",
                        Options.ResponseNodeId,
                        Options.TargetProfile);
                }
                else if (!StringComparer.Ordinal.Equals(_targetExecutionId, commit.WorkflowExecutionId))
                    throw new InvalidOperationException("The measured child observed an activity claim for more than one workflow execution.");
            }

            if (!durable && IsTargetResponseCompletion(commit))
            {
                _responseCompletionAttempts.Add(new ResponseReplayResponseBoundarySnapshot(
                    commit.Checkpoint.Name,
                    commit.WorkflowExecutionId,
                    commit.Metadata[RuntimeMetadataKeys.ExecutableArtifactId],
                    commit.Metadata[RuntimeMetadataKeys.ExecutableArtifactHash],
                    commit.Metadata[RuntimeMetadataKeys.ExecutableNodeId],
                    Options.TargetProfile,
                    decision.Mode.ToString()));
            }

            var bucket = Classify(Activity.Current, commit.WorkflowExecutionId);
            var flush = hasBufferedSegment || commit.Checkpoint.Metadata.ContainsKey(RuntimeCoalescingMetadataKeys.CoalescedFlush);
            _buckets[bucket].StartCheckpoint(durable, decision.Mode, claimBoundary, flush);
            return new CheckpointAttempt(bucket, durable, claimBoundary, flush);
        }
    }

    private void RecordEngineEvent(string workflowExecutionId, EngineEvent engineEvent)
    {
        lock (_sync)
        {
            if (!_capturing)
                return;

            var bucket = Classify(Activity.Current, workflowExecutionId);
            _buckets[bucket].RecordEngineEvent(engineEvent);
        }
    }

    private ResponseReplayWorkBucket Classify(Activity? current, string? workflowExecutionId)
    {
        string? activityExecutionId = null;
        var hasRequestMarker = false;
        for (var activity = current; activity is not null; activity = activity.Parent)
        {
            if (StringComparer.Ordinal.Equals(activity.GetTagItem(RequestCorrelationActivityTag) as string, Options.CorrelationId))
                hasRequestMarker = true;
            activityExecutionId ??= activity.GetTagItem(WorkflowEngineTelemetry.WorkflowExecutionIdTag) as string;
        }

        var observedExecutionId = workflowExecutionId ?? activityExecutionId;
        if (hasRequestMarker && (_targetExecutionId is null || observedExecutionId is null ||
                                StringComparer.Ordinal.Equals(_targetExecutionId, observedExecutionId)))
            return ResponseReplayWorkBucket.RequestDrain;

        return _targetExecutionId is not null && StringComparer.Ordinal.Equals(_targetExecutionId, observedExecutionId)
            ? ResponseReplayWorkBucket.BackgroundResumption
            : ResponseReplayWorkBucket.Unattributed;
    }

    private ResponseReplayObservationSnapshot CreateSnapshot()
    {
        var commands = _inFlightCommands.Values.Sum(queue => queue.Count(attempt => attempt.Key is not null));
        return new ResponseReplayObservationSnapshot(
            Options.CorrelationId,
            _targetExecutionId ?? throw new InvalidOperationException("No target execution was observed in the measured request."),
            Options.TargetArtifactId,
            Options.TargetArtifactHash,
            Options.TargetProfile,
            _targetClaim,
            _responseCompletionAttempts.ToArray(),
            _pageReuseEligible,
            _equivalentStartupPreparation,
            _buckets.ToDictionary(
                pair => BucketName(pair.Key),
                pair => pair.Value.Snapshot(),
                StringComparer.Ordinal),
            commands,
            _carryInCommandOutcomes,
            _orphanCommandOutcomes);
    }

    private bool IsTargetArtifactClaim(RuntimeCheckpointCommit commit) =>
        commit.Metadata.TryGetValue(RuntimeMetadataKeys.ExecutableArtifactId, out var artifactId) &&
        StringComparer.Ordinal.Equals(artifactId, Options.TargetArtifactId) &&
        commit.Metadata.TryGetValue(RuntimeMetadataKeys.ExecutableArtifactHash, out var artifactHash) &&
        StringComparer.Ordinal.Equals(artifactHash, Options.TargetArtifactHash) &&
        commit.Metadata.TryGetValue(RuntimeMetadataKeys.ExecutableNodeId, out var nodeId) &&
        StringComparer.Ordinal.Equals(nodeId, Options.EndpointNodeId) &&
        commit.Metadata.TryGetValue(RuntimeMetadataKeys.CheckpointSideEffectProfile, out var profile) &&
        StringComparer.Ordinal.Equals(profile, RuntimeMetadataKeys.CheckpointSideEffectProfileExternal);

    private bool IsTargetResponseCompletion(RuntimeCheckpointCommit commit)
    {
        if (_targetExecutionId is null ||
            !StringComparer.Ordinal.Equals(commit.WorkflowExecutionId, _targetExecutionId) ||
            !StringComparer.Ordinal.Equals(commit.Checkpoint.Name, RuntimeCheckpointNames.ActivityCompleted) ||
            !commit.Metadata.TryGetValue(RuntimeMetadataKeys.ExecutableArtifactId, out var artifactId) ||
            !StringComparer.Ordinal.Equals(artifactId, Options.TargetArtifactId) ||
            !commit.Metadata.TryGetValue(RuntimeMetadataKeys.ExecutableArtifactHash, out var artifactHash) ||
            !StringComparer.Ordinal.Equals(artifactHash, Options.TargetArtifactHash) ||
            !commit.Metadata.TryGetValue(RuntimeMetadataKeys.ExecutableNodeId, out var nodeId) ||
            !StringComparer.Ordinal.Equals(nodeId, Options.ResponseNodeId) ||
            !commit.Metadata.TryGetValue(RuntimeMetadataKeys.ActivityExecutionId, out var activityExecutionId))
            return false;

        var stateChange = commit.StateChanges.ActivityExecutions.SingleOrDefault(change =>
            change.Operation == RuntimeStateChangeOperation.Upsert &&
            StringComparer.Ordinal.Equals(change.StateId, activityExecutionId) &&
            StringComparer.Ordinal.Equals(change.State.Execution.ActivityExecutionId, activityExecutionId));

        return stateChange?.State is { } state &&
               state.Status == ActivityExecutionStatus.Completed &&
               StringComparer.Ordinal.Equals(state.Execution.WorkflowExecutionId, _targetExecutionId) &&
               StringComparer.Ordinal.Equals(state.Execution.AuthoredActivityId, Options.ResponseNodeId);
    }

    private static bool IsClaimBoundary(RuntimeCheckpointCommit commit)
    {
        if (!StringComparer.Ordinal.Equals(commit.Checkpoint.Name, RuntimeCheckpointNames.ActivityAttemptClaimed) ||
            !commit.Metadata.TryGetValue(RuntimeMetadataKeys.ExecutableNodeId, out var nodeId) ||
            !commit.Metadata.TryGetValue(RuntimeMetadataKeys.ActivityExecutionId, out var activityExecutionId) ||
            !commit.Metadata.TryGetValue(RuntimeMetadataKeys.SchedulerWorkItemId, out var workItemId) ||
            !commit.Metadata.TryGetValue(RuntimeMetadataKeys.ActivityAttemptActivationClaim, out var attemptId))
            return false;

        var stateChange = commit.StateChanges.ActivityExecutions.SingleOrDefault(change =>
            change.Operation == RuntimeStateChangeOperation.Upsert &&
            StringComparer.Ordinal.Equals(change.State.Execution.ActivityExecutionId, activityExecutionId));
        return stateChange?.State is { } state &&
               StringComparer.Ordinal.Equals(state.Execution.AuthoredActivityId, nodeId) &&
               state.Metadata.TryGetValue(RuntimeMetadataKeys.ActivityAttemptActivationClaim, out var stateAttemptId) &&
               StringComparer.Ordinal.Equals(stateAttemptId, attemptId) &&
               state.Metadata.TryGetValue(RuntimeMetadataKeys.ActivityAttemptActivationClaimWorkItemId, out var stateWorkItemId) &&
               StringComparer.Ordinal.Equals(stateWorkItemId, workItemId) &&
               state.Attempts?.SingleOrDefault(attempt => StringComparer.Ordinal.Equals(attempt.AttemptId, attemptId)) is { EndedAt: null };
    }

    private bool HasRequestCorrelation(Activity? current)
    {
        for (var activity = current; activity is not null; activity = activity.Parent)
        {
            if (StringComparer.Ordinal.Equals(activity.GetTagItem(RequestCorrelationActivityTag) as string, Options.CorrelationId))
                return true;
        }

        return false;
    }

    private static string BucketName(ResponseReplayWorkBucket bucket) => bucket switch
    {
        ResponseReplayWorkBucket.RequestDrain => "requestDrain",
        ResponseReplayWorkBucket.BackgroundResumption => "backgroundResumption",
        ResponseReplayWorkBucket.Unattributed => "unattributed",
        _ => throw new ArgumentOutOfRangeException(nameof(bucket), bucket, null)
    };

    internal sealed record CheckpointAttempt(ResponseReplayWorkBucket Bucket, bool Durable, bool IsClaimBoundary, bool IsSegmentFlush);

    private sealed record CommandAttemptKey(ResponseReplayWorkBucket Bucket, string Name);
    private sealed record CommandAttempt(CommandAttemptKey? Key);

    private enum EngineEvent
    {
        Dispatch,
        Drain,
        ActivityExecution
    }

    private sealed class MutableBucket
    {
        private readonly Dictionary<string, MutableCommandSnapshot> _commands = new(StringComparer.Ordinal);
        public long Dispatches;
        public long DrainCycles;
        public long ActivityExecutions;
        public long LogicalCheckpointAttempts;
        public long LogicalCheckpointSucceeded;
        public long LogicalCheckpointFailed;
        public long LogicalCheckpointCanceled;
        public long LogicalClaimCheckpointAttempts;
        public long LogicalClaimCheckpointSucceeded;
        public long DeferredDecisionAttempts;
        public long ImmediateDecisionAttempts;
        public long DurableCheckpointAttempts;
        public long DurableCheckpointSucceeded;
        public long DurableCheckpointFailed;
        public long DurableCheckpointCanceled;
        public long DurableClaimCheckpointAttempts;
        public long DurableClaimCheckpointSucceeded;
        public long SegmentFlushAttempts;
        public long SegmentFlushes;

        public void StartCheckpoint(bool durable, RuntimeCheckpointPersistenceMode mode, bool claimBoundary, bool flush)
        {
            if (durable)
            {
                DurableCheckpointAttempts++;
                if (claimBoundary)
                    DurableClaimCheckpointAttempts++;
                if (flush)
                    SegmentFlushAttempts++;
                return;
            }

            LogicalCheckpointAttempts++;
            if (claimBoundary)
                LogicalClaimCheckpointAttempts++;
            if (mode == RuntimeCheckpointPersistenceMode.Deferred)
                DeferredDecisionAttempts++;
            else
                ImmediateDecisionAttempts++;
        }

        public void CompleteLogical(ResponseReplayAttemptOutcome outcome, bool claimBoundary)
        {
            switch (outcome)
            {
                case ResponseReplayAttemptOutcome.Succeeded:
                    LogicalCheckpointSucceeded++;
                    if (claimBoundary)
                        LogicalClaimCheckpointSucceeded++;
                    break;
                case ResponseReplayAttemptOutcome.Failed: LogicalCheckpointFailed++; break;
                case ResponseReplayAttemptOutcome.Canceled: LogicalCheckpointCanceled++; break;
                default: throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null);
            }
        }

        public void CompleteDurable(ResponseReplayAttemptOutcome outcome, bool claimBoundary, bool segmentFlush)
        {
            switch (outcome)
            {
                case ResponseReplayAttemptOutcome.Succeeded:
                    DurableCheckpointSucceeded++;
                    if (claimBoundary)
                        DurableClaimCheckpointSucceeded++;
                    if (segmentFlush)
                        SegmentFlushes++;
                    break;
                case ResponseReplayAttemptOutcome.Failed: DurableCheckpointFailed++; break;
                case ResponseReplayAttemptOutcome.Canceled: DurableCheckpointCanceled++; break;
                default: throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null);
            }
        }

        public void StartCommand(string name)
        {
            if (!_commands.TryGetValue(name, out var counter))
            {
                counter = new MutableCommandSnapshot();
                _commands.Add(name, counter);
            }
            counter.Started++;
        }

        public void CompleteCommand(string name, ResponseReplayAttemptOutcome outcome)
        {
            var counter = _commands[name];
            switch (outcome)
            {
                case ResponseReplayAttemptOutcome.Succeeded: counter.Succeeded++; break;
                case ResponseReplayAttemptOutcome.Failed: counter.Failed++; break;
                case ResponseReplayAttemptOutcome.Canceled: counter.Canceled++; break;
                default: throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null);
            }
        }

        public void RecordEngineEvent(EngineEvent engineEvent)
        {
            switch (engineEvent)
            {
                case EngineEvent.Dispatch: Dispatches++; break;
                case EngineEvent.Drain: DrainCycles++; break;
                case EngineEvent.ActivityExecution: ActivityExecutions++; break;
                default: throw new ArgumentOutOfRangeException(nameof(engineEvent), engineEvent, null);
            }
        }

        public ResponseReplayBucketSnapshot Snapshot() => new(
            Dispatches,
            DrainCycles,
            ActivityExecutions,
            LogicalCheckpointAttempts,
            LogicalCheckpointSucceeded,
            LogicalCheckpointFailed,
            LogicalCheckpointCanceled,
            LogicalClaimCheckpointAttempts,
            LogicalClaimCheckpointSucceeded,
            DeferredDecisionAttempts,
            ImmediateDecisionAttempts,
            DurableCheckpointAttempts,
            DurableCheckpointSucceeded,
            DurableCheckpointFailed,
            DurableCheckpointCanceled,
            DurableClaimCheckpointAttempts,
            DurableClaimCheckpointSucceeded,
            SegmentFlushAttempts,
            SegmentFlushes,
            _commands.ToDictionary(
                pair => pair.Key,
                pair => new ResponseReplayCommandSnapshot(
                    pair.Value.Started,
                    pair.Value.Succeeded,
                    pair.Value.Failed,
                    pair.Value.Canceled),
                StringComparer.Ordinal));
    }

    private sealed class MutableCommandSnapshot
    {
        public long Started;
        public long Succeeded;
        public long Failed;
        public long Canceled;
    }
}
