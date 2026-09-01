using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// Applies the provider-neutral recovery decision and ordering rules to a finite set of liveness states.
/// Persistence implementations may preselect that set through provider-native bounded routes.
/// </summary>
public static class RuntimeRecoveryCandidateSelector
{
    private const string SourceMetadataKey = "runtime.recovery.source";

    public static IReadOnlyCollection<RuntimeRecoveryCandidate> Select(
        IEnumerable<ExecutionLivenessState> states,
        RuntimeRecoveryScanRequest request)
    {
        ArgumentNullException.ThrowIfNull(states);
        ArgumentNullException.ThrowIfNull(request);

        return states
            .Select(state => TryEvaluate(state, request))
            .Where(evaluation => evaluation is not null)
            .Select(evaluation => evaluation!)
            .OrderBy(evaluation => evaluation.EligibleAt)
            .ThenBy(evaluation => evaluation.Candidate.WorkflowExecutionId, StringComparer.Ordinal)
            .ThenBy(evaluation => evaluation.Candidate.OperationalStateId, StringComparer.Ordinal)
            .Take(request.Limit)
            .Select(evaluation => evaluation.Candidate)
            .ToArray();
    }

    /// <summary>Returns the stable ordering key for an eligible state without allocating a candidate.</summary>
    public static DateTimeOffset? GetEligibleAt(
        ExecutionLivenessState state,
        RuntimeRecoveryScanRequest request) =>
        TryEvaluate(state, request)?.EligibleAt;

    private static RuntimeRecoveryEvaluation? TryEvaluate(
        ExecutionLivenessState state,
        RuntimeRecoveryScanRequest request)
    {
        if (state.InterruptedExecution is { Status: RuntimeInterruptionStatus.Detected } interruptedExecution &&
            DetectedInterruptionOwnerMatches(state, request.OwnerId))
        {
            return Evaluate(
                state,
                request,
                interruptedExecution.Reason,
                interruptedExecution.LastCheckpointId,
                "InterruptedExecution");
        }

        if (state.ExecutionLease is { } lease &&
            OwnerMatches(lease.OwnerId, request.OwnerId) &&
            IsLeaseExpired(lease, request))
        {
            return Evaluate(
                state,
                request,
                RuntimeInterruptionReason.LeaseLost,
                state.InterruptedExecution?.LastCheckpointId,
                "ExecutionLease");
        }

        if (state.Heartbeat is { } heartbeat &&
            OwnerMatches(heartbeat.OwnerId, request.OwnerId) &&
            HeartbeatDueAt(heartbeat, request) <= request.Now)
        {
            return Evaluate(
                state,
                request,
                RuntimeInterruptionReason.HeartbeatExpired,
                state.InterruptedExecution?.LastCheckpointId,
                "Heartbeat");
        }

        return null;
    }

    private static RuntimeRecoveryEvaluation Evaluate(
        ExecutionLivenessState state,
        RuntimeRecoveryScanRequest request,
        RuntimeInterruptionReason reason,
        string? lastCheckpointId,
        string source)
    {
        var canRequeue = !string.IsNullOrWhiteSpace(lastCheckpointId);
        var candidate = new RuntimeRecoveryCandidate(
            workflowExecutionId: state.WorkflowExecutionId,
            operationalStateId: state.OperationalStateId,
            lastCheckpointId: lastCheckpointId,
            reason: reason,
            detectedAt: request.Now,
            requeueFromLastCheckpoint: canRequeue,
            metadata: new Dictionary<string, string>
            {
                [SourceMetadataKey] = source
            });

        return new RuntimeRecoveryEvaluation(candidate, EarliestEligibleAt(state, request));
    }

    private static DateTimeOffset EarliestEligibleAt(
        ExecutionLivenessState state,
        RuntimeRecoveryScanRequest request)
    {
        var signals = new List<DateTimeOffset>(3);

        if (state.InterruptedExecution is { Status: RuntimeInterruptionStatus.Detected } interruptedExecution &&
            DetectedInterruptionOwnerMatches(state, request.OwnerId))
        {
            signals.Add(interruptedExecution.InterruptedAt);
        }

        if (state.ExecutionLease is { } lease && OwnerMatches(lease.OwnerId, request.OwnerId))
            signals.Add(LeaseDueAt(lease, request));

        if (state.Heartbeat is { } heartbeat && OwnerMatches(heartbeat.OwnerId, request.OwnerId))
            signals.Add(HeartbeatDueAt(heartbeat, request));

        return signals.Min();
    }

    private static bool DetectedInterruptionOwnerMatches(ExecutionLivenessState state, string? ownerId) =>
        ownerId is null
        || HasNoOperationalOwner(state)
        || StringComparer.Ordinal.Equals(state.ExecutionLease?.OwnerId, ownerId)
        || StringComparer.Ordinal.Equals(state.Heartbeat?.OwnerId, ownerId);

    private static bool HasNoOperationalOwner(ExecutionLivenessState state) =>
        state.ExecutionLease is null && state.Heartbeat is null;

    private static bool OwnerMatches(string sourceOwnerId, string? requestedOwnerId) =>
        requestedOwnerId is null || StringComparer.Ordinal.Equals(sourceOwnerId, requestedOwnerId);

    private static bool IsLeaseExpired(RuntimeExecutionLease lease, RuntimeRecoveryScanRequest request) =>
        LeaseDueAt(lease, request) <= request.Now;

    private static DateTimeOffset LeaseDueAt(RuntimeExecutionLease lease, RuntimeRecoveryScanRequest request) =>
        Min(lease.ExpiresAt, lease.AcquiredAt.Add(request.LeaseTimeout));

    private static DateTimeOffset HeartbeatDueAt(RuntimeHeartbeat heartbeat, RuntimeRecoveryScanRequest request) =>
        heartbeat.RecordedAt.Add(request.HeartbeatTimeout);

    private static DateTimeOffset Min(DateTimeOffset first, DateTimeOffset second) =>
        first <= second ? first : second;

    private sealed record RuntimeRecoveryEvaluation(
        RuntimeRecoveryCandidate Candidate,
        DateTimeOffset EligibleAt);
}
