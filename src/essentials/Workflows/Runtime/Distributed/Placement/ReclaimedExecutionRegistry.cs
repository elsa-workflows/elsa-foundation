using System.Collections.Concurrent;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Distributed.Placement;

/// <summary>
/// The executions a reclaim made recovery candidates in this shell, per persistence scope (spec 184, FR-024): those whose
/// execution lease or heartbeat was held under a reclaimed host id. The recovery sweep re-drives them through
/// <see cref="ReclaimedRecoveryCandidateSource"/> (FR-027).
/// </summary>
/// <remarks>
/// It is memory only, an accelerator over the durable record (ADR 0031): if this process stops before the sweep
/// re-drives them, their execution leases still time out and the scanner finds them then. For the same reason a
/// candidate is forgotten once it is older than <see cref="CandidateLifetime"/>, so a shell that composes no recovery
/// sweep, where nothing settles candidates, does not accumulate them.
/// </remarks>
public sealed class ReclaimedExecutionRegistry
{
    /// <summary>How long a candidate is kept unsettled: past it, the lease it names has timed out and the recovery
    /// scanner finds the execution on its own.</summary>
    public static readonly TimeSpan CandidateLifetime = TimeSpan.FromMinutes(30);

    /// <summary>The recovery source a reclaim's candidates carry, so the recovery says it was reclaimed.</summary>
    public const string RecoverySource = "Reclaim";

    public const string RecoverySourceMetadataKey = "runtime.recovery.source";
    public const string ReclaimKindMetadataKey = "runtime.recovery.reclaim";
    public const string ReclaimedHostIdMetadataKey = "runtime.recovery.reclaimedHostId";

    private readonly ConcurrentDictionary<(string Scope, string WorkflowExecutionId), RuntimeRecoveryCandidate> _candidates = new();

    /// <summary>Forgets every candidate older than <see cref="CandidateLifetime"/> at <paramref name="now"/>.</summary>
    public void Prune(DateTimeOffset now)
    {
        foreach (var stale in _candidates.Where(pair => pair.Value.DetectedAt < now - CandidateLifetime).ToArray())
            _candidates.TryRemove(stale);
    }

    /// <summary>Makes the execution of <paramref name="state"/> a recovery candidate in <paramref name="scope"/>.</summary>
    /// <param name="scope">The persistence scope the execution lives in.</param>
    /// <param name="state">The execution's liveness state, whose execution lease or heartbeat was held under
    /// <paramref name="hostId"/>; its last checkpoint, if any, is where the recovery resumes from.</param>
    /// <param name="hostId">The reclaimed host id, recorded on the candidate.</param>
    /// <param name="kind">Whether a join sweep or a departure reclaimed it, recorded on the candidate.</param>
    /// <param name="reclaimedAt">When the reclaim ran: the candidate's detection time, from which it ages out.</param>
    public void Add(string scope, ExecutionLivenessState state, string hostId, HostIdReclaimKind kind, DateTimeOffset reclaimedAt)
    {
        var lastCheckpointId = state.InterruptedExecution?.LastCheckpointId;
        _candidates[(scope, state.WorkflowExecutionId)] = new RuntimeRecoveryCandidate(
            workflowExecutionId: state.WorkflowExecutionId,
            operationalStateId: state.ExecutionLivenessStateId,
            lastCheckpointId: lastCheckpointId,
            reason: RuntimeInterruptionReason.HostStopped,
            detectedAt: reclaimedAt,
            requeueFromLastCheckpoint: !string.IsNullOrWhiteSpace(lastCheckpointId),
            metadata: new Dictionary<string, string>
            {
                [RecoverySourceMetadataKey] = RecoverySource,
                [ReclaimKindMetadataKey] = kind == HostIdReclaimKind.JoinSweep ? "join-sweep" : "departure",
                [ReclaimedHostIdMetadataKey] = hostId
            });
    }

    public IReadOnlyCollection<RuntimeRecoveryCandidate> List(string scope, int limit) =>
        _candidates
            .Where(pair => StringComparer.Ordinal.Equals(pair.Key.Scope, scope))
            .Select(pair => pair.Value)
            .OrderBy(candidate => candidate.WorkflowExecutionId, StringComparer.Ordinal)
            .Take(limit)
            .ToArray();

    public void Settle(string scope, IEnumerable<string> workflowExecutionIds)
    {
        foreach (var workflowExecutionId in workflowExecutionIds)
            _candidates.TryRemove((scope, workflowExecutionId), out _);
    }
}

/// <summary>
/// The distributed runtime's <see cref="IRuntimeRecoveryCandidateSource"/>: the recovery sweep re-drives reclaimed
/// executions at once rather than when their execution leases time out (spec 184, FR-027).
/// </summary>
public sealed class ReclaimedRecoveryCandidateSource(
    ReclaimedExecutionRegistry registry,
    IPersistenceAccessContextAccessor accessContextAccessor) : IRuntimeRecoveryCandidateSource
{
    public ValueTask<IReadOnlyCollection<RuntimeRecoveryCandidate>> ListAsync(int limit, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(registry.List(Scope, Math.Max(0, limit)));
    }

    public ValueTask SettleAsync(IReadOnlyCollection<string> workflowExecutionIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workflowExecutionIds);
        registry.Settle(Scope, workflowExecutionIds);
        return ValueTask.CompletedTask;
    }

    private string Scope => accessContextAccessor.Current.Scope?.Value ?? PersistenceScope.DefaultValue;
}
